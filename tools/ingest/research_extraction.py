"""Measure the cheapest way to extract twelve months of closed tickets from Gorgias.

**Strictly read-only.** Every call is a GET. Nothing is created, mutated or deleted — in
particular the jobs API is probed for existence only, never used to submit a job.

Answers two questions the plan currently guesses at: how many closed tickets exist in the
window, and which access path is fast enough to make a backfill routine rather than an event.

    python tools/ingest/research_extraction.py [months]
"""

from __future__ import annotations

import base64
import json
import shutil
import statistics
import subprocess
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime, timedelta, timezone

USER_AGENT = "gorgias-ai-assistant-devtool/1.0"
PAGE_SIZE = 100
RATE_HEADER = "x-gorgias-account-api-call-limit"


def az(args: list[str]) -> str:
    cli = shutil.which("az")
    if cli is None:
        raise SystemExit("the Azure CLI ('az') is not on PATH")
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


# The documented budget is ~40 requests per 20 seconds, so one request every 0.55s sits just
# inside it. Without this the walk trips a 429 partway through and silently undercounts.
MIN_INTERVAL_SECONDS = 0.55
_last_request = 0.0


def get(path: str, timeout: int = 180, throttle: bool = True) -> tuple[int, object, float]:
    global _last_request

    for attempt in range(5):
        if throttle:
            wait = MIN_INTERVAL_SECONDS - (time.monotonic() - _last_request)
            if wait > 0:
                time.sleep(wait)

        request = urllib.request.Request(f"https://{SUBDOMAIN}.gorgias.com/api/{path}")
        request.add_header("User-Agent", USER_AGENT)
        request.add_header("Authorization", AUTH)
        started = time.monotonic()
        try:
            with urllib.request.urlopen(request, timeout=timeout) as response:
                _last_request = time.monotonic()
                return response.status, json.loads(response.read()), time.monotonic() - started
        except urllib.error.HTTPError as error:
            _last_request = time.monotonic()
            if error.code in (429, 500, 502, 503, 504):
                # 429 is the documented budget; 5xx appear sporadically on long walks and are
                # transient. Both are worth retrying — treating a 502 as fatal silently
                # undercounts, which is exactly the failure this script exists to avoid.
                delay = float(error.headers.get("Retry-After") or (2 ** attempt))
                time.sleep(delay)
                continue
            return error.code, error.read().decode()[:160], time.monotonic() - started
        except Exception as error:  # noqa: BLE001 - a failed probe is a result, not a crash
            _last_request = time.monotonic()
            return -1, repr(error)[:160], time.monotonic() - started

    return 429, "rate limited after retries", 0.0


def count_closed(months: int) -> tuple[int, int, int]:
    """Walk newest-first until past the window. Returns (closed, total, pages)."""
    boundary = datetime.now(timezone.utc) - timedelta(days=30 * months)
    cursor, closed, total, pages = None, 0, 0, 0
    oldest_seen = None

    while True:
        path = f"tickets?limit={PAGE_SIZE}&order_by=created_datetime:desc"
        if cursor:
            path += f"&cursor={cursor}"

        status, body, _ = get(path)
        if status != 200 or not isinstance(body, dict):
            print(f"  walk stopped: HTTP {status} {str(body)[:120]}", file=sys.stderr)
            break

        rows = body.get("data", [])
        pages += 1
        if not rows:
            break

        oldest_in_page = None
        for row in rows:
            created = row.get("created_datetime")
            if not created:
                continue
            when = datetime.fromisoformat(created.replace("Z", "+00:00"))
            # A handful of tickets carry future-dated created_datetime; they are inside any
            # backward-looking window, so they count rather than terminating the walk.
            oldest_in_page = when if oldest_in_page is None else min(oldest_in_page, when)
            if oldest_seen is None or when < oldest_seen:
                oldest_seen = when
            if when < boundary:
                continue
            total += 1
            if row.get("status") == "closed":
                closed += 1

        if pages % 20 == 0:
            print(f"  ...{pages} pages, {total:,} in window, {closed:,} closed, "
                  f"back to {oldest_in_page:%Y-%m-%d}" if oldest_in_page else "")

        if oldest_in_page is not None and oldest_in_page < boundary:
            break
        cursor = (body.get("meta") or {}).get("next_cursor")
        if not cursor:
            break

    return closed, total, pages, oldest_seen


def time_paths(ticket_ids: list[int]) -> None:
    """Compare the full ticket against the messages-only endpoint, across several tickets.

    An earlier spike recorded ~14s per ticket and attributed it to Gorgias assembling the
    embedded integrations blob. That is worth checking across a sample rather than one ticket,
    since it is the number the whole backfill estimate rests on.
    """
    full, messages, sizes, blobs = [], [], [], []
    for ticket_id in ticket_ids:
        status, body, secs = get(f"tickets/{ticket_id}")
        if status != 200 or not isinstance(body, dict):
            continue
        full.append(secs)
        sizes.append(len(json.dumps(body)))
        blobs.append(len(json.dumps(body.get("integrations") or {})))
        messages.append(get(f"tickets/{ticket_id}/messages?limit=30")[2])

    if not full:
        print("  no tickets sampled")
        return

    print(f"  full ticket    n={len(full)}  median {statistics.median(full):.2f}s  "
          f"max {max(full):.2f}s")
    print(f"  messages only  n={len(messages)}  median {statistics.median(messages):.2f}s")
    print(f"  payload        median {int(statistics.median(sizes)):,} chars  "
          f"max {max(sizes):,}")
    print(f"  integrations   median {int(statistics.median(blobs)):,} chars  "
          f"max {max(blobs):,}")


def probe(label: str, path: str) -> None:
    status, body, secs = get(path, timeout=60)
    detail = ""
    if status == 200 and isinstance(body, dict):
        rows = body.get("data")
        detail = f"{len(rows)} item(s)" if isinstance(rows, list) else "object"
    elif status != 200:
        detail = str(body)[:90]
    print(f"  {label:<34} HTTP {status:<4} {secs:>5.2f}s  {detail}")


def main() -> int:
    months = int(sys.argv[1]) if len(sys.argv) > 1 else 12
    print(f"account: {SUBDOMAIN}.gorgias.com   window: last {months} months   READ-ONLY\n")

    print("== endpoint availability (GET only) ==")
    probe("GET /views", "views?limit=5")
    probe("GET /jobs", "jobs?limit=5")
    probe("GET /events", "events?limit=5")
    probe("GET /tickets (list)", "tickets?limit=5")
    print("  POST /search and POST /jobs are writes/queries — not exercised here\n")

    print("== per-ticket cost ==")
    status, body, _ = get("tickets?limit=8&order_by=created_datetime:desc")
    ids = [r["id"] for r in body.get("data", [])] if isinstance(body, dict) else []
    if ids:
        time_paths(ids)
    print()

    print(f"== counting closed tickets in the last {months} months ==")
    started = time.monotonic()
    closed, total, pages, oldest = count_closed(months)
    elapsed = time.monotonic() - started
    print(f"  {closed:,} closed of {total:,} tickets, {pages} pages, {elapsed:.1f}s")
    if oldest:
        print(f"  oldest ticket reached: {oldest:%Y-%m-%d}")
    if total:
        print(f"  closed rate: {closed / total:.1%}")
        print(f"  backfill at 0.55s/ticket: {closed * 0.55 / 3600:.1f} hours")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
