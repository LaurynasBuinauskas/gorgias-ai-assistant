# Beta launch plan

**Target:** live for the support team next week, explicitly labelled Beta.
**Status of this document:** proposal for review. No pipeline code has been written.

Companion documents: `rag-pipeline-proposal.md`, `policy-management-proposal.md`,
`policy-adherence-eval-plan.md`. Task IDs referenced here (`R-*`, `P-*`, `E-*`) are defined
at the end of those documents.

---

## 1. Where we actually are

Honest baseline, verified against the repo rather than the plan:

| Capability | State |
|---|---|
| Draft from ticket conversation, streamed, refinable | ✅ live in production |
| Extension → panel → API → Gorgias → OpenAI | ✅ live, deployed |
| Kill switch via `/v1/config` | ✅ built, never exercised |
| **Any retrieval at all** | ❌ `Copilot.Knowledge` is an empty project |
| `IKnowledgeStore`, embeddings, index | ❌ do not exist |
| Ticket ingestion | ❌ `Copilot.Ingest` is `Hello, World!` |
| Input caps / `MaxOutputTokens` | ❌ deferred (audit #2) |

So the stakeholder's first two complaints have a single root cause: **the assistant has
never seen a policy.** It drafts from the ticket thread alone. "Generic" and "doesn't
follow policy" are the expected output of that design, not a tuning problem.

The corpus we now have is substantial and, encouragingly, already well-structured:

| Source | Size | Nature |
|---|---|---|
| `tr-cs-current-policies-2026-06-22.pdf` | 181 pages, 473k chars | Generated rollup of **99 markdown files across 14 markets** |
| `CS_ Support's Templates.pdf` | 71 pages, 120k chars | **162 tagged reply templates** — the house voice |
| `CS_ Internal Policies.pdf` | 19 pages, 24k chars | Internal procedure: Asana projects, Shopify discount steps, warehouse flow |

~617k characters total, roughly 150k tokens. Far too much to put in a prompt; this is
exactly the problem retrieval exists to solve.

## 2. Two findings that shape the whole plan

**(a) Market is a correctness boundary, not a ranking preference.**
The policy corpus covers **14 markets** — US, EU, UK, DE, FR, ES, IT, NL, PL, SE, CA,
AU_NZ, SG, GLOBAL — each with its own shipping, returns, warranty and consumer-law text
(the ES material cites RGPD/AEPD specifically). Answering a German customer with US return
terms is not a slightly worse answer, it is a wrong answer with legal weight. Retrieval
must **filter** by market, never merely rank. This is the single largest new failure mode
the beta introduces.

**(b) One of the three corpora must never reach a customer.**
`CS_ Internal Policies` contains the Asana project names used to track repairs, the
step-by-step for creating Shopify discount codes, the `REPAIR1` code, and warehouse
process. It has to inform *what the agent decides* while never appearing in *what the
customer reads*. If all three corpora go into one undifferentiated index, the assistant
will eventually write "I've logged this in CS: RETURNS/REPAIRS and generated your code in
Shopify." Every chunk therefore carries an explicit `exposure` field, and customer-facing
generation filters on it.

Neither point appears in the stakeholder feedback, and both are cheap to design in now and
expensive to retrofit.

## 3. Scope

### In scope for Beta

1. **Policy grounding across all 14 markets** — retrieval over the policy corpus, filtered
   by the ticket's market, with citations back to the source document.
2. **Template grounding** — the 162 approved replies as the voice and phrasing reference.
3. **Internal policy as decision support** — retrieved, never quoted (`exposure: internal`).
4. **English-only output, unconditionally** (see §4).
5. **A relevance gate** — when policy does not cover the question, the assistant says so
   instead of improvising. This is the mechanism that converts "confidently wrong" into
   "honestly silent", and it is the most important guardrail in the beta.
6. **Closed-ticket exemplars** — the stakeholder's top priority, subject to §5.
7. **Cost and input caps** — currently missing, and retrieval makes every prompt several
   times larger. This moves from "deferred" to blocking.

### Explicitly out of scope for Beta

| Deferred | Why |
|---|---|
| Policy self-service editing | Design must not block it (`policy-management-proposal.md`), but building an editing surface in a week trades against correctness |
| Autopilot / auto-send | Human-in-the-loop is the guardrail the whole design rests on |
| OIDC per-agent identity | Still a shared token; known, documented (audit #4) |
| Docking, insert-into-composer | Needs a DevTools selector; cosmetic against this scope |
| Attachments / vision | P3 |
| Multi-language output | Client explicitly wants to translate on their own end |

## 4. English-only — reopened 2026-08-01, currently **not** being implemented

This section originally required English-only output unconditionally and removal of the
translate quick-action. **That is on hold.** The decision was reversed pending confirmation
with the client (`open-questions.md` D-5):

- `L-1` is **deferred**. Drafts remain English by default with translation available on
  request — today's behaviour, unchanged.
- The translate quick-action **stays in the panel**.
- `L-2` is reduced to adding a Beta badge and removes nothing.

Two consequences worth tracking rather than rediscovering:

- `E-4` (eval class B) was written to assert every draft returns English even when an agent
  explicitly asks otherwise. That is no longer intended behaviour, so the class needs
  rewriting or dropping **before** it is built.
- `L-6` listed "answers are English only" among the things the client agrees to before
  go-live. That line comes out unless `L-1` is restored.

## 5. Sequencing, and one honest disagreement

The stakeholder's priority is **(a) tickets first, (b) policy adherence second.** I propose
inverting the *engineering* order while still delivering both, for reasons worth stating
plainly:

- Both complaints — "generic" and "doesn't follow policy" — are fixed by **policy
  grounding**. Ticket exemplars improve *how things are phrased*; policy fixes *whether the
  answer is right*. Shipping tone improvements on top of unpolicied answers would make the
  assistant more confidently wrong.
- The policy corpus is **known, bounded, structured, and PII-free**. It can be indexed and
  evaluated within days with high confidence.
- The ticket corpus is **unbounded, unverified, and full of personal data**. We do not yet
  know the 12-month volume, and extraction runs against a 40 req/20 s budget. The client has
  accepted per-ticket fetching (§10), so this is now a matter of elapsed time rather than
  feasibility — but elapsed time on an unmeasured corpus is still the week's biggest unknown,
  and redaction quality (`R-4`) is now the only control standing between customer PII and the
  index.

**Proposed:** policy grounding is the blocking path to beta; ticket ingestion runs in
parallel and joins the beta if `R-1` and `R-4` land in time. If ticket extraction proves
slower than expected, we ship the beta with policy + templates rather than slip the date —
templates already encode a great deal of "how support actually answers", which is much of
what the stakeholder is asking tickets to provide.

This respects the intent of their priority (better, less generic answers) while refusing to
gate the launch on the riskiest unknown. If they would rather slip the date than launch
without ticket exemplars, that is a legitimate call — but it should be made explicitly.

## 6. Indicative week

Assumes one engineer plus agent execution, and that `P-1` (authoritative policy markdown)
is unblocked on day 1. Days are sequencing, not guarantees.

| Day | Policy track (blocking) | Ticket track (parallel) |
|---|---|---|
| 1 | `P-1` source, `P-2` layout, `R-2` index provisioned | `R-1` extraction spike (timeboxed to half a day) |
| 2 | `P-3` `P-4` corpora converted, `R-3` ingestion | `R-4` extraction + redaction |
| 3 | `R-5` `R-6` `R-7` retrieval + gate in the pipeline | ticket chunks indexed |
| 4 | `R-8` grounded prompt, `L-1` English-only, `R-9` caps | `E-3` market cases |
| 5 | `E-*` eval run, fix, re-run; `L-7` go/no-go | join if green |

**Day 5 is a gate, not a ceremony.** If the eval thresholds in
`policy-adherence-eval-plan.md` are not met, the beta does not go live — that is the entire
purpose of having them.

## 7. Guardrails required to ship responsibly

| Guardrail | Mechanism | Task |
|---|---|---|
| Never sends autonomously | Existing design; agent copies into Gorgias | — |
| Wrong-market answers | Hard filter on market; eval class with pass threshold 100% | `R-6`, `E-3` |
| Internal process leaking to customers | `exposure` filter; eval class, threshold 100% | `P-4`, `E-4` |
| Confident answers with no policy behind them | Relevance gate → `insufficient_data` | `R-7`, `E-5` |
| Customer text hijacking the draft | Fenced untrusted content + injection eval class | `E-6` |
| Customer PII in the search index | Redaction before embedding — **the only control**, by client decision (§10); fail-closed batch check | `R-4` |
| Runaway LLM spend on larger prompts | Input caps + `MaxOutputTokens` | `R-9` |
| Bad release | Kill switch, index alias rollback | `L-3`, `L-5` |

## 8. What "Beta" must mean to the client

Written down and agreed **before** go-live, not implied. Proposed wording for `L-6`:

- **Every draft is a suggestion.** Nothing is sent to a customer without an agent reading
  it and choosing to send it. The assistant cannot send, edit tickets, change tags, or
  alter status.
- **Coverage is partial and it will say so.** When the policy corpus does not cover a
  question, it declines rather than inventing an answer. Declining is correct behaviour,
  not a fault to report.
- **Answers are English only**, by request, regardless of the customer's language.
- **Accuracy is not guaranteed.** Agents remain responsible for what they send —
  particularly figures, dates, order details, and anything market-specific.
- **Feedback is the point.** There is an in-panel way to flag a bad draft (`L-4`); those
  flags drive the tuning loop.
- **It can be switched off instantly**, by us or by them, with no deploy (`L-3`).
- **Known limitations at launch**, listed explicitly: shared team token rather than
  individual sign-in; conversation resets on refresh or ticket switch; ticket exemplars
  present only if the parallel track lands; no attachment/image understanding.

## 9. Rollback and kill switch

Four levers, fastest first:

1. **Kill switch (seconds, no deploy).** `/v1/config` already returns `killSwitch`; the
   shell mounts nothing when true. **This has never actually been exercised** — `L-3`
   verifies it end to end before launch, because an untested kill switch is not a kill
   switch.
2. **Ungrounded fallback (one config change).** A flag that bypasses retrieval and reverts
   to today's ticket-only prompt. Degrades quality to the current known-acceptable
   behaviour without taking the tool away.
3. **Index rollback (minutes).** Indexes are versioned (`knowledge-v2`) and the API is
   configured with the concrete name, so rolling back a bad reindex is an app-setting
   change — not a re-ingest, and **not an alias swap**. Aliases were the original design
   and do not work: Azure AI Search serves them only on preview api-versions, and a query
   through one returns 404 on the stable version the app uses. See `open-questions.md` D-4
   and `rollback-runbook.md`.
4. **Full revert (one deploy).** Redeploy the previous API build; `/health` reports the
   commit so the rollback is verifiable rather than assumed.

Note the deploy pipeline itself carries a known defect — a zip deploy can leave the
previous build serving while routes throw (audit #21). The deploy gate catches it, but
**rollback by redeploy should assume a restart may be required.**

## 10. Client decisions and remaining questions

### Decided (2026-07-29)

| Question | Decision | Effect |
|---|---|---|
| ~$90/month for Azure AI Search Basic | **Approved** | `R-2` unblocked; proceed on Basic |
| Retention / erasure for indexed ticket data | **Redact personal data, retain the rest** — no retention window or erasure workflow for beta | Redaction becomes the load-bearing control, so `R-4` gains a fail-closed check and a manual sample |
| Is per-ticket fetching acceptable? | **Yes** — offline job, may run overnight | `R-1` drops from gate to timeboxed research into a faster path (`POST /api/search`, views, the jobs API, bounded concurrency) |

The retention decision is sound **on the condition that redaction holds** — if personal data
is genuinely removed, what remains is not personal data. There is no second control behind
it, which is why `R-4` now treats redaction as its acceptance criterion rather than an
implementation detail.

### Still open

1. **Where is the authoritative policy markdown?** The PDF names its source root as
   `data_reference/markets`, 99 files. We hold only the generated PDF. This blocks `P-1`
   and is **the single most valuable thing the client can hand over** — the one remaining
   item that can genuinely cost us the week.
2. **Market resolution.** Shopify order locale, customer country, or Gorgias channel?
   Feeds `R-6`, and it is the highest-consequence correctness decision in the beta.
3. **74 files were excluded** from the rollup as "policy-adjacent" — what are they, and is
   any of it in scope?

Only 1 and 2 gate the beta.

---

## Task breakdown

Independently executable. Each states its dependencies and how completion is verified.

### L-1 — Make English-only unconditional *(DEFERRED 2026-08-01 — do not implement)*
**Status:** on hold pending the client answer in `open-questions.md` D-5. Translation stays
supported; today's prompt behaviour is unchanged. The specification below is retained for
when the decision is confirmed.
**Depends on:** none.
**Do:** In `Copilot.Pipeline/DraftPrompt.cs`, replace the "English by default … unless the
agent asks" rule with an unconditional one: always English regardless of customer language;
if the agent requests another language, produce the English draft and state plainly that
translation is out of scope for this release.
**Acceptance:** a unit test asserts the system prompt contains no conditional-language
wording; an eval case (`E-8` class "language") submits a German ticket **and** an explicit
"antworte auf Deutsch" instruction, and both drafts come back in English.

### L-2 — Label the panel as Beta
**Depends on:** none. **Scope reduced 2026-08-01** — badge only.
**Do:** Add a small, permanent "Beta" badge to the panel header. Remove nothing: the
translate quick action and the `languageName`-driven action list stay exactly as they are.
**Acceptance:** existing panel tests still pass; a new test asserts the Beta badge is present
in every authenticated state.

### L-3 — Prove the kill switch works end to end
**Depends on:** none.
**Do:** Add a documented, config-driven way to set `killSwitch: true` in `/v1/config`
(app setting, no deploy). Exercise it against production: flip on, confirm the shell mounts
no panel on a real Gorgias ticket, flip off, confirm it returns. Record the exact steps in
`docs/azure-setup.md`.
**Acceptance:** the runbook exists and has been followed once, with the observed result
noted; toggling requires no code deploy.

### L-4 — In-panel feedback capture *(DEFERRED 2026-08-01)*
**Status:** postponed as late-game polish — answer quality comes first. Revisit before
go-live: §8 and `L-6` promise the client an in-panel way to report a bad draft, so either
this ships or that promise is withdrawn.
**Depends on:** none (independent of retrieval).
**Do:** Add thumbs-up / thumbs-down to each completed draft, posting to a new
`POST /v1/telemetry/draft-feedback` endpoint: `{v, ticketId, draftId, verdict, reason?}`.
Log only; no storage. Reason is a short free-text field, validated and clamped like the
anchor telemetry endpoint.
**Acceptance:** feedback appears in App Service logs with the draft id; the endpoint
rejects oversized or control-character input; no ticket content is sent in the payload.

### L-5 — Rollback runbook
**Depends on:** R-10 (index alias), L-3 (kill switch).
**Do:** Write `docs/rollback-runbook.md` covering the four levers in §9, each with the exact
command and expected observable result, including verifying `/health` after a redeploy and
the possibility that a restart is needed (audit #21).
**Acceptance:** a reviewer who has not built the system can follow it end to end; each step
names what "it worked" looks like.

### L-6 — Beta expectations document for the client
**Depends on:** L-1, L-3, L-4.
**Do:** Write `docs/beta-terms.md` from §8, in client-facing language, including the known
limitations list and how to report a bad draft.
**Acceptance:** one page; no engineering jargon; states explicitly that the assistant never
sends and that declining to answer is intended behaviour.

### L-7 — Go/no-go checklist
**Depends on:** all of `E-*`, L-1 … L-6, R-7, R-9.
**Do:** Write `docs/go-live-checklist.md`: eval thresholds met (with the report attached),
kill switch exercised, rollback rehearsed, caps in place, beta terms acknowledged by the
client, redaction sample reviewed and signed off (§10 makes it the only PII control).
Infrastructure cost was signed off on 2026-07-29 and needs re-confirmation only if the
provisioned tier changes.
**Acceptance:** every line is objectively checkable — no "looks good" items. The beta does
not ship with an unchecked line.
