# Gorgias AI Assistant — Usage & Demo Flow

What the assistant does and a suggested order to show it off. It reads the open ticket and
drafts a reply — it never sends anything; you review and copy into Gorgias.

## Suggested demo flow

A clean 6-step walkthrough that shows the whole product in ~2 minutes:

1. **Open a ticket** with a non-English customer (e.g. the German return thread). The panel
   appears on the right.
2. **Click “✨ Generate a reply.”** Point out the ticket header that pops in (customer,
   subject, *German · 13 messages*) — it read the conversation — then the reply streaming in.
3. **Note the draft is in English** — the agent's working language, so they can review fast.
4. **Click “Translate to German.”** The whole reply is rewritten in the customer's language.
5. **Type a free-form instruction** — e.g. *“mention the refund takes 5 business days”* — to
   show it takes prompts and keeps context.
6. **Click “Copy latest,”** paste into the Gorgias reply box. Emphasise: nothing is sent
   automatically — the agent always approves.

## Feature reference

**Two ways to start.** Click **Generate a reply** for a suggested draft, or type your own
instruction (*“apologise and offer a 10% discount”*) and press Enter.

**English first.** Every draft comes out in English so it's quick to review, with one-tap
buttons once it's ready:

- **Translate to \<language\>** — names the customer's actual language and rewrites in it.
- **Friendlier**, **Shorter**, **More formal** — tone changes, applied to the whole reply.

**Keep chatting.** The box at the bottom is a conversation — each instruction builds on the
last draft (*“make paragraph two shorter,” “reply in Spanish instead”*).

**Copy in.** **Copy** (under a draft) or **Copy latest**, then paste into Gorgias with
<kbd>Ctrl</kbd>+<kbd>V</kbd> and send it there as normal.

**Regenerate / switch / hide.** **Regenerate** for a fresh take; open another ticket and the
panel follows along; **Hide Assistant** (top-right) collapses it.

## Good to know for the demo

- If it says *“This ticket has no customer message to reply to,”* that's expected — pick a
  ticket with a customer message.
- It won't invent order details, prices, or policies — only what's in the conversation. With
  no SOP documents loaded yet, drafts are grounded in the ticket alone (real RAG is the next
  step).
- Human-in-the-loop by design: it never sends, and never changes the ticket, tags, or status.
