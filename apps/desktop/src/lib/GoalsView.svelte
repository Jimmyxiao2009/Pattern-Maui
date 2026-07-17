<script lang="ts">
  import {onMount} from 'svelte';
  import {
    CheckCircle2,
    CircleDashed,
    Pause,
    Play,
    Plus,
    RefreshCw,
    Target,
    Trash2,
    X,
  } from 'lucide-svelte';
  import PageHeader from './PageHeader.svelte';
  import StatusDot from './StatusDot.svelte';
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

  let {notify}: {notify: (message: string) => void} = $props();
  let goals = $state<Goal[]>([]);
  let filter = $state<'active' | 'all'>('active');
  let creating = $state(false);
  let objective = $state('');
  let error = $state('');
  let offline = $state(false);

  const statusOrder: Record<Goal['status'], number> = {
    active: 0,
    paused: 1,
    blocked: 2,
    done: 3,
    cleared: 4,
  };

  const visibleGoals = $derived(
    [...goals]
      .filter((g) => (filter === 'active' ? g.status === 'active' || g.status === 'paused' || g.status === 'blocked' : true))
      .sort((a, b) => statusOrder[a.status] - statusOrder[b.status] || b.updatedAt - a.updatedAt),
  );

  const activeCount = $derived(goals.filter((g) => g.status === 'active' || g.status === 'paused').length);
  const blockedCount = $derived(goals.filter((g) => g.status === 'blocked').length);
  const doneCount = $derived(goals.filter((g) => g.status === 'done').length);

  async function refresh() {
    const connected = await runtime.connect();
    offline = !connected;
    if (!connected) return;
    const goalRes = await runtime.request<any>({type: 'goal.list', id: crypto.randomUUID()});
    if (goalRes.type === 'goal.list.result') goals = goalRes.goals || [];
  }

  onMount(() => {
    void refresh();
    return runtime.on((message: any) => {
      if (message.type === 'goal.updated') {
        if (Array.isArray(message.goals)) goals = message.goals;
        else if (message.goal) {
          const idx = goals.findIndex((g) => g.id === message.goal.id);
          if (idx >= 0) goals[idx] = message.goal;
          else goals = [message.goal, ...goals];
        }
      }
    });
  });

  function openCreateGoal() {
    objective = '';
    error = '';
    creating = true;
  }

  async function createGoal() {
    if (!objective.trim()) {
      error = '请写下可验证的目标';
      return;
    }
    if (!(await runtime.connect())) {
      error = '运行时未连接，目标尚未创建';
      return;
    }
    const res = await runtime.request<any>({
      type: 'goal.set',
      id: crypto.randomUUID(),
      objective: objective.trim(),
    });
    if (res.type === 'error') {
      error = res.message || '创建失败';
      return;
    }
    if (res.type === 'goal.list.result') goals = res.goals || [];
    creating = false;
    objective = '';
    notify('目标已创建并开始推进');
  }

  async function controlGoal(goal: Goal, action: 'pause' | 'resume' | 'clear' | 'complete') {
    if (!(await runtime.connect())) {
      notify('运行时未连接');
      return;
    }
    const res = await runtime.request<any>({
      type: 'goal.control',
      id: crypto.randomUUID(),
      goalId: goal.id,
      action,
    });
    if (res.type === 'error') {
      notify(res.message || '操作失败');
      return;
    }
    if (res.type === 'goal.list.result') goals = res.goals || [];
    const labels = {pause: '已暂停目标', resume: '已恢复目标', clear: '已清除目标', complete: '已标记完成'} as const;
    notify(labels[action]);
  }

  function goalStatusLabel(status: Goal['status']) {
    return (
      {
        active: '进行中',
        paused: '已暂停',
        done: '已完成',
        blocked: '已阻塞',
        cleared: '已清除',
      } as const
    )[status];
  }
</script>

