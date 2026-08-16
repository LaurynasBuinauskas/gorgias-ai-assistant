<script lang="ts">
import HistoryList from './admin/HistoryList.svelte';
import OperationCard from './admin/OperationCard.svelte';
import PolicyTree from './admin/PolicyTree.svelte';
import UploadForm from './admin/UploadForm.svelte';
import {
  type ApiFailure,
  getCurrent,
  getPublish,
  listFiles,
  listPublishes,
  startPublish,
  startRollback,
  startValidate,
  uploadFile,
} from './lib/admin/api';
import {
  type AdminEvent,
  initialAdminState,
  operationRunning,
  reduceAdmin,
} from './lib/admin/state';

const TOKEN_KEY = 'copilot:admin-token';
const NAME_KEY = 'copilot:admin-name';
const POLL_MS = 5000;

function readStored(key: string): string {
  try {
    return localStorage.getItem(key) ?? '';
  } catch {
    return '';
  }
}

function writeStored(key: string, value: string) {
  try {
    if (value.length === 0) localStorage.removeItem(key);
    else localStorage.setItem(key, value);
  } catch {
    // Storage blocked; the session still works, it just is not remembered.
  }
}

const storedToken = readStored(TOKEN_KEY);
let token = $state(storedToken);
let tokenInput = $state('');
let name = $state(readStored(NAME_KEY));
let admin = $state(initialAdminState);
let uploading = $state(false);
let actionError = $state('');
let selected = $state<string[]>([]);
let prefill = $state({ market: '', topic: '', key: 0 });

const named = $derived(name.trim().length > 1);
const busy = $derived(admin.status === 'ready' && operationRunning(admin.operation));

function dispatch(event: AdminEvent) {
  admin = reduceAdmin(admin, event);
}

function handleFailure(failure: ApiFailure) {
  if (failure.kind === 'unauthorized') {
    dispatch({ type: 'unauthorized', message: failure.message });
  } else if (failure.kind === 'refused') {
    // A refusal is advice ("one at a time", "not staged"), not a broken page.
    actionError = failure.message;
  } else {
    dispatch({ type: 'failed', message: failure.message });
  }
}

async function loadAll(candidate: string): Promise<boolean> {
  const [files, current, history] = await Promise.all([
    listFiles(candidate),
    getCurrent(candidate),
    listPublishes(candidate),
  ]);
  const failure = [files, current, history].find((r) => !r.ok);
  if (failure && !failure.ok) {
    handleFailure(failure);
    return false;
  }
  if (files.ok && current.ok && history.ok) {
    dispatch({
      type: 'loaded',
      data: {
        drafts: files.value,
        documents: current.value.documents,
        markets: current.value.markets,
        history: history.value,
      },
    });
  }
  return true;
}

async function connect(candidate: string) {
  dispatch({ type: 'unlock' });
  if (await loadAll(candidate)) {
    token = candidate;
    writeStored(TOKEN_KEY, candidate);
  }
}

function unlock() {
  if (tokenInput.trim().length > 0) void connect(tokenInput.trim());
}

function signOut() {
  token = '';
  tokenInput = '';
  writeStored(TOKEN_KEY, '');
  dispatch({ type: 'signed_out' });
}

if (storedToken.length > 0) void connect(storedToken);

$effect(() => {
  writeStored(NAME_KEY, name.trim());
});

async function upload(file: File, market: string, topic: string) {
  actionError = '';
  uploading = true;
  const result = await uploadFile(token, file, market, topic, name.trim());
  uploading = false;
  if (result.ok) await loadAll(token);
  else handleFailure(result);
}

async function beginOperation(
  kind: 'validate' | 'publish' | 'rollback',
  start: () => Promise<Awaited<ReturnType<typeof startPublish>>>,
) {
  actionError = '';
  const result = await start();
  if (result.ok) {
    dispatch({ type: 'operation_started', kind, publishId: result.value });
  } else {
    handleFailure(result);
  }
}

function check(blobName: string) {
  void beginOperation('validate', () => startValidate(token, [blobName], name.trim()));
}

function publishSelected() {
  void beginOperation('publish', () => startPublish(token, selected, name.trim()));
}

function rollback() {
  if (
    window.confirm(
      'This restores the policy as it was before the latest publish. ' +
        'It runs through the same safety checks. Continue?',
    )
  ) {
    void beginOperation('rollback', () => startRollback(token, name.trim()));
  }
}

function replaceDocument(market: string, topic: string) {
  prefill = { market, topic, key: prefill.key + 1 };
  document.getElementById('upload-market')?.scrollIntoView({ behavior: 'smooth', block: 'center' });
}

// Follows the running operation: poll while it runs, refresh everything once it settles.
// The effect re-arms on every state change; the interval lives only while polling makes
// sense, so a dismissed card or a sign-out stops it by construction.
$effect(() => {
  if (admin.status !== 'ready' || !operationRunning(admin.operation)) return;
  const publishId = admin.operation?.publishId;
  if (!publishId) return;

  const timer = setInterval(async () => {
    const result = await getPublish(token, publishId);
    if (!result.ok) {
      if (result.kind === 'unauthorized') handleFailure(result);
      return; // Transient read failures just wait for the next tick.
    }
    dispatch({
      type: 'operation_update',
      status: result.value.status,
      findings: result.value.findings,
    });
    if (result.value.status.state !== 'running') {
      selected = [];
      void loadAll(token);
    }
  }, POLL_MS);

  return () => clearInterval(timer);
});

