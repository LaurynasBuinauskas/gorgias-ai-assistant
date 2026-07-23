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
- [x] Refinement turns, **stateless** — the panel sends the full conversation history
      with each request; nothing persisted server-side. Shipped as `turns` +
      `instruction` on the streaming endpoint rather than a separate
      `/drafts/{id}/messages` route, so one code path serves first draft, revision and
      translation.
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

- [x] Mock harness page (`panel/public/harness.html`): iframes the panel and
      postMessages a fake `copilot:context` (`{v:1, ticketId, account}`), with a
      ticket-ID control — the daily dev environment.
- [x] Svelte 5 app: typed state machine (`unauthenticated → idle → generating →
      drafted | insufficient_data | error`; `context_switch` → `idle`) as a plain TS
      module (`src/lib/state.ts`) with exhaustive discriminated unions + `never` check;
      7 transition tests (vitest, wired into CI).
- [x] Chat UI: streamed draft (token-by-token via SSE), conversation view, free-form
      instruction composer, one-tap quick actions (Translate to English / Friendlier /
      Shorter / More formal), per-message and latest-draft **copy to clipboard**.
- [x] The panel owns conversation state (`turns`) and replays it to the stateless
      backend; a ticket switch or refresh starts a fresh conversation, by design.
- [x] `insufficient_data` rendered as a first-class state with the verbatim backend
      message — visibly distinct from errors.
- [x] Auth: bearer token in `localStorage`; postMessage origin-checked and
      runtime-validated (`parseCopilotContext`). API gained a CORS policy
      (loopback in dev, configured origin in prod).
- [x] Manual visual pass at panel dimensions (400 px iframe), verified live in-browser.

Exit criteria (lite): request → draft → copy loop working against the local backend,
driven from the mock harness, no extension involved — **verified live** (German draft
for the Time Resistance ticket rendered in the panel). Refine step carries forward with
refinement turns.

## Stage 5 — Extension shell

Goal: the ~200-line MV3 shell, done once, correctly.

- [x] `manifest.json`: MV3, `storage` permission only, content script matched to
      `https://*.gorgias.com/app/*`, `run_at: document_idle`.
- [x] Ticket detection: regex on `location.pathname` (`/app/views/{viewId}/{ticketId}`,
      verified against the live account; `/app/ticket/{id}` kept as a fallback); hooks
      `pushState`/`replaceState` + `popstate`; debounced MutationObserver fallback.
      9 unit tests over the real URL shapes.
- [x] Mount: single persistent iframe (`allow="clipboard-write"`), anchor-probe list
      from `/v1/config`, floating-panel fallback (+ hide/show toggle); on ticket change
      postMessage new context — **never recreates the iframe**.
- [x] Origin-pinned messaging both directions (shell passes its origin as
      `?shellOrigin=`; the panel validates it against Gorgias/loopback and replies only
      there); `storage`-held overrides for `panelOrigin` / `apiOrigin`.
- [x] Dock/floating telemetry to `/v1/telemetry/anchor`. `/v1/config` and
      `/v1/telemetry/anchor` are unauthenticated — the shell holds no credentials by
      design and neither endpoint carries ticket data or PII. This also makes the
      **kill switch** usable, which matters when testing against a live helpdesk.
- [x] **Manual test checklist** — `docs/extension-manual-test-checklist.md`: 13 checks
      covering ticket detection across SPA navigations, single-iframe reuse, hide/show,
      clipboard, kill switch, and "no ticket content leaves via the page".

Exit criteria: load-unpacked extension shows the local panel on a Gorgias ticket,
survives ticket navigation, falls back to floating, checklist passes.
**Core loop verified in Chrome against a live Gorgias ticket** — panel mounts floating,
ticket ID picked up from the URL, draft generated and regenerated end to end. The
remaining checklist items (ticket switching / single-iframe reuse, back-forward, kill
switch, no-ticket views) have not been run yet.

## Stage 6 — Deployment

Goal: everything running in Azure inside the cost budget; security essentials verified.

- [x] Provisioning runbook written — `docs/azure-setup.md`, CLI steps captured rather
      than Bicep (a handful of resources for one pilot): **App Service** (Linux B1;
      the API is stateless so F1 free also works for a demo), Static Web Apps (free),
      Key Vault + Managed Identity. **No database.**
      *(Decision change: App Service replaces Container Apps — simpler for this pilot
      and no container registry to run.)*
- [x] CI/CD: `deploy-api.yml` → App Service, `deploy-panel.yml` → Static Web Apps
      (bakes `API_ORIGIN` into the bundle), `build-extension.yml` → downloadable zip
      artifact with deployed origins baked in.
- [x] Secrets: Key Vault + Managed Identity via App Service **Key Vault references** —
      no code change, and the deploy workflows never see a secret. GitHub holds only
      the publish profile and the SWA token.
- [x] Security essentials wired: SWA serves
      `Content-Security-Policy: frame-ancestors 'self' https://*.gorgias.com` + `nosniff`
      via `staticwebapp.config.json` (`'self'` so the bundled harness can still frame the
      panel); API CORS = exactly `Api__AllowedOrigins__0` in production.
- [ ] **Run the runbook** (needs an Azure subscription — a human step), then verify:
      CSP header present, CORS restricted, and no secret in the extension or SPA bundle.
- [ ] Confirm LLM provider DPA / no-training / zero-retention terms (launch gate —
      it's a reading task, not an engineering task, but it blocks launch).
- [ ] Smoke test the deployed stack end to end with the load-unpacked extension.

Exit criteria: production URL serves the panel; deployed API drafts from a real ticket;
the security essentials above verified. **Pipelines and runbook are code-complete; the
Azure provisioning run is pending.**

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
