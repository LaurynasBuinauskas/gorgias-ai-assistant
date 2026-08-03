# Ticket exemplar runbook

Operating the `tickets-v2` index: rebuilding it, removing someone from it, and deciding when
it is stale. Policy knowledge (`knowledge-v1`) is not covered here — it has no customer data
and a different lifecycle.

**Current state: exemplars are ON.** `Retrieval__TicketTopK` is `3` in App Service as of
2026-08-03, set on the user's explicit instruction. Up to three past exchanges now reach the
model on every draft.

**This went ahead of D-3.** The privacy review of 400 exchanges in
`data/exemplar-review-pack.md` is drawn and still unsigned. The four previous samples of fifty
each found a leak class no automated check could see, and the fourth found two more the first
three had missed — so the corpus is not known to be clean, and this is a decision taken with
that outstanding, not a decision that the concern was resolved.

Verify the state, in either direction, with `curl -s <api>/v1/config` and read `exemplars`.
The setting restarts App Service; the flip on 2026-08-03 took 90 seconds to become observable.

**To turn them off again** — the fastest lever, no deploy:

```bash
az webapp config appsettings set --name gorgias-assistant-api \
  --resource-group gorgias-assistant-rg --settings Retrieval__TicketTopK=0
```

Then poll `/v1/config` until `exemplars` is `false`. `KnowledgeRetriever` short-circuits on
`topK <= 0` without querying the store, so no exemplar text can reach a draft.

**`tickets-v2` replaced `tickets-v1` on 2026-08-03.** It carries an extra field,
`questionVector`, holding an embedding of the customer's question alone; the store matches the
ticket corpus against that rather than against the whole question-plus-reply document, which
measurably retrieves the right exchange more often (`tools/evals/exemplar_recall.py`).

The two versions are not interchangeable. `tickets-v1` has no `questionVector` and rejects the
query outright with *"unknown field 'questionVector' in vector field list"*, so
`Knowledge__TicketIndexName` and the deployed code move together. `tickets-v1` is still
present and still populated, which is what makes rollback that one app setting.

---

## 1. Rebuild

Run in order. Each step's output is the next step's input, and skipping the dry run has
already cost one wasted embedding bill.

```bash
python tools/ingest/extract_tickets.py --months 12
python tools/ingest/sanitize_exemplars.py --in data/exemplars.jsonl --out data/exemplars.clean.jsonl
python tools/ingest/ingest_tickets.py --in data/exemplars.clean.jsonl --dry-run
python tools/ingest/ingest_tickets.py --in data/exemplars.clean.jsonl --prune
python tools/ingest/review_sample.py --size 400
```

Ingest writes to `tickets-v2` by default; `--index` overrides it. A fresh index must exist
first — `python tools/search-index/manage.py create --version 2 --alias tickets`.

If a run dies partway, `--resume` skips what is already indexed instead of re-embedding it. A
document is only written after both its vectors exist, so anything present is complete. Do not
use it after editing the corpus: it would skip the edits.

Things that are easy to get wrong:

- **`--prune` is not optional when the corpus shrinks.** Uploads overwrite by document key, so
  an exchange withdrawn from the file stays live in the index until pruned.
- **Extraction takes hours and must not be run twice concurrently.** It has been, once: both
  jobs appended to the same file and produced 18,537 duplicate rows, which then produced a
  false alarm about corpus size. Check for a running job before starting one.
- **`sanitize_exemplars` applies the removal ledger.** Never bypass it by ingesting the raw
  extraction file directly — that is precisely how an erased ticket comes back.
- Re-running is safe. Document keys are stable, so an interrupted ingest resumes by overwriting.

## 2. Remove someone

Both cases are the same command: a reviewer flagged an exchange as identifying, or a customer
invoked their right to erasure.

```bash
python tools/ingest/remove_exemplars.py --tickets 221595229 --reason "erasure request"
```

It finds documents by filtering on `ticketId` server-side rather than reconstructing keys, so
an exchange indexed under an unexpected ordinal is still caught. It records the removal in
`knowledge/_meta/removed-tickets.json` **before** verifying, because the ledger states an
intent — never index this again — that is settled the moment the delete is issued. It then
polls for up to 60 seconds: the index is eventually consistent, and an immediate check once
reported 17 of 29 documents surviving a delete that had actually succeeded.

Verified end to end on 2026-08-02 against ticket 221595229 — 29 documents removed, none
remaining, rebuild exclusion confirmed by the ledger.

**Removal covers the index, not drafts already produced.** Since 2026-08-03 exemplars reach
agents, so an exchange can have influenced a reply that was already edited and sent. Nothing
recalls that, and erasure from the index does not claim to. Section 2a is how you find out
which drafts were affected — assuming the telemetry sink below exists by then.

## 2a. Which exemplars fed a given draft?

