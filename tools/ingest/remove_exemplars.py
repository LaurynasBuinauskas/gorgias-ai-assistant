"""Remove a customer's exchanges from the ticket index, and prove they are gone.

Two jobs that are the same operation:

**Acting on a review.** A reviewer flags an exchange as identifying. Until now nothing could be
done with that flag — the review had no teeth. Pass the ticket IDs here.

**Right to erasure.** A customer asks to be forgotten. The two-index split was justified on
"provable erasure": exemplars live apart from policy, keyed by ticket, so a customer's material
can be removed without rebuilding anything. That claim had never been executed. This executes
it, and verifies afterwards rather than trusting the delete call — a delete that silently
matched nothing looks identical to one that worked.

Removal is also written to a ledger. A rebuild from the extracted file would otherwise quietly
resurrect everything ever deleted, which for an erasure request is the failure that matters.

    python tools/ingest/remove_exemplars.py --tickets 212618236,240034650
    python tools/ingest/remove_exemplars.py --tickets-file flagged.txt --reason "review sample 4"
"""

from __future__ import annotations

import argparse
import datetime
import json
import shutil
import subprocess
import time
import sys
import urllib.request
from pathlib import Path

SERVICE = "gorgias-assistant-search"
VAULT = "gorgias-assistant-kv"
INDEX = "tickets-v1"
API_VERSION = "2024-07-01"
ENDPOINT = f"https://{SERVICE}.search.windows.net"

# Every removal, so a later rebuild cannot resurrect what a customer asked us to erase.
LEDGER = Path("knowledge/_meta/removed-tickets.json")


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


def post(path: str, key: str, body: dict) -> dict:
    request = urllib.request.Request(
        f"{ENDPOINT}/{path}?api-version={API_VERSION}",
        data=json.dumps(body).encode(), method="POST")
    request.add_header("Content-Type", "application/json")
    request.add_header("api-key", key)
    with urllib.request.urlopen(request, timeout=180) as response:
        payload = response.read()
    return json.loads(payload) if payload else {}


def keys_for(ticket_ids: set[str], key: str) -> list[str]:
    """Find every indexed document belonging to these tickets.

    Filtered server-side on `ticketId` rather than reconstructed from the key format, so an
    exchange indexed under an ordinal nobody guessed is still found.
    """
    quoted = ",".join(f"'{t}'" for t in sorted(ticket_ids))
    found: list[str] = []
    skip = 0
    while True:
        page = post(f"indexes/{INDEX}/docs/search", key, {
            "search": "*",
            "filter": f"search.in(ticketId, {quoted}, ',')",
            "select": "id,ticketId",
            "top": 1000,
            "skip": skip,
        })["value"]
        if not page:
            return found
        found.extend(document["id"] for document in page)
        skip += len(page)


def record(ticket_ids: set[str], reason: str, removed: int) -> None:
    LEDGER.parent.mkdir(parents=True, exist_ok=True)
    entries = json.loads(LEDGER.read_text(encoding="utf-8")) if LEDGER.exists() else []
    entries.append({
        "removedAt": datetime.datetime.now(datetime.UTC).isoformat(timespec="seconds"),
        "reason": reason,
        "ticketIds": sorted(ticket_ids),
        "documentsRemoved": removed,
    })
    LEDGER.write_text(json.dumps(entries, indent=2) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tickets", help="comma-separated ticket IDs")
    parser.add_argument("--tickets-file", help="file with one ticket ID per line")
    parser.add_argument("--reason", default="unspecified",
                        help="why, e.g. 'erasure request' or 'review sample 4'")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    ticket_ids: set[str] = set()
    if args.tickets:
        ticket_ids.update(part.strip() for part in args.tickets.split(",") if part.strip())
    if args.tickets_file:
        ticket_ids.update(
            line.strip() for line in Path(args.tickets_file).read_text(encoding="utf-8").splitlines()
            if line.strip() and not line.startswith("#"))

    if not ticket_ids:
        print("error: pass --tickets or --tickets-file", file=sys.stderr)
        return 1

    key = secret("search-adminkey")
    keys = keys_for(ticket_ids, key)
    print(f"tickets named      {len(ticket_ids):,}")
    print(f"documents indexed  {len(keys):,}")

    if not keys:
        print("nothing to remove — these tickets are not in the index")
        return 0
    if args.dry_run:
        print("\ndry run: nothing deleted")
        return 0

    for start in range(0, len(keys), 500):
        post(f"indexes/{INDEX}/docs/index", key,
             {"value": [{"@search.action": "delete", "id": k}
                        for k in keys[start:start + 500]]})

    # Recorded before verification, not after. The ledger states an intent — "never index this
    # ticket again" — and that intent is settled the moment the delete is issued. Writing it
    # only on a clean verify lost the record of a deletion that had in fact happened, and a
    # rebuild would then have resurrected the very ticket someone asked us to erase.
    record(ticket_ids, args.reason, len(keys))
    print(f"recorded in {LEDGER}")

    # Then verify rather than trust: a delete that matched nothing returns the same 200 as one
    # that worked. Polled, because the index is eventually consistent — checking immediately
    # reported 17 of 29 documents surviving a delete that had succeeded, and a false failure
    # here is not harmless, it is the report someone would act on.
    for _ in range(12):
        remaining = keys_for(ticket_ids, key)
        if not remaining:
            print(f"\nremoved {len(keys):,} document(s); verified none remain")
            return 0
        print(f"  {len(remaining):,} still visible, waiting for the index to settle")
        time.sleep(5)

    print(f"\nFAILED: {len(remaining):,} document(s) still present after 60s. The removal is "
          f"recorded, so a rebuild will exclude them, but the live index needs checking.",
          file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
