<script lang="ts">
  import {onMount} from 'svelte';
  import {ShieldAlert, Check, X, Square, Pause, Hand, Play} from 'lucide-svelte';
  import StatusDot from './StatusDot.svelte';
  import {runtime} from './runtime';
  import type {TaskItem} from './types';

  let task = $state<TaskItem | null>(null);
  let screenshot = $state('');
  let pendingStep = $state<any>(null);
  let frozen = $state(false);
  const focusTaskId = new URLSearchParams(location.search).get('taskId') || '';
  let tierGuide = $state<Array<{tier: number; label: string; meaning: string}>>([
    {tier: 0, label: 'T0 只读', meaning: '读取界面/文件，不改变系统状态'},
    {tier: 1, label: 'T1 低风险', meaning: '可逆的本地操作，通常自动放行'},
    {tier: 2, label: 'T2 需审批', meaning: '破坏性、外发、安装、提交等，必须人工确认'},
    {tier: 3, label: 'T3 禁区', meaning: '银行/密码管理器等，默认拒绝并冻结'},
  ]);
  const statusZh: Record<string, string> = {
    pending: '等待', running: '进行中', done: '完成', failed: '失败', skipped: '跳过', awaiting_approval: '待审批',
  };

  async function focusTask(id: string) {
    if (!(await runtime.connect())) return;
    const res = await runtime.request<any>({type: 'task.list', id: crypto.randomUUID()});
    const found = res.type === 'task.list.result' ? res.tasks.find((item: TaskItem) => item.id === id) : null;
    if (!found) return;
    task = found;
    pendingStep = found.steps?.find((step: any) => step.status === 'awaiting_approval') || null;
    screenshot = '';
  }

  onMount(() => {
    void runtime.connect().then(async () => {
      try {
        const policy = await runtime.request<any>({type: 'security.policy.get', id: crypto.randomUUID()});
        if (policy.type === 'security.policy' && policy.policy?.tierGuide?.length) tierGuide = policy.policy.tierGuide;
      } catch { /* optional */ }
      const res = await runtime.request<any>({type: 'task.list', id: crypto.randomUUID()});
      if (res.type === 'task.list.result') {
        task =
          (focusTaskId && res.tasks.find((item: TaskItem) => item.id === focusTaskId)) ||
          res.tasks.find((item: TaskItem) =>
            ['running', 'paused', 'awaiting_approval', 'queued'].includes(item.status),
          ) ||
          res.tasks[0] ||
          null;
      }
    });
    const unsubscribe = runtime.on((message) => {
      if (message.type === 'task.updated' && (!task || task.id === message.task.id)) {
        task = message.task as TaskItem;
        if (message.task.status !== 'awaiting_approval') pendingStep = null;
      }
      if (message.type === 'task.approval_required') {
        if (task && task.id !== message.taskId) return;
        pendingStep = message.step as any;
        screenshot = message.screenshotBase64 ? `data:image/png;base64,${message.screenshotBase64}` : '';
        void runtime.request({type: 'task.list', id: crypto.randomUUID()}).then((res: any) => {
          const found = res.tasks?.find((t: TaskItem) => t.id === message.taskId);
          if (found) task = found;
        });
      }
      if (message.type === 'task.screenshot' && (!task || task.id === message.taskId)) {
        screenshot = `data:image/png;base64,${message.screenshotBase64}`;
      }
      if (message.type === 'task.list.result' && !task) task = message.tasks[0] ?? null;
    });
    const handleFocus = (event: Event) => {
      const id = (event as CustomEvent<string>).detail;
      if (id) void focusTask(id);
    };
    window.addEventListener('pattern-focus-task', handleFocus);
    return () => {
      unsubscribe();
      window.removeEventListener('pattern-focus-task', handleFocus);
    };
  });

  async function control(action: 'approve' | 'reject' | 'cancel' | 'pause' | 'resume') {
    if (!task) return;
    try {
      await runtime.request({type: 'task.control', id: crypto.randomUUID(), taskId: task.id, action});
    } catch {
      return;
    }
    if (action === 'approve' || action === 'reject') pendingStep = null;
    if (action === 'resume' && (window as any).__TAURI_INTERNALS__) {
      const {invoke} = await import('@tauri-apps/api/core');
      await invoke('resume_computer_use');
      frozen = false;
    }
  }

  async function hide() {
    if ((window as any).__TAURI_INTERNALS__) {
      const {getCurrentWindow} = await import('@tauri-apps/api/window');
      await getCurrentWindow().hide();
    }
  }

  async function pauseAndTakeOver() {
    if (task) await control('pause');
    if ((window as any).__TAURI_INTERNALS__) {
      const {invoke} = await import('@tauri-apps/api/core');
      await invoke('emergency_stop');
      frozen = true;
    }
    // Keep review surface visible so progress is not lost.
  }

  async function resumeFromTakeover() {
    if ((window as any).__TAURI_INTERNALS__) {
      const {invoke} = await import('@tauri-apps/api/core');
      await invoke('resume_computer_use');
      frozen = false;
    }
    if (task?.status === 'paused') await control('resume');
  }
