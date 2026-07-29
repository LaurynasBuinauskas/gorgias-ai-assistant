# Project status & what's next — July 2026

Where the project actually stands, and everything still open, tagged by phase. Companion
to `implementation-plan.md` (which tracks the staged build) and `code-audit-2026-07.md`
(which tracks defects).

## Honest summary

**A working product is deployed and demoable end to end.** An agent opens a Gorgias
ticket, the panel mounts, and a grounded reply streams in — which they can translate,
refine by instruction, and copy into the composer. That runs on Azure today.

**But "MVP done" needs one qualification.** The MVP as originally scoped had RAG at its
centre: drafts grounded in the company's SOP/FAQ documents. What shipped is **MVP-lite** —
drafts grounded in the ticket conversation alone, because no SOP documents exist yet. The
product is real and useful; it just cannot yet cite policy, which is the thing that
separates it from a generic LLM.

So: **the demo is complete. The MVP is ~80% complete.** The missing 20% is Stage 2
(knowledge base) plus the retrieval gate, and it is blocked on documents, not on code.

## Stage completion

| Stage | Status | Notes |
|---|---|---|
| 0 — Scaffolding | ✅ Done | Monorepo, CI, Aspire AppHost |
| 1 — Backend skeleton + Gorgias spike | ✅ Done | Live ticket fetch verified |
| 2 — Knowledge base & retrieval | ❌ **Not started** | Blocked on SOP/FAQ documents |
| 3 — Drafting pipeline | 🟡 **Lite** | Drafting, language, refinement done; retrieval + gate deferred; eval harness not built |
| 4 — Panel SPA | ✅ Done, **exceeded** | Gained SSE streaming, chat UI, stage events |
| 5 — Extension shell | ✅ Done | Core loop verified live; 4 checklist items unrun |
| 6 — Deployment | ✅ Done | Provisioned, deployed, verified on Azure |
| 7 — Pilot release | ❌ Not started | No agents onboarded yet |

**Delivered beyond the original plan:** SSE streaming and refinement turns (both were P2),
stage events with a ticket header, English-first drafts with one-tap translate, the
Azure runbook, user guides, and a code audit.

**Tests:** 35 passing (14 backend, 21 frontend). CI green on every push.
**Cost:** ~$14/month infrastructure, LLM tokens on top.

---

## What's left

### 🔴 MVP — blocking a real pilot

**1. Fix the three critical audit findings** — see `code-audit-2026-07.md`.
Effort: Easy, Easy, Medium.
The cross-ticket leak (#1) can put one customer's draft text into another customer's
panel. The missing input caps (#2) leave your OpenAI balance unbounded. The rate limiter
(#3) allows an unauthenticated caller to take the service down. These are cheap to fix and
should land before anyone outside the team uses it.

**2. Confirm the LLM provider's data terms.**
Effort: Easy (a reading task, but a launch gate).
Real customers' names, addresses, and order histories flow to OpenAI today. Confirm
no-training and zero-retention before this is anything but a private demo. This is the one
item that blocks launch for legal rather than technical reasons.

**3. Stage 2 — the knowledge base.**
Effort: Hard (~half a day of code, but blocked on inputs).
Chunking, embeddings via `IEmbeddingGenerator`, the in-memory `IKnowledgeStore`, and the
`Copilot.Ingest` CLI. **Needs the client's SOP/FAQ documents** — or a realistic stand-in
set, which is enough to build and prove the mechanism while the real ones are gathered.

**4. Stage 3 — retrieval + the relevance gate.**
Effort: Medium; depends on #3.
Wire retrieval ahead of the LLM call and return `InsufficientKnowledge` when nothing
relevant is found — so the assistant declines instead of improvising. The panel already
renders that state; the backend just never emits it for this reason yet.

**5. Finish the extension manual checklist.**
Effort: Easy (~10 minutes).
Four unrun checks in `extension-manual-test-checklist.md` — most importantly ticket
switching and single-iframe reuse, which guard a core design invariant.

**6. Stage 7 — pilot release.**
Effort: Medium.
Onboard agents, decide store-listing vs. manual install, and set up a handful of App
Insights queries (drafts/day, latency, token spend).

### 🟠 MVP — strongly recommended, not blocking

**7. Mini eval harness.**
Effort: Medium.
~10 anonymized tickets run through the pipeline with drafts dumped for review. This is
what makes prompt and model changes safe rather than guesswork — worth building before the
tuning loop starts, not after.

**8. The Easy audit cleanups.**
Effort: Easy each.
Findings #7–#11, #13, #15, #18: telemetry validation, stream cancellation, removing the
production harness, SSE error paths, CORS fail-fast, dead code, CI security scanning, and
a token-reset control.

**9. Docking + insert-into-composer.**
Effort: Easy once unblocked — **needs one DevTools snippet** of the container beside the
Gorgias ticket view.
Docking makes the panel look native instead of floating over the page; insert-into-composer
removes the copy-paste step entirely. Together they are the biggest remaining
"feels like a real product" win, and both ship via `/v1/config` without an extension
release.

### 🔵 Phase 2 — after the pilot proves itself

- **OIDC / PKCE sign-in** (audit #4, Hard) — replaces the shared static token; gives
  per-agent identity, attribution, and revocation. The right fix, but a real project.
- **Prompt-injection hardening** (audit #5, Medium) — fencing and adversarial tests.
- **PostgreSQL + pgvector** — introduce when a feature needs it: durable conversations,
  feedback capture, or a brand-voice index. Slots in behind `IKnowledgeStore`.
- **Feedback capture** — pair drafts with what the agent actually sent, to measure quality.
- **Brand-voice exemplar index** — retrieve past exemplary replies to steer tone.
- **Ticket cache via Gorgias HTTP integrations** — pre-warm the slow ticket fetch.
- **Warnings-as-errors + API-layer tests** (audit #14, #19).

### ⚪ Phase 3 — later

Attachments and vision, Shopify/carrier context, groundedness checks with citations, and
Autopilot — gated behind drafting-quality metrics, never as a new system.

---

## Recommended order

1. **Audit criticals** (#1–#3) — a day's work, removes the sharpest edges.
2. **Ask the client for SOP documents.** Start this now; it gates the largest remaining
   MVP feature and has the longest lead time.
3. **Stage 2 + retrieval gate**, using a stand-in document set if the real ones are slow.
4. **Eval harness**, before tuning begins.
5. **Docking + composer insert**, once the DevTools snippet arrives.
6. **DPA confirmation and pilot onboarding.**

## The two things only you can unblock

- **SOP/FAQ documents** — gates Stage 2, the single biggest quality jump available.
- **One DevTools snippet** from a Gorgias ticket page — gates docking and
  insert-into-composer.
