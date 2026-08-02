# Feedback loop proposal

**Status:** proposal for a post-beta phase. Nothing here is implemented.
**Goal:** let agents flag a bad draft, and turn those flags into fixes that stick.

Related: `L-4` in `launch-plan.md` is the *widget* — the thumbs-down button — deliberately
deferred as late-game. This document is the *system behind it*, which is the part that decides
whether the button is worth having.

---

## 1. The trap this design exists to avoid

The obvious reading of "the assistant learns from bad responses" is: collect flagged drafts,
feed them back, the model improves. **Do not build that.** Three reasons, all of which this
project has already run into somewhere else.

**A corrected draft is one agent's opinion.** Written under time pressure, unreviewed. Feeding
it into the retrieval corpus makes it authoritative-looking content that later drafts imitate.
The corpus already demonstrates how this goes: 322 past replies offer a discount, 76 of them at
40% or more, against a published policy stating that significant discounts are not possible.
Every one of those was a defensible goodwill decision in its moment and terrible teaching
material afterwards.

**It closes a loop with no damping.** Bad drafts produce flags, flags become corpus, corpus
shapes drafts. Nothing in that circuit is a check. Whatever drift exists gets amplified rather
than corrected, and it will be invisible because each individual entry looks reasonable.

**Learning from past conversations has an unproven benefit and a real privacy cost.** That is
not speculation here — it is measured. The exemplar corpus (17,863 redacted exchanges) showed
**no detectable draft improvement** under two independent instruments, both with their noise
floors established. Meanwhile four rounds of human review each found a leak class no automated
check could see. A feedback corpus inherits every one of those costs and has no better evidence
behind it. See `open-questions.md` D-3 and the decisions log for 2026-08-02.

## 2. What to build instead

**A flag becomes a test, not training data.**

Every defect found in this project was fixed the same way: reproduce it, write a case that
fails, fix it, keep the case. The injection vulnerability, the language failure, the market
leak, the redaction gaps — all of them. That loop demonstrably works and it has a human in it.

```
agent flags a draft
      ↓
capture enough context to diagnose it          (§3)
      ↓
triage into a cause                            (§4)
      ↓
  ┌───────────────┬──────────────┬─────────────┐
policy gap    prompt/retrieval   market      tone
  ↓               ↓                ↓           ↓
content fix    eval case +      resolver    template
(§policy mgmt) prompt change     fix        or exemplar
      ↓
the eval case stays forever, so the fix cannot silently regress
```

The assistant "learns" in the sense that matters: the same mistake stops happening, and there
is a permanent artefact proving it.

## 3. What a flag must capture

A flag without context is a complaint. To tell *why* a draft was bad, capture with it:

| Field | Why it is load-bearing |
|---|---|
| Draft text and ticket ID | The thing being judged |
| **Retrieved chunks with scores** | Distinguishes "policy was missing" from "policy was there and ignored" — completely different fixes, indistinguishable from the draft alone |
| Resolved market and the signal used | Market is a correctness boundary; a wrong market reads perfectly fluently |
| Prompt version and model snapshot | A regression is meaningless without knowing what changed |
| Whether reranking was available | Degraded retrieval produces different failures |
| **What it should have said** | The single most valuable field. Turns one complaint into a test case |
| Flag category | §4 |

Without the retrieval context, triage becomes guesswork and the loop stalls at "the draft was
bad."

## 4. Triage categories

They exist because each routes to a different fix. Ask the agent for a category, but treat it
as a hint — the retrieval context decides the real cause.

| Category | Real cause | Fix lands in |
|---|---|---|
| Made something up | Fabrication | Eval class E + prompt |
| Contradicted our policy | Retrieved correctly, ignored | Prompt, or chunking |
| Didn't know something it should | Policy gap or retrieval miss | Policy content, or `TopK`/chunking |
| Wrong for this market | Resolver or filter | `StorefrontMarketResolver`, eval class C |
| Wrong tone or phrasing | Style | Templates; exemplars if ever justified |
| Said something internal | Leakage | Eval class A — blocking, treat as an incident |
| Declined when it shouldn't have | Gate too tight, or genuine gap | Relevance gate, eval class D |
| Correct but useless | Product problem, not a bug | Backlog, not the eval suite |

## 5. What this costs

**It needs a database.** The backend is deliberately stateless with no store; flags are the
first feature that genuinely requires one. That is the PostgreSQL step already anticipated for
P2 in the technical reference — this is its justifying use case.

**It stores customer data.** A flagged draft embeds ticket content, so this inherits the entire
redaction, review and erasure apparatus built for exemplars — including the removal ledger and
a `remove_exemplars`-equivalent for flags. Budget for that; it was not cheap the first time.

**It needs someone to read them.** A feedback button nobody triages is worse than an email
address someone answers, because it manufactures the appearance of a loop. If no one owns
triage, do not ship the button.

## 6. Measure the right thing

- **Flag rate per 100 drafts**, trended. The absolute number is meaningless; the direction is
  the health signal.
- **Time from flag to eval case.** If this grows without bound, the loop is decorative.
- **Regression count** — cases that once failed and now pass. The actual evidence of learning.

Do **not** measure thumbs-up rate. Agents rate generously and inconsistently, and it will be
read as quality when it measures politeness.

## 7. Open questions

1. **Who triages, and how often?** Determines whether any of this is real. If the answer is
   "nobody, weekly at best", ship the email route from `beta-terms.md` and stop there.
2. **Does a flag capture the ticket, or a reference to it?** Storing a reference keeps
   customer data out of our database and makes erasure trivial, at the cost of the ticket
   being mutable and possibly deleted before triage. Storing content is diagnosable but
   inherits the full privacy apparatus. **Prefer the reference** unless triage proves it
   unworkable.
3. **Can agents see their own flags resolved?** Cheap to skip, and the fastest way to kill
   participation if skipped.
4. **Retention on flags** — the client chose no retention window for exemplars. That choice
   should be revisited here rather than inherited by default.

## 8. Staging

- **Stage 1 — email.** Already in `beta-terms.md`. Zero build, tests whether anyone reports
  anything at all. Answer question 1 before building anything.
- **Stage 2 — flag button + capture.** Widget (`L-4`), storage, retrieval context. No learning
  loop yet; just structured collection.
- **Stage 3 — triage workflow.** Categories, ownership, flag → eval case with the case
  generated from the captured context rather than hand-written.
- **Stage 4 — anything automated.** Only if stages 1–3 produce enough volume that manual
  triage is the bottleneck, and only with the same review gate the exemplar corpus needed.
  **Reaching stage 4 is not the goal.** Stage 3 is where the value is.
