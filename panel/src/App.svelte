<script lang="ts">
import { onMount, tick } from 'svelte';
import { copilotReady, parseCopilotContext, resolveShellOrigin } from './lib/contract';
import {
  initialState,
  type PanelContext,
  type PanelEvent,
  type PanelState,
  reduce,
} from './lib/state';
import { streamDraft } from './lib/stream';

const TOKEN_KEY = 'copilot:token';
// The one origin this panel exchanges messages with: the extension shell, or the dev harness.
const SHELL_ORIGIN = resolveShellOrigin();

const QUICK_ACTIONS = [
  { label: 'Translate to English', instruction: 'Translate the reply to English.' },
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
let instruction = $state('');
let copiedIndex = $state<number | null>(null);
let scroller = $state<HTMLElement | null>(null);

const busy = $derived(panel.status === 'generating');
const turns = $derived(panel.status === 'unauthenticated' ? [] : panel.turns);
const hasDraft = $derived(turns.some((t) => t.role === 'assistant'));
const lastDraft = $derived([...turns].reverse().find((t) => t.role === 'assistant')?.text ?? '');

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
    context = next;
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
    if (event.kind === 'delta') {
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

async function copy(text: string, index: number) {
  await navigator.clipboard.writeText(text);
  copiedIndex = index;
  setTimeout(() => (copiedIndex = null), 1500);
}

function onComposerKeydown(event: KeyboardEvent) {
  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault();
    if (instruction.trim()) void run(instruction.trim());
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
        <div class="empty">
          <p class="empty-title">Draft a reply</p>
          <p class="empty-sub">
            I'll read the ticket and write a reply in the customer's language. Then ask me to
            adjust it — shorter, warmer, or translated.
          </p>
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

      {#if panel.status === 'generating'}
        <div class="turn assistant">
          <div class="draft streaming">{panel.partial}<span class="caret"></span></div>
        </div>
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
          {#each QUICK_ACTIONS as action (action.label)}
            <button class="chip" onclick={() => run(action.instruction)}>{action.label}</button>
          {/each}
        </div>
      {/if}

      {#if !hasDraft && turns.length === 0}
        <button class="primary block" onclick={() => run()} disabled={busy}>
          {busy ? 'Writing…' : 'Generate reply'}
        </button>
      {:else}
        <div class="composer">
          <textarea
            rows="2"
            bind:value={instruction}
            onkeydown={onComposerKeydown}
            placeholder="Ask for a change — e.g. “translate to English”"
            disabled={busy}
          ></textarea>
          <div class="composer-actions">
            <button
              class="primary"
              onclick={() => (instruction.trim() ? run(instruction.trim()) : run())}
              disabled={busy}
            >
              {busy ? 'Writing…' : instruction.trim() ? 'Send' : 'Regenerate'}
            </button>
            {#if lastDraft && !busy}
              <button class="ghost" onclick={() => copy(lastDraft, -1)}>
                {copiedIndex === -1 ? '✓ Copied' : 'Copy latest'}
              </button>
            {/if}
          </div>
        </div>
      {/if}
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
    text-align: center;
    padding: 1.5rem 0.5rem;
  }
  .empty-title {
    font-weight: 600;
    color: #111827;
    margin: 0 0 0.35rem;
  }
  .empty-sub {
    margin: 0;
    line-height: 1.5;
    font-size: 0.86rem;
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
  .block {
    width: 100%;
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
