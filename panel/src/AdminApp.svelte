<script lang="ts">
import { type AdminDraft, listFiles } from './lib/admin/api';
import { type AdminEvent, initialAdminState, reduceAdmin } from './lib/admin/state';

const TOKEN_KEY = 'copilot:admin-token';

function readStoredToken(): string {
  try {
    return localStorage.getItem(TOKEN_KEY) ?? '';
  } catch {
    return '';
  }
}

const storedToken = readStoredToken();
let token = $state(storedToken);
let tokenInput = $state('');
let admin = $state(reduceAdmin(initialAdminState, { type: 'signed_out' }));

function dispatch(event: AdminEvent) {
  admin = reduceAdmin(admin, event);
}

async function connect(candidate: string) {
  dispatch({ type: 'unlock' });
  const result = await listFiles(candidate);
  if (result.ok) {
    token = candidate;
    try {
      localStorage.setItem(TOKEN_KEY, candidate);
    } catch {
      // Storage blocked; the session still works, the token just is not remembered.
    }
    dispatch({ type: 'loaded', drafts: result.value });
  } else if (result.kind === 'unauthorized') {
    dispatch({ type: 'unauthorized', message: result.message });
  } else {
    dispatch({ type: 'failed', message: result.message });
  }
}

function unlock() {
  if (tokenInput.trim().length > 0) void connect(tokenInput.trim());
}

function signOut() {
  token = '';
  tokenInput = '';
  try {
    localStorage.removeItem(TOKEN_KEY);
  } catch {
    // Nothing to do; the in-memory state is cleared either way.
  }
  dispatch({ type: 'signed_out' });
}

async function refresh() {
  const result = await listFiles(token);
  if (result.ok) dispatch({ type: 'loaded', drafts: result.value });
  else if (result.kind === 'unauthorized')
    dispatch({ type: 'unauthorized', message: result.message });
  else dispatch({ type: 'failed', message: result.message });
}

// A stored token from a previous visit connects on load; a bad one falls back to the gate.
if (storedToken.length > 0) void connect(storedToken);

function describe(draft: AdminDraft): string {
  const size =
    draft.sizeBytes >= 1024 ? `${Math.round(draft.sizeBytes / 1024)} KB` : `${draft.sizeBytes} B`;
  return `${draft.market} · ${draft.topic} · ${size} · uploaded by ${draft.uploadedBy}`;
}
</script>

<main>
  <header>
    <div class="title">
      <span class="brand">Policy manager</span>
      <span class="beta">Beta</span>
    </div>
    {#if admin.status === 'ready'}
      <button class="ghost" onclick={signOut}>Sign out</button>
    {/if}
  </header>

  {#if admin.status === 'locked'}
    <section class="gate">
      <label for="admin-token">Access token</label>
      <input
        id="admin-token"
        type="password"
        bind:value={tokenInput}
        placeholder="Paste the policy manager token"
        onkeydown={(e) => e.key === 'Enter' && unlock()}
      />
      <button class="primary" onclick={unlock} disabled={tokenInput.trim().length === 0}>
        Open
      </button>
      {#if admin.message}<p class="notice error">{admin.message}</p>{/if}
      <p class="hint">This is not the drafting token agents use — ask your admin for the policy one.</p>
    </section>
  {:else if admin.status === 'checking'}
    <section class="gate"><p class="hint">Connecting…</p></section>
  {:else if admin.status === 'error'}
    <section class="gate">
      <p class="notice error">{admin.message}</p>
      <button class="primary" onclick={() => connect(token)}>Try again</button>
    </section>
  {:else}
    <section class="content">
      <div class="row">
        <h2>Uploads</h2>
        <button class="ghost" onclick={refresh}>Refresh</button>
      </div>
      {#if admin.drafts.length === 0}
        <p class="hint">Nothing staged yet. Uploading and publishing arrive in the next step.</p>
      {/if}
      {#each admin.drafts as draft (draft.blobName)}
        <div class="card">
          <div class="card-head">
            <span class="name">{draft.fileName}</span>
            <span class="pill" class:published={draft.state === 'published'}>{draft.state}</span>
          </div>
          <div class="meta">{describe(draft)}</div>
        </div>
      {/each}
    </section>
  {/if}
</main>

<style>
  :global(body) {
    margin: 0;
    background: #f7f8fa;
  }
  main {
    --border: #e4e7eb;
    --muted: #6b7280;
    --accent: #2b6cb0;
    font-family: system-ui, -apple-system, 'Segoe UI', sans-serif;
    font-size: 14px;
    color: #111827;
    max-width: 720px;
    margin: 0 auto;
    padding: 0 1rem 2rem;
  }
  header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 1rem 0;
    border-bottom: 1px solid var(--border);
    margin-bottom: 1.2rem;
  }
  .title {
    display: flex;
    align-items: center;
    gap: 0.5rem;
  }
  .brand {
    font-weight: 600;
    font-size: 16px;
  }
  .beta {
    font-size: 0.66rem;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    color: var(--accent);
    background: #eaf1f8;
    border: 1px solid #cfe0ef;
    border-radius: 999px;
    padding: 0.18rem 0.4rem;
  }
  .gate {
    max-width: 380px;
    margin: 3rem auto 0;
    display: flex;
    flex-direction: column;
    gap: 0.6rem;
  }
  .content {
    display: flex;
    flex-direction: column;
    gap: 0.6rem;
  }
  .row {
    display: flex;
    align-items: center;
    justify-content: space-between;
  }
  h2 {
    font-size: 15px;
    margin: 0;
  }
  .card {
    background: #fff;
    border: 1px solid var(--border);
    border-radius: 10px;
    padding: 0.65rem 0.8rem;
  }
  .card-head {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.5rem;
  }
  .name {
    font-weight: 600;
  }
  .pill {
    font-size: 0.72rem;
    border-radius: 999px;
    padding: 0.1rem 0.5rem;
    background: #fef3c7;
    color: #92400e;
  }
  .pill.published {
    background: #dcfce7;
    color: #166534;
  }
  .meta {
    color: var(--muted);
    font-size: 0.8rem;
    margin-top: 0.2rem;
  }
  label {
    font-size: 0.85rem;
    font-weight: 600;
  }
  input {
    font: inherit;
    padding: 0.5rem 0.6rem;
    border: 1px solid #cbd2d9;
    border-radius: 8px;
  }
  input:focus {
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
  .ghost {
    background: transparent;
    border-color: transparent;
    color: var(--accent);
  }
  .notice {
    background: #fff8e1;
    border: 1px solid #f0d98c;
    border-radius: 8px;
    padding: 0.65rem 0.75rem;
    margin: 0;
  }
  .notice.error {
    background: #fdecea;
    border-color: #f5c2c0;
  }
  .hint {
    color: #9ca3af;
    font-size: 0.8rem;
    margin: 0;
  }
</style>
