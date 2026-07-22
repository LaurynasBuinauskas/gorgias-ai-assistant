# Gorgias AI Copilot — Technical Reference

Purpose: single source of truth for architecture, stack, and constraints. Audience: developers and AI coding assistants. Keep this document updated when decisions change.

## 1. What this is

An AI drafting assistant for Gorgias support agents. A browser extension mounts a panel (iframe) beside the Gorgias ticket view. The panel is a chat UI: agent requests a draft, backend generates it from ticket content + company SOP/FAQ documents (RAG), agent edits/refines, copies to the Gorgias composer. Human-in-the-loop only; the system never sends replies autonomously (Autopilot is a future, gated phase).

## 2. Non-negotiable principles

1. **Extension is a dumb shell.** No business logic, no credentials, no ticket content. It detects the ticket ID and mounts the iframe. All product logic lives in the SPA + backend, which deploy independently of extension releases.
2. **Minimal DOM use.** Touch the Gorgias DOM as little as possible — only when there is no alternative. Ticket ID comes from the URL path (`/app/ticket/{id}`), not from page markup. Expected DOM interactions: anchor-probing to dock the panel (with floating fallback) and navigation-detection fallbacks. Any new DOM read must be justified, isolated in the shell, and driven by config-served selectors so fixes deploy without an extension release.
3. **Ticket content never transits the browser into our system.** The shell passes only `{ticketId, account}`. The backend obtains ticket data server-side (cache or Gorgias REST API).
4. **Pull-first; cache is a deferred option.** Phase 1 fetches ticket data on demand from the Gorgias REST API (comfortably within the ~40 req/20 s budget at pilot scale). A push-primary cache (Gorgias HTTP integrations → Postgres, pull-fallback on miss/stale) MAY be adopted in Phase 2 for pre-warming and feedback transport. If adopted, the database is a cache, never the source of truth.
5. **Drafts are generated on demand,** not pre-generated per ticket. LLM spend must be proportional to actual agent usage.
6. **No vendor LLM SDK types outside `Copilot.Ai`.** All model access via `Microsoft.Extensions.AI` abstractions. Models pinned to dated snapshots; changes are deliberate, evaluated config changes.
7. **Low operating cost.** Target ≤ $30/month infrastructure at pilot scale. **The MVP has no database**: knowledge embeddings live in a precomputed file loaded in-memory, conversation state stays client-side (stateless backend), telemetry goes to Application Insights. PostgreSQL (pgvector + app data, plus the ticket cache if adopted) is introduced in P2 only when a feature requires it. No Redis/Service Bus/Cosmos until a phase justifies them.

## 3. Architecture

```
Agent browser
├─ Gorgias UI (https://<sub>.gorgias.com/app/…)
├─ Extension content script (shell): URL → ticketId → mount iframe → postMessage
└─ Panel SPA (our origin, in iframe) ── HTTPS/JSON (SSE in P2) ──▶ .NET 10 API
                                                                    │
[P2, optional] Gorgias HTTP integrations ──POST──▶ /v1/ingest ──────┤
                                                                    │
                              ┌─────────────────────────────────────┤
                              ▼                 ▼                   ▼
                        Knowledge file    LLM providers        Gorgias REST API
                        (precomputed      (external APIs)      (on-demand fetch,
                         embeddings,                            service-acct key)
                         in-memory; →
                         PostgreSQL in P2)
```

## 4. Browser extension (Manifest V3 — mandatory)

- `manifest_version: 3`. No MV2 constructs. No remotely hosted code in the extension (MV3 prohibition); all updatable logic lives in the iframe-served SPA.
- Permissions: `storage` only. No `tabs`, `scripting`, `webRequest`, no host_permissions. Content script matched to `https://*.gorgias.com/app/*`, `run_at: document_idle`. No background service worker in v1.
- Ticket detection: regex on `location.pathname`; hook `history.pushState/replaceState` + `popstate`; MutationObserver (debounced) as fallback for missed navigations.
- Panel mount: single persistent iframe (`allow="clipboard-write"`), docked via ordered anchor-probe list; fallback = fixed floating panel. On ticket change, postMessage new context — do not recreate the iframe.
- Anchor probes are served from backend config (`/v1/config`), so selector fixes deploy without extension release. Shell reports dock/floating mode to `/v1/telemetry/anchor`.
- Shell ↔ panel messages: versioned, append-only, origin-pinned both directions (never `*`). v1 types: `copilot:context`, `copilot:ready`, `copilot:resize`, `copilot:visibility`.
- Distribution: Chrome Web Store unlisted; force-install via `ExtensionInstallForcelist` (Workspace/GPO/Intune). Extension releases are rare (contract/manifest changes only).

