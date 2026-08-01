"""Extract closed-ticket exchanges from Gorgias, redacted, ready to embed.

Writes JSONL to disk. **It does not index anything** — uploading is a separate, deliberate
act that should only happen once the independent PII sweep exists to verify the output.

What an exemplar is: one customer message paired with the agent reply that answered it. Not a
whole ticket — a sixteen-message thread is several questions each with an answer, and indexing
it whole would retrieve a wall of text with the useful sentence buried inside.

Measured behaviour this relies on (see `docs/gorgias-extraction-findings.md`):

* ~20,000 closed tickets in twelve months, of which ~58 % contain a real exchange.
* The listing carries `status` and `messages_count`, so a quarter of the work can be dropped
  before fetching anything.
* The binding constraint is ~40 requests per 20 seconds, not latency, so one throttled worker
  is as fast as eight and does not trip a 429 mid-run.

Run from the repository root:

    python tools/ingest/extract_tickets.py --months 12 --out data/exemplars.jsonl
    python tools/ingest/extract_tickets.py --months 12 --limit 50 --dry-run
"""

from __future__ import annotations

import argparse
import base64
import json
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request
from dataclasses import asdict, dataclass
from datetime import datetime, timedelta, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from redaction import redact, residual_identifiers  # noqa: E402

USER_AGENT = "gorgias-ai-assistant-devtool/1.0"
MIN_INTERVAL_SECONDS = 0.55
PAGE_SIZE = 100
_last_request = 0.0

PLACEHOLDER_TOKENS = [
    "[CUSTOMER]", "[AGENT]", "[EMAIL]", "[PHONE]", "[ORDER]", "[TRACKING]",
    "[ADDRESS]", "[POSTCODE]", "[IBAN]", "[CARD]", "[SIGNATURE]",
]


@dataclass(frozen=True)
class Exchange:
    ticket_id: int
    ordinal: int
    question: str
    answer: str
    closed_at: str | None
    channel: str | None


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


def get(path: str) -> tuple[int, object]:
    """Throttled GET with retry. A 429 or 5xx treated as fatal silently truncates the run."""
    global _last_request
    for attempt in range(6):
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
        except Exception as error:  # noqa: BLE001
            _last_request = time.monotonic()
            time.sleep(2 ** attempt)
            if attempt == 5:
                return -1, repr(error)[:160]
    return 429, "gave up after retries"


def is_public_reply(message: dict) -> bool:
    """Internal notes are agent-to-agent chatter, not an example of customer communication."""
    return bool(message.get("public")) and \
        (message.get("source") or {}).get("type") != "internal-note"


def message_text(message: dict) -> str:
    """`stripped_text` drops the quoted thread; `body_text` repeats it in every message."""
    return (message.get("stripped_text") or "").strip()


def exchanges_from(ticket: dict) -> list[Exchange]:
    """Pair each customer message with the agent reply that answered it."""
    messages = [m for m in (ticket.get("messages") or []) if is_public_reply(m)]

    names: list[tuple[str, str]] = []
    customer = ticket.get("customer") or {}
    if customer.get("name"):
        names.append((customer["name"], "[CUSTOMER]"))
    for message in messages:
        sender = (message.get("sender") or {}).get("name")
        if sender:
            names.append((sender, "[AGENT]" if message.get("from_agent") else "[CUSTOMER]"))

    found: list[Exchange] = []
    pending: str | None = None
    for message in messages:
        text = message_text(message)
        if not text:
            continue
        if message.get("from_agent"):
            if pending:
                found.append(Exchange(
                    ticket_id=ticket["id"],
                    ordinal=len(found),
                    question=redact(pending, names),
                    answer=redact(text, names),
                    closed_at=ticket.get("closed_datetime"),
                    channel=ticket.get("channel"),
                ))
                pending = None
        else:
            # Consecutive customer messages: keep the latest, which is what the agent answered.
            pending = text
    return found


