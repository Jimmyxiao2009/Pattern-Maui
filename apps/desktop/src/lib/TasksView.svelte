<script lang="ts">
  import {onMount} from 'svelte';
  import {Plus, Pause, Play, Square, Trash2, X, ShieldAlert} from 'lucide-svelte';
  import PageHeader from './PageHeader.svelte';
  import StatusDot from './StatusDot.svelte';
  import type {TaskItem} from './types';
  import {runtime} from './runtime';

  let {notify, initialDraft = null, onDraftConsumed = () => {}}: {
    notify: (message: string) => void;
    initialDraft?: {title: string; detail: string; nonce: number} | null;
    onDraftConsumed?: () => void;
  } = $props();
  let tasks = $state<TaskItem[]>([]);
  let creating = $state(false);
  let title = $state('');
  let detail = $state('');
  let recurring = $state(false);
  let scheduleKind = $state<'daily' | 'weekly' | 'interval' | 'once'>('daily');
  let scheduleTime = $state('09:00');
  let scheduleDays = $state<number[]>([1]);
  let intervalMinutes = $state(60);
  let onceAt = $state('');
  let error = $state('');
  let consumedDraft = $state<number | null>(null);

  $effect(() => {
    if (initialDraft && initialDraft.nonce !== consumedDraft) {
      title = initialDraft.title;
      detail = initialDraft.detail;
      creating = true;
      consumedDraft = initialDraft.nonce;
      onDraftConsumed();
    }
  });

  async function refresh() {
    if (!(await runtime.connect())) return;
    const res = await runtime.request<any>({type: 'task.list', id: crypto.randomUUID()});
    if (res.type === 'task.list.result') tasks = res.tasks;
  }

  onMount(() => {
    void refresh();
    return runtime.on((message) => {
      if (message.type === 'task.updated') {
        const idx = tasks.findIndex((t) => t.id === message.task.id);
        if (idx >= 0) tasks[idx] = message.task as TaskItem;
        else tasks = [message.task as TaskItem, ...tasks];
        if (message.task.status === 'awaiting_approval' && (window as any).__TAURI_INTERNALS__) {
          void import('@tauri-apps/api/core').then(({invoke}) => invoke('show_review'));
        }
      }
      if (message.type === 'task.approval_required' && (window as any).__TAURI_INTERNALS__) {
        void import('@tauri-apps/api/core').then(({invoke}) => invoke('show_review'));
      }
    });
  });

  async function create() {
    if (!title.trim()) {
      error = '请写下任务名称';
      return;
    }
    const schedule = recurring ? buildSchedule() : undefined;
    if (recurring && !schedule) { error = '请完整填写定时规则'; return; }
    const scheduled = !!schedule;
    if (!(await runtime.connect())) {
      // fallback local queue
      tasks.unshift({
        id: crypto.randomUUID(),
        title: title.trim(),
        detail: detail.trim(),
        status: scheduled ? 'scheduled' : 'queued',
        createdAt: new Date().toLocaleString('zh-CN'),
        schedule,
      });
      creating = false;
      title = '';
      detail = '';
      recurring = false;
      notify('任务已加入本地队列（运行时未连接）');
      return;
    }
    await runtime.request({
      type: 'task.create',
      id: crypto.randomUUID(),
      title: title.trim(),
      detail: detail.trim(),
      schedule,
    });
    creating = false;
    title = '';
    detail = '';
    recurring = false;
    await refresh();
    notify(scheduled ? `已创建${scheduleLabel(schedule!)}定时任务` : '任务已提交执行引擎');
    if (!scheduled && (window as any).__TAURI_INTERNALS__) {
      const {invoke} = await import('@tauri-apps/api/core');
      await invoke('show_review');
    }
  }

  function buildSchedule(): TaskItem['schedule'] | undefined {
    if (scheduleKind === 'daily') return {kind:'daily', time:scheduleTime, enabled:true};
    if (scheduleKind === 'weekly') return scheduleDays.length ? {kind:'weekly', time:scheduleTime, days:scheduleDays, enabled:true} : undefined;
    if (scheduleKind === 'interval') return intervalMinutes > 0 ? {kind:'interval', intervalMinutes, enabled:true} : undefined;
    const at = onceAt ? new Date(onceAt).getTime() : 0;
    return at > Date.now() ? {kind:'once', at, enabled:true} : undefined;
  }
  function scheduleLabel(schedule: NonNullable<TaskItem['schedule']>) {
    if (schedule.kind === 'daily') return `每日 ${schedule.time}`;
    if (schedule.kind === 'weekly') return `每周${(schedule.days || []).map((d)=>'日一二三四五六'[d]).join('、')} ${schedule.time}`;
    if (schedule.kind === 'interval') return `每 ${schedule.intervalMinutes} 分钟`;
    return `于 ${new Date(schedule.at || 0).toLocaleString('zh-CN')}`;
  }
  const weekdays = ['日','一','二','三','四','五','六'];

  async function control(task: TaskItem, action: 'pause' | 'resume' | 'cancel') {
    if (await runtime.connect()) {
      await runtime.request({type: 'task.control', id: crypto.randomUUID(), taskId: task.id, action});
      await refresh();
    } else {
      task.status = action === 'cancel' ? 'cancelled' : action === 'pause' ? 'paused' : 'queued';
    }
    notify(action === 'cancel' ? '任务已终止' : action === 'pause' ? '任务已暂停' : '任务已恢复');
  }

  async function remove(id: string) {
    const task = tasks.find((item)=>item.id===id);
    if (task && ['running','queued','paused','awaiting_approval'].includes(task.status)) {
      notify('请先终止任务，再删除记录'); return;
    }
    if (await runtime.connect()) {
      const response = await runtime.request<any>({type:'task.delete',id:crypto.randomUUID(),taskId:id});
      if (response.type==='task.list.result') tasks=response.tasks;
    } else tasks = tasks.filter((item) => item.id !== id);
    notify('任务记录已删除');
  }

  function statusLabel(status: TaskItem['status']) {
    return (
      {
        queued: '排队中',
        scheduled: '定时待命',
        running: '执行中',
        paused: '已暂停',
        awaiting_approval: '等待确认',
        cancelled: '已终止',
        done: '已完成',
        failed: '失败',
      } as const
    )[status];
  }
