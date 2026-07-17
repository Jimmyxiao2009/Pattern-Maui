<script lang="ts">
  import {CheckCircle2, CircleDashed, Pause, Play, Target, Trash2, ChevronDown, ChevronRight, Loader2} from 'lucide-svelte';
  import {runtime} from './runtime';

  type Goal = {
    id: string;
    objective: string;
    status: 'active' | 'paused' | 'done' | 'blocked' | 'cleared';
    createdAt: number;
    updatedAt: number;
    taskId?: string;
    conversationId?: string;
    progress: string[];
    blockedReason?: string;
  };

  let {
    collapsed = false,
    onToggle = () => {},
    onOpenGoals = () => {},
  }: {
    collapsed?: boolean;
    onToggle?: () => void;
    onOpenGoals?: () => void;
  } = $props();

  let goal = $state<Goal | null>(null);
  let loading = $state(false);
  let flash = $state(false);
  let lastProgressLen = $state(0);

  const visible = $derived(!!goal && (goal.status === 'active' || goal.status === 'paused' || goal.status === 'blocked'));
  const progress = $derived([...(goal?.progress || [])].slice(-8).reverse());

  function statusLabel(status: Goal['status']) {
    return ({active: '进行中', paused: '已暂停', blocked: '已阻塞', done: '已完成', cleared: '已清除'} as const)[status];
  }

  function applyGoal(next: Goal | null) {
    if (next && lastProgressLen && (next.progress?.length || 0) > lastProgressLen) {
      flash = true;
      window.setTimeout(() => (flash = false), 900);
      if (collapsed) onToggle();
    }
    lastProgressLen = next?.progress?.length || 0;
    goal = next;
  }

  async function refresh() {
    if (!(await runtime.connect())) return;
    loading = true;
    try {
      const res = await runtime.request<any>({type: 'goal.list', id: crypto.randomUUID()});
      if (res.type === 'goal.list.result') {
        const goals: Goal[] = res.goals || [];
        applyGoal(goals.find((g) => g.status === 'active' || g.status === 'paused' || g.status === 'blocked') || null);
      }
    } finally {
      loading = false;
    }
  }

  async function control(action: 'pause' | 'resume' | 'clear' | 'complete') {
    if (!goal || !(await runtime.connect())) return;
    const res = await runtime.request<any>({
      type: 'goal.control',
      id: crypto.randomUUID(),
      goalId: goal.id,
      action,
    });
    if (res.type === 'goal.list.result') {
      const goals: Goal[] = res.goals || [];
      applyGoal(goals.find((g) => g.id === goal!.id && (g.status === 'active' || g.status === 'paused' || g.status === 'blocked')) || null);
    } else if (res.type === 'error') {
      // keep current
    }
  }

  $effect(() => {
    void refresh();
    const off = runtime.on((message: any) => {
      if (message.type === 'goal.updated') {
        if (Array.isArray(message.goals)) {
          applyGoal(message.goals.find((g: Goal) => g.status === 'active' || g.status === 'paused' || g.status === 'blocked') || null);
        } else if (message.goal) {
          const g = message.goal as Goal;
          if (g.status === 'active' || g.status === 'paused' || g.status === 'blocked') applyGoal(g);
          else if (goal?.id === g.id) applyGoal(null);
        }
      }
    });
    return () => off();
  });
</script>

{#if visible && goal}
  <aside class="agent-dock goal-dock" class:collapsed class:paused={goal.status === 'paused'} class:blocked={goal.status === 'blocked'} class:flash aria-label="当前目标">
    <header class="agent-dock-head">
      <button type="button" class="agent-dock-toggle" onclick={onToggle} aria-expanded={!collapsed}>
        <span class="agent-dock-icon goal"><Target size={14} /></span>
        <div class="agent-dock-titles">
          <div class="agent-dock-row">
            <strong>Goal</strong>
            <span class="agent-dock-chip" class:amber={goal.status === 'active'} class:dim={goal.status === 'paused'} class:danger={goal.status === 'blocked'}>
              {statusLabel(goal.status)}
            </span>
            {#if loading}<span class="spin" aria-hidden="true"><Loader2 size={12} /></span>{/if}
          </div>
          <small title={goal.objective}>{goal.objective}</small>
        </div>
        {#if collapsed}<ChevronRight size={15} />{:else}<ChevronDown size={15} />{/if}
      </button>
      <div class="agent-dock-toolbar">
        {#if goal.status === 'active'}
          <button type="button" class="quiet-button tiny" title="暂停" onclick={() => control('pause')}><Pause size={12} />暂停</button>
          <button type="button" class="quiet-button tiny primary-tint" title="标记完成" onclick={() => control('complete')}><CheckCircle2 size={12} />完成</button>
        {:else if goal.status === 'paused'}
          <button type="button" class="quiet-button tiny primary-tint" title="恢复" onclick={() => control('resume')}><Play size={12} />恢复</button>
        {:else if goal.status === 'blocked'}
          <button type="button" class="quiet-button tiny" title="标记完成" onclick={() => control('complete')}><CheckCircle2 size={12} />完成</button>
        {/if}
        <button type="button" class="quiet-button tiny" title="清除目标" onclick={() => control('clear')}><Trash2 size={12} /></button>
      </div>
    </header>
    {#if !collapsed}
      <div class="agent-dock-body">
        {#if goal.blockedReason}
          <p class="agent-dock-alert"><CircleDashed size={13} />阻塞：{goal.blockedReason}</p>
        {/if}
        {#if goal.taskId}
          <p class="agent-dock-meta">关联任务 <code>{goal.taskId.slice(0, 8)}</code> · 更新于 {new Date(goal.updatedAt).toLocaleString('zh-CN')}</p>
        {/if}
        {#if progress.length}
          <ol class="agent-progress-list">
            {#each progress as line, i}
              <li class:latest={i === 0}>{line}</li>
            {/each}
          </ol>
        {:else}
          <p class="agent-dock-empty">还没有进度。Agent 会用 pattern.update_goal 追加说明。</p>
        {/if}
        <footer class="agent-dock-foot">
          <span>跨回合持续推进 · 完成前会一直显示在这里</span>
          <button type="button" class="quiet-button tiny" onclick={onOpenGoals}>全部目标</button>
        </footer>
      </div>
    {/if}
  </aside>
{/if}
