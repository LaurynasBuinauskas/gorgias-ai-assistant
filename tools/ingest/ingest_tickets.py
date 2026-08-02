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
INDEX = "tickets-v1"
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

    raise SystemExit(f"{url.split('?')[0]}: gave up after repeated rate limiting")


def document_key(natural: str) -> str:
    """Search keys accept only letters, digits, _, - and =, so the natural key is encoded."""
    return base64.urlsafe_b64encode(natural.encode()).decode().rstrip("=")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--in", dest="source", default="data/exemplars.jsonl")
    parser.add_argument("--dry-run", action="store_true",
                        help="check and report without embedding or writing")
    args = parser.parse_args()

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

    # Embed and upload in the same pass. Embedding everything first meant a rate limit at 40 %
    # discarded every vector bought up to that point; batching makes progress durable, and the
    # stable document key means a re-run simply overwrites what is already there.
    uploaded = 0
    for start in range(0, len(documents), EMBED_BATCH):
        batch = documents[start:start + EMBED_BATCH]
        payload = request("https://api.openai.com/v1/embeddings",
                          {"Content-Type": "application/json",
                           "Authorization": f"Bearer {openai_key}"},
                          {"model": EMBEDDING_MODEL,
                           "input": [d["content"] for d in batch],
                           "dimensions": EMBEDDING_DIMENSIONS})
        vectors = [item["embedding"] for item in payload["data"]]

        request(f"{ENDPOINT}/indexes/{INDEX}/docs/index?api-version={API_VERSION}",
                {"Content-Type": "application/json", "api-key": search_key},
                {"value": [dict(document, contentVector=vector,
                                **{"@search.action": "mergeOrUpload"})
                           for document, vector in zip(batch, vectors, strict=True)]})

        uploaded += len(batch)
        if uploaded % (EMBED_BATCH * 10) == 0 or uploaded == len(documents):
            print(f"  indexed {uploaded:,}/{len(documents):,}", flush=True)

    print(f"\nindexed {uploaded:,} exemplar(s) into {INDEX}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
