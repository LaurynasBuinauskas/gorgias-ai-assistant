"""R-3 acceptance checks against the live index.

Each check states what it would mean if it failed, because "all markets present" is easy to
satisfy accidentally and the market filter is the one that carries legal weight.

Run from the repository root:  python tools/ingest/verify.py
"""

from __future__ import annotations

import json
import shutil
import subprocess
import sys
import urllib.error
import urllib.request

SERVICE = "gorgias-assistant-search"
VAULT = "gorgias-assistant-kv"
ALIAS = "knowledge"
API_VERSION = "2024-05-01-preview"  # alias resolution needs preview; see azure-setup.md
ENDPOINT = f"https://{SERVICE}.search.windows.net"

MARKETS = ["US", "EU", "UK", "DE", "FR", "ES", "IT", "NL", "PL", "SE", "CA", "AU_NZ", "SG", "GLOBAL"]


def admin_key() -> str:
    az = shutil.which("az")
    result = subprocess.run(
        [az, "keyvault", "secret", "show", "--vault-name", VAULT,
         "--name", "search-adminkey", "--query", "value", "-o", "tsv"],
        capture_output=True, text=True)
    if result.returncode != 0:
        raise SystemExit(f"could not read the search key: {result.stderr.strip()}")
    return result.stdout.strip()


def search(key: str, body: dict) -> dict:
    req = urllib.request.Request(
        f"{ENDPOINT}/indexes/{ALIAS}/docs/search?api-version={API_VERSION}",
        data=json.dumps(body).encode(), method="POST")
    req.add_header("Content-Type", "application/json")
    req.add_header("api-key", key)
    try:
        with urllib.request.urlopen(req, timeout=60) as response:
            return json.loads(response.read())
    except urllib.error.HTTPError as error:
        raise SystemExit(f"search failed: HTTP {error.code}\n"
                         f"{error.read().decode(errors='replace')[:300]}") from error


def main() -> int:
    key = admin_key()
    failures: list[str] = []

    total = search(key, {"search": "*", "count": True, "top": 0})["@odata.count"]
    print(f"documents in index: {total}")

    print("\n[1] every market present with the expected exposure")
    for market in MARKETS:
        result = search(key, {"search": "*", "filter": f"market eq '{market}'",
                              "count": True, "top": 0})
        count = result["@odata.count"]
        status = "ok" if count else "MISSING"
        print(f"    {market:<7} {count:>4} chunks   {status}")
        if not count:
            failures.append(f"market {market} has no chunks — it would be unanswerable")

    print("\n[2] corpus and exposure split")
    for corpus in ("policy", "template", "internal"):
        count = search(key, {"search": "*", "filter": f"corpus eq '{corpus}'",
                             "count": True, "top": 0})["@odata.count"]
        print(f"    {corpus:<9} {count:>4}")
        if not count:
            failures.append(f"corpus {corpus} is empty")

    internal_leak = search(key, {
        "search": "*", "filter": "corpus eq 'internal' and exposure eq 'customer'",
        "count": True, "top": 0})["@odata.count"]
    print(f"    internal chunks marked customer-facing: {internal_leak}")
    if internal_leak:
        failures.append("internal content is exposed as customer-facing — it could be quoted")

    print("\n[3] a DE-filtered query never returns another market's policy")
    result = search(key, {
        "search": "return window refund policy",
        "filter": "market eq 'DE' and corpus eq 'policy'",
        "select": "market,sourcePath", "top": 20})
    markets_returned = {hit["market"] for hit in result.get("value", [])}
    print(f"    hits: {len(result.get('value', []))}, markets returned: {markets_returned or 'none'}")
    if markets_returned - {"DE"}:
        failures.append(f"DE-filtered query returned {markets_returned} — wrong-market answer")

    print("\n[4] customer-facing retrieval excludes internal procedure")
    result = search(key, {
        "search": "repair discount code warranty",
        "filter": "exposure eq 'customer'",
        "select": "exposure,corpus", "top": 20})
    exposures = {hit["exposure"] for hit in result.get("value", [])}
    print(f"    hits: {len(result.get('value', []))}, exposures returned: {exposures or 'none'}")
    if exposures - {"customer"}:
        failures.append(f"customer-filtered query returned {exposures}")

    print(f"\nfailures: {len(failures)}")
    for failure in failures:
        print("  -", failure)
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
