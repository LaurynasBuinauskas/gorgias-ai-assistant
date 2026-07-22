# Extension shell — manual test checklist

The shell changes rarely by design, so it is regression-tested by hand rather than with a
Playwright rig (see `implementation-plan.md`, Stage 5). **Run this whole list before every
extension release** — a release is any change to `manifest.json` or the shell↔panel contract.

## Setup

1. Start the backend and panel (either `dotnet run` in `apphost/`, or the two-terminal
   route in the README). The panel must be reachable at the origin the shell expects.
2. Build the shell:

   ```sh
   cd extension && pnpm build
   ```

3. Chrome → `chrome://extensions` → **Developer mode** ON → **Load unpacked** →
   select `extension/dist`.
4. If the panel is not on `http://localhost:5173`, set the storage override. On the
   extension's service-worker/console page:

   ```js
   chrome.storage.local.set({ panelOrigin: 'http://localhost:5173', apiOrigin: 'http://localhost:5249' })
   ```

5. Open your Gorgias account and sign in.

> Defaults are `panelOrigin: http://localhost:5173`, `apiOrigin: http://localhost:5249`.
> A production build must point these at the deployed origins.

## Checklist

| # | Check | Expected |
|---|---|---|
| 1 | Open a ticket (`/app/views/{viewId}/{ticketId}`) | Panel appears; header shows that exact ticket ID |
| 2 | Click **Generate reply** | Draft appears in the customer's language within a few seconds |
| 3 | Click **Copy**, paste into the Gorgias composer | Full draft text pastes correctly |
| 4 | Click another ticket in the same view | Header switches to the new ticket ID; panel returns to the Generate state |
| 5 | Ticket switch (repeat #4) — watch DevTools → Elements | Only **one** `#copilot-panel-frame` exists; the iframe is **not** recreated |
| 6 | Browser Back / Forward between two tickets | Ticket ID follows the navigation |
| 7 | Navigate to a list view with no ticket selected | Panel hides |
| 8 | Navigate back into a ticket | Panel reappears with the correct ticket |
| 9 | Click **Hide Copilot** / **Copilot** toggle | Panel collapses and restores (floating mode only) |
| 10 | Open a non-`/app` Gorgias page | Shell does not inject anything |
| 11 | Set the kill switch: return `killSwitch: true` from `/v1/config`, reload | No panel mounts at all |
| 12 | DevTools → Console on the Gorgias page | No errors from the shell; no Gorgias errors it caused |
| 13 | DevTools → Network | No ticket content leaves via the page — the shell only calls `/v1/config` and `/v1/telemetry/anchor` |

## Notes

- **Docking vs floating:** with no anchor selectors configured (today's default), the panel
  mounts **floating** on the right and reports `floating` to `/v1/telemetry/anchor`. That is
  expected, not a failure. Docking activates once real selectors are served from
  `/v1/config` — at which point re-run #1, #5 and #9.
- **Never test against live customer tickets** unless the account owner has explicitly
  accepted it; prefer a trial/sandbox account.
- The shell reads only `location.pathname`. If a check fails because the URL shape changed,
  fix `TICKET_PATTERNS` in `extension/src/ticket.ts` and add the new shape to
  `ticket.test.ts` — do not start reading the DOM.
