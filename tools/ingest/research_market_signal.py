"""Find out, from real tickets, which signal identifies a ticket's market.

The plan lists this as the one blocking client question. It may not need to be: the answer
might already be sitting in ticket metadata. This checks the candidates in order of how
trustworthy they would be.

**Privacy:** only company-side and aggregate values are printed — the support inbox a customer
wrote *to*, integration shop domains, channel and language distributions. Customer email
addresses, names and message bodies are never printed. Strictly read-only.

    python tools/ingest/research_market_signal.py [sample-size]
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

USER_AGENT = "gorgias-ai-assistant-devtool/1.0"
MIN_INTERVAL_SECONDS = 0.55
_last_request = 0.0

# The 14 storefronts, from the policy corpus. A signal is useful if it maps onto these.
STOREFRONTS = {
    "timeresistance.com": "US", "eu.timeresistance.com": "EU",
    "global.timeresistance.com": "GLOBAL", "timeresistance.co.uk": "UK",
    "timeresistance.de": "DE", "timeresistance.fr": "FR", "timeresistance.es": "ES",
    "timeresistance.it": "IT", "timeresistance.nl": "NL", "timeresistance.pl": "PL",
    "timeresistance.se": "SE", "timeresistance.sg": "SG",
    "ca.timeresistance.com": "CA", "au.timeresistance.com": "AU_NZ",
}


def az(args: list[str]) -> str:
    cli = shutil.which("az")
    result = subprocess.run([cli, *args], capture_output=True, text=True)
    if result.returncode != 0:
        raise SystemExit(f"az failed: {result.stderr.strip()[:200]}")
    return result.stdout.strip()


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
        request.add_header("User-Agent", USER_AGENT)
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
            return error.code, error.read().decode()[:160]
    return 429, "rate limited"


def domains_in(blob: object) -> collections.Counter[str]:
    """Storefront domains appearing anywhere in a payload — cheap way to spot the signal."""
    found: collections.Counter[str] = collections.Counter()
    text = json.dumps(blob) if not isinstance(blob, str) else blob
    for domain in STOREFRONTS:
        if domain in text:
            found[domain] += text.count(domain)
    return found


def main() -> int:
    sample_size = int(sys.argv[1]) if len(sys.argv) > 1 else 40
    print(f"sampling {sample_size} tickets from {SUBDOMAIN}.gorgias.com  READ-ONLY")
    print("company-side and aggregate values only; no customer data is printed\n")

    tickets: list[dict] = []
    cursor = None
    while len(tickets) < sample_size:
        path = "tickets?limit=100&order_by=created_datetime:desc"
        if cursor:
            path += f"&cursor={cursor}"
        status, body = get(path)
        if status != 200 or not isinstance(body, dict):
            break
        rows = body.get("data", [])
        if not rows:
            break
        tickets.extend(r for r in rows if (r.get("messages_count") or 0) >= 1)
        cursor = (body.get("meta") or {}).get("next_cursor")
        if not cursor:
            break
    tickets = tickets[:sample_size]

    inbox_addresses: collections.Counter[str] = collections.Counter()
    from_addresses: collections.Counter[str] = collections.Counter()
    integration_domains: collections.Counter[str] = collections.Counter()
    payload_domains: collections.Counter[str] = collections.Counter()
    integration_types: collections.Counter[str] = collections.Counter()
    channels: collections.Counter[str] = collections.Counter()
    languages: collections.Counter[str] = collections.Counter()
    has_source_to = 0
    examined = 0

    for summary in tickets:
        status, ticket = get(f"tickets/{summary['id']}")
        if status != 200 or not isinstance(ticket, dict):
            continue
        examined += 1
        channels[ticket.get("channel") or "unknown"] += 1
        languages[ticket.get("language") or "none"] += 1

        for message in ticket.get("messages") or []:
            source = message.get("source") or {}
            if message.get("from_agent"):
                # Which inbox we replied *from* — also company-side.
                sender = (source.get("from") or {}).get("address") or ""
                if sender.endswith(("timeresistance.com", "timeresistance.de",
                                    "timeresistance.co.uk", "timeresistance.fr",
                                    "timeresistance.es", "timeresistance.it",
                                    "timeresistance.nl", "timeresistance.pl",
                                    "timeresistance.se", "timeresistance.sg")):
                    from_addresses[sender.lower()] += 1
                continue

            recipients = source.get("to") or []
            if recipients:
                has_source_to += 1
            for recipient in recipients:
                address = (recipient.get("address") or "").lower()
                # Only our own inboxes; a customer's own address is never recorded.
                if "timeresistance" in address:
                    inbox_addresses[address] += 1

        customer = ticket.get("customer") or {}
        for integration in (customer.get("integrations") or {}).values():
            if isinstance(integration, dict):
                integration_types[integration.get("__integration_type__") or "unknown"] += 1
                integration_domains.update(domains_in(integration))

        payload_domains.update(domains_in(ticket))

    print(f"== tickets examined: {examined} ==\n")

    print("== signal 2: which support inbox the customer wrote to "
          f"(source.to present on {has_source_to} inbound message(s)) ==")
    if inbox_addresses:
        for address, count in inbox_addresses.most_common(20):
            domain = address.split("@")[-1]
            market = STOREFRONTS.get(domain, "—")
            print(f"  {address:<45} {count:>4}   market {market}")
    else:
        print("  no company inbox addresses found on inbound messages")

    print("\n== which address we reply from ==")
    for address, count in from_addresses.most_common(10) or [("none found", 0)]:
        print(f"  {address:<45} {count:>4}")

    print("\n== signal 1: storefront domains inside the Shopify integration ==")
    print(f"  integration types: {dict(integration_types)}")
    if integration_domains:
        for domain, count in integration_domains.most_common(20):
            print(f"  {domain:<32} {count:>4}   market {STOREFRONTS[domain]}")
    else:
        print("  no storefront domain found in integration payloads")

    print("\n== storefront domains anywhere in the ticket payload ==")
    for domain, count in payload_domains.most_common(20) or [("none", 0)]:
        market = STOREFRONTS.get(domain, "—")
        print(f"  {domain:<32} {count:>4}   market {market}")

    print(f"\n== channel ==\n  {dict(channels)}")
    print(f"== ticket language field ==\n  {dict(languages)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
