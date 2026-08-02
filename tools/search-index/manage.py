"""Create and verify the knowledge index on Azure AI Search.

The index definition lives in `index-schema.json` so it is reviewable as code; this script
only applies it, points the `knowledge` alias at a version, and proves the alias answers a
filtered hybrid query. Reindex, alias swap and rollback are R-10 and build on this.

The admin key is never stored in the repository — it is read from Key Vault at run time via
the Azure CLI, so rotating the secret needs no change here.

Usage, from the repository root:

    python tools/search-index/manage.py create --version 1
    python tools/search-index/manage.py smoke
"""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

SERVICE = "gorgias-assistant-search"
VAULT = "gorgias-assistant-kv"
SECRET = "search-adminkey"
ALIAS = "knowledge"
# Ticket exemplars are customer-derived and kept in their own index, so that personal data is
# isolated from policy and erasure is "drop the index" rather than a filtered delete anyone has
# to trust. See KnowledgeOptions.TicketIndexName.
TICKET_ALIAS = "tickets"
API_VERSION = "2024-07-01"
# Index aliases are not exposed on any stable api-version yet (2024-07-01 and 2023-11-01
# both reject the endpoint). Alias management is therefore pinned to a preview version,
# deliberately confined to this offline tool — indexing and querying, including querying
# *through* the alias, stay on the stable contract above.
ALIAS_API_VERSION = "2024-05-01-preview"
SCHEMA = Path(__file__).with_name("index-schema.json")
VECTOR_DIMENSIONS = 1536
POLL_ATTEMPTS = 10
POLL_INTERVAL_SECONDS = 2

ENDPOINT = f"https://{SERVICE}.search.windows.net"


def admin_key() -> str:
    """Read the ingestion credential from Key Vault. Never print or persist the result."""
    # On Windows the CLI is a .cmd shim, which CreateProcess will not resolve on its own.
    az = shutil.which("az")
    if az is None:
        raise SystemExit("the Azure CLI ('az') is not on PATH")
    result = subprocess.run(
        [az, "keyvault", "secret", "show", "--vault-name", VAULT,
         "--name", SECRET, "--query", "value", "-o", "tsv"],
        capture_output=True, text=True, shell=False,
    )
    if result.returncode != 0:
        raise SystemExit(
            f"could not read {SECRET} from {VAULT}: {result.stderr.strip()}\n"
            "run 'az login' and confirm you have get permission on the vault"
        )
    return result.stdout.strip()


def request(method: str, path: str, key: str, body: dict | None = None,
            api_version: str = API_VERSION) -> dict:
    url = f"{ENDPOINT}/{path}?api-version={api_version}"
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("Content-Type", "application/json")
    req.add_header("api-key", key)
    try:
        with urllib.request.urlopen(req) as response:
            payload = response.read()
            return json.loads(payload) if payload else {}
    except urllib.error.HTTPError as error:
        detail = error.read().decode(errors="replace")
        raise SystemExit(f"{method} {path} failed: HTTP {error.code}\n{detail}") from error


def create(version: int, alias: str = ALIAS) -> int:
    """Apply the schema to <alias>-v<version> and point the alias at it."""
    key = admin_key()
    name = f"{alias}-v{version}"

    schema = json.loads(SCHEMA.read_text(encoding="utf-8").replace("{{INDEX_NAME}}", name))
    request("PUT", f"indexes/{name}", key, schema)
    print(f"index {name} created or updated ({len(schema['fields'])} fields)")

    request("PUT", f"aliases/{alias}", key, {"name": alias, "indexes": [name]},
            api_version=ALIAS_API_VERSION)
    print(f"alias {alias} -> {name}")
    return 0


def smoke() -> int:
    """Insert a probe document, prove filtered hybrid retrieval works, then remove it."""
    key = admin_key()
    # A synthetic unit vector keeps this check independent of the embedding provider:
    # querying with the same vector scores 1.0, so a miss means the vector path is broken.
    probe_vector = [0.0] * VECTOR_DIMENSIONS
    probe_vector[0] = 1.0
    probe = {
        "id": "smoke--probe",
        "corpus": "policy",
        "market": "DE",
        "exposure": "customer",
        "topic": "warranty",
        "title": "Smoke probe",
        "content": "Widerrufsbelehrung smoke probe for index verification.",
        "contentVector": probe_vector,
        "sourcePath": "smoke/probe.md",
        "sourceVersion": "smoke",
    }
    request("POST", f"indexes/{ALIAS}/docs/index", key,
            {"value": [dict(probe, **{"@search.action": "mergeOrUpload"})]},
            api_version=ALIAS_API_VERSION)
    print("probe document uploaded")

    query = {
        "search": "Widerrufsbelehrung",
        "filter": "market eq 'DE' and exposure eq 'customer'",
        "vectorQueries": [{
            "kind": "vector", "vector": probe_vector,
            "fields": "contentVector", "k": 5,
        }],
        "select": "id,market,exposure,title",
        "top": 5,
    }
    # Indexing is asynchronous: a document accepted by the index API is not immediately
    # queryable. Poll rather than assume, or this check fails intermittently — and R-10
    # gates the alias swap on it.
    hits: list[dict] = []
    found = False
    for attempt in range(POLL_ATTEMPTS):
        if attempt:
            time.sleep(POLL_INTERVAL_SECONDS)
        hits = request("POST", f"indexes/{ALIAS}/docs/search", key, query,
                       api_version=ALIAS_API_VERSION).get("value", [])
        found = any(hit["id"] == "smoke--probe" for hit in hits)
        if found:
            break
    print(f"filtered hybrid query returned {len(hits)} hit(s); probe found: {found} "
          f"(after {attempt + 1} attempt(s))")

    excluded = request("POST", f"indexes/{ALIAS}/docs/search", key,
                       dict(query, filter="market eq 'US' and exposure eq 'customer'"),
                       api_version=ALIAS_API_VERSION)
    leaked = any(hit["id"] == "smoke--probe" for hit in excluded.get("value", []))
    print(f"market filter excludes the DE probe from a US query: {not leaked}")

    request("POST", f"indexes/{ALIAS}/docs/index", key,
            {"value": [{"@search.action": "delete", "id": "smoke--probe"}]},
            api_version=ALIAS_API_VERSION)
    print("probe document removed")

    if not found or leaked:
        print("SMOKE FAILED", file=sys.stderr)
        return 1
    print("SMOKE PASSED")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    create_cmd = sub.add_parser("create", help="apply the schema and point the alias at it")
    create_cmd.add_argument("--version", type=int, default=1)
    create_cmd.add_argument("--alias", default=ALIAS,
                            choices=[ALIAS, TICKET_ALIAS],
                            help="which corpus family this index holds")
    sub.add_parser("smoke", help="verify filtered hybrid retrieval through the alias")

    args = parser.parse_args()
    return create(args.version, args.alias) if args.command == "create" else smoke()


if __name__ == "__main__":
    raise SystemExit(main())
