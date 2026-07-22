# Gorgias AI Copilot — Implementation Plan

Companion to `gorgias-copilot-technical-reference.md` (the *what* and *why*) and
`gorgias-copilot-extension-dev-guide.md` (tooling and shipping). This document is the
*when and in what order*. Effort estimates assume 1 developer + Claude Code.

**Scope note:** this is freelance work, not a corporate production system. The plan
optimizes for speed with acceptable quality: where a lighter approach gets ~95% of the
result, take it. Security boundaries, the append-only contracts, and the "extension is a
dumb shell" rule are **not** negotiable — everything else (test breadth, infra ceremony,
resilience layers) is sized to a single-tenant pilot and can be upgraded later if the
project earns it. P2/P3 feature ideas live in the technical reference §10; they are out
of scope here.

Guiding sequencing principles:

- **De-risk external dependencies first.** The two things we don't control are the
  Gorgias REST API and Chrome Web Store review. Touch both as early as possible.
- **Panel-first, extension last.** ~90% of product work runs in a plain browser tab
  against a mock harness; the shell is ~200 lines and changes rarely.
- **Walking skeleton before features.** Get a thin end-to-end slice working early, then
  fatten it.

---

## Stage 0 — Scaffolding

Goal: a wired monorepo with just enough guardrails — no CI gold-plating.

- [x] Repo layout: `extension/`, `panel/`, `backend/`, `docs/` (as in dev guide §2).
- [x] pnpm workspace (Node 22 LTS); `panel/` and `extension/` packages with Vite.
- [x] `backend/` .NET 10 solution, projects referenced inward
      (`Copilot.Domain` ← `Copilot.Pipeline` ← `Copilot.Api`; `Copilot.Ai`,
      `Copilot.Gorgias`, `Copilot.Knowledge`, `tools/Copilot.Ingest`).
      **One** test project (`Copilot.Tests`) — split later only if it gets unwieldy.
- [x] Root `.editorconfig` + `biome.json`; nullable enable + analyzers on
      (warnings-as-errors can wait until the codebase settles).
- [x] Aspire AppHost (`apphost/`, in the solution): `dotnet run` starts API + panel
      together for local dev. Dev-time orchestration only — not deployed.
- [x] One simple GitHub Actions workflow: build + test on PR. No deploy yet.

No database in the MVP — nothing to provision locally. (PostgreSQL arrives in P2 with
the first feature that needs it; see technical reference §7.)

Exit criteria: `pnpm build` and `dotnet build && dotnet test` green locally and in CI.

## Stage 1 — Backend walking skeleton + Gorgias access spike

Goal: prove the riskiest integration (Gorgias REST API) and stand up the API surface
with stub internals.

- [x] **Spike (do first):** fetched a real ticket + messages via Postman; confirmed
      Basic auth, payload shapes (stripped_text, from_agent, public/internal-note,
      embedded integrations blob, ~14 s ticket endpoint), rate-limit headers. Domain
      types shaped from the real payload.
- [x] `Copilot.Domain`: core types — `TicketContext`, `TicketMessage`,
      `TicketCustomer`, `Draft`, `PipelineResult` (success / `InsufficientKnowledge`).
- [x] `Copilot.Gorgias`: typed REST client behind `IGorgiasCredentialProvider`
      (Basic Auth from user-secrets/Key Vault), snake_case DTOs → domain mapper,
      `Microsoft.Extensions.Http.Resilience` with widened attempt timeout (the ticket
      endpoint runs integration lookups server-side and can take ~14 s).
- [x] `Copilot.Api`: minimal API skeleton — `POST /v1/tickets/{id}/drafts` (canned
      draft built from the real server-side fetch), `GET /v1/config`,
      `POST /v1/telemetry/anchor`. Shared bearer token (constant-time compare),
      global fixed-window rate limiting, fail-fast startup validation.
- [x] API DTOs as versioned records (`v1`), append-only from day one.

Exit criteria: `curl` with the bearer token returns a canned draft built from a *real*
ticket fetched server-side from the Gorgias sandbox.

## Stage 2 — Knowledge base & retrieval

Goal: SOP/FAQ documents embedded into a knowledge file, retrieval good enough to draft
from. No database.

- [ ] Knowledge file format: versioned JSON (or similar) holding chunk text + metadata
      + embedding vectors; produced by the CLI, shipped with the container or read from
      blob storage, loaded into memory at API startup.
