# CLAUDE.md

Guidance for Claude Code (and other AI assistants) working in this repository.

## What this project is

An AI drafting assistant (copilot) for Gorgias support agents. A Chrome MV3 browser
extension mounts an iframe panel beside the Gorgias ticket view; the panel is a chat UI
where an agent requests a reply draft, the backend generates it from ticket content +
company SOP/FAQ documents (RAG), and the agent edits and copies it into the Gorgias
composer. Human-in-the-loop only — the system never sends replies autonomously.

**Read these before making architectural or design decisions — they are the source of truth:**

- `docs/gorgias-copilot-technical-reference.md` — architecture, stack, constraints, phases.
- `docs/gorgias-copilot-extension-dev-guide.md` — tooling, dev workflow, build/release.
- `docs/implementation-plan.md` — phased implementation plan with stage-by-stage exit criteria.

Update those documents whenever a decision they record changes.

## Architecture at a glance

```
Agent browser
├─ Gorgias UI (https://<sub>.gorgias.com/app/…)
├─ Extension content script (shell): URL → ticketId → mount iframe → postMessage
└─ Panel SPA (our origin, in iframe) ── HTTPS/JSON ──▶ .NET 10 API (stateless)
                                                        ├─ Knowledge file (in-memory embeddings; PostgreSQL in P2)
                                                        ├─ LLM providers (external APIs)
                                                        └─ Gorgias REST API (service-account key)
```

Planned repository layout:

```
extension/    # MV3 shell: plain TypeScript + Vite, no framework (~200 lines)
panel/        # Svelte 5 + Vite SPA, deployed to Azure Static Web Apps
backend/      # .NET 10 solution (Copilot.Api, .Pipeline, .Ai, .Gorgias, .Knowledge, .Domain, tools/Copilot.Ingest)
apphost/      # Aspire AppHost (Copilot.AppHost): local-dev orchestrator for API + panel, not deployed
docs/         # Technical reference + dev guide
```

## Non-negotiable principles (summary — full detail in the technical reference)

1. **The extension is a dumb shell.** No business logic, credentials, or ticket content.
   It detects the ticket ID from the URL and mounts the iframe. Product logic lives in
   the SPA + backend, which deploy independently of extension releases.
2. **Minimal DOM use.** Ticket ID comes from `location.pathname`, never page markup.
   Any new DOM read must be justified, isolated in the shell, and driven by
   config-served selectors.
3. **Ticket content never transits the browser into our system.** The shell passes only
   `{ticketId, account}`; the backend fetches ticket data server-side.
4. **Drafts are generated on demand** — LLM spend must be proportional to actual usage.
5. **No vendor LLM SDK types outside `Copilot.Ai`.** All model access goes through
   `Microsoft.Extensions.AI` abstractions (`IChatClient`, `IEmbeddingGenerator`).
   Models are pinned to dated snapshots; changes are deliberate, evaluated config changes.
6. **Keep it cheap and boring.** The MVP has **no database**: knowledge lives in a
   precomputed embeddings file loaded in-memory, conversation state stays client-side
   (stateless backend), telemetry goes to Application Insights. PostgreSQL (pgvector)
   arrives in P2 with the first feature that needs it. One container app,
   request/response before streaming, no new Azure service without a phase-linked
   justification. Target ≤ $30/month at pilot scale (MVP runs at ~$1–6/month).
7. **Contracts are versioned and append-only.** Shell↔panel postMessage types and API
   DTOs never change shape in place; breaking changes require a new version. Shell
   contract changes trigger the (rare) extension release.
8. **Extension permissions stay at `storage` only.** Adding any permission requires a
   technical-reference update + justification.
9. **Security boundaries are pinned.** postMessage origins are explicit (never `*`);
   CORS allows exactly one origin; no secrets in the extension, SPA bundle, or app
   config (Key Vault + Managed Identity). The backend authorizes (agent, tenant,
   ticket) on every request and never trusts client-supplied IDs beyond lookup.

## Coding standards

These apply to all code in this repository. The overarching rule: **code must be clean,
readable, and no more complex than the problem requires.** Prefer the simple, obvious
implementation; reach for abstraction only when a second concrete use case exists.

### General (all languages)

- Optimize for the reader. Clear names beat comments; comments explain *why*, never *what*.
- Small units: short methods/functions that do one thing; avoid deep nesting — use
  guard clauses and early returns.
- No dead code, no commented-out code, no speculative "we might need it" parameters or layers.
- Fail fast with specific, actionable error messages. Never swallow exceptions silently.
- Immutability by default; mutate only where it measurably matters.
- Tests accompany behavior: unit tests for pipeline/domain logic, Playwright E2E for the
  shell integration points (ticket-ID detection, mount/unmount, floating fallback, clipboard).

### C# (.NET 10) — follows Microsoft's C# Coding Conventions and .NET Framework Design Guidelines

**Naming**
- `PascalCase` for types, methods, properties, constants, and public fields.
- `camelCase` for locals and parameters; `_camelCase` for private instance fields;
  `s_camelCase` for private static fields.
