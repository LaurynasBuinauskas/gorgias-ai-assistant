<script lang="ts">
import type { Operation } from '../lib/admin/state';
import { operationRunning } from '../lib/admin/state';

const {
  operation,
  onDismiss,
}: {
  operation: Operation;
  onDismiss: () => void;
} = $props();

// The steps each kind is expected to walk, in order — the same narration the workflow
// writes, translated for the person watching.
const STEPS: Record<Operation['kind'], readonly { id: string; label: string }[]> = {
  validate: [
    { id: 'queued', label: 'Queued' },
    { id: 'validating', label: 'Reading and checking the document' },
    { id: 'validate-complete', label: 'Check complete' },
  ],
  publish: [
    { id: 'queued', label: 'Queued' },
    { id: 'building-snapshot', label: 'Building a trial version of the knowledge' },
    { id: 'gating', label: 'Testing the trial against the safety checks' },
    { id: 'applying', label: 'Making it live' },
    { id: 'published', label: 'Published' },
  ],
  rollback: [
    { id: 'queued', label: 'Queued' },
    { id: 'building-snapshot', label: 'Rebuilding the previous version' },
    { id: 'gating', label: 'Testing it against the safety checks' },
    { id: 'applying', label: 'Restoring it' },
    { id: 'published', label: 'Restored' },
  ],
};

const TITLES: Record<Operation['kind'], string> = {
  validate: 'Checking the upload',
  publish: 'Publishing',
  rollback: 'Rolling back',
};

const running = $derived(operationRunning(operation));
const steps = $derived(STEPS[operation.kind]);
const stepIndex = $derived(
  operation.status === null ? 0 : steps.findIndex((s) => s.id === operation.status?.step),
);
const failed = $derived(operation.status?.state === 'failed');
const blocked = $derived(operation.status?.step === 'blocked-by-validation');
const cleanCheck = $derived(
  operation.kind === 'validate' &&
    operation.status?.state === 'succeeded' &&
    operation.findings.length === 0,
);
</script>

<div class="card" class:failed>
  <div class="head">
    <span class="title">{TITLES[operation.kind]}</span>
    {#if !running}
      <button class="ghost" onclick={onDismiss}>Dismiss</button>
    {/if}
  </div>

  {#if failed && !blocked && stepIndex === -1}
    <p class="notice error">
      This run failed unexpectedly. Nothing was changed — try again, and if it repeats,
      contact support.
    </p>
  {:else}
    <div class="steps">
      {#each steps as step, index (step.id)}
        {#if index < stepIndex || (index === stepIndex && !running && !failed)}
          <div class="step done"><span class="tick">✓</span>{step.label}</div>
        {:else if index === stepIndex && running}
          <div class="step current"><span class="spinner"></span>{step.label}…</div>
        {:else if index === stepIndex}
          <div class="step done"><span class="tick">✓</span>{step.label}</div>
        {/if}
      {/each}
      {#if blocked}
        <div class="step blocked"><span class="cross">✕</span>Blocked — see below</div>
      {/if}
    </div>
  {/if}

  {#if cleanCheck}
    <p class="notice ok">No issues found. This document is ready to publish.</p>
  {/if}

  {#if operation.findings.length > 0}
    <div class="findings">
      {#each operation.findings as finding, i (i)}
        <p class="notice error">{finding.message}</p>
      {/each}
      <p class="hint">Fix the document and upload it again — nothing was changed.</p>
    </div>
  {/if}
</div>

<style>
  .card {
    background: #fff;
    border: 1px solid #cfe0ef;
    border-radius: 10px;
    padding: 0.8rem 0.9rem;
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
  }
  .card.failed {
    border-color: #f5c2c0;
  }
  .head {
    display: flex;
    justify-content: space-between;
    align-items: center;
  }
  .title {
    font-weight: 600;
  }
  .steps {
    display: flex;
    flex-direction: column;
    gap: 0.3rem;
    font-size: 0.86rem;
  }
  .step {
    display: flex;
    align-items: center;
    gap: 0.45rem;
    color: #374151;
  }
  .tick {
    color: #16a34a;
  }
  .cross {
    color: #b91c1c;
  }
  .spinner {
    width: 12px;
    height: 12px;
    border: 2px solid #cbd5e1;
    border-top-color: #2b6cb0;
    border-radius: 50%;
    animation: spin 0.7s linear infinite;
  }
  @keyframes spin {
    to {
      transform: rotate(360deg);
    }
  }
  .notice {
    border-radius: 8px;
    padding: 0.55rem 0.7rem;
    margin: 0;
    font-size: 0.85rem;
  }
  .notice.error {
    background: #fdecea;
    border: 1px solid #f5c2c0;
  }
  .notice.ok {
    background: #ecfdf3;
    border: 1px solid #b5e2c5;
  }
  .findings {
    display: flex;
    flex-direction: column;
    gap: 0.4rem;
  }
  .hint {
    color: #9ca3af;
    font-size: 0.78rem;
    margin: 0;
  }
  .ghost {
    font: inherit;
    font-size: 0.82rem;
    border: none;
    background: transparent;
    color: #2b6cb0;
    cursor: pointer;
  }
</style>
