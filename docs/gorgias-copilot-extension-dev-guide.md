# Gorgias AI Copilot — Extension Development & Deployment Guide

Companion to `gorgias-copilot-technical-reference.md` (architecture and constraints live there; this doc covers tooling, workflow, and shipping). Audience: developers and AI coding assistants.

## 1. Framework decisions

| Artifact | Choice | Rationale |
|---|---|---|
| Extension shell | **Plain TypeScript + Vite** (no framework) | ~200 lines, no UI of its own. Frameworks add weight and review surface for zero benefit. (WXT is the fallback if the shell ever grows — not planned.) |
| Panel SPA | **Svelte 5 + Vite** | Fixed in the technical reference. The panel is NOT part of the extension — it is a normal web app deployed to Azure Static Web Apps and loaded in the shell's iframe. |
| Backend | .NET 10 | See technical reference. |

Consequence to internalize: extension releases are rare (shell contract/manifest changes only). Panel and backend ship continuously with no store involvement.

## 2. Repository layout

```
gorgias-ai-assistant/
├── extension/    # MV3 shell: manifest.json, inject.ts, panel.css
├── panel/        # Svelte 5 SPA
├── backend/      # .NET 10 solution
├── apphost/      # Aspire AppHost: local-dev orchestrator (API + panel), not deployed
└── docs/         # Technical reference, dev guide, implementation plan
```

The README documents the full structure and local-run instructions.

Tooling: Node.js 22 LTS, pnpm workspaces, TypeScript everywhere in JS-land, `@types/chrome` in `extension/`, Biome for lint/format, Playwright for E2E.

## 3. Development workflow

**Panel first, extension rarely.** The shell↔panel contract is 4 postMessage types (see technical reference §4), so ~90 % of product work needs no extension loaded:

1. `pnpm dev` in `panel/` → Vite dev server at `http://localhost:5173` — or run
   `dotnet run` in `apphost/` to start API + panel together under Aspire (dashboard
   with logs/traces included).
2. Open it in a normal tab with a mock harness page that postMessages a fake `copilot:context` (`{v:1, ticketId, account}`). Full HMR, normal devtools.
3. Backend runs locally (`dotnet run`) or against the deployed dev API.

**Shell integration loop** (docking, navigation detection, clipboard):

1. `pnpm build` in `extension/`.
2. Chrome → `chrome://extensions` → Developer mode ON → **Load unpacked** → select `extension/dist`.
3. Set the `storage`-held config override so `PANEL_ORIGIN = http://localhost:5173` (production shell default points at the deployed panel).
4. Open your Gorgias account; the shell injects the dev panel. After shell edits: rebuild + click ↻ on the extensions page (no HMR needed; the shell changes rarely).

**E2E:** Playwright launching persistent Chromium context with `--load-extension=extension/dist` + `--disable-extensions-except=…`. Regression-test: ticket-ID detection across SPA navigations, mount/unmount, floating fallback, clipboard write.

Test target: use a low-traffic view or a Gorgias trial/sandbox account — never develop against live customer tickets.

## 4. Build & release pipeline

- Shell: `vite build` → `extension/dist` → zip. Version = `manifest.json` `version`, bumped per release, semver.
- Panel: `vite build` → deployed by GitHub Actions to Azure Static Web Apps on merge.
- Backend: container → Azure Container Apps on merge.
- Extension upload automated with `chrome-webstore-upload-cli` in GitHub Actions (manual trigger, not on every merge — releases are deliberate).

## 5. Deployment / installation

Three stages, in order of formality:

1. **Solo dev:** Load unpacked (above). No accounts, no review.
2. **Team pilot & production — Chrome Web Store, Unlisted.**
   - One-time developer account, $5 fee.
   - Upload zip via Developer Dashboard → visibility **Unlisted** → submit.
   - Review for a minimal-permission content-script extension is typically fast (often ~1–2 days; not guaranteed).
   - Unlisted = installable only via direct link; no public listing.
   - Do NOT plan on raw `.crx` distribution — Chrome blocks off-store installs for normal users on Windows/macOS; the store is effectively mandatory.
3. **Managed rollout:** add the extension ID to `ExtensionInstallForcelist` via Google Admin (managed Chrome) or GPO/Intune. Agents get automatic install, no removal, automatic updates. Edge: submit the same zip to Microsoft Edge Add-ons (separate free account) or force-install the Chrome-store package via Edge policy.

**Updates:** bump manifest `version` → upload → store review → auto-update to all installed clients within hours of approval.

## 6. Gotchas (read before first release)

- **CSP, dev vs prod:** the deployed panel must serve `Content-Security-Policy: frame-ancestors https://*.gorgias.com` (set in Static Web Apps config). Do NOT apply this header on the Vite dev server — it would block your own local framing. Pre-release check: confirm the header IS present in the deployed environment; it is what prevents arbitrary sites from framing the panel.
- **MV3 remote-code prohibition:** no fetched/eval'd JS in the extension. Fine by design — updatable logic lives in the iframe-served SPA; config-served anchor selectors are data, not code.
- **Keep permissions at `storage` only.** Every added permission slows store review and widens the security surface. Adding one requires a technical-reference update + justification.
- **Don't recreate the iframe on ticket change** — postMessage the new context (session/auth continuity). This is in the reference; repeated here because it's the most tempting shortcut.
- **Web Store review is the only slow, external step** in the whole system. Anything you can move from shell to panel, move.

## 7. Tool checklist

| Purpose | Tool |
|---|---|
| Runtime / packages | Node.js 22 LTS, pnpm |
| Language | TypeScript (+ `@types/chrome`) |
| Build | Vite; `@sveltejs/vite-plugin-svelte` |
| Lint / format | Biome |
| E2E | Playwright (Chromium + `--load-extension`) |
| Publishing | Chrome Web Store dev account ($5); `chrome-webstore-upload-cli`; Edge Add-ons account (optional) |
| Fleet install | Google Admin / GPO / Intune → `ExtensionInstallForcelist` |
| Test environment | Gorgias trial/sandbox account or low-traffic view |

Keep this document updated when workflow or tooling changes.
