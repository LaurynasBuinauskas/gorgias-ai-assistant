"""Embed redacted ticket exemplars and upsert them into the ticket index.

Reads the JSONL that `extract_tickets.py` produced. Deliberately a separate step: extraction
touches Gorgias and produces a file you can inspect, indexing is the irreversible act — the
embedding is computed from the text, so an under-redacted index is not repaired by deleting
rows.

**The fail-closed check runs again here**, over the file rather than over the extractor's
in-memory state. Extraction already refused anything with residual identifiers; re-checking at
the boundary costs nothing and means a hand-edited or half-written file cannot slip past.

    python tools/ingest/ingest_tickets.py --in data/exemplars.jsonl --dry-run
    python tools/ingest/ingest_tickets.py --in data/exemplars.jsonl
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import shutil
import subprocess
import sys
import re
import time
import urllib.error
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from redaction import residual_identifiers  # noqa: E402

SERVICE = "gorgias-assistant-search"
VAULT = "gorgias-assistant-kv"
# `tickets-v2` adds the separately embedded `questionVector`. Rebuilding into a new index
# rather than over the old one keeps rollback to one app setting (`Knowledge__TicketIndexName`)
# with no reindex to undo.
INDEX = "tickets-v2"
API_VERSION = "2024-07-01"
ENDPOINT = f"https://{SERVICE}.search.windows.net"

EMBEDDING_MODEL = "text-embedding-3-small"
EMBEDDING_DIMENSIONS = 1536
EMBED_BATCH = 96
UPLOAD_BATCH = 500

# text-embedding-3-small accepts 8,192 tokens. The usual four-characters-per-token rule does
# not hold here: German, Polish and Lithuanian tokenise closer to two, so a 16,000-character
# exchange overruns the limit and rejects the whole batch. Three exchanges of 18,555 exceed
# this; truncating them keeps the substance — the median exchange is 746 characters — where
# dropping them would silently lose the longest conversations in the corpus.
MAX_EMBED_CHARS = 12_000


def secret(name: str) -> str:
    cli = shutil.which("az")
    if cli is None:
        raise SystemExit("the Azure CLI ('az') is not on PATH")
    result = subprocess.run(
        [cli, "keyvault", "secret", "show", "--vault-name", VAULT,
         "--name", name, "--query", "value", "-o", "tsv"],
        capture_output=True, text=True)
    if result.returncode != 0:
        raise SystemExit(f"could not read {name}: {result.stderr.strip()[:200]}")
    return result.stdout.strip()


def request(url: str, headers: dict[str, str], body: dict) -> dict:
    """POST with backoff.

    Embedding a corpus this size reliably trips the tokens-per-minute limit, which is a pause
    rather than a failure. Treating it as fatal threw away 7,392 embeddings on the first run.

    Connection failures are retried for the same reason. A run of 17,863 exchanges makes
    roughly 560 calls over half an hour, and a single dropped TCP connection ended one of them
    at 43 % — the vectors already bought were fine, but the process was gone.
    """
    for attempt in range(8):
        req = urllib.request.Request(url, data=json.dumps(body).encode(), method="POST")
        for key, value in headers.items():
            req.add_header(key, value)
        try:
            with urllib.request.urlopen(req, timeout=300) as response:
                payload = response.read()
                return json.loads(payload) if payload else {}
        except urllib.error.HTTPError as error:
            detail = error.read().decode(errors="replace")
            if error.code in (429, 500, 502, 503, 504):
                # OpenAI states the wait in the message; honour it rather than guessing.
                suggested = re.search(r"try again in ([\d.]+)s", detail)
                delay = float(suggested.group(1)) + 1 if suggested else min(2 ** attempt, 30)
                print(f"    rate limited, waiting {delay:.1f}s", flush=True)
                time.sleep(delay)
                continue
            raise SystemExit(f"{url.split('?')[0]}: HTTP {error.code}\n{detail[:400]}") from error
        except (urllib.error.URLError, TimeoutError, ConnectionError) as error:
            delay = min(2 ** attempt, 30)
            print(f"    connection failed ({error}); retrying in {delay}s", flush=True)
            time.sleep(delay)

    raise SystemExit(f"{url.split('?')[0]}: gave up after repeated failures")


def indexed_keys(search_key: str, index: str) -> set[str]:
    """Every document key currently in the index."""
    keys: set[str] = set()
    skip = 0
    while True:
        page = request(f"{ENDPOINT}/indexes/{index}/docs/search?api-version={API_VERSION}",
                       {"Content-Type": "application/json", "api-key": search_key},
                       {"search": "*", "top": 1000, "skip": skip, "select": "id"})["value"]
        if not page:
            return keys
        keys.update(document["id"] for document in page)
        skip += len(page)


def document_key(natural: str) -> str:
    """Search keys accept only letters, digits, _, - and =, so the natural key is encoded."""
    return base64.urlsafe_b64encode(natural.encode()).decode().rstrip("=")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--in", dest="source", default="data/exemplars.jsonl")
    parser.add_argument("--dry-run", action="store_true",
                        help="check and report without embedding or writing")
    parser.add_argument("--prune", action="store_true",
                        help="delete indexed documents absent from the file. Needed whenever "
                             "the file shrinks: uploading overwrites by key, so an exchange "
                             "withdrawn from the corpus otherwise stays live in the index.")
    parser.add_argument("--index", default=INDEX,
                        help=f"target index (default {INDEX})")
    parser.add_argument("--resume", action="store_true",
                        help="skip exchanges already in the index. For continuing an "
                             "interrupted run over an unchanged file: a document is only "
                             "written after its vectors exist, so anything present is complete. "
                             "Do not use it after editing the corpus — it would skip the edits.")
    args = parser.parse_args()
    index = args.index

    path = Path(args.source)
    if not path.exists():
        print(f"error: {path} not found — run extract_tickets.py first", file=sys.stderr)
        return 1

    rows = [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line]
    print(f"read {len(rows):,} exchange(s) from {path}")

    documents, blocked, truncated = [], [], 0
    for row in rows:
        text = f"Customer asked: {row['question']}\n\nSupport replied: {row['answer']}"
        if len(text) > MAX_EMBED_CHARS:
            text = text[:MAX_EMBED_CHARS]
            truncated += 1

        residual = residual_identifiers(text)
        if residual:
            blocked.append((row["ticket_id"], [(f.kind, f.value[:24]) for f in residual]))
            continue

        natural = f"ticket:{row['ticket_id']}:{row['ordinal']}"
        documents.append({
            "id": document_key(natural),
            "corpus": "ticket",
            # Exemplars teach phrasing, not entitlements, so they are not market-scoped:
            # scoping them would hide a well-phrased reply from thirteen other markets.
            "market": "GLOBAL",
            "exposure": "customer",
            "topic": "exemplar",
            "title": f"Past resolution ({row.get('channel') or 'email'})",
            "content": text,
            # The customer's question on its own, embedded separately into `questionVector`.
            # Retrieval is looking for an exchange that asked the same thing, and matching that
            # against question-plus-reply matches partly on agent phrasing: measured over 1,000
            # exchanges, question-only recall@3 beat whole-exchange recall@3. See
            # `tools/evals/exemplar_recall.py`.
            "question": row["question"][:MAX_EMBED_CHARS],
            "sourcePath": f"gorgias/ticket/{row['ticket_id']}",
            "sourceVersion": hashlib.sha256(text.encode()).hexdigest()[:16],
            "ticketId": str(row["ticket_id"]),
            "resolvedAt": row.get("closed_at"),
        })

    print(f"ready to index      {len(documents):,}")
    print(f"truncated           {truncated:,}  (over {MAX_EMBED_CHARS:,} characters)")
    print(f"withheld            {len(blocked):,}  (residual identifiers at the boundary)")
    for ticket_id, findings in blocked[:10]:
        print(f"  ticket {ticket_id}: {findings}")

    if args.dry_run:
        print("\ndry run: nothing embedded, nothing written")
        return 0
    if blocked:
        print("\nrefusing to index: resolve the withheld exchanges first", file=sys.stderr)
        return 1
    if not documents:
        print("nothing to index")
        return 0

    openai_key, search_key = secret("openai-apikey"), secret("search-adminkey")

    if args.resume:
        already = indexed_keys(search_key, index)
        before = len(documents)
        documents = [d for d in documents if d["id"] not in already]
        print(f"resuming           skipping {before - len(documents):,} already indexed, "
              f"{len(documents):,} to go")
        if not documents:
            print("nothing left to index")
            return 0

    if args.prune:
        stale = indexed_keys(search_key, index) - {document["id"] for document in documents}
        print(f"pruning            {len(stale):,} document(s) no longer in the corpus")
        for start in range(0, len(stale), UPLOAD_BATCH):
            batch = list(stale)[start:start + UPLOAD_BATCH]
            request(f"{ENDPOINT}/indexes/{index}/docs/index?api-version={API_VERSION}",
                    {"Content-Type": "application/json", "api-key": search_key},
                    {"value": [{"@search.action": "delete", "id": key} for key in batch]})

    # Embed and upload in the same pass. Embedding everything first meant a rate limit at 40 %
    # discarded every vector bought up to that point; batching makes progress durable, and the
    # stable document key means a re-run simply overwrites what is already there.
    def embed(texts: list[str]) -> list[list[float]]:
        payload = request("https://api.openai.com/v1/embeddings",
                          {"Content-Type": "application/json",
                           "Authorization": f"Bearer {openai_key}"},
                          {"model": EMBEDDING_MODEL,
                           "input": texts,
                           "dimensions": EMBEDDING_DIMENSIONS})
        return [item["embedding"] for item in payload["data"]]

    uploaded = 0
    for start in range(0, len(documents), EMBED_BATCH):
        batch = documents[start:start + EMBED_BATCH]
        content_vectors = embed([d["content"] for d in batch])
        question_vectors = embed([d["question"] or d["content"] for d in batch])

        request(f"{ENDPOINT}/indexes/{index}/docs/index?api-version={API_VERSION}",
                {"Content-Type": "application/json", "api-key": search_key},
                {"value": [dict(document,
                                contentVector=content,
                                questionVector=question,
                                **{"@search.action": "mergeOrUpload"})
                           for document, content, question
                           in zip(batch, content_vectors, question_vectors, strict=True)]})

        uploaded += len(batch)
        if uploaded % (EMBED_BATCH * 10) == 0 or uploaded == len(documents):
            print(f"  indexed {uploaded:,}/{len(documents):,}", flush=True)

    print(f"\nindexed {uploaded:,} exemplar(s) into {index}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
