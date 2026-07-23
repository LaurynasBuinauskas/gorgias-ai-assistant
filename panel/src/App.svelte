<script lang="ts">
import { onMount, tick } from 'svelte';
import { copilotReady, parseCopilotContext, resolveShellOrigin } from './lib/contract';
import { isEnglish, languageName } from './lib/language';
import {
  initialState,
  type PanelContext,
  type PanelEvent,
  type PanelState,
  reduce,
} from './lib/state';
import { streamDraft, type TicketInfo } from './lib/stream';

const TOKEN_KEY = 'copilot:token';
// The one origin this panel exchanges messages with: the extension shell, or the dev harness.
const SHELL_ORIGIN = resolveShellOrigin();

type QuickAction = { readonly label: string; readonly instruction: string };

const BASE_ACTIONS: readonly QuickAction[] = [
  { label: 'Friendlier', instruction: 'Make the reply warmer and more personal.' },
  { label: 'Shorter', instruction: 'Make the reply noticeably shorter, keeping every fact.' },
  { label: 'More formal', instruction: 'Make the reply more formal and professional.' },
];

// localStorage, not sessionStorage: the panel runs in a third-party iframe whose
// sessionStorage is per-tab, so agents would re-enter the token on every Gorgias tab.
let token = $state(
  localStorage.getItem(TOKEN_KEY) ?? (import.meta.env.DEV ? 'local-dev-token' : ''),
);
let panel = $state<PanelState>(initialState);
let context = $state<PanelContext | null>(null);
let ticketInfo = $state<TicketInfo | null>(null);
let instruction = $state('');
let copiedIndex = $state<number | null>(null);
let scroller = $state<HTMLElement | null>(null);

const busy = $derived(panel.status === 'generating');
const turns = $derived(panel.status === 'unauthenticated' ? [] : panel.turns);
const hasDraft = $derived(turns.some((t) => t.role === 'assistant'));
const lastDraft = $derived([...turns].reverse().find((t) => t.role === 'assistant')?.text ?? '');
const canSend = $derived(instruction.trim().length > 0 || hasDraft);

// The customer's language earns a one-tap translate action, since drafts default to English.
const quickActions = $derived.by<QuickAction[]>(() => {
  const name = languageName(ticketInfo?.language);
  const translate =
    name && !isEnglish(ticketInfo?.language)
      ? [{ label: `Translate to ${name}`, instruction: `Translate the reply to ${name}.` }]
      : [];
  return [...translate, ...BASE_ACTIONS];
});

const ticketMeta = $derived.by(() => {
  if (!ticketInfo) return '';
  const count = ticketInfo.messageCount;
  const messages = `${count} ${count === 1 ? 'message' : 'messages'}`;
  const name = languageName(ticketInfo.language);
  return name ? `${name} · ${messages}` : messages;
});

const statusLabel = $derived.by(() => {
  if (panel.status !== 'generating' || panel.partial.length > 0) return null;
  if (panel.phase === 'writing') return 'Writing the reply…';
  return hasDraft ? 'Revising…' : 'Reading the ticket…';
});

function dispatch(event: PanelEvent) {
  panel = reduce(panel, event);
}

$effect(() => {
  localStorage.setItem(TOKEN_KEY, token);
});

// Bootstrap: once a token and a ticket are both present, enter the draft lifecycle.
$effect(() => {
  if (token.trim().length === 0) {
    if (panel.status !== 'unauthenticated') dispatch({ type: 'signed_out' });
    return;
  }
  if (context && panel.status === 'unauthenticated') {
    dispatch({ type: 'authenticated', context });
  }
});

onMount(() => {
  const handler = (event: MessageEvent) => {
    // Origin-pinned in both directions — never '*'.
    if (event.origin !== SHELL_ORIGIN) return;

    const parsed = parseCopilotContext(event.data);
    if (!parsed) return;

    const next: PanelContext = { ticketId: parsed.ticketId, account: parsed.account };
    const changed = next.ticketId !== context?.ticketId;
    context = next;
    if (changed) ticketInfo = null;
    if (token.trim().length > 0 && panel.status !== 'unauthenticated') {
      dispatch({ type: 'context', context: next });
    }
  };

  window.addEventListener('message', handler);
  window.parent.postMessage(copilotReady, SHELL_ORIGIN);
  return () => window.removeEventListener('message', handler);
});