</script>

<section class="view">
  <PageHeader eyebrow="执行槽" title="任务" subtitle="创建后会走 Computer Use 管线：截屏 → 分级动作 → 必要时人工确认。">
    <button class="primary-button" onclick={() => (creating = true)}><Plus size={14} />新任务</button>
  </PageHeader>
  <div class="task-list">
    {#each tasks as task}
      <article>
        <div class="task-state">
          <StatusDot active={task.status === 'running' || task.status === 'queued' || task.status === 'scheduled'} off={task.status === 'cancelled' || task.status === 'failed'} />
        </div>
        <div class="task-copy">
          <div>
            <span class="badge" class:amber={task.status === 'queued' || task.status === 'awaiting_approval' || task.status === 'scheduled'} class:green={task.status === 'done'} class:dim={task.status === 'cancelled'}>{statusLabel(task.status)}</span>
            {#if task.riskTier !== undefined}<span class="badge">T{task.riskTier}</span>{/if}
            <time>{task.createdAt}</time>
          </div>
          <h3>{task.title}</h3>
          {#if task.detail}<p>{task.detail}</p>{/if}
          {#if task.schedule}<p>↻ {scheduleLabel(task.schedule)} 自动执行 · 已运行 {task.runCount || 0} 次</p>{/if}
          {#if task.workflow}<p>⚙ {task.workflow.name} · {task.workflow.agents || task.workflow.stepCount} 个 Agent · {task.workflow.workspace || '当前工作区'}</p>{/if}
          {#if task.agentResults?.length}<details class="task-runs"><summary>子 Agent 结果（{task.agentResults.length}）</summary>{#each task.agentResults as result}<p><b>{result.skillId}</b> · {result.status} · {result.output.slice(0,240)}</p>{/each}</details>{/if}
          {#if task.runs?.length}<details class="task-runs"><summary>运行记录（{task.runs.length}）</summary>{#each task.runs.slice(0,5) as run}<p>{new Date(run.startedAt).toLocaleString('zh-CN')} · {run.status}{run.error ? ` · ${run.error}` : ''}</p>{/each}</details>{/if}
          {#if task.error}<p class="validation-error">{task.error}</p>{/if}
        </div>
        <div class="task-actions">
          {#if task.status === 'awaiting_approval'}
            <button title="打开审查窗" aria-label="打开审查窗" onclick={async () => (window as any).__TAURI_INTERNALS__ && (await import('@tauri-apps/api/core')).invoke('show_review')}>
              <ShieldAlert size={14} />
            </button>
          {/if}
          {#if task.status === 'queued' || task.status === 'running' || task.status === 'scheduled'}
            <button title="暂停" aria-label="暂停" onclick={() => control(task, 'pause')}><Pause size={14} /></button>
          {:else if task.status === 'paused'}
            <button title="恢复" aria-label="恢复" onclick={() => control(task, 'resume')}><Play size={14} /></button>
          {/if}
          {#if task.status !== 'cancelled' && task.status !== 'done' && task.status !== 'scheduled'}
            <button title="终止" aria-label="终止" onclick={() => control(task, 'cancel')}><Square size={12} /></button>
          {/if}
          <button title="删除记录" aria-label="删除记录" disabled={task.status === 'running' || task.status === 'queued' || task.status === 'paused' || task.status === 'awaiting_approval'} onclick={() => remove(task.id)}><Trash2 size={14} /></button>
        </div>
      </article>
    {:else}
      <div class="blank-state">
        <div class="blank-mark">⌁</div>
        <h3>还没有任务</h3>
        <p>创建任务后，执行引擎会截屏并按安全分级推进；T2 动作会弹出审查窗。</p>
        <button class="primary-button" onclick={() => (creating = true)}><Plus size={14} />创建第一个任务</button>
      </div>
    {/each}
  </div>
</section>

{#if creating}
  <div class="modal-backdrop" role="presentation" onclick={(event) => { if (event.target === event.currentTarget) creating = false; }}>
    <div class="memory-editor" role="dialog" aria-modal="true" aria-labelledby="task-title">
      <header>
        <div>
          <p class="eyebrow">执行槽</p>
          <h2 id="task-title">创建任务</h2>
        </div>
        <button aria-label="关闭" onclick={() => (creating = false)}><X size={16} /></button>
      </header>
      <label>任务名称<input bind:value={title} placeholder="例如：整理下载目录中的文件" /></label>
      <label>补充说明<textarea bind:value={detail} rows="4" placeholder="期望结果、限制或需要注意的事项"></textarea></label>
      <label class="task-schedule-toggle"><input type="checkbox" bind:checked={recurring} /> 设为定时任务（Agent 也可理解“每天 09:00 …”后创建）</label>
      {#if recurring}
        <label>执行规则<select bind:value={scheduleKind}><option value="daily">每天</option><option value="weekly">每周</option><option value="interval">按间隔</option><option value="once">仅一次</option></select></label>
        {#if scheduleKind === 'daily' || scheduleKind === 'weekly'}<label>执行时间<input type="time" bind:value={scheduleTime} /></label>{/if}
        {#if scheduleKind === 'weekly'}<div class="weekday-picker">{#each weekdays as day, i}<label><input type="checkbox" checked={scheduleDays.includes(i)} onchange={() => scheduleDays = scheduleDays.includes(i) ? scheduleDays.filter((item)=>item!==i) : [...scheduleDays, i]} />周{day}</label>{/each}</div>{/if}
        {#if scheduleKind === 'interval'}<label>间隔分钟<input type="number" min="1" bind:value={intervalMinutes} /></label>{/if}
        {#if scheduleKind === 'once'}<label>执行时间<input type="datetime-local" bind:value={onceAt} /></label>{/if}
      {/if}
      {#if error}<p class="validation-error">{error}</p>{/if}
      <footer>
        <button onclick={() => (creating = false)}>取消</button>
        <button class="primary-button" onclick={create}>开始执行</button>
      </footer>
    </div>
  </div>
{/if}