## 5. Panel SPA

- Framework: **Svelte 5** (React acceptable if team prefers; nothing is framework-coupled). Build: Vite. Hosted on Azure Static Web Apps.
- State machine: `unauthenticated → idle → generating → drafted | insufficient_data | error`; `context_switch` resets to `idle`. Render `insufficient_data` as a first-class state (verbatim backend message), not an error.
- Clipboard: `navigator.clipboard.writeText` (enabled by iframe allow attribute).
- Auth: no cookies ever (third-party partitioning). MVP: per-team bearer token in panel `sessionStorage`. Full: OIDC auth-code + PKCE via popup on our origin (Entra ID), tokens in memory, silent renewal. Extension never holds credentials.
- Headers on SPA origin: `Content-Security-Policy: frame-ancestors https://*.gorgias.com`, `X-Content-Type-Options: nosniff`.

## 6. Backend (.NET 10, C#)

Projects: `Copilot.Api` (minimal APIs, auth, rate limiting) · `Copilot.Pipeline` (language detect → retrieve → gate → draft) · `Copilot.Ai` (provider wiring) · `Copilot.Gorgias` (REST client + credential provider) · `Copilot.Knowledge` (chunking, embeddings, in-memory vector store behind `IKnowledgeStore`; pgvector in P2) · `Copilot.Domain` (no dependencies) · `tools/Copilot.Ingest` (SOP ingestion CLI).

The MVP backend is **stateless**: no database. The knowledge file is loaded at startup; conversation state is held by the panel and sent with each refinement request.

Endpoints (v1 = MVP):

| Endpoint | Phase | Notes |
|---|---|---|
| `POST /v1/tickets/{id}/drafts` | MVP | Fetch ticket from Gorgias REST API on demand (cache-read-first only if P2 ingest is adopted) → pipeline → draft JSON or typed `insufficient_data`. |
| `POST /v1/drafts/{draftId}/messages` | MVP | Refinement turn; the panel sends the full conversation history with each request (stateless backend, no server-side conversation storage in MVP). |
| `GET /v1/config` | MVP | Feature flags, anchor probes, min shell version, kill switch. |
| `POST /v1/telemetry/anchor` | MVP | Dock-mode telemetry. |
| `GET /v1/tickets/{id}/draft-stream` | P2 | SSE tokens + stage events (SSE chosen over SignalR: one-way, no extra service, iframe-friendly). |
| `POST /v1/ingest` | P2 (optional) | Gorgias HTTP-integration receiver, only if the ticket cache is adopted. Shared-secret header check; idempotent upsert by ticket/message ID; must respond < 5 s (Gorgias timeout; 3 retries only — hence pull-fallback semantics). |
| `POST /v1/drafts/{draftId}/accepted` | P2 | Feedback capture. Pairing with the agent's actually-sent reply requires the ingest stream — adopt ingest alongside this feature. |

Pipeline gates: retrieval relevance below threshold ⇒ return `InsufficientKnowledge` without calling the LLM. Language of newest customer message is pinned on output.

AI providers: external APIs only (no self-hosting). `IChatClient`/`IEmbeddingGenerator` via `Microsoft.Extensions.AI`. Separate configured models: drafting (frontier), classifier (small/cheap), embeddings (pinned hard — change forces reindex), fallback provider behind circuit breaker. Resilience: `Microsoft.Extensions.Http.Resilience` (retry w/ jitter on 429/5xx, per-provider circuit breaker). Per-tenant token metering, queue don't burst. Provider DPA + no-training/zero-retention terms are a launch gate (EU personal data).

Gorgias access: **private app model** — Basic Auth with a dedicated service-account API key from Key Vault. Rate limit ≈ 40 req/20 s (leaky bucket; honor `Retry-After`, read `X-Gorgias-Account-Api-Call-Limit`). Auth behind `IGorgiasCredentialProvider` (seam for future public-app OAuth2; Gorgias OAuth2 has no PKCE — confidential client flow server-side).

## 7. Data

**MVP: no database.**

