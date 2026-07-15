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
  let editingTask = $state<TaskItem | null>(null);
  let title = $state('');
  let detail = $state('');
  let recurring = $state(false);
  let scheduleKind = $state<'daily' | 'weekly' | 'interval' | 'once'>('daily');
  let scheduleTime = $state('09:00');
  let scheduleDays = $state<number[]>([1]);
  let intervalMinutes = $state(60);
  let onceAt = $state('');
  let planSteps = $state<Array<{id:string; title:string; detail:string; enabled:boolean}>>([]);
  let stepTitle = $state('');
  let stepDetail = $state('');
  let error = $state('');
  let consumedDraft = $state<number | null>(null);
  const scheduledTasks = $derived(tasks.filter((task) => !!task.schedule));

  $effect(() => {
    if (initialDraft && initialDraft.nonce !== consumedDraft) {
      consumedDraft = initialDraft.nonce;
      onDraftConsumed();
      notify('执行请求应直接在聊天中交给主 Agent；这里仅管理定时任务');
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
    const schedule = buildSchedule();
    if (!schedule) { error = '请完整填写定时规则'; return; }
    if (!(await runtime.connect())) {
      error = '运行时未连接，定时任务尚未保存';
      return;
    }
    const wasEditing = !!editingTask;
    if (editingTask) {
      const response = await runtime.request<any>({type:'task.update', id:crypto.randomUUID(), taskId:editingTask.id, title:title.trim(), detail:detail.trim(), schedule, plan:planSteps});
      if (response.type === 'error') { error = response.message; return; }
    } else await runtime.request({type:'task.create', id:crypto.randomUUID(), title:title.trim(), detail:detail.trim(), schedule, plan:planSteps});
    creating = false;
    editingTask = null;
    title = '';
    detail = '';
    planSteps = [];
    await refresh();
    notify(wasEditing ? '定时任务已更新' : `已创建${scheduleLabel(schedule)}定时任务`);
  }

  function openCreate() {
    editingTask = null; title = ''; detail = ''; planSteps = []; stepTitle = ''; stepDetail = ''; recurring = true; error = ''; scheduleKind = 'daily'; scheduleTime = '09:00'; scheduleDays = [1]; intervalMinutes = 60; onceAt = ''; creating = true;
  }
  function openEdit(task: TaskItem) {
    const schedule = task.schedule;
    if (!schedule) return;
    editingTask = task; title = task.title; detail = task.detail || ''; planSteps = (task.plan || []).map((step) => ({...step})); stepTitle = ''; stepDetail = ''; recurring = true; scheduleKind = schedule.kind; scheduleTime = schedule.time || '09:00'; scheduleDays = schedule.days || [1]; intervalMinutes = schedule.intervalMinutes || 60; onceAt = schedule.at ? new Date(schedule.at).toISOString().slice(0,16) : ''; error = ''; creating = true;
  }

  function addPlanStep() {
    if (!stepTitle.trim() || !stepDetail.trim()) { error = '请填写步骤名称和操作说明'; return; }
    planSteps = [...planSteps, {id:crypto.randomUUID(), title:stepTitle.trim(), detail:stepDetail.trim(), enabled:true}];
    stepTitle = ''; stepDetail = ''; error = '';
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
  <PageHeader eyebrow="定时任务" title="定时任务" subtitle="聊天里直接告诉主 Agent 做事；这里只查看和编辑需要按时间自动执行的任务。">
    <button class="primary-button" onclick={openCreate}><Plus size={14} />新建定时任务</button>
  </PageHeader>
  <div class="task-list">
    {#each scheduledTasks as task}
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
          {#if task.plan?.length}<p>☷ {task.plan.length} 个自动化步骤：{task.plan.map((step) => step.title).join(' → ')}</p>{/if}
          {#if task.workflow}<p>⚙ {task.workflow.name} · {task.workflow.agents || task.workflow.stepCount} 个 Agent · {task.workflow.workspace || '当前工作区'}</p>{/if}
          {#if task.agentResults?.length}<details class="task-runs"><summary>子 Agent 结果（{task.agentResults.length}）</summary>{#each task.agentResults as result}<p><b>{result.skillId}</b> · {result.status} · {result.output.slice(0,240)}</p>{/each}</details>{/if}
          {#if task.runs?.length}<details class="task-runs"><summary>运行记录（{task.runs.length}）</summary>{#each task.runs.slice(0,5) as run}<p>{new Date(run.startedAt).toLocaleString('zh-CN')} · {run.status}{run.error ? ` · ${run.error}` : ''}</p>{/each}</details>{/if}
          {#if task.error}<p class="validation-error">{task.error}</p>{/if}
        </div>
        <div class="task-actions">
          <button title="编辑定时任务" aria-label="编辑定时任务" onclick={() => openEdit(task)}>编辑</button>
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
        <h3>还没有定时任务</h3>
        <p>日常执行直接在聊天里交给主 Agent；需要每天、每周或按间隔自动运行时，再在这里设置。</p>
        <button class="primary-button" onclick={openCreate}><Plus size={14} />新建定时任务</button>
      </div>
    {/each}
  </div>
</section>

{#if creating}
  <div class="modal-backdrop" role="presentation" onclick={(event) => { if (event.target === event.currentTarget) creating = false; }}>
    <div class="memory-editor" role="dialog" aria-modal="true" aria-labelledby="task-title">
      <header>
        <div>
          <p class="eyebrow">定时任务</p>
          <h2 id="task-title">{editingTask ? '编辑定时任务' : '新建定时任务'}</h2>
        </div>
        <button aria-label="关闭" onclick={() => (creating = false)}><X size={16} /></button>
      </header>
      <label>任务名称<input bind:value={title} placeholder="例如：整理下载目录中的文件" /></label>
      <label>补充说明<textarea bind:value={detail} rows="4" placeholder="期望结果、限制或需要注意的事项"></textarea></label>
      <div class="automation-steps">
        <div class="setting-inline"><strong>自动化步骤（可选）</strong><span class="settings-note">按顺序执行，类似步骤记录器</span></div>
        {#each planSteps as step, index (step.id)}
          <div class="automation-step"><b>{index + 1}</b><span><strong>{step.title}</strong><small>{step.detail}</small></span><button type="button" aria-label={`删除步骤 ${index + 1}`} onclick={() => planSteps = planSteps.filter((item) => item.id !== step.id)}><X size={13} /></button></div>
        {/each}
        <div class="automation-step-form"><input aria-label="步骤名称" bind:value={stepTitle} placeholder="步骤名称，例如：打开邮件客户端" /><input aria-label="步骤说明" bind:value={stepDetail} placeholder="具体操作，例如：筛选今天未读邮件" /><button type="button" class="quiet-button" onclick={addPlanStep}><Plus size={13} />添加步骤</button></div>
      </div>
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
        <button class="primary-button" onclick={create}>{editingTask ? '保存修改' : '创建定时任务'}</button>
      </footer>
    </div>
  </div>
{/if}
