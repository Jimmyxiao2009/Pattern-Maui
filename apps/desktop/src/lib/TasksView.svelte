<script lang="ts">
  import {onMount} from 'svelte';
  import {Bell, Plus, Pause, Play, RotateCcw, Square, Trash2, X, ShieldAlert} from 'lucide-svelte';
  import PageHeader from './PageHeader.svelte';
  import StatusDot from './StatusDot.svelte';
  import type {TaskItem} from './types';
  import {runtime} from './runtime';

  /** open = 未完成（默认，不含已完成）；done = 已完成归档 */
  type TaskFilter = 'open' | 'active' | 'scheduled' | 'remind' | 'done';

  type Reminder = {id: string; time: string; message: string; enabled: boolean};

  let {notify, initialDraft = null, onDraftConsumed = () => {}}: {
    notify: (message: string) => void;
    initialDraft?: {title: string; detail: string; nonce: number} | null;
    onDraftConsumed?: () => void;
  } = $props();

  let tasks = $state<TaskItem[]>([]);
  let reminders = $state<Reminder[]>([]);
  let filter = $state<TaskFilter>('open');
  let creating = $state(false);
  let createMode = $state<'task' | 'remind'>('task');
  let editingTask = $state<TaskItem | null>(null);
  let editingReminder = $state<Reminder | null>(null);
  let title = $state('');
  let detail = $state('');
  let scheduleKind = $state<'daily' | 'weekly' | 'interval' | 'once'>('daily');
  let scheduleTime = $state('09:00');
  let scheduleDays = $state<number[]>([1]);
  let intervalMinutes = $state(60);
  let onceAt = $state('');
  let planSteps = $state<Array<{id: string; title: string; detail: string; enabled: boolean}>>([]);
  let stepTitle = $state('');
  let stepDetail = $state('');
  let remindTime = $state('09:00');
  let remindMessage = $state('');
  let error = $state('');
  let consumedDraft = $state<number | null>(null);

  const DONE_STATUSES = new Set(['done', 'cancelled', 'failed']);

  function isOpenTask(task: TaskItem) {
    return !DONE_STATUSES.has(task.status);
  }

  function isGoalTask(task: TaskItem) {
    return /\[Goal\]/i.test(task.detail || '');
  }

  function isLoopTask(task: TaskItem) {
    return /\[Loop\]/i.test(task.detail || '') || task.schedule?.kind === 'interval';
  }

  function isReminderTask(task: TaskItem) {
    return /\[Reminder\]/i.test(task.detail || '');
  }

  const openTasks = $derived(tasks.filter(isOpenTask));
  const doneTasks = $derived(
    [...tasks.filter((t) => DONE_STATUSES.has(t.status))].sort((a, b) => {
      // newer finished first when possible
      const at = a.lastRunAt || 0;
      const bt = b.lastRunAt || 0;
      if (bt !== at) return bt - at;
      return String(b.createdAt).localeCompare(String(a.createdAt));
    }),
  );

  const filterCounts = $derived({
    open: openTasks.length + reminders.filter((r) => r.enabled).length,
    active: tasks.filter((t) => ['queued', 'running', 'paused', 'awaiting_approval'].includes(t.status)).length,
    scheduled: openTasks.filter((t) => !!t.schedule?.enabled).length + reminders.length,
    remind: reminders.length,
    done: doneTasks.length,
  });

  type Row =
    | {kind: 'task'; task: TaskItem; sort: number}
    | {kind: 'reminder'; reminder: Reminder; sort: number};

  const visibleRows = $derived.by((): Row[] => {
    const rows: Row[] = [];
    if (filter === 'done') {
      for (const task of doneTasks) {
        rows.push({kind: 'task', task, sort: task.lastRunAt || 0});
      }
      return rows;
    }

    if (filter === 'remind') {
      for (const r of reminders) {
        rows.push({kind: 'reminder', reminder: r, sort: timeToMinutes(r.time)});
      }
      return rows.sort((a, b) => a.sort - b.sort);
    }

    const taskPool =
      filter === 'active'
        ? tasks.filter((t) => ['queued', 'running', 'paused', 'awaiting_approval'].includes(t.status))
        : filter === 'scheduled'
          ? openTasks.filter((t) => !!t.schedule?.enabled)
          : openTasks; // open / default

    for (const task of taskPool) {
      const activeBoost = ['queued', 'running', 'paused', 'awaiting_approval'].includes(task.status) ? 1e15 : 0;
      rows.push({kind: 'task', task, sort: activeBoost + (task.nextRunAt || 0)});
    }

    // Reminders show in open + scheduled (and all-open)
    if (filter === 'open' || filter === 'scheduled') {
      for (const r of reminders) {
        rows.push({kind: 'reminder', reminder: r, sort: timeToMinutes(r.time)});
      }
    }

    return rows.sort((a, b) => {
      if (a.kind === 'task' && b.kind === 'task') {
        const aa = ['queued', 'running', 'paused', 'awaiting_approval'].includes(a.task.status) ? 1 : 0;
        const ba = ['queued', 'running', 'paused', 'awaiting_approval'].includes(b.task.status) ? 1 : 0;
        if (ba !== aa) return ba - aa;
      }
      if (a.kind !== b.kind) return a.kind === 'task' ? -1 : 1;
      return a.sort - b.sort;
    });
  });

  function timeToMinutes(time: string) {
    const [h, m] = time.split(':').map(Number);
    return (h || 0) * 60 + (m || 0);
  }

  $effect(() => {
    if (initialDraft && initialDraft.nonce !== consumedDraft) {
      consumedDraft = initialDraft.nonce;
      onDraftConsumed();
      notify('执行请求应直接在聊天中交给主 Agent；这里管理定时任务与提醒');
    }
  });

  async function refresh() {
    if (!(await runtime.connect())) return;
    const [taskRes, cronRes] = await Promise.all([
      runtime.request<any>({type: 'task.list', id: crypto.randomUUID()}),
      runtime.request<any>({type: 'cron.getConfig', id: crypto.randomUUID()}),
    ]);
    if (taskRes.type === 'task.list.result') tasks = taskRes.tasks || [];
    if (cronRes.type === 'cron.config') reminders = cronRes.triggers || [];
  }

  function applyTaskUpdate(task: TaskItem) {
    const idx = tasks.findIndex((t) => t.id === task.id);
    if (idx >= 0) {
      const next = [...tasks];
      next[idx] = task;
      tasks = next;
    } else {
      tasks = [task, ...tasks];
    }
    // 完成后仍留在数据源；默认「未完成」筛掉，自动进「已完成」tab
    if (DONE_STATUSES.has(task.status) && filter === 'open') {
      // soft nudge once in a while is noisy — skip toast
    }
  }

  onMount(() => {
    void refresh();
    return runtime.on((message: any) => {
      if (message.type === 'task.updated') {
        applyTaskUpdate(message.task as TaskItem);
        if (message.task.status === 'awaiting_approval' && (window as any).__TAURI_INTERNALS__) {
          void import('@tauri-apps/api/core').then(({invoke}) => invoke('show_review'));
        }
      }
      if (message.type === 'task.list.result' && Array.isArray(message.tasks)) {
        tasks = message.tasks;
      }
      if (message.type === 'task.approval_required' && (window as any).__TAURI_INTERNALS__) {
        void import('@tauri-apps/api/core').then(({invoke}) => invoke('show_review', {taskId: message.taskId}));
      }
      if (message.type === 'cron.config') {
        reminders = message.triggers || [];
      }
    });
  });

  function openCreateTask() {
    createMode = 'task';
    editingTask = null;
    editingReminder = null;
    title = '';
    detail = '';
    planSteps = [];
    stepTitle = '';
    stepDetail = '';
    error = '';
    scheduleKind = 'daily';
    scheduleTime = '09:00';
    scheduleDays = [1];
    intervalMinutes = 60;
    onceAt = '';
    creating = true;
  }

  function openCreateRemind() {
    createMode = 'remind';
    editingTask = null;
    editingReminder = null;
    remindTime = '21:30';
    remindMessage = '';
    error = '';
    creating = true;
  }

  function openEditTask(task: TaskItem) {
    const schedule = task.schedule;
    if (!schedule) return;
    createMode = 'task';
    editingTask = task;
    editingReminder = null;
    title = task.title;
    detail = task.detail || '';
    planSteps = (task.plan || []).map((step) => ({...step}));
    stepTitle = '';
    stepDetail = '';
    scheduleKind = schedule.kind;
    scheduleTime = schedule.time || '09:00';
    scheduleDays = schedule.days || [1];
    intervalMinutes = schedule.intervalMinutes || 60;
    onceAt = schedule.at ? new Date(schedule.at).toISOString().slice(0, 16) : '';
    error = '';
    creating = true;
  }

  function openEditReminder(reminder: Reminder) {
    createMode = 'remind';
    editingTask = null;
    editingReminder = reminder;
    remindTime = reminder.time;
    remindMessage = reminder.message;
    error = '';
    creating = true;
  }

  function addPlanStep() {
    if (!stepTitle.trim() || !stepDetail.trim()) {
      error = '请填写步骤名称和操作说明';
      return;
    }
    planSteps = [...planSteps, {id: crypto.randomUUID(), title: stepTitle.trim(), detail: stepDetail.trim(), enabled: true}];
    stepTitle = '';
    stepDetail = '';
    error = '';
  }

  function buildSchedule(): TaskItem['schedule'] | undefined {
    if (scheduleKind === 'daily') return {kind: 'daily', time: scheduleTime, enabled: true};
    if (scheduleKind === 'weekly') return scheduleDays.length ? {kind: 'weekly', time: scheduleTime, days: scheduleDays, enabled: true} : undefined;
    if (scheduleKind === 'interval') return intervalMinutes > 0 ? {kind: 'interval', intervalMinutes, enabled: true} : undefined;
    const at = onceAt ? new Date(onceAt).getTime() : 0;
    return at > Date.now() ? {kind: 'once', at, enabled: true} : undefined;
  }

  function scheduleLabel(schedule: NonNullable<TaskItem['schedule']>) {
    if (schedule.kind === 'daily') return `每日 ${schedule.time}`;
    if (schedule.kind === 'weekly') return `每周${(schedule.days || []).map((d) => '日一二三四五六'[d]).join('、')} ${schedule.time}`;
    if (schedule.kind === 'interval') return `每 ${schedule.intervalMinutes} 分钟`;
    return `于 ${new Date(schedule.at || 0).toLocaleString('zh-CN')}`;
  }

  const weekdays = ['日', '一', '二', '三', '四', '五', '六'];

  async function save() {
    if (!(await runtime.connect())) {
      error = '运行时未连接，尚未保存';
      return;
    }
    if (createMode === 'remind') {
      if (!remindMessage.trim()) {
        error = '请写下提醒内容';
        return;
      }
      if (!/^([01]\d|2[0-3]):[0-5]\d$/.test(remindTime)) {
        error = '请选择有效时间';
        return;
      }
      const next = editingReminder
        ? reminders.map((r) => (r.id === editingReminder!.id ? {...r, time: remindTime, message: remindMessage.trim()} : r))
        : [{id: crypto.randomUUID(), time: remindTime, message: remindMessage.trim(), enabled: true}, ...reminders];
      const res = await runtime.request<any>({type: 'cron.setConfig', id: crypto.randomUUID(), triggers: next});
      if (res.type === 'error') {
        error = res.message || '保存失败';
        return;
      }
      if (res.type === 'cron.config') reminders = res.triggers || [];
      creating = false;
      notify(editingReminder ? '提醒已更新' : `已设置每日 ${remindTime} 提醒`);
      return;
    }

    if (!title.trim()) {
      error = '请写下任务名称';
      return;
    }
    const schedule = buildSchedule();
    if (!schedule) {
      error = '请完整填写定时规则';
      return;
    }
    const wasEditing = !!editingTask;
    if (editingTask) {
      const response = await runtime.request<any>({
        type: 'task.update',
        id: crypto.randomUUID(),
        taskId: editingTask.id,
        title: title.trim(),
        detail: detail.trim(),
        schedule,
        plan: planSteps,
      });
      if (response.type === 'error') {
        error = response.message;
        return;
      }
      if (response.type === 'task.list.result') tasks = response.tasks;
    } else {
      const response = await runtime.request<any>({
        type: 'task.create',
        id: crypto.randomUUID(),
        title: title.trim(),
        detail: detail.trim(),
        schedule,
        plan: planSteps,
      });
      if (response.type === 'task.list.result') tasks = response.tasks;
    }
    creating = false;
    editingTask = null;
    title = '';
    detail = '';
    planSteps = [];
    await refresh();
    notify(wasEditing ? '定时任务已更新' : `已创建${scheduleLabel(schedule)}定时任务`);
  }

  async function control(task: TaskItem, action: 'pause' | 'resume' | 'cancel' | 'run') {
    if (!(await runtime.connect())) {
      notify('运行时未连接，任务状态没有改变');
      return;
    }
    const response = await runtime.request<any>({type: 'task.control', id: crypto.randomUUID(), taskId: task.id, action});
    if (response.type === 'error') {
      notify(response.message || '操作失败');
      return;
    }
    if (response.type === 'task.list.result') tasks = response.tasks;
    else await refresh();
    notify(
      action === 'cancel' ? '任务已终止' : action === 'pause' ? '任务已暂停' : action === 'run' ? '任务已开始执行' : '任务已恢复',
    );
  }

  async function removeTask(id: string) {
    const task = tasks.find((item) => item.id === id);
    if (task && ['running', 'queued', 'paused', 'awaiting_approval'].includes(task.status)) {
      notify('请先终止任务，再删除记录');
      return;
    }
    if (await runtime.connect()) {
      const response = await runtime.request<any>({type: 'task.delete', id: crypto.randomUUID(), taskId: id});
      if (response.type === 'task.list.result') tasks = response.tasks;
    } else tasks = tasks.filter((item) => item.id !== id);
    notify('任务记录已删除');
  }

  async function toggleReminder(reminder: Reminder) {
    if (!(await runtime.connect())) return;
    const next = reminders.map((r) => (r.id === reminder.id ? {...r, enabled: !r.enabled} : r));
    const res = await runtime.request<any>({type: 'cron.setConfig', id: crypto.randomUUID(), triggers: next});
    if (res.type === 'cron.config') reminders = res.triggers || [];
    notify(reminder.enabled ? '提醒已暂停' : '提醒已启用');
  }

  async function removeReminder(id: string) {
    if (!(await runtime.connect())) return;
    const next = reminders.filter((r) => r.id !== id);
    const res = await runtime.request<any>({type: 'cron.setConfig', id: crypto.randomUUID(), triggers: next});
    if (res.type === 'cron.config') reminders = res.triggers || [];
    notify('提醒已删除');
  }

  async function rollbackRecovery(task: TaskItem) {
    if (!(await runtime.connect())) {
      notify('运行时未连接，恢复没有执行');
      return;
    }
    const assumeExclusive = task.recovery?.state === 'recovery_required';
    if (assumeExclusive && !window.confirm('该事务在 Pattern 异常退出时中断。只有确认恢复范围内没有其它进程在此后写入，才能继续恢复。')) return;
    const response = await runtime.request<any>({
      type: 'task.recovery.rollback',
      id: crypto.randomUUID(),
      taskId: task.id,
      assumeExclusive,
    });
    if (response.type === 'error') {
      notify(`恢复失败：${response.message}`);
      return;
    }
    if (response.type === 'task.list.result') tasks = response.tasks;
    notify('AgentOS 已恢复任务修改');
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
  <PageHeader
    eyebrow="提醒 · 定时 · 执行"
    title="任务"
    subtitle="每日提醒、循环和定时执行都在这里。AI 会不会主动找你，去「主动」页。完成的会自动进「已完成」。"
  >
    <button class="quiet-button" onclick={openCreateRemind}><Bell size={14} />新建提醒</button>
    <button class="primary-button" onclick={openCreateTask}><Plus size={14} />新建定时任务</button>
  </PageHeader>

  <div class="goals-filter task-filter" role="tablist" aria-label="任务筛选">
    <button class:active={filter === 'open'} role="tab" aria-selected={filter === 'open'} onclick={() => (filter = 'open')}>
      未完成 <span>{filterCounts.open}</span>
    </button>
    <button class:active={filter === 'active'} role="tab" aria-selected={filter === 'active'} onclick={() => (filter = 'active')}>
      执行中 <span>{filterCounts.active}</span>
    </button>
    <button class:active={filter === 'scheduled'} role="tab" aria-selected={filter === 'scheduled'} onclick={() => (filter = 'scheduled')}>
      定时 <span>{filterCounts.scheduled}</span>
    </button>
    <button class:active={filter === 'remind'} role="tab" aria-selected={filter === 'remind'} onclick={() => (filter = 'remind')}>
      提醒 <span>{filterCounts.remind}</span>
    </button>
    <button class:active={filter === 'done'} role="tab" aria-selected={filter === 'done'} onclick={() => (filter = 'done')}>
      已完成 <span>{filterCounts.done}</span>
    </button>
  </div>

  <div class="task-list">
    {#each visibleRows as row (row.kind === 'task' ? row.task.id : `r:${row.reminder.id}`)}
      {#if row.kind === 'reminder'}
        {@const reminder = row.reminder}
        <article class="task-row-remind" class:disabled={!reminder.enabled}>
          <div class="task-state">
            <span class="remind-orb" class:off={!reminder.enabled}><Bell size={13} /></span>
          </div>
          <div class="task-copy">
            <div>
              <span class="badge blue">提醒</span>
              <span class="badge" class:amber={reminder.enabled} class:dim={!reminder.enabled}>
                {reminder.enabled ? '每日发送' : '已暂停'}
              </span>
              <time>{reminder.time}</time>
            </div>
            <h3>{reminder.message}</h3>
            <p>↻ 每天 {reminder.time} · 系统消息（不跑 Computer Use）</p>
          </div>
          <div class="task-actions">
            <button title="编辑提醒" aria-label="编辑提醒" onclick={() => openEditReminder(reminder)}>编辑</button>
            <button title={reminder.enabled ? '暂停' : '启用'} aria-label={reminder.enabled ? '暂停' : '启用'} onclick={() => toggleReminder(reminder)}>
              {#if reminder.enabled}<Pause size={14} />{:else}<Play size={14} />{/if}
            </button>
            <button title="删除提醒" aria-label="删除提醒" onclick={() => removeReminder(reminder.id)}><Trash2 size={14} /></button>
          </div>
        </article>
      {:else}
        {@const task = row.task}
        <article class:task-row-done={DONE_STATUSES.has(task.status)}>
          <div class="task-state">
            <StatusDot
              active={task.status === 'running' || task.status === 'queued' || task.status === 'scheduled'}
              off={task.status === 'cancelled' || task.status === 'failed' || task.status === 'done'}
            />
          </div>
          <div class="task-copy">
            <div>
              <span
                class="badge"
                class:amber={task.status === 'queued' || task.status === 'awaiting_approval' || task.status === 'scheduled'}
                class:green={task.status === 'done'}
                class:dim={task.status === 'cancelled' || task.status === 'failed'}
              >{statusLabel(task.status)}</span>
              {#if isReminderTask(task)}<span class="badge blue">提醒任务</span>{/if}
              {#if isLoopTask(task)}<span class="badge blue">循环</span>{/if}
              {#if isGoalTask(task)}<span class="badge amber">目标</span>{/if}
              {#if task.riskTier !== undefined}<span class="badge">T{task.riskTier}</span>{/if}
              {#if task.recovery}
                <span
                  class="badge"
                  class:green={task.recovery.state === 'committed'}
                  class:amber={task.recovery.state === 'active' || task.recovery.state === 'prepared'}
                  class:dim={task.recovery.state === 'unavailable'}
                >恢复 · {task.recovery.state}</span>
              {/if}
              <time>{task.createdAt}</time>
            </div>
            <h3>{task.title}</h3>
            {#if task.detail}<p>{task.detail}</p>{/if}
            {#if task.schedule}<p>↻ {scheduleLabel(task.schedule)} 自动执行 · 已运行 {task.runCount || 0} 次</p>{/if}
            {#if task.plan?.length}<p>☷ {task.plan.length} 个自动化步骤：{task.plan.map((step) => step.title).join(' → ')}</p>{/if}
            {#if task.workflow}<p>⚙ {task.workflow.name} · {task.workflow.agents || task.workflow.stepCount} 个 Agent · {task.workflow.workspace || '当前工作区'}</p>{/if}
            {#if task.recovery?.transactionId}<p>↶ AgentOS 事务 {task.recovery.transactionId.slice(0, 12)} · {task.recovery.fileScopes.join('、')}</p>{/if}
            {#if task.recovery?.error}<p class="validation-error">恢复层：{task.recovery.error}</p>{/if}
            {#if task.agentResults?.length}
              <details class="task-runs">
                <summary>子 Agent 结果（{task.agentResults.length}）</summary>
                {#each task.agentResults as result}<p><b>{result.skillId}</b> · {result.status} · {result.output.slice(0, 240)}</p>{/each}
              </details>
            {/if}
            {#if task.runs?.length}
              <details class="task-runs">
                <summary>运行记录（{task.runs.length}）</summary>
                {#each task.runs.slice(0, 5) as run}
                  <p>{new Date(run.startedAt).toLocaleString('zh-CN')} · {run.status}{run.error ? ` · ${run.error}` : ''}</p>
                {/each}
              </details>
            {/if}
            {#if task.error}<p class="validation-error">{task.error}</p>{/if}
          </div>
          <div class="task-actions">
            {#if task.recovery?.transactionId && ['committed', 'prepared', 'conflicted', 'recovery_required'].includes(task.recovery.state)}
              <button title="恢复任务修改" aria-label="恢复任务修改" onclick={() => rollbackRecovery(task)}><RotateCcw size={14} /></button>
            {/if}
            {#if task.schedule && isOpenTask(task)}
              <button title="编辑定时任务" aria-label="编辑定时任务" onclick={() => openEditTask(task)}>编辑</button>
            {/if}
            {#if task.status === 'awaiting_approval'}
              <button
                title="打开审查窗"
                aria-label="打开审查窗"
                onclick={async () => (window as any).__TAURI_INTERNALS__ && (await import('@tauri-apps/api/core')).invoke('show_review')}
              >
                <ShieldAlert size={14} />
              </button>
            {/if}
            {#if task.status === 'queued' || task.status === 'running' || task.status === 'scheduled'}
              <button title="暂停" aria-label="暂停" onclick={() => control(task, 'pause')}><Pause size={14} /></button>
            {:else if task.status === 'paused'}
              <button title="恢复" aria-label="恢复" onclick={() => control(task, 'resume')}><Play size={14} /></button>
            {/if}
            {#if isOpenTask(task) && task.status !== 'scheduled'}
              <button title="终止" aria-label="终止" onclick={() => control(task, 'cancel')}><Square size={12} /></button>
            {/if}
            <button
              title="删除记录"
              aria-label="删除记录"
              disabled={['running', 'queued', 'paused', 'awaiting_approval'].includes(task.status)}
              onclick={() => removeTask(task.id)}
            ><Trash2 size={14} /></button>
          </div>
        </article>
      {/if}
    {:else}
      <div class="blank-state">
        <div class="blank-mark">{filter === 'done' ? '✓' : filter === 'remind' ? 'bell' : '⌁'}</div>
        <h3>
          {#if filter === 'done'}还没有已完成任务
          {:else if filter === 'remind'}还没有每日提醒
          {:else if filter === 'open'}当前没有未完成项
          {:else}这个筛选下没有内容{/if}
        </h3>
        <p>
          {#if filter === 'done'}任务跑完或终止后会自动出现在这里。
          {:else if filter === 'remind'}用「新建提醒」或聊天 <code>/remind 21:30 该休息了</code>。
          {:else}可新建定时任务、每日提醒，或在聊天里用 /task /loop /remind。{/if}
        </p>
        {#if filter === 'remind'}
          <button class="primary-button" onclick={openCreateRemind}><Bell size={14} />新建提醒</button>
        {:else if filter !== 'done'}
          <button class="primary-button" onclick={openCreateTask}><Plus size={14} />新建定时任务</button>
        {/if}
      </div>
    {/each}
  </div>
</section>

{#if creating}
  <div class="modal-backdrop" role="presentation" onclick={(event) => { if (event.target === event.currentTarget) creating = false; }}>
    <div class="memory-editor" role="dialog" aria-modal="true" aria-labelledby="task-title">
      <header>
        <div>
          <p class="eyebrow">{createMode === 'remind' ? '每日提醒' : '定时任务'}</p>
          <h2 id="task-title">
            {#if createMode === 'remind'}
              {editingReminder ? '编辑提醒' : '新建提醒'}
            {:else}
              {editingTask ? '编辑定时任务' : '新建定时任务'}
            {/if}
          </h2>
        </div>
        <button aria-label="关闭" onclick={() => (creating = false)}><X size={16} /></button>
      </header>

      {#if !editingTask && !editingReminder}
        <div class="goals-filter" style="padding:0 0 12px" role="tablist" aria-label="创建类型">
          <button type="button" class:active={createMode === 'task'} onclick={() => (createMode = 'task')}>定时任务</button>
          <button type="button" class:active={createMode === 'remind'} onclick={() => (createMode = 'remind')}>每日提醒</button>
        </div>
      {/if}

      {#if createMode === 'remind'}
        <label>发送时间<input type="time" bind:value={remindTime} /></label>
        <label>
          提醒内容
          <textarea bind:value={remindMessage} rows="3" maxlength="500" placeholder="例如：该休息了，明天还有重要的事"></textarea>
          <small class="field-help">到点发送系统提醒，不会启动 Computer Use。也会出现在任务页「提醒」列表。</small>
        </label>
      {:else}
        <label>任务名称<input bind:value={title} placeholder="例如：整理下载目录中的文件" /></label>
        <label>补充说明<textarea bind:value={detail} rows="4" placeholder="期望结果、限制或需要注意的事项"></textarea></label>
        <div class="automation-steps">
          <div class="setting-inline"><strong>自动化步骤（可选）</strong><span class="settings-note">按顺序执行</span></div>
          {#each planSteps as step, index (step.id)}
            <div class="automation-step">
              <div class="automation-step-head">
                <b>{index + 1}</b>
                <strong>{step.title}</strong>
                <button type="button" aria-label={`删除步骤 ${index + 1}`} onclick={() => (planSteps = planSteps.filter((item) => item.id !== step.id))}>
                  <X size={13} />
                </button>
              </div>
              <small>{step.detail}</small>
            </div>
          {/each}
          <div class="automation-step-form">
            <label>步骤名称<input aria-label="步骤名称" bind:value={stepTitle} placeholder="例如：打开邮件客户端" /></label>
            <label>操作说明<textarea aria-label="步骤说明" bind:value={stepDetail} rows="3" placeholder="例如：筛选今天未读邮件"></textarea></label>
            <button type="button" class="quiet-button" onclick={addPlanStep}><Plus size={13} />添加步骤</button>
          </div>
        </div>
        <label>
          执行规则
          <select bind:value={scheduleKind}>
            <option value="daily">每天</option>
            <option value="weekly">每周</option>
            <option value="interval">按间隔</option>
            <option value="once">仅一次</option>
          </select>
        </label>
        {#if scheduleKind === 'daily' || scheduleKind === 'weekly'}
          <label>执行时间<input type="time" bind:value={scheduleTime} /></label>
        {/if}
        {#if scheduleKind === 'weekly'}
          <div class="weekday-picker">
            {#each weekdays as day, i}
              <label>
                <input
                  type="checkbox"
                  checked={scheduleDays.includes(i)}
                  onchange={() =>
                    (scheduleDays = scheduleDays.includes(i) ? scheduleDays.filter((item) => item !== i) : [...scheduleDays, i])}
                />周{day}
              </label>
            {/each}
          </div>
        {/if}
        {#if scheduleKind === 'interval'}
          <label>间隔分钟<input type="number" min="1" bind:value={intervalMinutes} /></label>
        {/if}
        {#if scheduleKind === 'once'}
          <label>执行时间<input type="datetime-local" bind:value={onceAt} /></label>
        {/if}
      {/if}

      {#if error}<p class="validation-error">{error}</p>{/if}
      <footer>
        <button onclick={() => (creating = false)}>取消</button>
        <button class="primary-button" onclick={save}>
          {#if createMode === 'remind'}
            {editingReminder ? '保存提醒' : '创建提醒'}
          {:else}
            {editingTask ? '保存修改' : '创建定时任务'}
          {/if}
        </button>
      </footer>
    </div>
  </div>
{/if}
