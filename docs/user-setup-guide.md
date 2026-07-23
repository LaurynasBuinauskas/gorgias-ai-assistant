# Gorgias AI Assistant — Demo Setup

Quick install for running the demo. Audience: a developer on the team. ~5 minutes.

## 1. Get the extension

**Cloud demo (recommended)** — the build already points at Azure:

GitHub → **Actions** → **Build extension** → latest run on `main` → download the
`gorgias-ai-assistant-extension` artifact → unzip.

**Local demo** — build it and run the servers yourself:

```sh
pnpm --filter @copilot/extension build   # → extension/dist, points at localhost
```

Then start the backend + panel (`dotnet run` in `apphost/`, or the two-terminal route in
the README).

> Keep the unzipped folder somewhere permanent — Chrome loads it from there on every start.

## 2. Load it into Chrome

`chrome://extensions` → **Developer mode** on → **Load unpacked** → select the folder.
The card should read **Gorgias AI Assistant 0.1.0**.

> If an older localhost build is still loaded, **remove it first** — two copies inject two
> panels.

## 3. Sign in

Open a real ticket (`https://<sub>.gorgias.com/app/views/…`). The panel appears on the
right and asks for a token once:

- **Cloud build** → the production bearer token (Key Vault secret `api-bearertoken`).
- **Local build** → `local-dev-token`.

Stored per browser, so you only do this once per machine.

## 4. Run it

Click **✨ Generate a reply**. You should see the ticket header appear, then a draft stream
in. That's the demo ready.

## Gotchas

- **Download the latest artifact.** Origins are baked in at build time — an older run
  points at localhost and fails with “Could not reach the assistant.”
- **Don't move the folder** after loading; it breaks the unpacked extension.
- **Cloud cold start:** the first draft after the API has been idle can take ~10–15 s, then
  settles to a few seconds.
- **Wrong token → “Unauthorized.”** Right endpoint, wrong secret — swap it in the panel.
