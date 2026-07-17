<script lang="ts">
  import {SLASH_CATALOG} from '@pattern/core';

  let {
    query = '',
    onPick,
  }: {
    query: string;
    onPick: (command: string) => void;
  } = $props();

  const items = $derived.by(() => {
    const q = query.replace(/^\//, '').toLowerCase();
    const list = SLASH_CATALOG.filter(
      (item) => !q || item.command.toLowerCase().includes(q) || item.summary.includes(q),
    );
    return list.slice(0, 8);
  });
</script>

{#if items.length}
  <div class="slash-menu" role="listbox" aria-label="斜杠指令">
    {#each items as item (item.command + item.example)}
      <button type="button" class="slash-item" role="option" aria-selected="false" onclick={() => onPick(item.command + (item.command.includes(' ') ? '' : ' '))}>
        <strong>{item.command}</strong>
        <span>{item.summary}</span>
        <small>{item.example}</small>
      </button>
    {/each}
  </div>
{/if}
