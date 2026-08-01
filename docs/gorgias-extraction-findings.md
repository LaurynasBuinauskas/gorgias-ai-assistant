# Gorgias extraction — measured findings

**Date:** 2026-08-01 · **Account:** `timeresistance.gorgias.com` · **Method:** strictly
read-only, every call a `GET`. Nothing created, mutated or deleted; the jobs API was probed
for existence only and no job was submitted.

Reproduce with `python tools/ingest/research_extraction.py 12`.

---

## The headline

**20,042 closed tickets in the last 12 months.** A full backfill takes about **3 hours**, not
an overnight run — and the reason is that the assumption this task was created to work around
does not hold on this account.

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

**Volume is larger than the plan assumed, and time is far smaller.** At roughly two
question-and-answer exchanges per ticket, 20,042 closed tickets produce on the order of
**40,000 chunks** — against 400 in the index today, so about a hundredfold increase.

Two consequences worth checking before ingesting:

- **Index headroom.** 40,000 vectors at 1536 dimensions is roughly 250 MB before text. Azure
  AI Search Basic allows 2 GB per partition, and a reindex briefly holds two versions at once,
  so it fits — but with less margin than the current corpus suggests.
- **Redaction volume.** The manual review sample stays at 50 exemplars, but it is now sampling
  from 40,000 rather than a few thousand. The fail-closed batch check and the independent
  sweep over the live index carry proportionally more weight.

## Anomaly worth noting

A small number of tickets carry a `created_datetime` in the future — one sampled ticket is
dated `2026-12-31` while its `closed_datetime` is `2025-12-31`. Harmless for a
backward-looking window, but any code that assumes `created <= closed` or that terminates a
walk on the first out-of-range date will behave oddly. The counting walk here terminates on
the oldest date in a page rather than the first, for that reason.