- [ ] `Copilot.Knowledge`: simple chunking (fixed-size with overlap, split on headings
      where easy — don't build a clever chunker), embeddings via
      `IEmbeddingGenerator` (model pinned), `IKnowledgeStore` backed by an **in-memory
      store with brute-force cosine similarity** — at pilot corpus size this is
      milliseconds; no ANN index needed. The interface is the seam: pgvector slots in
      behind it in P2 without touching the pipeline.
- [ ] `tools/Copilot.Ingest` CLI: folder of Markdown/docs → chunk → embed → write the
      knowledge file; idempotent by content hash (skip re-embedding unchanged chunks).
- [ ] Ingest the pilot company's real SOP/FAQ set (or a realistic stand-in).
- [ ] A quick retrieval sanity script: ~10 known questions → print top chunks + scores.
      Eyeball it; note the score range to set the gate threshold. (No formal test
      suite for retrieval quality — the eval harness in Stage 3 covers it.)

Exit criteria: CLI produces a knowledge file; the API loads it and queries return
relevant chunks; you know roughly where the relevance-gate threshold sits.

## Stage 3 — Drafting pipeline

Goal: the actual product logic — ticket in, grounded draft (or honest refusal) out.

**Built as "Stage 3-lite" for the demo** (drafts from ticket content alone, no RAG —
there are no SOP/FAQ documents yet). Retrieval + gate wait on Stage 2; refinement turns
and the eval harness are still open. `IKnowledgeStore` seam stays reserved so retrieval
slots in ahead of the LLM call without reworking the pipeline.

- [x] `Copilot.Ai`: provider wiring via `Microsoft.Extensions.AI` only (OpenAI),
      `IChatClient` behind `AddAi()`, model pinned to a dated snapshot in config.
      Drafting model also handles language (via the prompt) — no separate classifier.
- [~] `Copilot.Pipeline`: detect language (pinned on output) → **[deferred: retrieve →
      relevance gate]** → draft from ticket context. Internal notes excluded from the
      prompt; empty/absent customer message ⇒ typed `InsufficientKnowledge`.
- [ ] Refinement turns: `POST /v1/drafts/{draftId}/messages`, **stateless** — the panel
      sends the full conversation history with each request; nothing persisted
      server-side (no PII at rest, no retention job). *(Not started.)*
- [x] Prompt templates versioned in-repo (`DraftPrompt`). Token usage logged per request.
- [ ] **Mini eval harness:** ~10 anonymized real tickets, a script that runs the
      pipeline and dumps gate decision + draft to console/markdown for eyeball review.
      *(Not started — deferred until there's a knowledge base to evaluate against.)*
- [x] Wire the pipeline into the Stage 1 endpoints, replacing canned responses;
      `insufficient_data` returned as a first-class typed response.
- [x] Unit tests for pipeline logic (language pinning, internal-note exclusion,
      empty-reply and no-customer-message handling) — 4 tests with a fake `IChatClient`.

Exit criteria (lite): real ticket → useful draft in the customer's language, verified
live against the Time Resistance ticket. Full exit criteria (retrieval gate + refinement)
carry forward to after Stage 2.

## Stage 4 — Panel SPA

Goal: the full agent-facing UI, developed entirely in a normal browser tab.

- [ ] Mock harness page: static page that iframes the panel and postMessages fake
      `copilot:context` messages (`{v:1, ticketId, account}`), with a control to
      switch tickets — this is the daily dev environment.
- [ ] Svelte 5 app: typed state machine (`unauthenticated → idle → generating →
      drafted | insufficient_data | error`; `context_switch` → `idle`) as a plain TS
      module with exhaustive discriminated unions; components are thin views over it.
      The discriminated-union typing largely replaces the need for a state-machine
      test suite — a handful of transition tests is plenty.
- [ ] Chat UI: request draft, loading state, refinement input, draft display,
      **copy to clipboard** (`navigator.clipboard.writeText`). Functional and tidy
      beats polished — restyle later if the pilot sticks.
- [ ] The panel owns conversation state (backend is stateless): keep the draft +
      refinement history in memory and send it with each refinement request; a ticket
      switch or refresh starts fresh — that's acceptable and by design.
- [ ] `insufficient_data` rendered as a first-class state with the verbatim backend
      message — visibly distinct from errors.
- [ ] Auth: bearer token entry, kept in `sessionStorage`; postMessage handling
      origin-checked and runtime-validated (this is a security boundary — don't trim it).
- [ ] Manual visual pass at panel dimensions (~360–420 px wide).

Exit criteria: full request → draft → refine → copy loop working against the local
backend, driven from the mock harness, with no extension involved.

## Stage 5 — Extension shell

Goal: the ~200-line MV3 shell, done once, correctly.

- [ ] `manifest.json`: MV3, `storage` permission only, content script matched to
      `https://*.gorgias.com/app/*`, `run_at: document_idle`.
- [ ] Ticket detection: regex on `location.pathname` (`/app/ticket/{id}`); hook
      `pushState`/`replaceState` + `popstate`; debounced MutationObserver fallback.
- [ ] Mount: single persistent iframe (`allow="clipboard-write"`), anchor-probe list
      from `/v1/config`, floating-panel fallback; on ticket change postMessage new
      context — **never recreate the iframe**.
- [ ] Origin-pinned messaging both directions; `storage`-held dev override for
      `PANEL_ORIGIN` → `http://localhost:5173`.
- [ ] Dock/floating telemetry to `/v1/telemetry/anchor`.
- [ ] **Manual test checklist** (in-repo markdown) instead of a Playwright rig:
      ticket-ID detection across SPA navigations, mount/unmount, floating fallback,
      clipboard write, ticket switch keeps session. Run it before each of the rare
      extension releases. (Automate with Playwright later only if shell churn ever
      makes the manual run tedious — by design it shouldn't.)

Exit criteria: load-unpacked extension on the Gorgias sandbox shows the local panel,
survives ticket navigation, docks (or falls back to floating), checklist passes.

## Stage 6 — Deployment

Goal: everything running in Azure inside the cost budget; security essentials verified.

- [ ] Provision via Azure Portal/CLI, and capture the steps in a short
      `docs/azure-setup.md` as you go — no Bicep/IaC for a handful of resources:
      Container Apps (consumption, scale-to-zero — the backend is stateless so this is
      free to enable), Static Web Apps (free), Key Vault + Managed Identity,
      Application Insights (sampled). Optionally a blob container for the knowledge
      file if it shouldn't ship inside the container image. **No database.**
- [ ] CI/CD: panel → Static Web Apps on merge; backend container → Container Apps on
      merge; extension zip built on manual trigger.
- [ ] Security essentials (non-negotiable, and quick): SWA serves
      `Content-Security-Policy: frame-ancestors https://*.gorgias.com` + `nosniff`
      (verify present in deployed env); API CORS = exactly the panel origin; secrets
      only in Key Vault — grep the extension and SPA bundles to confirm none leaked.
- [ ] Confirm LLM provider DPA / no-training / zero-retention terms (launch gate —
      it's a reading task, not an engineering task, but it blocks launch).
- [ ] Smoke test the deployed stack end to end with the load-unpacked extension.

Exit criteria: production URL serves the panel; deployed API drafts from a sandbox
ticket; the security essentials above verified.

## Stage 7 — Pilot release (MVP done)

- [ ] Chrome Web Store developer account; upload zip, visibility **Unlisted**; submit
      (review typically ~1–2 days — submit as early as the shell is stable, iterate on
      panel/backend while waiting; they deploy without store involvement).
- [ ] Install for pilot agents: direct unlisted link is fine for a small team;
      `ExtensionInstallForcelist` via Google Admin/GPO only if the client already has
      managed Chrome (don't set up device management just for this).
- [ ] Onboard pilot team: bearer token, a half-page agent guide (request → refine → copy).
- [ ] Monitor with a few basic App Insights queries: drafts/day, gate rate, latency,
      token spend. A saved-queries file beats building dashboards now.
- [ ] Tuning loop: retrieval threshold, prompts, chunking — driven by the mini eval
      harness + real pilot feedback. Expect this to be most of the post-launch effort.

**MVP complete.** Total ≈ 41–68 h. Fixed infrastructure cost ≈ $1–6/month (LLM tokens
are the only real variable).

---

## Deliberate simplifications (and their escape hatches)

Recorded so future-you knows these were choices, not oversights:

| Simplification | Upgrade path if needed |
|---|---|
| No database: in-memory knowledge file, stateless backend, client-held conversations | PostgreSQL + pgvector in P2 behind `IKnowledgeStore` + a conversation-store seam |
| Vector-only brute-force retrieval, simple chunking | pgvector HNSW, then `tsvector` + RRF if quality demands |
| One LLM provider, no fallback | Add fallback provider behind existing `IChatClient` config |
| Drafting model doubles as classifier | Wire a small classifier model in `Copilot.Ai` config |
| Shared bearer token | OIDC/PKCE (planned in technical reference P2) |
| Token logging, no per-tenant quotas | Add metering when there's a second tenant |
| Manual shell test checklist | Playwright + `--load-extension` rig |
| Portal/CLI provisioning + setup doc | Bicep, if the infra ever needs reproducing |
| Request/response only, no SSE | Technical reference P2 |

## Cross-cutting rules (every stage)

- Contracts (shell↔panel, API DTOs) append-only from Stage 1 onward.
- Prompt/model changes go through the mini eval harness — no ad-hoc swaps.
- Never develop or test against live customer tickets.
- Update the technical reference when a recorded decision changes; update this plan
  when sequencing changes.
