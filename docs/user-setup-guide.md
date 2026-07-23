# Gorgias AI Assistant — Setup Guide

A one-time setup, about five minutes. After this, the assistant appears automatically
whenever you open a ticket.

## What you'll need

- **Google Chrome** or **Microsoft Edge** (desktop).
- **The extension folder** — a folder named `gorgias-ai-assistant-extension` containing
  `manifest.json`, `inject.js`, and `panel.css`. Your administrator provides this (or
  downloads it from the project's **Actions → Build extension → Artifacts**).
- **Your access token** — a short secret string, also from your administrator. You enter
  it once.

## Step 1 — Put the extension folder somewhere permanent

Unzip the folder to a location you won't delete or move — for example
`Documents\gorgias-ai-assistant-extension`.

> ⚠️ Chrome loads the extension from this folder every time it starts. If you move or
> delete it, the assistant stops working. Don't leave it in Downloads.

## Step 2 — Load it into your browser

1. Open a new tab and go to **`chrome://extensions`** (or `edge://extensions` on Edge).
2. Turn on **Developer mode** — the toggle is in the top-right corner.
3. Click **Load unpacked** (top-left).
4. Select the folder from Step 1 and confirm.

You should now see a card titled **Gorgias AI Assistant** with version **0.1.0**. That's it —
the extension is installed.

> Chrome shows a small "Developer mode extensions" notice each time it starts. That's
> normal for this kind of install and safe to dismiss.

## Step 3 — Open a ticket and sign in

1. Go to your Gorgias account and open any ticket
   (the URL looks like `https://yourcompany.gorgias.com/app/views/…`).
2. The **AI Assistant** panel appears on the right side of the page.
3. The first time, it asks for an **Access token**. Paste the token from your
   administrator and you're in.

Your token is saved in this browser only, so you won't be asked again on this computer.

## Step 4 — Confirm it works

Click **✨ Generate a reply**. Within a few seconds you should see the customer's details
appear at the top of the panel, followed by a drafted reply writing itself out. If you see
that, setup is complete — head to the **Usage Guide**.

## Keeping it up to date

There are no automatic updates for this kind of install. When your administrator sends a
newer version:

1. Replace the old folder with the new one (same location).
2. Go to `chrome://extensions` and click the **↻ reload** icon on the Gorgias AI Assistant
   card.

## Troubleshooting

| What you see | What to do |
|---|---|
| **No panel appears** on a ticket | Make sure you're on a ticket page (`/app/views/…`), then refresh. Check the extension card at `chrome://extensions` has no red "Errors" button. |
| **"Could not reach the assistant"** | Usually a connection issue — check your internet and try **Retry**. If it persists, tell your administrator. |
| **"Unauthorized — check your access token"** | The token is wrong or expired. Clear it and paste the correct one (see below), or ask your administrator for a fresh token. |
| **Two panels, or an old-looking one** | You may have an older copy installed. At `chrome://extensions`, remove any duplicate **Gorgias AI Assistant** cards and keep only the current folder. |
| **Panel is in the way** | Click **Hide Assistant** in the top-right of the page to collapse it; click **Assistant** to bring it back. |

**To re-enter your access token:** open the panel, and if you need to clear a saved token,
your administrator can walk you through it, or simply use a fresh browser profile. (A token
reset option is on the roadmap.)

Still stuck? Send your administrator a short note with what you clicked and what the panel
said — that's usually enough to sort it out.