<section class="view goals-view">
  <PageHeader
    eyebrow="Goal"
    title="目标"
    subtitle="跨回合的 run-until-done 目标。当前对话里的待办清单请用 /plan，会出现在聊天输入框上方。"
  >
    <button class="quiet-button" onclick={() => void refresh()}><RefreshCw size={14} />刷新</button>
    <button class="primary-button" onclick={openCreateGoal}><Plus size={14} />新建目标</button>
  </PageHeader>

  {#if offline}
    <p class="settings-note" style="padding:0 42px 12px">运行时未连接，列表可能不是最新状态。</p>
  {/if}

  <div class="proactive-overview">
    <article>
      <span class="overview-icon amber"><Target size={16} /></span>
      <div><strong>{activeCount}</strong><small>进行中 / 暂停</small></div>
    </article>
    <article>
      <span class="overview-icon blue"><CircleDashed size={16} /></span>
      <div><strong>{blockedCount}</strong><small>阻塞</small></div>
    </article>
    <article>
      <span class="overview-icon green"><CheckCircle2 size={16} /></span>
      <div><strong>{doneCount}</strong><small>已完成</small></div>
    </article>
  </div>

  <div class="goals-filter" role="group" aria-label="目标筛选">
    <button class:active={filter === 'active'} onclick={() => (filter = 'active')}>当前</button>
    <button class:active={filter === 'all'} onclick={() => (filter = 'all')}>全部</button>
  </div>

  <div class="task-list goals-list">
    {#each visibleGoals as goal (goal.id)}
      <article>
        <div class="task-state">
          <StatusDot active={goal.status === 'active'} off={goal.status === 'cleared' || goal.status === 'done'} />
        </div>
        <div class="task-copy">
          <div>
            <span
              class="badge"
              class:amber={goal.status === 'active' || goal.status === 'blocked'}
              class:green={goal.status === 'done'}
              class:dim={goal.status === 'cleared' || goal.status === 'paused'}
            >{goalStatusLabel(goal.status)}</span>
            {#if goal.taskId}<span class="badge dim">任务 · {goal.taskId.slice(0, 8)}</span>{/if}
            <time>{new Date(goal.updatedAt).toLocaleString('zh-CN')}</time>
          </div>
          <h3>{goal.objective}</h3>
          {#if goal.blockedReason}<p class="validation-error">阻塞：{goal.blockedReason}</p>{/if}
          {#if goal.progress?.length}
            <details class="task-runs" open={goal.status === 'active' || goal.status === 'blocked'}>
              <summary>进度（{goal.progress.length}）</summary>
              {#each goal.progress.slice(-8).reverse() as line}
                <p>{line}</p>
              {/each}
            </details>
          {/if}
        </div>
        <div class="task-actions">
          {#if goal.status === 'active'}
            <button title="暂停" aria-label="暂停目标" onclick={() => controlGoal(goal, 'pause')}><Pause size={14} /></button>
            <button title="标记完成" aria-label="标记完成" onclick={() => controlGoal(goal, 'complete')}><CheckCircle2 size={14} /></button>
            <button title="清除" aria-label="清除目标" onclick={() => controlGoal(goal, 'clear')}><Trash2 size={14} /></button>
          {:else if goal.status === 'paused'}
            <button title="恢复" aria-label="恢复目标" onclick={() => controlGoal(goal, 'resume')}><Play size={14} /></button>
            <button title="清除" aria-label="清除目标" onclick={() => controlGoal(goal, 'clear')}><Trash2 size={14} /></button>
          {:else if goal.status === 'blocked'}
            <button title="标记完成" aria-label="标记完成" onclick={() => controlGoal(goal, 'complete')}><CheckCircle2 size={14} /></button>
            <button title="清除" aria-label="清除目标" onclick={() => controlGoal(goal, 'clear')}><Trash2 size={14} /></button>
          {/if}
        </div>
      </article>
    {:else}
      <div class="blank-state">
        <div class="blank-mark">◎</div>
        <h3>{filter === 'active' ? '当前没有进行中的目标' : '还没有目标'}</h3>
        <p>用「新建目标」或聊天里的 <code>/goal</code> 设定可验证目标。会话内的分步待办请用 <code>/plan</code>。</p>
        <button class="primary-button" onclick={openCreateGoal}><Plus size={14} />新建目标</button>
      </div>
    {/each}
  </div>
</section>

{#if creating}
  <div class="modal-backdrop" role="presentation" onclick={(event) => { if (event.target === event.currentTarget) creating = false; }}>
    <div class="memory-editor" role="dialog" aria-modal="true" aria-labelledby="goal-form-title">
      <header>
        <div>
          <p class="eyebrow">Goal</p>
          <h2 id="goal-form-title">新建目标</h2>
        </div>
        <button aria-label="关闭" onclick={() => (creating = false)}><X size={16} /></button>
      </header>
      <label>
        可验证的目标
        <textarea bind:value={objective} rows="5" maxlength="2000" placeholder="例如：让计算器算出 1+1 并验证结果是 2"></textarea>
        <small class="field-help">会创建关联任务并排队执行；可用暂停 / 恢复 / 完成管理。</small>
      </label>
      {#if error}<p class="validation-error">{error}</p>{/if}
      <footer>
        <button onclick={() => (creating = false)}>取消</button>
        <button class="primary-button" onclick={createGoal}>创建并推进</button>
      </footer>
    </div>
  </div>
{/if}
