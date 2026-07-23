<script lang="ts">
import { onMount } from 'svelte';
import { requestDraft } from './lib/api';
import { copilotReady, parseCopilotContext, resolveShellOrigin } from './lib/contract';
import {
  initialState,
  type PanelContext,
  type PanelEvent,
  type PanelState,
  reduce,
} from './lib/state';

const TOKEN_KEY = 'copilot:token';
// The one origin this panel exchanges messages with: the extension shell, or the dev harness.
const SHELL_ORIGIN = resolveShellOrigin();

let token = $state(
  sessionStorage.getItem(TOKEN_KEY) ?? (import.meta.env.DEV ? 'local-dev-token' : ''),
);
let panel = $state<PanelState>(initialState);
let context = $state<PanelContext | null>(null);
let copied = $state(false);

function dispatch(event: PanelEvent) {
  panel = reduce(panel, event);
}

// Persist the token so a refresh keeps the agent signed in.
$effect(() => {
  sessionStorage.setItem(TOKEN_KEY, token);
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

async function generate() {
  if (!context) return;
  dispatch({ type: 'generate' });

  const outcome = await requestDraft(token.trim(), context.ticketId);
  if (outcome.kind === 'draft') {
    dispatch({
      type: 'drafted',
      draft: { draftId: outcome.draftId, body: outcome.body, language: outcome.language },
    });
  } else if (outcome.kind === 'insufficient') {
    dispatch({ type: 'insufficient', message: outcome.message });
  } else {
    dispatch({ type: 'failed', message: outcome.message });
  }
}

async function copy(text: string) {
  await navigator.clipboard.writeText(text);
  copied = true;
  setTimeout(() => (copied = false), 1500);
}
</script>

<main>
  <header>
    <span class="brand">AI Assistant</span>
    {#if panel.status !== 'unauthenticated'}
      <span class="ticket">Ticket #{panel.context.ticketId}</span>
    {/if}
  </header>

  {#if token.trim().length === 0}
    <section class="pad">
      <label for="token">Access token</label>
      <input id="token" type="password" bind:value={token} placeholder="Paste your team token" />
      <p class="hint">Your token is kept only in this browser session.</p>
    </section>
  {:else if !context}
    <section class="pad muted">Waiting for a ticket…</section>
  {:else if panel.status === 'idle'}
    <section class="pad">
      <button class="primary" onclick={generate}>Generate reply</button>
    </section>
  {:else if panel.status === 'generating'}
    <section class="pad muted">Generating a draft…</section>
  {:else if panel.status === 'drafted'}
    <section class="pad">
      <textarea readonly rows="12">{panel.draft.body}</textarea>
      <div class="row">
        <button class="primary" onclick={() => copy(panel.status === 'drafted' ? panel.draft.body : '')}>
          {copied ? 'Copied!' : 'Copy'}
        </button>
        <button onclick={generate}>Regenerate</button>
      </div>
    </section>
  {:else if panel.status === 'insufficient_data'}
    <section class="pad">
      <div class="notice">{panel.message}</div>
      <button onclick={generate}>Try again</button>
    </section>
  {:else if panel.status === 'error'}
    <section class="pad">
      <div class="notice error">{panel.message}</div>
      <button onclick={generate}>Retry</button>
    </section>
  {/if}
</main>

<style>
  main {
    font-family: system-ui, sans-serif;
    display: flex;
    flex-direction: column;
    height: 100vh;
    color: #1a1a1a;
    background: #fff;
  }
  header {
    display: flex;
    align-items: baseline;
    gap: 0.5rem;
    padding: 0.75rem 1rem;
    border-bottom: 1px solid #e5e5e5;
  }
  .brand {
    font-weight: 600;
  }
  .ticket {
    color: #777;
    font-size: 0.85rem;
  }
  .pad {
    padding: 1rem;
    display: flex;
    flex-direction: column;
    gap: 0.75rem;
  }
  .muted {
    color: #777;
  }
  label {
    font-size: 0.85rem;
    font-weight: 600;
  }
  input,
  textarea {
    font: inherit;
    padding: 0.5rem;
    border: 1px solid #ccc;
    border-radius: 6px;
    width: 100%;
    box-sizing: border-box;
  }
  textarea {
    resize: vertical;
    white-space: pre-wrap;
  }
  .row {
    display: flex;
    gap: 0.5rem;
  }
  button {
    font: inherit;
    padding: 0.5rem 0.9rem;
    border: 1px solid #ccc;
    border-radius: 6px;
    background: #f5f5f5;
    cursor: pointer;
  }
  button.primary {
    background: #2b6cb0;
    border-color: #2b6cb0;
    color: #fff;
  }
  .hint {
    color: #999;
    font-size: 0.8rem;
    margin: 0;
  }
  .notice {
    padding: 0.75rem;
    border-radius: 6px;
    background: #fff8e1;
    border: 1px solid #f0d98c;
    font-size: 0.9rem;
  }
  .notice.error {
    background: #fdecea;
    border-color: #f5c2c0;
  }
</style>
