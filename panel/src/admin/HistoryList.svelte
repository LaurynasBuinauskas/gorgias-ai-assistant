<script lang="ts">
import type { PublishLedger } from '../lib/admin/api';

const {
  history,
  busy,
  onRollback,
}: {
  history: readonly PublishLedger[];
  busy: boolean;
  onRollback: () => void;
} = $props();

function when(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : date.toLocaleString();
}

function describe(ledger: PublishLedger): string {
  const what =
    ledger.blobs.length === 0
      ? 'restored the original policy'
      : `${ledger.blobs.length} document${ledger.blobs.length === 1 ? '' : 's'}`;
  return `${ledger.mode === 'rollback' ? 'Rollback' : 'Publish'} · ${what} · by ${ledger.publishedBy}`;
}
</script>

<div class="history">
  {#if history.length > 0}
    <button class="undo" onclick={onRollback} disabled={busy}>
      Undo the latest publish
    </button>
  {/if}
  {#each history as ledger (ledger.publishId)}
    <div class="row">
      <span class="when">{when(ledger.publishedAt)}</span>
      <span class="what">{describe(ledger)}</span>
    </div>
  {:else}
    <p class="hint">Nothing has been published from this page yet.</p>
  {/each}
</div>

<style>
  .history {
    display: flex;
    flex-direction: column;
    gap: 0.4rem;
  }
  .undo {
    font: inherit;
    align-self: flex-start;
    padding: 0.4rem 0.8rem;
    border-radius: 8px;
    border: 1px solid #e4e7eb;
    background: #fff;
    cursor: pointer;
  }
  .undo:disabled {
    opacity: 0.55;
    cursor: default;
  }
  .row {
    display: flex;
    gap: 0.8rem;
    font-size: 0.84rem;
    background: #fff;
    border: 1px solid #e4e7eb;
    border-radius: 8px;
    padding: 0.45rem 0.7rem;
  }
  .when {
    color: #6b7280;
    min-width: 150px;
  }
  .hint {
    color: #9ca3af;
    font-size: 0.8rem;
    margin: 0;
  }
</style>