async function scrollToBottom() {
  await tick();
  // Instant, not smooth: streaming updates arrive faster than a smooth scroll animates,
  // and each new one would restart the animation.
  if (scroller) scroller.scrollTop = scroller.scrollHeight;
}

async function run(newInstruction?: string) {
  if (!context || busy) return;

  const history = panel.status === 'unauthenticated' ? [] : panel.turns;
  dispatch(
    newInstruction ? { type: 'generate', instruction: newInstruction } : { type: 'generate' },
  );
  instruction = '';
  void scrollToBottom();

  const payload = newInstruction
    ? { turns: history, instruction: newInstruction }
    : { turns: history };

  for await (const event of streamDraft(token.trim(), context.ticketId, payload)) {
    if (event.kind === 'ticket') {
      ticketInfo = event.ticket;
      dispatch({ type: 'writing' });
    } else if (event.kind === 'delta') {
      dispatch({ type: 'delta', text: event.text });
      void scrollToBottom();
    } else if (event.kind === 'done') {
      dispatch({ type: 'completed' });
    } else if (event.kind === 'insufficient') {
      dispatch({ type: 'insufficient', message: event.message });
    } else {
      dispatch({ type: 'failed', message: event.message });
    }
  }

  // A stream that ends without a terminal event still needs to leave `generating`.
  if (panel.status === 'generating') dispatch({ type: 'completed' });
  void scrollToBottom();
}

function send() {
  if (busy) return;
  run(instruction.trim() || undefined);
}

async function copy(text: string, index: number) {
  await navigator.clipboard.writeText(text);
  copiedIndex = index;
  setTimeout(() => (copiedIndex = null), 1500);
}

function onComposerKeydown(event: KeyboardEvent) {
  if (event.key === 'Enter' && !event.shiftKey && canSend) {
    event.preventDefault();
    send();
  }
}
</script>