- Knowledge base: the ingestion CLI chunks + embeds SOP/FAQ docs and writes a single
  versioned knowledge file (chunk text + embedding vectors). The API loads it into
  memory at startup and retrieves via brute-force cosine similarity — at pilot corpus
  size (hundreds to a few thousand chunks) this is milliseconds; an ANN index buys
  nothing. Updating knowledge = rerun CLI, redeploy (or re-upload file to blob storage).
- Conversation/draft state: held client-side in the panel; each refinement request
  carries the full history. Nothing persisted server-side → no PII at rest, no
  retention job needed (GDPR surface is minimal by construction).
- Telemetry: Application Insights (anchor dock/floating events, token usage, latency).

**P2: introduce PostgreSQL when a feature requires it** (Azure Database for PostgreSQL
Flexible Server B1ms, pgvector). Triggers: durable conversations, feedback capture,
brand-voice exemplar index, or the optional ticket cache. When adopted:

- Knowledge moves behind the same `IKnowledgeStore` interface into pgvector (HNSW;
  hybrid = vector + `tsvector` with RRF fusion if retrieval quality demands it).
- If ingest is adopted: it is lossy by design (5 s timeout, 3 retries) → cache-with-fallback semantics everywhere; stamp `last_event_at`, backfill on stale.
- Retention: scheduled TTL job purges conversations/drafts (and cached tickets, if any) N weeks after ticket closure (cost + GDPR in one mechanism). Ticket excerpts inside conversations are PII — treat accordingly.

## 8. Azure stack & cost budget

| Concern | Service | ~$/mo |
|---|---|---|
| API + ingest | Container Apps (consumption, scale-to-zero OK) | 0–5 |
| Data | None in MVP (knowledge file in container/blob); PostgreSQL Flexible B1ms in P2 | 0 (13–18 in P2) |
| SPA | Static Web Apps (free tier) | 0 |
| Secrets | Key Vault + Managed Identity | ~1 |
| Observability | Application Insights (sampled) | 1–5 |

LLM tokens are the only variable cost; on-demand generation keeps it usage-proportional. Deferred until justified: Service Bus, Redis, Blob+scanning (attachments, P3), Front Door/WAF (multi-tenant), Azure AI Search (only if pgvector retrieval quality proves insufficient — swap behind `IKnowledgeStore`).

## 9. Security requirements

- Three pinned origin boundaries: Gorgias page ↔ panel (postMessage origin checks), panel ↔ API (CORS: exactly one origin, bearer only, no credentials), API ↔ Gorgias/LLM (server-side, Key Vault secrets, Managed Identity).
- `/v1/ingest` (P2, if adopted): long random shared-secret header, reject otherwise; endpoint only upserts by ID.
- No secrets in extension, SPA bundle, or app config. Rotate Gorgias key on schedule.
- Backend authorizes (agent, tenant, ticket) on every request; never trust client-supplied IDs beyond lookup.

## 10. Phases

- **MVP (~45–70 h, 1 dev + Claude Code):** shell, panel chat + copy, on-demand Gorgias fetch, RAG pipeline (request/response, in-memory knowledge file), SOP ingestion CLI, deploy. Bearer auth. No database, no ticket cache; stateless backend.
- **P2 (~55–75 h):** SSE streaming + stage indicators, OIDC/PKCE, brand-voice exemplar index. Introduce PostgreSQL (pgvector) with the first feature that needs it. Optional (decide then): HTTP-integration ingest + Postgres ticket cache (pre-warm + feedback transport); feedback capture depends on it. No queue needed at this volume either way.
- **P3 (~90–130 h):** attachments/vision (Blob + scanning), Shopify/carrier context proxy, groundedness checks + citations UI, hardening, polish. Autopilot only after drafting quality metrics justify it (policy gate on existing pipeline, not a new system).
- Standing overhead all phases: 10–20 % for retrieval/prompt tuning against real tickets.

## 11. Conventions for future work

- Contracts (shell↔panel messages, API DTOs) are versioned and append-only; breaking changes require a new version, and shell contract changes trigger the rare extension release.
- Model/provider changes = config + eval run, never ad-hoc. Keep an eval harness of real anonymized tickets.
- Prefer boring: one database, one container app, request/response before streaming, no new Azure service without a phase-linked justification.
- Update this document when any decision here changes; it is the context primer for future Claude sessions.
