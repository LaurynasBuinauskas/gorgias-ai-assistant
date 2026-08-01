# Gorgias extraction — measured findings

**Date:** 2026-08-01 · **Account:** `timeresistance.gorgias.com` · **Method:** strictly
read-only, every call a `GET`. Nothing created, mutated or deleted; the jobs API was probed
for existence only and no job was submitted.

Reproduce with `python tools/ingest/research_extraction.py 12`.

---

## The headline

**20,042 closed tickets in the last 12 months, of which roughly 11,700 are usable.** A full
backfill takes about **3 hours**, not an overnight run — the assumption this task was created
to work around does not hold on this account.

**"Closed" is not "resolved".** Gorgias has no status distinguishing the two, and a support
inbox closes far more than support conversations: carrier notifications, review requests,
marketing sends and vendor spam all arrive as tickets and all get closed. Sampling 60 closed
tickets, only **58 % contain a real customer message followed by a public agent reply** — the
unit that becomes an exemplar. Applied to the window, that is **~11,700 usable tickets**, not
20,042.

| Question | Planned assumption | Measured |
|---|---|---|
| How many closed tickets in 12 months? | Unknown — "the week's biggest unknown" | **20,042** (of 20,482 total) |
| Cost per ticket fetch | ~14 s, blamed on the embedded integrations blob | **0.20 s median**, 0.28 s max |
| Size of the integrations blob | "a large blob" driving the 14 s | **2 characters median** — effectively empty here |
| Full backfill wall-clock | Overnight, accepted as a risk | **~3.1 hours** at the sustainable rate |
| Is a faster access path needed? | Search / views / jobs worth investigating | **No.** The plain walk is already rate-limited, not latency-limited |

The ~14 s figure from the earlier spike did not reproduce across an 8-ticket sample. Whatever
produced it — a single unusually heavy ticket, or an account whose integrations were populated
at the time — it is not the current behaviour, and the backfill estimate built on it was
pessimistic by roughly two orders of magnitude.

## What the numbers were

```
closed tickets (12 months)   20,042
total tickets (12 months)    20,482        closed rate 97.9%
pages walked                 205           100 tickets per page
time to count                203 s
oldest ticket reached        2025-08-06
```

Per-ticket cost, sampled over 8 tickets:

```
full ticket        median 0.20 s   max 0.28 s
messages only      median 0.14 s
payload            median 37,684 chars   max 122,269
integrations       median 2 chars        max 2
```

## Is a closed ticket a usable exemplar?

Measured over a 60-ticket sample, with a wider 941-ticket scan for the structural numbers.
Reproduce with `python tools/ingest/research_exchanges.py 60`.

```
closed tickets sampled                          60
with a customer message answered by an agent    35   (58 %)
median agent reply length                       359 chars
```

The waste is concentrated by source, and that is actionable:

| Arrived via | Sampled | Usable | Rate |
|---|---|---|---|
| `gorgias_chat` | 15 | 15 | **100 %** |
| `email` | 39 | 17 | **44 %** |
| `helpdesk` | 4 | 2 | 50 % |
| `instagram` | 2 | 1 | 50 % |

Chat is almost pure signal. Email is where the notifications, marketing and vendor mail live.

**A quarter of closed tickets have exactly one message** (233 of 941 scanned), which cannot be
an exchange by definition. `messages_count` is on the *listing*, so filtering those out costs
no extra request — a free 25 % reduction in tickets to fetch before any content is read.

Spam and trash are not inflating the count: **0 %** of 941 scanned closed tickets were flagged
`spam` or carried a `trashed_datetime`.

**What we still cannot tell** is whether the customer was actually satisfied. There is no
resolution field, no CSAT on the ticket object, and a ticket closed after an agent reply may
have been closed because the customer gave up. "An agent answered" is the strongest available
proxy, and it is a proxy.

## Do we get the whole conversation?

**Yes.** Across 60 sampled tickets, zero returned fewer messages than their declared
`messages_count`. The deepest threads in a 941-ticket scan were fetched directly to test the
edge:

```
ticket 274313359   declared 23   returned 23   OK
ticket 274207905   declared 22   returned 22   OK
ticket 276826100   declared 21   returned 21   OK
```

