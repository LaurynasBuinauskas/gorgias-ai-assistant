"""How much of the ticket population can be assigned a market from metadata alone?

The plan treats market resolution as a blocking client question. This tests whether the answer
is already present in the data, and — more importantly — what fraction of tickets each signal
actually covers, since a signal that is precise but present on 10 % of tickets does not solve
the problem.

Signals, in the order they would be trusted:

1. `customer.integrations.<id>.orders[].order_status_url` — the storefront that took the
   order. Most precise: it is the shop whose terms apply.
2. `messages[].meta.current_page` — the page the customer was on. Present for chat.
3. `messages[].source.to[].address` — the support inbox written to. Coarser: most mail lands
   on a shared `.com` inbox, and some local parts are language-specific rather than
   market-specific (`kundenservice@timeresistance.com` is the German queue on a US domain).

**Privacy:** prints markets and counts only. No customer address, name or message text.
Strictly read-only.

    python tools/ingest/research_market_coverage.py [sample-size]
"""

from __future__ import annotations

import base64
import collections
import json
import re
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request

MIN_INTERVAL_SECONDS = 0.55
_last_request = 0.0

# Longest first: "eu.timeresistance.com" must win over "timeresistance.com".
STOREFRONTS: dict[str, str] = {
    "eu.timeresistance.com": "EU", "global.timeresistance.com": "GLOBAL",
    "ca.timeresistance.com": "CA", "au.timeresistance.com": "AU_NZ",
    "timeresistance.co.uk": "UK", "timeresistance.de": "DE", "timeresistance.fr": "FR",
    "timeresistance.es": "ES", "timeresistance.it": "IT", "timeresistance.nl": "NL",
    "timeresistance.pl": "PL", "timeresistance.se": "SE", "timeresistance.sg": "SG",
    "timeresistance.com": "US",
}

# Local parts that name a language queue rather than a storefront. These sit on the shared
# .com domain, so reading the domain alone would label every one of them US.
LANGUAGE_INBOXES = {
    "kundenservice": "DE", "magazin": "DE",
    "serviceclient": "FR", "bonjour": "FR",
    "servicioalcliente": "ES", "hola": "ES",
    "servizioclienti": "IT", "ciao": "IT",
    "klantenservice": "NL",
}


def market_of(text: str) -> str | None:
    for domain, market in STOREFRONTS.items():
        if domain in text:
            return market
    return None


def az(args: list[str]) -> str:
    return subprocess.run([shutil.which("az"), *args],
                          capture_output=True, text=True).stdout.strip()


def app_setting(name: str) -> str:
    return az(["webapp", "config", "appsettings", "list", "--name", "gorgias-assistant-api",
               "--resource-group", "gorgias-assistant-rg",
               "--query", f"[?name=='{name}'].value", "-o", "tsv"])


SUBDOMAIN = app_setting("Gorgias__Subdomain")
AUTH = "Basic " + base64.b64encode(
    f"{app_setting('Gorgias__Email')}:"
    f"{az(['keyvault', 'secret', 'show', '--vault-name', 'gorgias-assistant-kv', '--name', 'gorgias-apikey', '--query', 'value', '-o', 'tsv'])}"
    .encode()).decode()


def get(path: str) -> tuple[int, object]:
    global _last_request
    for attempt in range(5):
        wait = MIN_INTERVAL_SECONDS - (time.monotonic() - _last_request)
        if wait > 0:
            time.sleep(wait)
        request = urllib.request.Request(f"https://{SUBDOMAIN}.gorgias.com/api/{path}")
        request.add_header("User-Agent", "gorgias-ai-assistant-devtool/1.0")
        request.add_header("Authorization", AUTH)
        try:
            with urllib.request.urlopen(request, timeout=180) as response:
                _last_request = time.monotonic()
                return response.status, json.loads(response.read())
        except urllib.error.HTTPError as error:
            _last_request = time.monotonic()
            if error.code in (429, 500, 502, 503, 504):
                time.sleep(float(error.headers.get("Retry-After") or 2 ** attempt))
                continue
            return error.code, None
    return 429, None


def from_orders(ticket: dict) -> str | None:
    for holder in ("customer", "requester"):
        for integration in ((ticket.get(holder) or {}).get("integrations") or {}).values():
            if not isinstance(integration, dict):
                continue
            for order in integration.get("orders") or []:
                if not isinstance(order, dict):
                    continue
                for field in ("order_status_url", "referring_site"):
                    if (market := market_of(str(order.get(field) or ""))):
                        return market
    return None


def from_current_page(ticket: dict) -> str | None:
    for message in ticket.get("messages") or []:
        page = ((message.get("meta") or {}).get("current_page")) or ""
        if (market := market_of(str(page))):
            return market
    return None


def from_inbox(ticket: dict) -> str | None:
    for message in ticket.get("messages") or []:
        if message.get("from_agent"):
            continue
        for recipient in (message.get("source") or {}).get("to") or []:
            address = (recipient.get("address") or "").lower()
            if "timeresistance" not in address:
                continue
            local = address.split("@")[0]
            if local in LANGUAGE_INBOXES:
                return LANGUAGE_INBOXES[local]
            if (market := market_of(address)):
                return market
    return None


def main() -> int:
    sample_size = int(sys.argv[1]) if len(sys.argv) > 1 else 80
    print(f"sampling {sample_size} tickets — markets and counts only, read-only\n")

    listing: list[dict] = []
    cursor = None
    while len(listing) < sample_size:
        path = "tickets?limit=100&order_by=created_datetime:desc"
        if cursor:
            path += f"&cursor={cursor}"
        status, body = get(path)
        if status != 200 or not isinstance(body, dict):
            break
        listing.extend(body.get("data", []))
        cursor = (body.get("meta") or {}).get("next_cursor")
        if not cursor:
            break

    signals = ["orders", "current_page", "inbox"]
    hits = collections.Counter()
    resolved_by = collections.Counter()
    markets = collections.Counter()
    disagreements = 0
    examined = 0

    for summary in listing[:sample_size]:
        status, ticket = get(f"tickets/{summary['id']}")
        if status != 200 or not isinstance(ticket, dict):
            continue
        examined += 1

        found = {
            "orders": from_orders(ticket),
            "current_page": from_current_page(ticket),
            "inbox": from_inbox(ticket),
        }
        for name in signals:
            if found[name]:
                hits[name] += 1

        distinct = {value for value in found.values() if value}
        if len(distinct) > 1:
            disagreements += 1

        # First match wins, in trust order.
        for name in signals:
            if found[name]:
                resolved_by[name] += 1
                markets[found[name]] += 1
                break
        else:
            resolved_by["unresolved"] += 1

    print(f"== tickets examined: {examined} ==\n")
    print("== coverage per signal (how often it is present at all) ==")
    for name in signals:
        print(f"  {name:<14} {hits[name]:>4}  ({hits[name] / examined:.0%})")

    print("\n== resolved by, first match wins ==")
    for name in [*signals, "unresolved"]:
        count = resolved_by[name]
        print(f"  {name:<14} {count:>4}  ({count / examined:.0%})")

    covered = examined - resolved_by["unresolved"]
    print(f"\n  total resolved  {covered:>4}  ({covered / examined:.0%})")
    print(f"  signals disagreeing on a ticket: {disagreements}")

    print("\n== market distribution of resolved tickets ==")
    for market, count in markets.most_common():
        print(f"  {market:<8} {count:>4}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