<main>
  <header>
    <div class="title">
      <span class="dot" class:busy></span>
      <span class="brand">AI Assistant</span>
    </div>
    {#if panel.status !== 'unauthenticated'}
      <span class="ticket">#{panel.context.ticketId}</span>
    {/if}
  </header>

  {#if ticketInfo}
    <div class="ticket-bar">
      <div class="who">{ticketInfo.customerName ?? 'Customer'}</div>
      {#if ticketInfo.subject}<div class="subject">{ticketInfo.subject}</div>{/if}
      <div class="meta">{ticketMeta}</div>
    </div>
  {/if}

  {#if token.trim().length === 0}
    <section class="pad">
      <label for="token">Access token</label>
      <input id="token" type="password" bind:value={token} placeholder="Paste your team token" />
      <p class="hint">Stored in this browser only — you'll enter it once.</p>
    </section>
  {:else if !context}
    <section class="pad empty">Waiting for a ticket…</section>
  {:else}
    <div class="scroll" bind:this={scroller}>
      {#if turns.length === 0 && panel.status !== 'generating'}
        <div class="welcome">
          <p class="welcome-title">How can I help with this ticket?</p>
          <p class="welcome-sub">
            I'll read the conversation and draft a reply in English. Ask me to translate it
            or change the tone once it's ready.
          </p>
          <button class="primary big" onclick={() => run()}>✨ Generate a reply</button>
          <p class="welcome-or">or type your own instruction below</p>
        </div>
      {/if}

      {#each turns as turn, i (i)}
        {#if turn.role === 'agent'}
          <div class="turn agent"><span>{turn.text}</span></div>
        {:else}
          <div class="turn assistant">
            <div class="draft">{turn.text}</div>
            <button class="ghost copy" onclick={() => copy(turn.text, i)}>
              {copiedIndex === i ? '✓ Copied' : 'Copy'}
            </button>
          </div>
        {/if}
      {/each}

      {#if panel.status === 'generating' && panel.partial.length > 0}
        <div class="turn assistant">
          <div class="draft streaming">{panel.partial}<span class="caret"></span></div>
        </div>
      {/if}

      {#if statusLabel}
        <div class="working"><span class="spinner"></span>{statusLabel}</div>
      {/if}

      {#if panel.status === 'insufficient_data'}
        <div class="notice">{panel.message}</div>
      {/if}
      {#if panel.status === 'error'}
        <div class="notice error">{panel.message}</div>
      {/if}
    </div>

    <footer>
      {#if hasDraft && !busy}
        <div class="chips">
          {#each quickActions as action (action.label)}
            <button class="chip" onclick={() => run(action.instruction)}>{action.label}</button>
          {/each}
        </div>
      {/if}

      <div class="composer">
        <textarea
          rows="2"
          bind:value={instruction}
          onkeydown={onComposerKeydown}
          placeholder={hasDraft
            ? 'Ask for a change — e.g. “translate to German”'
            : 'Ask for something specific — e.g. “apologise and offer a refund”'}
          disabled={busy}
        ></textarea>
        <div class="composer-actions">
          <button class="primary" onclick={send} disabled={busy || !canSend}>
            {busy ? 'Working…' : instruction.trim() ? 'Send' : 'Regenerate'}
          </button>
          {#if hasDraft && !busy}
            <button class="ghost" onclick={() => copy(lastDraft, -1)}>
              {copiedIndex === -1 ? '✓ Copied' : 'Copy latest'}
            </button>
          {/if}
        </div>
      </div>
    </footer>
  {/if}
</main>

<style>
  :global(body) {
    margin: 0;
  }

  main {
    --border: #e4e7eb;
    --muted: #6b7280;
    --accent: #2b6cb0;
    font-family:
      system-ui,
      -apple-system,
      'Segoe UI',
      sans-serif;
    font-size: 14px;
    display: flex;
    flex-direction: column;
    height: 100vh;
    color: #111827;
    background: #f7f8fa;
  }

  header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 0.7rem 0.9rem;
    background: #fff;
    border-bottom: 1px solid var(--border);
    flex: none;
  }
  .title {
    display: flex;
    align-items: center;
    gap: 0.5rem;
  }
  .brand {
    font-weight: 600;
    letter-spacing: -0.01em;
  }
  .dot {
    width: 8px;
    height: 8px;
    border-radius: 50%;
    background: #22c55e;
  }
  .dot.busy {
    background: var(--accent);
    animation: pulse 1s ease-in-out infinite;
  }
  @keyframes pulse {
    50% {
      opacity: 0.3;
    }
  }
  .ticket {
    color: var(--muted);
    font-size: 0.8rem;
    font-variant-numeric: tabular-nums;
  }

  .ticket-bar {
    flex: none;
    padding: 0.55rem 0.9rem;
    background: #eef3f9;
    border-bottom: 1px solid var(--border);
  }
  .ticket-bar .who {
    font-weight: 600;
  }
  .ticket-bar .subject {
    color: #374151;
    font-size: 0.84rem;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .ticket-bar .meta {
    color: var(--muted);
    font-size: 0.78rem;
    margin-top: 0.1rem;
  }

  .scroll {
    flex: 1;
    overflow-y: auto;
    padding: 0.9rem;
    display: flex;
    flex-direction: column;
    gap: 0.7rem;
  }

  .pad {
    padding: 1rem;
    display: flex;
    flex-direction: column;
    gap: 0.6rem;
  }
  .empty {
    color: var(--muted);
  }

  .welcome {
    text-align: center;
    padding: 1.5rem 0.5rem;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 0.5rem;
  }
  .welcome-title {
    font-weight: 600;
    color: #111827;
    margin: 0;
  }
  .welcome-sub {
    margin: 0 0 0.4rem;
    color: var(--muted);
    line-height: 1.5;
    font-size: 0.86rem;
  }
  .welcome-or {
    margin: 0.2rem 0 0;
    color: #9ca3af;
    font-size: 0.8rem;
  }

  .turn.agent {
    align-self: flex-end;
    max-width: 85%;
    background: var(--accent);
    color: #fff;
    padding: 0.45rem 0.7rem;
    border-radius: 12px 12px 3px 12px;
    font-size: 0.86rem;
  }
  .turn.assistant {
    display: flex;
    flex-direction: column;
    gap: 0.35rem;
    align-items: flex-start;
  }
  .draft {
    background: #fff;
    border: 1px solid var(--border);
    border-radius: 12px 12px 12px 3px;
    padding: 0.7rem 0.8rem;
    white-space: pre-wrap;
    line-height: 1.55;
    width: 100%;
    box-sizing: border-box;
  }
  .caret {
    display: inline-block;
    width: 7px;
    height: 1em;
    background: var(--accent);
    vertical-align: text-bottom;
    margin-left: 2px;
    animation: pulse 0.9s steps(2) infinite;
  }

  .working {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    color: var(--muted);
    font-size: 0.86rem;
    padding: 0.2rem 0.1rem;
  }
  .spinner {
    width: 13px;
    height: 13px;
    border: 2px solid #cbd5e1;
    border-top-color: var(--accent);
    border-radius: 50%;
    animation: spin 0.7s linear infinite;
  }
  @keyframes spin {
    to {
      transform: rotate(360deg);
    }
  }

  .notice {
    background: #fff8e1;
    border: 1px solid #f0d98c;
    border-radius: 8px;
    padding: 0.65rem 0.75rem;
    font-size: 0.86rem;
  }
  .notice.error {
    background: #fdecea;
    border-color: #f5c2c0;
  }

  footer {
    flex: none;
    padding: 0.7rem 0.9rem;
    background: #fff;
    border-top: 1px solid var(--border);
    display: flex;
    flex-direction: column;
    gap: 0.55rem;
  }
  .chips {
    display: flex;
    flex-wrap: wrap;
    gap: 0.35rem;
  }
  .chip {
    font: inherit;
    font-size: 0.78rem;
    padding: 0.28rem 0.6rem;
    border-radius: 999px;
    border: 1px solid var(--border);
    background: #f3f4f6;
    color: #374151;
    cursor: pointer;
  }
  .chip:hover {
    background: #e9ebef;
  }

  .composer {
    display: flex;
    flex-direction: column;
    gap: 0.45rem;
  }
  .composer-actions {
    display: flex;
    gap: 0.4rem;
  }

  label {
    font-size: 0.85rem;
    font-weight: 600;
  }
  input,
  textarea {
    font: inherit;
    padding: 0.5rem 0.6rem;
    border: 1px solid #cbd2d9;
    border-radius: 8px;
    width: 100%;
    box-sizing: border-box;
    resize: none;
  }
  input:focus,
  textarea:focus {
    outline: 2px solid rgba(43, 108, 176, 0.35);
    border-color: var(--accent);
  }

  button {
    font: inherit;
    padding: 0.45rem 0.85rem;
    border-radius: 8px;
    border: 1px solid var(--border);
    background: #fff;
    cursor: pointer;
  }
  button:disabled {
    opacity: 0.55;
    cursor: default;
  }
  .primary {
    background: var(--accent);
    border-color: var(--accent);
    color: #fff;
    font-weight: 500;
  }
  .primary.big {
    padding: 0.6rem 1.2rem;
    font-size: 0.95rem;
  }
  .ghost {
    background: transparent;
    border-color: transparent;
    color: var(--accent);
    padding: 0.25rem 0.4rem;
    font-size: 0.82rem;
  }
  .ghost:hover {
    background: #eef2f7;
  }
  .copy {
    align-self: flex-end;
  }

  .hint {
    color: #9ca3af;
    font-size: 0.8rem;
    margin: 0;
  }
</style>