def walk_closed(months: int, limit: int | None) -> list[dict]:
    """Candidate tickets: closed, in window, and with enough messages to hold an exchange."""
    boundary = datetime.now(timezone.utc) - timedelta(days=30 * months)
    cursor, candidates, pages = None, [], 0

    while True:
        path = f"tickets?limit={PAGE_SIZE}&order_by=created_datetime:desc"
        if cursor:
            path += f"&cursor={cursor}"
        status, body = get(path)
        if status != 200 or not isinstance(body, dict):
            print(f"  listing stopped: HTTP {status}", file=sys.stderr)
            break

        rows = body.get("data", [])
        if not rows:
            break
        pages += 1

        oldest = None
        for row in rows:
            created = row.get("created_datetime")
            if not created:
                continue
            when = datetime.fromisoformat(created.replace("Z", "+00:00"))
            oldest = when if oldest is None else min(oldest, when)
            if when < boundary or row.get("status") != "closed":
                continue
            # Free filter from the listing: one message cannot be an exchange.
            if (row.get("messages_count") or 0) < 2:
                continue
            candidates.append(row)

        if limit and len(candidates) >= limit:
            return candidates[:limit]
        if oldest is not None and oldest < boundary:
            break
        cursor = (body.get("meta") or {}).get("next_cursor")
        if not cursor:
            break

    print(f"  {len(candidates):,} candidate ticket(s) from {pages} page(s)")
    return candidates


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--months", type=int, default=12)
    parser.add_argument("--limit", type=int, help="stop after this many candidate tickets")
    parser.add_argument("--out", default="data/exemplars.jsonl")
    parser.add_argument("--dry-run", action="store_true", help="extract but write nothing")
    parser.add_argument("--resume", action="store_true",
                        help="skip tickets already present in the output file")
    args = parser.parse_args()

    output = Path(args.out)
    done: set[int] = set()
    if args.resume and output.exists():
        done = {json.loads(line)["ticket_id"] for line in output.read_text(encoding="utf-8").splitlines() if line}
        print(f"resuming: {len(done):,} ticket(s) already extracted")

    print(f"walking closed tickets, last {args.months} month(s)")
    candidates = [t for t in walk_closed(args.months, args.limit) if t["id"] not in done]

    exchanges: list[Exchange] = []
    no_exchange = 0
    blocked: list[tuple[int, list]] = []

    output.parent.mkdir(parents=True, exist_ok=True)
    handle = None if args.dry_run else output.open("a", encoding="utf-8")

    try:
        for index, candidate in enumerate(candidates, start=1):
            status, ticket = get(f"tickets/{candidate['id']}")
            if status != 200 or not isinstance(ticket, dict):
                continue

            declared = candidate.get("messages_count") or 0
            if declared and len(ticket.get("messages") or []) < declared:
                print(f"  warning: ticket {candidate['id']} returned "
                      f"{len(ticket.get('messages') or [])} of {declared} messages",
                      file=sys.stderr)

            found = exchanges_from(ticket)
            if not found:
                no_exchange += 1
                continue

            # Fail closed, per exchange: anything still matching an identifier pattern after
            # redaction is withheld rather than written. Redaction is the only control between
            # customer data and the index, so "probably fine" is not a state this can be in.
            for exchange in found:
                residual = residual_identifiers(f"{exchange.question}\n{exchange.answer}")
                if residual:
                    blocked.append((exchange.ticket_id, [(f.kind, f.value) for f in residual]))
                    continue
                exchanges.append(exchange)
                if handle:
                    handle.write(json.dumps(asdict(exchange), ensure_ascii=False) + "\n")

            if index % 100 == 0:
                print(f"  {index:,}/{len(candidates):,} tickets, "
                      f"{len(exchanges):,} exchanges, {len(blocked)} blocked")
    finally:
        if handle:
            handle.close()

    print(f"\ntickets examined        {len(candidates):,}")
    print(f"  no usable exchange    {no_exchange:,}")
    print(f"exchanges extracted     {len(exchanges):,}")
    print(f"exchanges withheld      {len(blocked):,}  (failed the fail-closed check)")

    # Placeholder counts, never the text. Enough to see redaction actually firing without
    # printing a single customer's data to a terminal or a log.
    tally: dict[str, int] = {}
    characters = 0
    for exchange in exchanges:
        combined = f"{exchange.question}\n{exchange.answer}"
        characters += len(combined)
        for token in PLACEHOLDER_TOKENS:
            count = combined.count(token)
            if count:
                tally[token] = tally.get(token, 0) + count

    print(f"\nredactions applied ({characters:,} characters of exemplar text)")
    for token, count in sorted(tally.items(), key=lambda pair: -pair[1]):
        print(f"  {token:<13} {count:,}")
    if not tally:
        print("  none — suspicious for real ticket text; check the redaction rules")

    for ticket_id, findings in blocked[:10]:
        print(f"  ticket {ticket_id}: {findings}")

    if not args.dry_run:
        print(f"\nwritten to {output}")
        print("Nothing has been indexed. Uploading is a separate, deliberate step.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
