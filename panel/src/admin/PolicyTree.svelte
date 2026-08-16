<script lang="ts">
import type { PolicyDocument } from '../lib/admin/api';

const {
  documents,
  onReplace,
}: {
  documents: readonly PolicyDocument[];
  onReplace: (market: string, topic: string) => void;
} = $props();

const byMarket = $derived.by(() => {
  const groups = new Map<string, PolicyDocument[]>();
  for (const document of documents) {
    const group = groups.get(document.market) ?? [];
    group.push(document);
    groups.set(document.market, group);
  }
  return [...groups.entries()];
});

function label(topic: string): string {
  return topic.length > 0 ? topic.replaceAll('-', ' ') : '(no topic)';
}
</script>

<div class="tree">
  {#each byMarket as [market, group] (market)}
    <div class="market">
      <div class="market-name">{market}</div>
      {#each group as document (document.sourcePath)}
        <div class="doc">
          <span class="topic" title={document.sourcePath}>{label(document.topic)}</span>
          <span class="chunks">{document.chunks} section{document.chunks === 1 ? '' : 's'}</span>
          {#if document.sourcePath.startsWith('staged/')}
            <span class="origin">from an upload</span>
          {/if}
          <button class="ghost" onclick={() => onReplace(document.market, document.topic)}>
            Replace
          </button>
        </div>
      {/each}
    </div>
  {/each}
</div>

<style>
  .tree {
    display: flex;
    flex-direction: column;
    gap: 0.8rem;
  }
  .market-name {
    font-weight: 600;
    font-size: 0.8rem;
    letter-spacing: 0.03em;
    color: #6b7280;
    margin-bottom: 0.25rem;
  }
  .doc {
    display: flex;
    align-items: center;
    gap: 0.6rem;
    background: #fff;
    border: 1px solid #e4e7eb;
    border-radius: 8px;
    padding: 0.4rem 0.7rem;
    margin-bottom: 0.3rem;
  }
  .topic {
    font-weight: 500;
    flex: 1;
  }
  .chunks,
  .origin {
    color: #6b7280;
    font-size: 0.78rem;
  }
  .origin {
    background: #eef3f9;
    border-radius: 999px;
    padding: 0.05rem 0.5rem;
  }
  .ghost {
    font: inherit;
    font-size: 0.82rem;
    border: none;
    background: transparent;
    color: #2b6cb0;
    cursor: pointer;
    padding: 0.2rem 0.4rem;
  }
</style>
