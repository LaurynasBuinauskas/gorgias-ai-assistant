# Gorgias AI Copilot

An AI drafting assistant for Gorgias support agents. A Chrome extension mounts a chat
panel beside the Gorgias ticket view; the agent requests a reply draft, the backend
generates it from ticket content + company SOP/FAQ documents (RAG), and the agent
edits and copies it into the Gorgias composer. Human-in-the-loop only — the system
never sends replies autonomously.

**Documentation** (source of truth for all decisions — read before changing anything):

| Document | Contents |
|---|---|
| [docs/gorgias-copilot-technical-reference.md](docs/gorgias-copilot-technical-reference.md) | Architecture, stack, constraints, phases |
| [docs/gorgias-copilot-extension-dev-guide.md](docs/gorgias-copilot-extension-dev-guide.md) | Tooling, dev workflow, build & release |
| [docs/implementation-plan.md](docs/implementation-plan.md) | Stage-by-stage plan with exit criteria |
| [docs/azure-setup.md](docs/azure-setup.md) | One-time cloud provisioning runbook (CLI + portal) |
| [docs/user-setup-guide.md](docs/user-setup-guide.md) | Demo setup: install the widget (for the dev running the demo) |
| [docs/user-usage-guide.md](docs/user-usage-guide.md) | Usage + a suggested demo flow |
| [CLAUDE.md](CLAUDE.md) | Coding standards + instructions for AI assistants |

## Repository structure

```
gorgias-ai-assistant/
├─ extension/         Chrome MV3 "shell" — plain TypeScript + Vite, no framework
├─ panel/             Agent-facing chat UI — Svelte 5 + Vite SPA
├─ backend/           .NET 10 solution — all product logic
├─ apphost/           Aspire AppHost — runs API + panel together for local dev
├─ docs/              Project documentation
├─ package.json       pnpm workspace root (scripts: build, check, lint, format)
├─ pnpm-workspace.yaml
├─ biome.json         Lint + format config for all TypeScript
└─ .editorconfig      Authoritative code style (mainly C# conventions)
```

The three-piece split mirrors the architecture: the extension is a tiny loader, the
panel is the product's face, and the backend is the product's brain. Each deploys
independently — panel and backend ship continuously; extension releases are rare and
deliberate (Chrome Web Store review is the only slow external step).

### `extension/` — the dumb shell

A content script for `https://*.gorgias.com/app/*` whose only jobs are: read the
ticket ID from the URL, mount a persistent iframe pointing at the panel, and forward
`{ticketId, account}` via origin-pinned postMessage. No business logic, no
credentials, no ticket content. Builds to a single `dist/inject.js` (IIFE, stable
filename — `manifest.json` references it by exact path).

### `panel/` — the agent UI

A normal web app (deployed to Azure Static Web Apps) that happens to be loaded inside
the shell's iframe. This is where ~90% of frontend work happens — run it in a plain
browser tab with a mock harness; no extension needed.

### `backend/` — the .NET solution

Clean/onion architecture: **dependencies point inward toward `Copilot.Domain`**, and
external systems sit behind interfaces at the edge. Each seam corresponds to a real
planned change (provider swap, pgvector in P2, OAuth2 later), not speculative
flexibility.

| Project | Role |
|---|---|
| `Copilot.Domain` | Core types (`TicketContext`, `Draft`, …). References nothing. |
| `Copilot.Pipeline` | Product logic: detect language → retrieve → relevance gate → draft. |
| `Copilot.Ai` | The **only** project allowed to touch LLM vendor SDKs; exposes `Microsoft.Extensions.AI` abstractions. |
| `Copilot.Gorgias` | Gorgias REST client + credential provider (server-side ticket fetch). |
| `Copilot.Knowledge` | Chunking, embeddings, in-memory vector store behind `IKnowledgeStore` (pgvector in P2). |
| `Copilot.Api` | HTTP entry point (minimal APIs, auth, rate limiting); composition root wiring everything together. |
| `tools/Copilot.Ingest` | CLI: SOP/FAQ folder → chunk → embed → knowledge file consumed by the API at startup. |
| `Copilot.Tests` | Single xunit project for all backend tests. |

Shared build settings (net10.0, nullable, analyzers) live in
`backend/Directory.Build.props`, which is why individual `.csproj` files are nearly
empty.

There is **no database in the MVP**: knowledge lives in a precomputed embeddings file
loaded in memory, conversation state stays client-side (stateless backend), telemetry
goes to Application Insights. PostgreSQL arrives in P2 with the first feature that
needs it.

## Running locally

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download), Node.js ≥ 22,
and pnpm — install it via corepack **from an Administrator terminal**:

```sh
corepack enable pnpm
```

This puts the pnpm shim in the system-wide Node directory, which matters: IDE debug
launchers (e.g. the VS Code Aspire extension) construct a reduced environment that
includes the system PATH but not user-profile PATH entries, so a user-level pnpm
install (`npm i -g pnpm`) works in terminals but breaks F5 debugging of the AppHost.
The pinned pnpm version comes from `packageManager` in the root `package.json`.

Then run `pnpm install` once at the repo root.

**Option A — one command (Aspire):**

```sh
cd apphost
dotnet run
```

Starts the API (http://localhost:5249) and the panel dev server (hot reload; Aspire
assigns its port via the `PORT` env var) together, plus the Aspire dashboard at
https://localhost:17139 (login URL with token printed in the console) — endpoint links,
logs, traces, and restart controls for both. The panel automatically receives
`VITE_API_URL` pointing at the API.

To use the panel, open the **`panel` resource URL from the dashboard and append
`/harness.html`** (e.g. `http://localhost:52314/harness.html`) — the panel itself is an
iframe-only app, so the harness is what drives it.

**Option B — two terminals, no Aspire:**

```sh
# Terminal 1 — backend API  →  http://localhost:5249
cd backend/Copilot.Api
dotnet run

# Terminal 2 — panel with hot reload  →  http://localhost:5173
cd panel
pnpm dev
```

Then open **http://localhost:5173/harness.html**.

**Using the harness** (either option): it stands in for the Gorgias page — it iframes the
panel and postMessages a fake `copilot:context`. Enter a ticket ID (a real one from your
Gorgias account), click **Load ticket**, then **Generate reply** in the panel. In dev the
access token is pre-filled with `local-dev-token`, matching
`Api:BearerToken` in `appsettings.Development.json`.

**Extension loop** (only for shell work — docking, navigation detection, clipboard):

```sh
cd extension && pnpm build
```

Then `chrome://extensions` → Developer mode ON → **Load unpacked** → select
`extension/dist`, and set the storage config override so `PANEL_ORIGIN` points at
`http://localhost:5173`. Test against a Gorgias trial/sandbox account — never live
customer tickets.

**Checks** (all from the repo root unless noted):

```sh
pnpm build         # build panel + extension
pnpm check         # type-check both (svelte-check + tsc)
pnpm lint          # Biome lint
pnpm format        # Biome format --write
dotnet build       # from backend/
dotnet test        # from backend/
```
