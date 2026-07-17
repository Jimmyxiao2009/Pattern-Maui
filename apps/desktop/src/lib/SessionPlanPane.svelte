<script lang="ts">
  import {Check, ChevronDown, ChevronRight, Circle, ListTodo, Loader2, Minus, Trash2, X} from 'lucide-svelte';
  import {runtime} from './runtime';

  export type PlanItem = {
    id: string;
    content: string;
    status: 'pending' | 'in_progress' | 'completed' | 'cancelled';
  };

  export type SessionPlan = {
    conversationId: string;
    title?: string;
    items: PlanItem[];
    updatedAt: number;
  };

  let {
    conversationId = '',
    collapsed = false,
    onToggle = () => {},
    onPlanChange = (_plan: SessionPlan | null) => {},
  }: {
    conversationId?: string;
    collapsed?: boolean;
    onToggle?: () => void;
    onPlanChange?: (plan: SessionPlan | null) => void;
  } = $props();

  let plan = $state<SessionPlan | null>(null);
  let loading = $state(false);
  let flashIds = $state<Set<string>>(new Set());
  let prevSnapshot = $state<Record<string, string>>({});

  const items = $derived(plan?.items || []);
  const activeItems = $derived(items.filter((i) => i.status !== 'cancelled'));
  const done = $derived(activeItems.filter((i) => i.status === 'completed').length);
  const total = $derived(activeItems.length);
  const pct = $derived(total > 0 ? Math.round((done / total) * 100) : 0);
  const hasPlan = $derived(items.length > 0);
  const allDone = $derived(total > 0 && done === total);
  const inProgress = $derived(items.find((i) => i.status === 'in_progress'));
  const remaining = $derived(activeItems.filter((i) => i.status === 'pending' || i.status === 'in_progress').length);

  function markFlash(ids: string[]) {
    if (!ids.length) return;
    const next = new Set(flashIds);
    for (const id of ids) next.add(id);
    flashIds = next;
    window.setTimeout(() => {
      const cleared = new Set(flashIds);
      for (const id of ids) cleared.delete(id);
      flashIds = cleared;
    }, 900);
  }

  function applyPlan(next: SessionPlan | null) {
    const nextItems = next?.items || [];
    const changed: string[] = [];
    for (const item of nextItems) {
      const prev = prevSnapshot[item.id];
      if (prev && prev !== item.status) changed.push(item.id);
      else if (!prev && item.status === 'completed') changed.push(item.id);
    }
    const snap: Record<string, string> = {};
    for (const item of nextItems) snap[item.id] = item.status;
    prevSnapshot = snap;
    plan = next;
    onPlanChange(next);
    if (changed.length) markFlash(changed);
    // Keep list open when agent is actively updating statuses.
    if (changed.length && collapsed) onToggle();
  }

  async function refresh() {
    if (!conversationId) {
      applyPlan(null);
      return;
    }
    if (!(await runtime.connect())) return;
    loading = true;
    try {
      const res = await runtime.request<any>({
        type: 'session_plan.get',
        id: crypto.randomUUID(),
        conversationId,
      });
      if (res.type === 'session_plan.result') applyPlan(res.plan || null);
    } finally {
      loading = false;
    }
  }

  async function clearPlan() {
    if (!conversationId || !(await runtime.connect())) return;
    const res = await runtime.request<any>({
      type: 'session_plan.clear',
      id: crypto.randomUUID(),
      conversationId,
    });
    if (res.type === 'session_plan.result') applyPlan(res.plan);
  }

  async function setItemStatus(item: PlanItem, status: PlanItem['status']) {
    if (!conversationId || !(await runtime.connect())) return;
    const res = await runtime.request<any>({
      type: 'session_plan.set',
      id: crypto.randomUUID(),
      conversationId,
      merge: true,
      items: [{id: item.id, content: item.content, status}],
    });
    if (res.type === 'session_plan.result') applyPlan(res.plan);
  }

  function cycleStatus(item: PlanItem) {
    // Click cycles: pending → in_progress → completed → pending (cancelled stays)
    if (item.status === 'cancelled') return setItemStatus(item, 'pending');
    if (item.status === 'pending') return setItemStatus(item, 'in_progress');
    if (item.status === 'in_progress') return setItemStatus(item, 'completed');
    return setItemStatus(item, 'pending');
  }

  $effect(() => {
    const id = conversationId;
    prevSnapshot = {};
    void id;
    void refresh();
    const off = runtime.on((message: any) => {
      if (message.type === 'session_plan.updated' && message.plan?.conversationId === id) {
        applyPlan(message.plan);
      }
    });
    return () => off();
  });