A single `GET /api/tickets/{id}` returns the complete thread, including internal notes, which
extraction then discards. No pagination is needed at the thread depths this account produces
(median 2 messages, deepest observed 23). If a far deeper thread ever appears, the
`messages_count` field is the check that would catch truncation — worth asserting in the
extraction job rather than assuming.

## What decides the schedule

**The rate limit, not latency.** The budget is ~40 requests per 20 seconds
(`x-gorgias-account-api-call-limit`), so the sustainable rate is about 1.8 requests/second. At
0.20 s per fetch, a single worker spends most of its time waiting.

That makes **bounded concurrency pointless** for this job. Eight workers measured 28
tickets/second and tripped a `429` almost immediately, truncating a walk mid-run — the first
version of this research undercounted at 439 because of exactly that. More workers do not buy
throughput when the constraint is a shared account budget; they only reach it sooner.

`20,042 ÷ 1.8/s ≈ 3.1 hours`, and that is the floor regardless of how the work is parallelised.

## Access paths

| Path | Result |
|---|---|
| `GET /api/tickets` (walk, `created_datetime:desc`) | **Recommended.** 0.14 s per page of 100, and the listing already carries `status`, `closed_datetime` and `created_datetime`, so closed-only and date-window filtering happen client-side for free |
| `GET /api/tickets/{id}` | 0.20 s median. Needed for message content — the listing does not include messages |
| `GET /api/tickets/{id}/messages` | 0.14 s median. Marginally faster, and worth using since it avoids fetching a 37 KB payload to read a few messages |
| `GET /api/views` | Exists (200). Not needed — client-side status filtering is already free |
| `GET /api/jobs` | Exists (200). **Not exercised beyond listing** — submitting a job is a write and was out of scope for read-only research |
| `GET /api/events` | Exists (200). The natural basis for the monthly incremental refresh, keyed by timestamp |
| `POST /api/search` | **Not exercised.** A POST, and unnecessary given the walk is already cheap |

## Recommended strategy

Walk `GET /api/tickets` newest-first, filter to `status == "closed"` client-side, then fetch
`GET /api/tickets/{id}/messages` per ticket. One worker, throttled to ~0.55 s between
requests, honouring `Retry-After` and retrying `429` and transient `5xx`.

Two failure modes to design for, both observed during this research:

- **`429` mid-walk.** Trips easily and, if treated as terminal, silently undercounts. The
  first run of this script reported 439 closed tickets because of it.
- **Sporadic `502`.** Appeared once on a long walk and cleared on retry. Also silently
  truncating if treated as fatal.

Resumability matters more than speed. A persisted cursor turns a 3-hour job that fails at
hour two into a 1-hour job, and both error classes above make that likely enough to plan for.

## What this means downstream

**Volume is larger than the plan assumed, time is far smaller, and the usable share is well
under the headline.** ~11,700 usable tickets at a median of 2 messages produce on the order of
**12,000–20,000 chunks** — against 400 in the index today, so roughly a thirtyfold increase
rather than the hundredfold a naive read of "20,042" would suggest.

Two consequences worth checking before ingesting:

- **Index headroom.** ~20,000 vectors at 1536 dimensions is roughly 120 MB before text. Azure
  AI Search Basic allows 2 GB per partition, and a reindex briefly holds two versions at once,
  so this fits comfortably.
- **Redaction volume.** The manual review sample stays at 50 exemplars, but it is now sampling
  from tens of thousands rather than a few thousand. The fail-closed batch check and the
  independent sweep over the live index carry proportionally more weight.
- **Fetch only what is worth fetching.** Filtering the listing to `status == "closed"` and
  `messages_count >= 2` costs nothing and removes about a quarter of the work before any
  content is read. The agent-reply test needs the messages, so the remaining ~42 % of waste
  can only be dropped after fetching.

## Anomaly worth noting

A small number of tickets carry a `created_datetime` in the future — one sampled ticket is
dated `2026-12-31` while its `closed_datetime` is `2025-12-31`. Harmless for a
backward-looking window, but any code that assumes `created <= closed` or that terminates a
walk on the first out-of-range date will behave oddly. The counting walk here terminates on
the oldest date in a page rather than the first, for that reason.