function toggleSelected(blobName: string) {
  selected = selected.includes(blobName)
    ? selected.filter((b) => b !== blobName)
    : [...selected, blobName];
}

const stagedDrafts = $derived(
  admin.status === 'ready' ? admin.data.drafts.filter((d) => d.state === 'staged') : [],
);
const knownTopics = $derived(
  admin.status === 'ready' ? [...new Set(admin.data.documents.map((d) => d.topic))] : [],
);
</script>

<main>
  <header>
    <div class="title">
      <span class="brand">Policy manager</span>
      <span class="beta">Beta</span>
    </div>
    {#if admin.status === 'ready'}
      <div class="who">
        <input
          class="name"
          bind:value={name}
          placeholder="Your name (required to act)"
          aria-label="Your name"
        />
        <button class="ghost" onclick={signOut}>Sign out</button>
      </div>
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
      <p class="hint">
        This is not the drafting token agents use — ask your admin for the policy one.
      </p>
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
      {#if admin.operation}
        <OperationCard
          operation={admin.operation}
          onDismiss={() => dispatch({ type: 'operation_dismissed' })}
        />
      {/if}
      {#if actionError}
        <p class="notice error">{actionError}</p>
      {/if}

      <h2>Upload a document</h2>
      {#key prefill.key}
        <UploadForm
          markets={admin.data.markets}
          topics={knownTopics}
          initialMarket={prefill.market}
          initialTopic={prefill.topic}
          busy={uploading || !named}
          onSubmit={upload}
        />
      {/key}
      {#if !named}
        <p class="hint">Enter your name at the top right first — every change is recorded.</p>
      {/if}

      {#if stagedDrafts.length > 0}
        <h2>Waiting to publish</h2>
        {#each stagedDrafts as draft (draft.blobName)}
          <div class="staged">
            <input
              type="checkbox"
              checked={selected.includes(draft.blobName)}
              onchange={() => toggleSelected(draft.blobName)}
              disabled={busy}
              aria-label={`Select ${draft.fileName}`}
            />
            <div class="staged-info">
              <span class="staged-name">{draft.fileName}</span>
              <span class="staged-meta">
                {draft.market} · {draft.topic} · by {draft.uploadedBy}
              </span>
            </div>
            <button class="ghost" onclick={() => check(draft.blobName)} disabled={busy || !named}>
              Check
            </button>
          </div>
        {/each}
        <button
          class="primary"
          onclick={publishSelected}
          disabled={busy || !named || selected.length === 0}
        >
          Publish {selected.length || ''} selected
        </button>
      {/if}

      <h2>Live policy</h2>
      <PolicyTree documents={admin.data.documents} onReplace={replaceDocument} />

      <h2>History</h2>
      <HistoryList history={admin.data.history} busy={busy || !named} onRollback={rollback} />
    </section>
  {/if}
</main>

<style>
  :global(body) {
    margin: 0;
    background: #f7f8fa;
  }
  main {
    font-family: system-ui, -apple-system, 'Segoe UI', sans-serif;
    font-size: 14px;
    color: #111827;
    max-width: 760px;
    margin: 0 auto;
    padding: 0 1rem 3rem;
  }
  header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 1rem 0;
    border-bottom: 1px solid #e4e7eb;
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
    color: #2b6cb0;
    background: #eaf1f8;
    border: 1px solid #cfe0ef;
    border-radius: 999px;
    padding: 0.18rem 0.4rem;
  }
  .who {
    display: flex;
    align-items: center;
    gap: 0.5rem;
  }
  .name {
    font: inherit;
    font-size: 0.84rem;
    padding: 0.35rem 0.55rem;
    border: 1px solid #cbd2d9;
    border-radius: 8px;
    width: 210px;
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
    gap: 0.7rem;
  }
  h2 {
    font-size: 15px;
    margin: 0.8rem 0 0;
  }
  .staged {
    display: flex;
    align-items: center;
    gap: 0.6rem;
    background: #fff;
    border: 1px solid #e4e7eb;
    border-radius: 8px;
    padding: 0.5rem 0.7rem;
  }
  .staged-info {
    flex: 1;
    display: flex;
    flex-direction: column;
  }
  .staged-name {
    font-weight: 500;
  }
  .staged-meta {
    color: #6b7280;
    font-size: 0.78rem;
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
    border-color: #2b6cb0;
  }
  button {
    font: inherit;
    cursor: pointer;
  }
  .primary {
    align-self: flex-start;
    padding: 0.45rem 0.9rem;
    border-radius: 8px;
    border: 1px solid #2b6cb0;
    background: #2b6cb0;
    color: #fff;
    font-weight: 500;
  }
  .primary:disabled {
    opacity: 0.55;
    cursor: default;
  }
  .ghost {
    border: none;
    background: transparent;
    color: #2b6cb0;
    padding: 0.25rem 0.4rem;
    font-size: 0.84rem;
  }
  .notice {
    border-radius: 8px;
    padding: 0.65rem 0.75rem;
    margin: 0;
  }
  .notice.error {
    background: #fdecea;
    border: 1px solid #f5c2c0;
  }
  .hint {
    color: #9ca3af;
    font-size: 0.8rem;
    margin: 0;
  }
</style>