</script>

{#if conversationId && hasPlan}
  <aside class="session-plan-dock" class:collapsed class:all-done={allDone} aria-label="当前会话计划">
    <div class="session-plan-track" aria-hidden="true">
      <span class="session-plan-fill" style={`width:${pct}%`}></span>
    </div>

    <header class="session-plan-head">
      <button type="button" class="session-plan-toggle" onclick={onToggle} aria-expanded={!collapsed}>
        <span class="session-plan-icon" class:done={allDone}>
          <ListTodo size={14} />
        </span>
        <div class="session-plan-titles">
          <strong>{plan?.title || '会话计划'}</strong>
          <small>
            {#if allDone}
              全部完成 · {done}/{total}
            {:else if inProgress}
              进行中 · {inProgress.content}
            {:else}
              待办 · 还剩 {remaining} 项 · {done}/{total}
            {/if}
          </small>
        </div>
        <span class="session-plan-badge" class:done={allDone}>{done}/{total}</span>
        {#if loading}<span class="spin" aria-hidden="true"><Loader2 size={13} /></span>{/if}
        {#if collapsed}<ChevronRight size={15} />{:else}<ChevronDown size={15} />{/if}
      </button>
      <button
        type="button"
        class="session-plan-clear"
        title="清空会话计划"
        aria-label="清空会话计划"
        onclick={clearPlan}
      >
        <Trash2 size={13} />
      </button>
    </header>

    {#if !collapsed}
      <ol class="session-plan-list">
        {#each items as item, index (item.id)}
          <li
            class:pending={item.status === 'pending'}
            class:active={item.status === 'in_progress'}
            class:done={item.status === 'completed'}
            class:cancelled={item.status === 'cancelled'}
            class:flash={flashIds.has(item.id)}
          >
            <button
              type="button"
              class="session-plan-check"
              class:checked={item.status === 'completed'}
              class:running={item.status === 'in_progress'}
              title="点击切换：未开始 → 进行中 → 完成"
              aria-label={`${item.content}，状态 ${item.status}`}
              onclick={() => cycleStatus(item)}
            >
              {#if item.status === 'completed'}
                <Check size={14} />
              {:else if item.status === 'in_progress'}
                <span class="spin" aria-hidden="true"><Loader2 size={13} /></span>
              {:else if item.status === 'cancelled'}
                <X size={13} />
              {:else}
                <Circle size={13} />
              {/if}
            </button>
            <div class="session-plan-body">
              <span class="session-plan-index">{index + 1}</span>
              <span class="session-plan-text">{item.content}</span>
              <span class="session-plan-status">
                {#if item.status === 'completed'}已完成
                {:else if item.status === 'in_progress'}进行中
                {:else if item.status === 'cancelled'}已取消
                {:else}待办{/if}
              </span>
            </div>
            <div class="session-plan-actions">
              {#if item.status === 'pending'}
                <button type="button" class="quiet-button tiny" onclick={() => setItemStatus(item, 'in_progress')}>开始</button>
                <button type="button" class="quiet-button tiny" onclick={() => setItemStatus(item, 'completed')}>完成</button>
              {:else if item.status === 'in_progress'}
                <button type="button" class="quiet-button tiny primary-tint" onclick={() => setItemStatus(item, 'completed')}>标完成</button>
              {:else if item.status === 'completed'}
                <button type="button" class="quiet-button tiny" onclick={() => setItemStatus(item, 'pending')}>撤销</button>
              {:else}
                <button type="button" class="quiet-button tiny" onclick={() => setItemStatus(item, 'pending')}>恢复</button>
              {/if}
              {#if item.status !== 'cancelled' && item.status !== 'completed'}
                <button
                  type="button"
                  class="icon-mute"
                  title="取消此项"
                  aria-label="取消此项"
                  onclick={() => setItemStatus(item, 'cancelled')}
                ><Minus size={12} /></button>
              {/if}
            </div>
          </li>
        {/each}
      </ol>
      <footer class="session-plan-foot">
        <span>本对话持续显示 · Agent 用 update_plan 勾进度 · 你也可手动标记</span>
      </footer>
    {/if}
  </aside>
{/if}