</script>

<main class="review-window">
  <header data-tauri-drag-region>
    <StatusDot active={task?.status === 'running' || task?.status === 'awaiting_approval'} />
    <strong>执行审查</strong>
    <span>{task?.title || '等待任务'}</span>
    {#if frozen}<span class="badge amber">已冻结</span>{/if}
    <button aria-label="关闭" onclick={hide}><X size={15} /></button>
  </header>
  <div class="review-body">
    <section class="review-shot">
      {#if screenshot}
        <img src={screenshot} alt="当前截屏" />
      {:else}
        <div class="blank-state"><div class="blank-mark">⬚</div><p>等待截屏或审批步骤</p></div>
      {/if}
    </section>
    <section class="review-steps">
      <p class="eyebrow">步骤流</p>
      <div class="tier-guide" aria-label="风险等级说明">
        {#each tierGuide as item}
          <span class="badge" class:green={item.tier === 0} class:amber={item.tier >= 2} class:dim={item.tier === 1} title={item.meaning}>{item.label}</span>
        {/each}
      </div>
      {#if task?.riskTier != null}
        <p class="settings-note">当前任务最高风险：T{task.riskTier} · {tierGuide.find((g) => g.tier === task?.riskTier)?.meaning || ''}</p>
      {/if}
      {#if task?.steps?.length}
        {#each task.steps as step}
          <article class:await={step.status === 'awaiting_approval'}>
            <div>
              <span class="badge" class:amber={step.tier >= 2} class:green={step.tier === 0} title={tierGuide.find((g) => g.tier === step.tier)?.meaning || ''}>{`T${step.tier}`}</span>
              <strong>{step.action}</strong>
            </div>
            <p>{step.detail}</p>
            {#if step.receipt}<small>{step.receipt}</small>{/if}
            <small>{statusZh[step.status] || step.status}</small>
          </article>
        {/each}
      {:else}
        <p class="settings-note">任务开始后，动作会显示在这里。</p>
      {/if}
      {#if pendingStep}
        <div class="approval-banner">
          <ShieldAlert size={18} />
          <div>
            <strong>需要确认 {tierGuide.find((g) => g.tier === pendingStep.tier)?.label || `T${pendingStep.tier}`} 动作</strong>
            <p>{pendingStep.detail}</p>
            <p class="settings-note">{tierGuide.find((g) => g.tier === pendingStep.tier)?.meaning}</p>
          </div>
        </div>
      {/if}
    </section>
  </div>
  <footer>
    <button class="quiet-button" onclick={() => control('cancel')}><Square size={12} />终止</button>
    <button class="quiet-button" onclick={() => control('pause')} disabled={!task || task.status !== 'running'}><Pause size={12}/>暂停</button>
    <button class="quiet-button" onclick={resumeFromTakeover} disabled={!task || (task.status !== 'paused' && !frozen)}><Play size={12}/>继续</button>
    <button class="quiet-button" onclick={pauseAndTakeOver} disabled={!task || task.status !== 'running'}><Hand size={12}/>接管</button>
    <span></span>
    <button class="quiet-button" onclick={() => control('reject')} disabled={!pendingStep}><X size={14} />拒绝</button>
    <button class="primary-button" onclick={() => control('approve')} disabled={!pendingStep}><Check size={14} />批准</button>
  </footer>
</main>

<style>
  .review-window {
    height: 100vh;
    display: grid;
    grid-template-rows: auto 1fr auto;
    background: var(--bg, #12141a);
    color: var(--text, #f3f0ea);
  }
  header,
  footer {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 12px 16px;
    border-bottom: 1px solid rgba(255, 255, 255, 0.06);
  }
  footer {
    border-bottom: 0;
    border-top: 1px solid rgba(255, 255, 255, 0.06);
  }
  header button {
    margin-left: auto;
  }
  .review-body {
    display: grid;
    grid-template-columns: 1.2fr 1fr;
    min-height: 0;
  }
  .review-shot,
  .review-steps {
    min-height: 0;
    overflow: auto;
    padding: 16px;
  }
  .review-shot {
    border-right: 1px solid rgba(255, 255, 255, 0.06);
  }
  .review-shot img {
    width: 100%;
    border-radius: 12px;
    border: 1px solid rgba(255, 255, 255, 0.08);
  }
  .review-steps article {
    padding: 10px 12px;
    border-radius: 12px;
    background: rgba(255, 255, 255, 0.03);
    margin-bottom: 8px;
  }
  .review-steps article.await {
    outline: 1px solid rgba(245, 166, 35, 0.5);
  }
  .approval-banner {
    display: flex;
    gap: 10px;
    margin-top: 12px;
    padding: 12px;
    border-radius: 12px;
    background: rgba(245, 166, 35, 0.12);
  }
  .tier-guide { display:flex; flex-wrap:wrap; gap:6px; margin: 0 0 10px; }
</style>