`RetrievalLog` already records this, and has all along: one line per draft carrying the draft
id, the Gorgias ticket being answered, and every retrieved chunk with its score. Ticket chunk
ids decode to `ticket:<ticketId>:<ordinal>`, so the line names the resolved tickets that fed
the reply. **No chunk text is ever logged** — ids, scores, paths and counts only, because a log
quoting a customer is a second copy of personal data in a system not designed to hold it.

What was missing was somewhere to keep it. As of 2026-08-03 App Service application logging is
on at Information, which is a stopgap: it is ephemeral and not queryable, good for watching a
problem happen and useless for answering a question about last week.

```bash
az webapp log tail --name gorgias-assistant-api --resource-group gorgias-assistant-rg
```

The durable answer is Application Insights, which `Program.cs` binds as soon as
`APPLICATIONINSIGHTS_CONNECTION_STRING` is set and ignores until then. **The resource does not
exist yet** — see `go-no-go.md`. Once it does:

```kusto
traces
| where message has "Draft <draftId>"
| where message has "ticketExemplars"
| project timestamp, message
```

Until that resource exists, there is no way to answer "which past customers' exchanges fed
this draft" for anything older than the log buffer. That is a real gap while exemplars are on.

## 2b. Sweep the whole corpus

Eval class I sweeps only the chunks its own fixtures happen to retrieve — a few dozen of
17,863. This runs the same patterns over every indexed document:

```bash
dotnet run --project backend/tools/Copilot.Evals -c Release -- --sweep-corpus --out sweep.md
```

Exits non-zero if anything matches. Run it after every rebuild.

Last run 2026-08-03 over `tickets-v2`: **17,863 documents, 9 patterns, zero findings.** The
document count matching the index count is part of the result — a sweep that silently stopped
paging would also report zero.

**Zero here does not mean the corpus is clean, and the report says so itself.** Every leak class
found so far — orphaned street names, per-recipient tracking links, inline corporate
signatures, regulated-industry disclaimers, engraved third-party names, customer-announced
address blocks — was found by a person reading exchanges and matched no pattern. This check
proves the pattern-shaped classes are at zero. D-3 is what covers the rest.

The patterns themselves are tested (`PiiSweepTests`): each is shown to bite on a planted value
and to ignore redaction placeholders. Before that, a sweep reporting zero was indistinguishable
from a sweep that could not find anything — which is exactly how a green PII class survived
four rounds of real leaks.

## 3. When is it stale?

Exemplars decay differently from policy: nothing tells you they are wrong, because they were
right when they were written. Two decay modes matter.

**Policy changed underneath them.** Measured 2026-08-02: 69 exemplars state the 30-day return
window correctly and 2 state 60 days. That ratio is fine. It stops being fine the first time a
published number changes, at which point every exemplar quoting the old one is actively wrong
teaching material. **Rebuild whenever a policy number changes** — a window, a fee, a warranty
term — and do not wait for a schedule.

**Precedent drift.** 322 exemplars offer a discount, 76 of them at 40% or more, against a
published policy that says significant discounts are not possible. Both are true in context —
the policy line is about promotional discounting, the exemplars are goodwill remedies for
faulty goods — but the corpus teaches "discounts happen" more loudly than the policy says they
do not. Eval class J (`j-precedent-pressure`) is the guard, and it is blocking.

**Otherwise: quarterly.** Support language drifts slowly, and each rebuild costs a few dollars
of embeddings and roughly an hour. There is no argument for more often.

## 4. Before turning exemplars on

In order, because each one can veto the next:

1. **Does the corpus earn its exposure?** Compare drafts with and without, using a same-config
   control run to separate exemplar effect from model nondeterminism:
   `--ticket-topk 3` against `--ticket-topk 0`. If drafts do not measurably improve, delete the
   index — the privacy exposure buys nothing.
2. **Eval class J passes** with `--ticket-topk 3`. It is the only class that can see verbatim
   reuse or precedent pressure, and it is meaningless at `0`.
3. **D-3 signed off** by someone on the client side, over 300–500 exchanges rather than 50.
   Four samples of fifty each found a new leak class no automated check could see; fifty is
   0.3% of the corpus and a class absent from it means very little.
4. **Flagged tickets removed** via §2, and the index count re-verified.

Only then set `Retrieval__TicketTopK` to 3. As with every app setting, it restarts App Service
and takes 70–90 seconds; poll `/v1/config` rather than trusting the CLI's return.

## 5. Local copies

Extraction and sanitising leave customer-derived text on whoever ran them:

| File | Keep while |
|---|---|
| `data/exemplars.jsonl` | The rebuild is in progress; delete after `clean` is verified |
| `data/exemplars.clean.jsonl` | It matches what is indexed |
| `data/exemplars.deduped.jsonl` | Redaction rules may still change and need re-applying |
| `data/exemplar-review-sample.md` | The reviewer has not finished; delete after sign-off |

All are git-ignored. None should be emailed, moved into `docs/`, or published — the review
sample especially, since its whole purpose is to concentrate the riskiest text in one file.
