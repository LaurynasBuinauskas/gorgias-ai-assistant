# Go/no-go checklist

Every line is a claim someone can check, not a box to tick from memory. Where a claim was
once made and later turned out to be false, the check says how to establish it rather than
who asserted it — several things on this list passed for weeks while being untrue.

Status as of **2026-08-02**. `[x]` verified, `[ ]` outstanding, `[!]` verified false or
deliberately deferred.

---

## Blocking — the beta does not start without these

### Safety of what the assistant says

- [x] **51 eval cases pass**, every release-blocking class at threshold —
      `dotnet run --project backend/tools/Copilot.Evals -- --out eval-report.md`
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
- [x] **Budget alert configured** on the subscription
- [ ] **Semantic-ranking quota decided** — `open-questions.md` D-7. Billing is enabled, so the
      outage cannot recur the same way, but the standing cost has not been agreed

### Client-facing

- [x] **Beta terms written** in client language — `docs/beta-terms.md`
- [ ] **Beta terms acknowledged** by the client before go-live — D-2. Not ours to tick
- [x] **Bad-draft reporting route exists** (email; the in-panel widget is deliberately deferred)

---

## Blocking *only if* ticket exemplars are switched on

They are currently **off** — `TicketTopK` is `0` and the retriever short-circuits without
querying. Nothing below blocks a policy-only beta.

- [ ] **Privacy review signed off** over **300–500** exchanges, not 50 — D-3.
      `data/exemplar-review-pack.md` is drawn and waiting. Four separate samples of fifty each
      found a leak class no automated check could see, and the fourth found two more that the
      first three had missed, both from small markets. **A green PII eval class is not
      evidence of a clean corpus** — it passed through all four rounds
- [ ] **Flagged exchanges removed** via `tools/ingest/remove_exemplars.py`, index count
      re-verified
- [!] **Exemplars shown to improve drafts** — **not established.** Two independent instruments
      found no detectable benefit, both with their noise floors measured: mechanical diffing,
      and blind pairwise judging where the same config against itself produced a *larger*
      apparent gap than exemplars did. This does not show exemplars are useless; it shows the
      experiment cannot see the effect. **Switching them on means accepting a privacy exposure
      for an unproven benefit** — a decision to make deliberately, not by default
- [x] **Eval class J passes** with `--ticket-topk 3`, 16/16 across eight runs — verbatim reuse
      and precedent pressure
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
