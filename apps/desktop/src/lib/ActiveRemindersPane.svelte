<script lang="ts">
  import {Bell, ChevronDown, ChevronRight, Trash2} from 'lucide-svelte';
  import {runtime} from './runtime';

  type Trigger = {id: string; time: string; message: string; enabled: boolean};

  let {
    collapsed = false,
    onToggle = () => {},
    onOpenProactive = () => {},
  }: {
    collapsed?: boolean;
    onToggle?: () => void;
    onOpenProactive?: () => void;
  } = $props();

  let triggers = $state<Trigger[]>([]);
  let loading = $state(false);

  const enabled = $derived(triggers.filter((t) => t.enabled));
  const has = $derived(enabled.length > 0);

  async function refresh() {
    if (!(await runtime.connect())) return;
    loading = true;
    try {
      const res = await runtime.request<any>({type: 'cron.getConfig', id: crypto.randomUUID()});
      if (res.type === 'cron.config') triggers = res.triggers || [];
    } finally {
      loading = false;
    }
  }

  async function remove(id: string) {
    if (!(await runtime.connect())) return;
    const next = triggers.filter((t) => t.id !== id);
    const res = await runtime.request<any>({type: 'cron.setConfig', id: crypto.randomUUID(), triggers: next});
    if (res.type === 'cron.config') triggers = res.triggers || [];
  }

  async function toggle(trigger: Trigger) {
    if (!(await runtime.connect())) return;
    const next = triggers.map((t) => (t.id === trigger.id ? {...t, enabled: !t.enabled} : t));
    const res = await runtime.request<any>({type: 'cron.setConfig', id: crypto.randomUUID(), triggers: next});
    if (res.type === 'cron.config') triggers = res.triggers || [];
  }

  $effect(() => {
    void refresh();
    const off = runtime.on((message: any) => {
      if (message.type === 'cron.config') triggers = message.triggers || [];
    });
    return () => off();
  });
</script>

{#if has}
  <aside class="agent-dock remind-dock" class:collapsed aria-label="每日提醒">
    <header class="agent-dock-head">
      <button type="button" class="agent-dock-toggle" onclick={onToggle} aria-expanded={!collapsed}>
        <span class="agent-dock-icon remind"><Bell size={14} /></span>
        <div class="agent-dock-titles">
          <div class="agent-dock-row">
            <strong>Remind</strong>
            <span class="agent-dock-chip amber">{enabled.length} 条</span>
          </div>
          <small>{enabled[0]?.time} · {enabled[0]?.message}{enabled.length > 1 ? ' …' : ''}</small>
        </div>
        {#if collapsed}<ChevronRight size={15} />{:else}<ChevronDown size={15} />{/if}
      </button>
      <div class="agent-dock-toolbar">
        <button type="button" class="quiet-button tiny" onclick={onOpenProactive}>主动页</button>
      </div>
    </header>
    {#if !collapsed}
      <ul class="loop-list">
        {#each enabled as trigger (trigger.id)}
          <li>
            <div class="loop-copy">
              <strong>{trigger.time}</strong>
              <small>{trigger.message}</small>
            </div>
            <div class="agent-dock-toolbar">
              <button type="button" class="quiet-button tiny" onclick={() => toggle(trigger)}>暂停</button>
              <button type="button" class="quiet-button tiny" onclick={() => remove(trigger.id)}><Trash2 size={12} /></button>
            </div>
          </li>
        {/each}
      </ul>
      <footer class="agent-dock-foot"><span>/remind HH:MM 文案 · 每日系统提醒</span></footer>
    {/if}
  </aside>
{/if}
