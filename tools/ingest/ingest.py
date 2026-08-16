"""Ingest the knowledge tree into Azure AI Search.

Offline and idempotent: chunks are keyed by a stable identity and carry a content hash, so a
second run over unchanged content embeds nothing and writes nothing. That property is what
makes reindexing cheap enough to do on every content change.

Secrets are read from Key Vault at run time via the Azure CLI — nothing is stored here.

Run from the repository root:

    python tools/ingest/ingest.py --dry-run     # chunk and report, no network
    python tools/ingest/ingest.py               # embed and upsert into the alias target
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import re
import shutil
import subprocess
import sys
import urllib.error
import urllib.request
from dataclasses import dataclass
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from chunking import split  # noqa: E402
from staged import StagedDoc, to_policy_text  # noqa: E402
from staged import fetch as fetch_staged  # noqa: E402
from staged import validate as validate_staged  # noqa: E402

KNOWLEDGE = Path("knowledge")
CORPORA = {"policy": "policy", "template": "templates", "internal": "internal"}

SERVICE = "gorgias-assistant-search"
VAULT = "gorgias-assistant-kv"
INDEX = "knowledge-v1"
API_VERSION = "2024-07-01"
ENDPOINT = f"https://{SERVICE}.search.windows.net"

EMBEDDING_MODEL = "text-embedding-3-small"
EMBEDDING_DIMENSIONS = 1536
EMBED_BATCH = 96
UPLOAD_BATCH = 500

FRONT_MATTER = re.compile(r"^---\n(.*?)\n---\n", re.S)


@dataclass(frozen=True)
class Document:
    id: str
    corpus: str
    market: str
    exposure: str
    topic: str
    tags: list[str]
    title: str
    content: str
    source_path: str
    source_version: str
    effective_date: str


def secret(name: str) -> str:
    # Environment first, Key Vault via the CLI second — same contract as the eval runner,
    # so CI can pass repository secrets without an Azure login on the runner.
    import os
    from_env = os.environ.get(name.replace("-", "_").upper())
    if from_env:
        return from_env

    az = shutil.which("az")
    if az is None:
        raise SystemExit("the Azure CLI ('az') is not on PATH")
    result = subprocess.run(
        [az, "keyvault", "secret", "show", "--vault-name", VAULT,
         "--name", name, "--query", "value", "-o", "tsv"],
        capture_output=True, text=True)
    if result.returncode != 0:
        raise SystemExit(f"could not read {name} from {VAULT}: {result.stderr.strip()}")
    return result.stdout.strip()


def request(url: str, headers: dict[str, str], body: dict | None = None,
            method: str | None = None, tolerate: tuple[int, ...] = ()) -> dict | None:
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data,
                                 method=method or ("POST" if data else "GET"))
    for key, value in headers.items():
        req.add_header(key, value)
    try:
        with urllib.request.urlopen(req, timeout=120) as response:
            payload = response.read()
            return json.loads(payload) if payload else {}
    except urllib.error.HTTPError as error:
        if error.code in tolerate:
            return None
        raise SystemExit(f"{url.split('?')[0]} failed: HTTP {error.code}\n"
                         f"{error.read().decode(errors='replace')[:400]}") from error


def parse_front_matter(text: str) -> tuple[dict[str, str], str]:
    match = FRONT_MATTER.match(text)
    if not match:
        return {}, text
    fields = dict(re.findall(r"^(\w+):\s*(.+)$", match.group(1), re.M))
    return fields, text[match.end():]


def document_key(natural: str) -> str:
    """Search keys accept only letters, digits, _, - and =, so the natural key is encoded."""
    return base64.urlsafe_b64encode(natural.encode()).decode().rstrip("=")


def collect(staged: list[StagedDoc] | None = None) -> list[Document]:
    documents: list[Document] = []
    excluded: list[str] = []

    # A staged upload replaces the git-managed policy for its (market, topic) — the client
    # is editing the live document, not adding a competing version of it.
    superseded = {(doc.market, doc.topic) for doc in staged or []}

    for corpus, folder in CORPORA.items():
        root = KNOWLEDGE / folder
        if not root.exists():
            continue
        for path in sorted(root.rglob("*.md")):
            if path.name == "README.md":
                continue
            fields, body = parse_front_matter(path.read_text(encoding="utf-8"))
            if not fields:
                print(f"warning: {path} has no front-matter; skipped", file=sys.stderr)
                continue

            if (corpus == "policy"
                    and (fields.get("market", "GLOBAL"), fields.get("topic", "")) in superseded):
                print(f"superseded by staged upload: {path.as_posix()}")
                continue

            # A document can be internal *and* still be wrong to retrieve. Internal guidance is
            # never quoted into a reply, but the model reads it to decide what to say, and it
            # demonstrably converts what it reads: shown "repair at headquarters can take 2
            # months" it told a customer "about 8 weeks", contradicting the published policy of
            # one week that was sitting at the top of POLICY in the same prompt. Class A did not
            # catch it because class A looks for internal wording and the units had changed.
            #
            # So a document whose content is agent workflow rather than anything a reply should
            # rest on can opt out of retrieval entirely and stay in the repository for people.
            if fields.get("retrieval", "").strip().lower() == "exclude":
                excluded.append(path.as_posix())
                continue

            source_path = path.as_posix()
            title = next((m.group(1) for m in [re.search(r"^#\s+(.+)$", body, re.M)] if m),
                         path.stem)
            root_crumb = f"{fields.get('market', 'GLOBAL')} > {title}"
            tags = re.findall(r'"([^"]+)"', fields.get("tags", ""))

            for chunk in split(body, corpus, root_crumb):
                natural = f"{corpus}:{source_path}:{chunk.ordinal}"
                documents.append(Document(
                    id=document_key(natural),
                    corpus=corpus,
                    market=fields.get("market", "GLOBAL"),
                    exposure=fields.get("exposure", ""),
                    topic=fields.get("topic", ""),
                    tags=tags,
                    title=chunk.title,
                    content=chunk.content,
                    source_path=source_path,
                    source_version=hashlib.sha256(chunk.content.encode()).hexdigest()[:16],
                    effective_date=fields.get("effective_date", ""),
                ))
    for path in excluded:
        print(f"excluded from retrieval: {path}")

    # Staged docs go through the same front-matter parse and chunker as a git file, so what
    # gets indexed is exactly what a committed file with this content would produce.
    for doc in staged or []:
        source_path, text = to_policy_text(doc)
        fields, body = parse_front_matter(text)
        title = next((m.group(1) for m in [re.search(r"^#\s+(.+)$", body, re.M)] if m),
                     doc.topic)
        for chunk in split(body, "policy", f"{fields['market']} > {title}"):
            natural = f"policy:{source_path}:{chunk.ordinal}"
            documents.append(Document(
                id=document_key(natural),
                corpus="policy",
                market=fields["market"],
                exposure=fields["exposure"],
                topic=fields["topic"],
                tags=[],
                title=chunk.title,
                content=chunk.content,
                source_path=source_path,
                source_version=hashlib.sha256(chunk.content.encode()).hexdigest()[:16],
                effective_date="",
            ))

    return documents


def ensure_index(index: str, clone_from: str, key: str) -> None:
    """Create a staging index as a schema clone of the live one, if it does not exist.

    The schema (vector profile, semantic configuration, analyzers) must match exactly or
    the eval gate would measure a different retrieval system than production runs.
    """
    headers = {"Content-Type": "application/json", "api-key": key}
    if request(f"{ENDPOINT}/indexes/{index}?api-version={API_VERSION}",
               headers, tolerate=(404,)) is not None:
        return

    definition = request(f"{ENDPOINT}/indexes/{clone_from}?api-version={API_VERSION}", headers)
    assert definition is not None
    definition = {k: v for k, v in definition.items() if not k.startswith("@odata")}
    definition["name"] = index
    request(f"{ENDPOINT}/indexes/{index}?api-version={API_VERSION}",
            headers, definition, method="PUT")
    print(f"created index {index} from {clone_from}'s schema")


def existing_versions(index: str, key: str) -> dict[str, str]:
    """Map id -> sourceVersion for everything already indexed, so unchanged chunks are skipped."""
    versions: dict[str, str] = {}
    skip = 0
    while True:
        page = request(
            f"{ENDPOINT}/indexes/{index}/docs/search?api-version={API_VERSION}",
            {"Content-Type": "application/json", "api-key": key},
            {"search": "*", "select": "id,sourceVersion", "top": 1000, "skip": skip})
        assert page is not None
        rows = page.get("value", [])
        versions.update({r["id"]: r.get("sourceVersion", "") for r in rows})
        if len(rows) < 1000:
            return versions
        skip += len(rows)


def embed(texts: list[str], key: str) -> list[list[float]]:
    vectors: list[list[float]] = []
    for start in range(0, len(texts), EMBED_BATCH):
        batch = texts[start:start + EMBED_BATCH]
        payload = request(
            "https://api.openai.com/v1/embeddings",
            {"Content-Type": "application/json", "Authorization": f"Bearer {key}"},
            {"model": EMBEDDING_MODEL, "input": batch, "dimensions": EMBEDDING_DIMENSIONS})
        vectors.extend(item["embedding"] for item in payload["data"])
        print(f"  embedded {min(start + len(batch), len(texts))}/{len(texts)}")
    return vectors


def upload(documents: list[Document], vectors: list[list[float]], index: str, key: str) -> None:
    payload = [{
        "@search.action": "mergeOrUpload",
        "id": d.id, "corpus": d.corpus, "market": d.market, "exposure": d.exposure,
        "topic": d.topic, "tags": d.tags, "title": d.title, "content": d.content,
        "contentVector": vector, "sourcePath": d.source_path,
        "sourceVersion": d.source_version,
        "effectiveDate": f"{d.effective_date}T00:00:00Z" if d.effective_date else None,
    } for d, vector in zip(documents, vectors, strict=True)]

    for start in range(0, len(payload), UPLOAD_BATCH):
        request(f"{ENDPOINT}/indexes/{index}/docs/index?api-version={API_VERSION}",
                {"Content-Type": "application/json", "api-key": key},
                {"value": payload[start:start + UPLOAD_BATCH]})
        print(f"  uploaded {min(start + UPLOAD_BATCH, len(payload))}/{len(payload)}")


def delete(ids: list[str], index: str, key: str) -> None:
    for start in range(0, len(ids), UPLOAD_BATCH):
        request(f"{ENDPOINT}/indexes/{index}/docs/index?api-version={API_VERSION}",
                {"Content-Type": "application/json", "api-key": key},
                {"value": [{"@search.action": "delete", "id": i}
                           for i in ids[start:start + UPLOAD_BATCH]]})
    print(f"  deleted {len(ids)} stale chunk(s)")


def summarise(documents: list[Document]) -> None:
    by_corpus: dict[str, int] = {}
    by_market: dict[str, int] = {}
    for d in documents:
        by_corpus[d.corpus] = by_corpus.get(d.corpus, 0) + 1
        by_market[d.market] = by_market.get(d.market, 0) + 1
    print(f"chunks: {len(documents)}")
    print("  by corpus: " + ", ".join(f"{k}={v}" for k, v in sorted(by_corpus.items())))
    print("  by market: " + ", ".join(f"{k}={v}" for k, v in sorted(by_market.items())))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dry-run", action="store_true",
                        help="chunk and report without embedding or writing")
    parser.add_argument("--index", default=INDEX,
                        help=f"target index (default {INDEX}); a missing target is created "
                             "as a schema clone for staging builds")
    parser.add_argument("--staged", action="append", default=[],
                        help="blob name in knowledge-drafts to merge in; repeatable. "
                             "Each staged (market, topic) supersedes its git-managed policy")
    parser.add_argument("--report", default=None,
                        help="write the validation report JSON here")
    parser.add_argument("--validate-only", action="store_true",
                        help="convert, validate and report the staged uploads, then stop "
                             "before anything touches an index — the pre-publish check")
    args = parser.parse_args()

    staged_docs = []
    if args.staged:
        import os
        connection = os.environ.get("STORAGE_CONNECTION") or secret("storage-connection")
        staged_docs = fetch_staged(args.staged, connection)

    # Content validation blocks before anything touches an index — a publish must be able
    # to show the uploader exactly why nothing happened.
    findings = [f for doc in staged_docs for f in validate_staged(doc)]
    documents = collect(staged_docs)

    if args.report:
        Path(args.report).write_text(json.dumps({
            "staged": [d.blob_name for d in staged_docs],
            "findings": [{"blobName": f.blob_name, "kind": f.kind, "message": f.message}
                         for f in findings],
            "chunks": len(documents),
            "index": args.index,
        }, indent=2, ensure_ascii=False), encoding="utf-8")
        print(f"validation report written to {args.report}")

    if findings:
        print(f"{len(findings)} validation finding(s); nothing was written:", file=sys.stderr)
        for finding in findings:
            print(f"  [{finding.kind}] {finding.blob_name}: {finding.message}",
                  file=sys.stderr)
        return 2

    if args.validate_only:
        print("validate-only: clean; nothing was written")
        return 0

    if not documents:
        print("error: no documents found under knowledge/", file=sys.stderr)
        return 1
    summarise(documents)

    if args.dry_run:
        print("dry run: nothing embedded, nothing written")
        return 0

    search_key = secret("search-adminkey")
    if args.index != INDEX:
        ensure_index(args.index, INDEX, search_key)
    indexed = existing_versions(args.index, search_key)

    changed = [d for d in documents if indexed.get(d.id) != d.source_version]
    stale = sorted(set(indexed) - {d.id for d in documents})
    print(f"already indexed: {len(indexed)}; to write: {len(changed)}; to delete: {len(stale)}")

    if changed:
        vectors = embed([d.content for d in changed], secret("openai-apikey"))
        upload(changed, vectors, args.index, search_key)
    if stale:
        delete(stale, args.index, search_key)
    if not changed and not stale:
        print("no changes — nothing embedded, nothing written")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
