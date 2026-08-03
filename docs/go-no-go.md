# Go/no-go checklist

Every line is a claim someone can check, not a box to tick from memory. Where a claim was
once made and later turned out to be false, the check says how to establish it rather than
who asserted it — several things on this list passed for weeks while being untrue.

Status as of **2026-08-02**. `[x]` verified, `[ ]` outstanding, `[!]` verified false or
deliberately deferred.

---

## Blocking — the beta does not start without these

### Safety of what the assistant says

- [x] **53 eval cases pass**, every release-blocking class at threshold —
      `dotnet run --project backend/tools/Copilot.Evals -- --out eval-report.md`.
      Was reported as 51/51 on 2026-08-02 from a single run; re-running found class D
      failing about three runs in four on a *correct* refusal, because its assertion was a
      vocabulary allowlist. Class D now tests whether the draft granted the demand or
      invented a specific, and passed five consecutive runs
- [x] **Injection resistance**, including instructions planted in quoted email history. Found
      as a real vulnerability at 1 failure in 6 runs; 0 in 10 after the prompt fix. **Re-run
      the class more than once** — a single green run would not have found it
- [x] **Drafts are English regardless of customer language.** Was reported fixed once while
      still failing ~8% of the time; now 0 in 36 executions
- [x] **Internal guidance is never quoted or alluded to** (class A, blocking at 100%)
- [x] **Uncovered questions decline** rather than inventing an answer, with no model call
- [x] **No market's statutory text leaks into another market's draft**

### Operational

- [x] **Kill switch works end to end**, verified rather than assumed. Takes **70–90 seconds**,
      not seconds — poll `/v1/config` for `killSwitch:true`
- [x] **Rollback runbook** exists and names the observable result of each lever —
      `docs/rollback-runbook.md`
- [x] **`/health` reports the deployed commit**, so a rollback is verifiable. Check the SHA,
      not just `"status":"healthy"` — the known deploy defect produces a healthy status on the
      wrong build
- [x] **Retrieval degradation is visible.** `/health` reports `degraded` when semantic ranking
      is unavailable, after an outage where quota exhaustion 500'd every draft silently
- [x] **Budget alert configured** — `gorgias-assistant-monthly`, $150/month, verified via
      `az consumption budget list`
- [x] **Semantic-ranking quota decided** — D-7 closed. Billing enabled *and* reranking cut to
      policy only, which is the change that mattered: one semantic query per draft rather than
      four, so the free allowance covers ~1,000 drafts a month instead of 250. Accepted risk:
      eval runs spend the same meter as production

### Client-facing

- [x] **Beta terms written** in client language — `docs/beta-terms.md`
- [ ] **Beta terms acknowledged** by the client before go-live — D-2. Not ours to tick
- [x] **Bad-draft reporting route exists** (email; the in-panel widget is deliberately deferred)

---

## Blocking *only if* ticket exemplars are switched on

**They are now ON.** `Retrieval__TicketTopK` was set to `3` on 2026-08-03 on the user's
explicit instruction, verified via `exemplars: true` in `/v1/config`. This section is therefore
live, and the first item in it is outstanding.

- [ ] **Privacy review signed off** over **300–500** exchanges, not 50 — D-3.
      **Outstanding at the time exemplars were enabled.** Recorded plainly rather than
      reworded: this was decided with the review unsigned, not because the concern was met.
      `data/exemplar-review-pack.md` is drawn and waiting. Four separate samples of fifty each
      found a leak class no automated check could see, and the fourth found two more that the
      first three had missed, both from small markets. **A green PII eval class is not
      evidence of a clean corpus** — it passed through all four rounds
- [ ] **Flagged exchanges removed** via `tools/ingest/remove_exemplars.py`, index count
      re-verified
- [x] **A telemetry sink exists.** `gorgias-assistant-insights`, workspace-backed by
      `gorgias-assistant-logs` (Sweden Central, 30-day retention, 0.5 GB/day cap), created
      2026-08-03. Verified end to end rather than assumed: request telemetry and
      **Information-level** traces both arrive and are queryable. That level matters — the
      Application Insights provider ignores `Logging:LogLevel` and defaults to Warning, which
      would have dropped every provenance line while the resource still looked healthy
- [ ] **A real draft's provenance confirmed queryable.** The plumbing is proven, but no draft
      has been generated since the sink existed, so no `ticketExemplars` line has been written
      yet. Query in `exemplar-runbook.md` §2a. Worth checking during the first agent session
- [!] **Exemplars shown to improve drafts** — **still not established.** Two instruments found
      no detectable benefit, and the blind pairwise judge produced a *larger* apparent gap
      between a configuration and itself than between exemplars on and off, so it finds
      effects that do not exist and cannot be used to certify one that does. **Switching
      exemplars on still means accepting a privacy exposure for an unproven benefit.**

      What *is* now established is narrower and does not substitute for it: retrieval returns
      the right past exchange more often than it did. Over a 1,000-exchange pool with 150
      held-out paraphrased questions, matching on the customer's question alone rather than on
      the whole exchange moved recall@3 from 91% to 97% (`tools/evals/exemplar_recall.py`).
      That is about finding the right exemplar, not about whether the resulting draft is
      better — which remains a question for a human watching real drafts
- [x] **Eval class J passes** with `--ticket-topk 3` — verbatim reuse, precedent pressure, and
      order-status copying. **At `--ticket-topk 0` the report now marks the class NOT
      EXERCISED** rather than PASS: nothing is retrieved, so its assertions hold trivially, and
      it had been rendering as a green release-blocking row in every default run
- [x] **Erasure works**, executed against a real ticket: 29 documents removed, verified gone,
      and excluded from rebuilds by the ledger

---

## Not blocking, but know before you start

- **The assistant does not learn from resolved tickets.** Whatever the corpus eventually
  proves worth, today the drafts come from published policy alone
- **Coverage is uneven by market.** Return windows, duties and warranty are identical
  everywhere; divergence is concentrated in statutory apparatus — German Widerrufsbelehrung
  and Impressum, EU international page, Spanish data-protection references
- **The relevance gate is a floor, not a filter.** Measured against real tickets it does not
  discriminate — a genuine returns question scored 2.186 while "Christmas Greetings" scored
  2.923. Set to 1.6 so it fires only when retrieval found essentially nothing; coverage rests
  on the prompt rule and eval class D
- **Conversation state is client-side.** A refresh loses the thread. Deliberate — it keeps the
  backend stateless and the MVP without a database
- **Policy content contains a pre-existing defect**: the client's PDF strips diacritics from
  every non-English market (`fur`, not `für`). Ours to flag, theirs to fix at source

## Sign-off

| | Name | Date |
|---|---|---|
| Engineering — checklist verified | | |
| Client — beta terms acknowledged (D-2) | | |
| Client — privacy review, only if exemplars are enabled (D-3) | | |
