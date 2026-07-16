<script lang="ts">
  export type ContextMenuItem = {
    id: string;
    label: string;
    danger?: boolean;
    disabled?: boolean;
  };

  let {
    open = false,
    x = 0,
    y = 0,
    items = [],
    onSelect,
    onClose,
  }: {
    open?: boolean;
    x?: number;
    y?: number;
    items?: ContextMenuItem[];
    onSelect: (id: string) => void;
    onClose: () => void;
  } = $props();

  let root = $state<HTMLDivElement>();

  $effect(() => {
    if (!open) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };
    const onPointer = (event: MouseEvent) => {
      if (root && !root.contains(event.target as Node)) onClose();
    };
    window.addEventListener('keydown', onKey);
    window.addEventListener('mousedown', onPointer, true);
    return () => {
      window.removeEventListener('keydown', onKey);
      window.removeEventListener('mousedown', onPointer, true);
    };
  });

  $effect(() => {
    if (!open || !root) return;
    const rect = root.getBoundingClientRect();
    const maxX = Math.max(8, window.innerWidth - rect.width - 8);
    const maxY = Math.max(8, window.innerHeight - rect.height - 8);
    root.style.left = `${Math.min(Math.max(8, x), maxX)}px`;
    root.style.top = `${Math.min(Math.max(8, y), maxY)}px`;
  });
</script>

{#if open}
  <div class="context-menu" role="menu" bind:this={root} style={`left:${x}px;top:${y}px`}>
    {#each items as item}
      <button
        type="button"
        role="menuitem"
        class:danger={item.danger}
        disabled={item.disabled}
        onclick={() => {
          if (item.disabled) return;
          onSelect(item.id);
          onClose();
        }}
      >
        {item.label}
      </button>
    {/each}
  </div>
{/if}
