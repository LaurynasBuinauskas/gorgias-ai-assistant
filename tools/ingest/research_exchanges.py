"""How many closed tickets are actually usable exemplars, and do we get the whole conversation?

`status == "closed"` is not the same as "a customer asked something and an agent answered".
A support inbox also closes carrier notifications, review requests, marketing bounces and
vendor spam. Counting those as training material would teach the assistant the house voice of
a shipping robot.

This samples closed tickets and reports two things the backfill estimate depends on:

1. **Completeness** — does a ticket fetch return every message, or a truncated set?
2. **Usable yield** — how many closed tickets contain at least one customer message followed
   by a public agent reply, which is the unit that becomes an exemplar.

Strictly read-only. Every call is a GET.

    python tools/ingest/research_exchanges.py [sample-size]
"""

from __future__ import annotations

import base64
import collections
import json
import shutil
import statistics
import subprocess
import sys
import time
import urllib.error
import urllib.request

USER_AGENT = "gorgias-ai-assistant-devtool/1.0"
MIN_INTERVAL_SECONDS = 0.55
_last_request = 0.0


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


def has_usable_exchange(messages: list[dict]) -> bool:
    """A public agent reply that follows a public customer message, ignoring internal notes."""
    seen_customer = False
    for message in messages:
        if not message.get("public"):
            continue
        if (message.get("source") or {}).get("type") == "internal-note":
            continue
        if message.get("from_agent"):
            if seen_customer:
                return True
        else:
            seen_customer = True
    return False


def main() -> int:
    sample_size = int(sys.argv[1]) if len(sys.argv) > 1 else 60
    print(f"sampling {sample_size} closed tickets from {SUBDOMAIN}.gorgias.com  READ-ONLY\n")

    closed: list[dict] = []
    cursor = None
    while len(closed) < sample_size:
        path = "tickets?limit=100&order_by=created_datetime:desc"
        if cursor:
            path += f"&cursor={cursor}"
        status, body = get(path)
        if status != 200 or not isinstance(body, dict):
            break
        rows = body.get("data", [])
        if not rows:
            break
        closed.extend(r for r in rows if r.get("status") == "closed")
        cursor = (body.get("meta") or {}).get("next_cursor")
        if not cursor:
            break
    closed = closed[:sample_size]

    truncated = 0
    usable = 0
    by_channel: collections.Counter[str] = collections.Counter()
    by_via: collections.Counter[str] = collections.Counter()
    usable_by_via: collections.Counter[str] = collections.Counter()
    message_counts: list[int] = []
    agent_reply_lengths: list[int] = []

    for ticket in closed:
        status, full = get(f"tickets/{ticket['id']}")
        if status != 200 or not isinstance(full, dict):
            continue

        messages = full.get("messages") or []
        declared = ticket.get("messages_count") or full.get("messages_count") or 0
        message_counts.append(declared)
        if declared and len(messages) < declared:
            truncated += 1

        via = (ticket.get("via") or "unknown")
        by_channel[ticket.get("channel") or "unknown"] += 1
        by_via[via] += 1

        if has_usable_exchange(messages):
            usable += 1
            usable_by_via[via] += 1
            for message in messages:
                if message.get("public") and message.get("from_agent"):
                    text = message.get("stripped_text") or message.get("body_text") or ""
                    if text:
                        agent_reply_lengths.append(len(text))

    print(f"== completeness ({len(closed)} tickets fetched) ==")
    print(f"  tickets whose fetch returned fewer messages than messages_count: {truncated}")
    print(f"  messages per ticket: median {statistics.median(message_counts):.0f}, "
          f"max {max(message_counts)}")

    print(f"\n== usable exemplars ==")
    print(f"  closed tickets sampled          {len(closed)}")
    print(f"  with a real customer->agent exchange  {usable}  ({usable / len(closed):.0%})")
    if agent_reply_lengths:
        print(f"  agent reply length: median {statistics.median(agent_reply_lengths):.0f} chars")

    print(f"\n== where tickets come from (via) ==")
    for name, count in by_via.most_common(10):
        rate = usable_by_via[name] / count if count else 0
        print(f"  {name:<24} {count:>4}  usable {usable_by_via[name]:>4} ({rate:.0%})")

    print(f"\n== channel ==")
    for name, count in by_channel.most_common(8):
        print(f"  {name:<24} {count:>4}")

    print(f"\nProjected usable exemplars from 20,042 closed tickets: "
          f"~{int(20042 * usable / len(closed)):,}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
