<script lang="ts">
  import {ChevronDown, ChevronRight, Pause, Play, RefreshCw, Timer, Trash2} from 'lucide-svelte';
  import {runtime} from './runtime';
  import type {TaskItem} from './types';

  let {
    collapsed = false,
    onToggle = () => {},
    onOpenTasks = () => {},
  }: {
    collapsed?: boolean;
    onToggle?: () => void;
    onOpenTasks?: () => void;
  } = $props();

  let loops = $state<TaskItem[]>([]);
  let loading = $state(false);

  const hasLoops = $derived(loops.length > 0);

  function isLoopTask(task: TaskItem): boolean {
    if (/\[Loop\]/i.test(task.detail || '')) return true;
    return task.schedule?.kind === 'interval' && task.schedule.enabled !== false && !['cancelled', 'done', 'failed'].includes(task.status);
  }

  function intervalLabel(task: TaskItem) {
    const m = task.schedule?.intervalMinutes;
    if (!m) return '循环';
    if (m >= 60 && m % 60 === 0) return `每 ${m / 60} 小时`;
    return `每 ${m} 分钟`;
  }

  async function refresh() {
    if (!(await runtime.connect())) return;
    loading = true;
    try {
      const res = await runtime.request<any>({type: 'task.list', id: crypto.randomUUID()});
      if (res.type === 'task.list.result') {
        loops = (res.tasks || []).filter(isLoopTask);
      }
    } finally {
      loading = false;
    }
  }

  async function control(task: TaskItem, action: 'pause' | 'resume' | 'cancel') {
    if (!(await runtime.connect())) return;
    const res = await runtime.request<any>({type: 'task.control', id: crypto.randomUUID(), taskId: task.id, action});
    if (res.type === 'task.list.result') loops = (res.tasks || []).filter(isLoopTask);
    else await refresh();
  }

  $effect(() => {
    void refresh();
    const off = runtime.on((message: any) => {
      if (message.type === 'task.updated' || message.type === 'task.list.result') {
        void refresh();
      }
    });
    return () => off();
  });
</script>

{#if hasLoops}
  <aside class="agent-dock loop-dock" class:collapsed aria-label="循环任务">
    <header class="agent-dock-head">
      <button type="button" class="agent-dock-toggle" onclick={onToggle} aria-expanded={!collapsed}>
        <span class="agent-dock-icon loop"><Timer size={14} /></span>
        <div class="agent-dock-titles">
          <div class="agent-dock-row">
            <strong>Loop</strong>
            <span class="agent-dock-chip amber">{loops.length} 个循环</span>
            {#if loading}<RefreshCw size={12} />{/if}
          </div>
          <small>{loops[0]?.title}{loops.length > 1 ? ` 等` : ''}</small>
        </div>
        {#if collapsed}<ChevronRight size={15} />{:else}<ChevronDown size={15} />{/if}
      </button>
      <div class="agent-dock-toolbar">
        <button type="button" class="quiet-button tiny" onclick={onOpenTasks}>任务页</button>
      </div>
    </header>
    {#if !collapsed}
      <ul class="loop-list">
        {#each loops as task (task.id)}
          <li>
            <div class="loop-copy">
              <strong>{task.title}</strong>
              <small>{intervalLabel(task)} · {task.status}{task.nextRunAt ? ` · 下次 ${new Date(task.nextRunAt).toLocaleString('zh-CN')}` : ''}</small>
            </div>
            <div class="agent-dock-toolbar">
              {#if task.status === 'paused' || task.schedule?.enabled === false}
                <button type="button" class="quiet-button tiny" onclick={() => control(task, 'resume')}><Play size={12} /></button>
              {:else}
                <button type="button" class="quiet-button tiny" onclick={() => control(task, 'pause')}><Pause size={12} /></button>
              {/if}
              <button type="button" class="quiet-button tiny" onclick={() => control(task, 'cancel')}><Trash2 size={12} /></button>
            </div>
          </li>
        {/each}
      </ul>
      <footer class="agent-dock-foot"><span>/loop 创建的周期任务会挂在这里</span></footer>
    {/if}
  </aside>
{/if}