- Interfaces prefixed with `I` (`IKnowledgeStore`, `IGorgiasCredentialProvider`).
- Async methods end in `Async` (`GenerateDraftAsync`).
- No abbreviations or Hungarian notation; acronyms of 3+ letters are Pascal-cased (`HttpClient`, not `HTTPClient`).

**Language usage (modern C#)**
- File-scoped namespaces; one type per file; `using` directives outside the namespace.
- Enable nullable reference types (`<Nullable>enable</Nullable>`) everywhere; no `!`
  null-forgiveness except in tests with a comment.
- `record` types for DTOs, API contracts, and immutable domain values; `required` and
  `init` properties over mutable setters.
- Pattern matching and switch expressions over `if`/`else` chains and type checks.
- `var` when the type is apparent from the right-hand side; explicit type otherwise.
- Collection expressions (`[]`), target-typed `new`, and raw string literals where they
  improve readability.
- `async`/`await` end to end — never `.Result`, `.Wait()`, or `async void` (except event
  handlers). Accept and flow `CancellationToken` on every async public API.
- LINQ for querying, loops for side effects; avoid multiple enumeration of `IEnumerable<T>`.
- Prefer exceptions for exceptional cases and typed results (e.g. `InsufficientKnowledge`)
  for expected pipeline outcomes — do not use exceptions for control flow.

**Design**
- Dependency injection via constructor; register with the built-in container. No service
  locators, no statics holding state.
- `Copilot.Domain` has zero dependencies; dependencies point inward
  (Api → Pipeline → Domain). Provider-specific types stay inside `Copilot.Ai` and
  `Copilot.Gorgias`.
- Minimal APIs with typed request/response records; validate at the boundary.
- Configuration via `IOptions<T>` bound to strongly typed settings classes; secrets from
  Key Vault, never in `appsettings.json`.
- Use `Microsoft.Extensions.Http.Resilience` for outbound HTTP (retry with jitter on
  429/5xx, circuit breakers) rather than hand-rolled retry loops.
- Analyzers on: `<AnalysisLevel>latest</AnalysisLevel>`, warnings as errors in CI.
  Format with `dotnet format`; an `.editorconfig` at the repo root is authoritative.

### TypeScript (extension shell + panel) — modern, strict TypeScript

**Compiler & tooling**
- `"strict": true` plus `noUncheckedIndexedAccess`, `noImplicitOverride`,
  `exactOptionalPropertyTypes`. No `any` — use `unknown` and narrow.
- ES modules only; Node.js 22 LTS; pnpm workspaces; Biome for lint + format (no
  ESLint/Prettier split). Vite for builds.

**Naming**
- `camelCase` for variables/functions, `PascalCase` for types/interfaces/classes/Svelte
  components, `SCREAMING_SNAKE_CASE` only for true compile-time constants.
- No `I` prefix on interfaces (TypeScript convention differs from C# here).
- File names: `kebab-case.ts` for modules, `PascalCase.svelte` for components.

**Language usage**
- `type`/`interface` for all cross-boundary shapes; postMessage payloads and API DTOs
  are explicitly typed, versioned, discriminated unions
  (e.g. `{ v: 1, type: 'copilot:context', ticketId: string, account: string }`).
- Validate all untrusted input (postMessage data, API responses) at runtime before use —
  a type assertion is not validation.
- `const` by default; `readonly` arrays/properties for data that shouldn't change.
- Discriminated unions + exhaustive `switch` (with `never` checks) for state machines —
  the panel's `unauthenticated → idle → generating → drafted | insufficient_data | error`
  machine must be typed this way.
- Async/await over raw promise chains; every `fetch` handles failure explicitly.
- Narrow scope: no module-level mutable state in the shell; keep the shell dependency-free.
- Prefer plain functions and modules over classes unless there is genuine state + behavior
  to encapsulate.

**Svelte 5 (panel)**
- Use runes (`$state`, `$derived`, `$effect`, `$props`) — no legacy stores/`$:` syntax.
- Components stay small and presentational; state-machine and API logic live in plain
  TypeScript modules, imported by components.
- Render `insufficient_data` as a first-class UI state (verbatim backend message), not an error.

## Development workflow (summary — full detail in the dev guide)

- **Panel first, extension rarely.** ~90% of work needs no extension loaded: run the
  panel via `pnpm dev` in a normal tab with a mock harness page that postMessages a fake
  `copilot:context`. Backend via `dotnet run`.
- Shell loop: `pnpm build` → `chrome://extensions` → Load unpacked → `extension/dist`;
  point `PANEL_ORIGIN` at `http://localhost:5173` via the storage config override.
- Never develop against live customer tickets — use a Gorgias trial/sandbox account.
- Don't recreate the iframe on ticket change; postMessage the new context.
- The deployed panel must serve `Content-Security-Policy: frame-ancestors https://*.gorgias.com`;
  do not set that header on the local Vite dev server.

## Git conventions

- Small, focused commits with imperative-mood messages ("Add draft endpoint", not "Added").
- Never commit secrets, API keys, or `.env` files. `manifest.json` version bumps are
  deliberate, release-triggering changes.
