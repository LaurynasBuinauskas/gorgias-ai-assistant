<script lang="ts">
const {
  markets,
  topics,
  initialMarket = '',
  initialTopic = '',
  busy,
  onSubmit,
}: {
  markets: readonly string[];
  topics: readonly string[];
  initialMarket?: string;
  initialTopic?: string;
  busy: boolean;
  onSubmit: (file: File, market: string, topic: string) => void;
} = $props();

// Capturing the initial prop values is the intent: the parent re-mounts this form via
// {#key} whenever a Replace click changes the prefill, so these never need to track.
// svelte-ignore state_referenced_locally
let market = $state(initialMarket);
// svelte-ignore state_referenced_locally
let topic = $state(initialTopic);
let files = $state<FileList | null>(null);

const canSubmit = $derived(
  !busy && market.length > 0 && topic.trim().length > 1 && (files?.length ?? 0) > 0,
);

function submit() {
  const file = files?.item(0);
  if (file && canSubmit) onSubmit(file, market, topic.trim().toLowerCase());
}
</script>

<div class="form">
  <div class="field">
    <label for="upload-market">Market</label>
    <select id="upload-market" bind:value={market} disabled={busy}>
      <option value="" disabled>Choose…</option>
      {#each markets as option (option)}
        <option value={option}>{option}</option>
      {/each}
    </select>
  </div>
  <div class="field">
    <label for="upload-topic">Topic</label>
    <input
      id="upload-topic"
      list="known-topics"
      bind:value={topic}
      placeholder="shipping-and-returns"
      disabled={busy}
    />
    <datalist id="known-topics">
      {#each topics as known (known)}<option value={known}></option>{/each}
    </datalist>
  </div>
  <div class="field">
    <label for="upload-file">Document (.docx or .md)</label>
    <input id="upload-file" type="file" accept=".md,.docx" bind:files disabled={busy} />
  </div>
  <button class="primary" onclick={submit} disabled={!canSubmit}>
    {busy ? 'Uploading…' : 'Upload to staging'}
  </button>
  <p class="hint">
    Uploading stages the file — nothing reaches the assistant until you publish it.
  </p>
</div>

<style>
  .form {
    display: flex;
    flex-direction: column;
    gap: 0.6rem;
    background: #fff;
    border: 1px solid #e4e7eb;
    border-radius: 10px;
    padding: 0.9rem;
  }
  .field {
    display: flex;
    flex-direction: column;
    gap: 0.25rem;
  }
  label {
    font-size: 0.8rem;
    font-weight: 600;
  }
  input,
  select {
    font: inherit;
    padding: 0.45rem 0.55rem;
    border: 1px solid #cbd2d9;
    border-radius: 8px;
    background: #fff;
  }
  .primary {
    font: inherit;
    align-self: flex-start;
    padding: 0.45rem 0.9rem;
    border-radius: 8px;
    border: 1px solid #2b6cb0;
    background: #2b6cb0;
    color: #fff;
    font-weight: 500;
    cursor: pointer;
  }
  .primary:disabled {
    opacity: 0.55;
    cursor: default;
  }
  .hint {
    color: #9ca3af;
    font-size: 0.78rem;
    margin: 0;
  }
</style>
