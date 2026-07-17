<script lang="ts">
  import {onMount} from 'svelte';
  import {BellRing, Pause, Play, RefreshCw, Save, Sparkles, Zap} from 'lucide-svelte';
  import PageHeader from './PageHeader.svelte';
  import SettingRow from './SettingRow.svelte';
  import Toggle from './Toggle.svelte';
  import {runtime} from './runtime';

  type Chain = {
    id: string;
    purpose: string;
    status: string;
    nextRunAt: number | null;
    kind?: string;
  };
  type Connection = {id: string; name?: string; provider: string; endpoint: string};
  type LogItem = {
    id: string;
    body: string;
    reason: string;
    type: string;
    ts: number;
    origin?: 'ai' | 'system';
    state?: string;
  };

  let {
    notify,
    onOpenTasks,
  }: {
    notify: (message: string) => void;
    onOpenTasks?: () => void;
  } = $props();

  let chains = $state<Chain[]>([]);
  let log = $state<LogItem[]>([]);
  let reminderCount = $state(0);
  let connections = $state<Connection[]>([]);
  let proactiveEnabled = $state(true);
  let paused = $state(false);
  let bedtimeHour = $state(23);
  let saved = $state('');
  let offline = $state(false);

  /** Only AI-origin messages on this page; system reminders live under 任务. */
  const aiLog = $derived(log.filter((item) => item.origin !== 'system'));
  const activeChains = $derived(chains.filter((c) => c.status === 'active' || c.status === 'running'));
  const engineLabel = $derived(
    offline ? '未连接' : !proactiveEnabled ? '已关闭' : paused ? '已暂停' : '运行中',
  );

  function readConnections() {
    try {
      const value = JSON.parse(localStorage.getItem('pattern-model-profiles') || '{}');
      connections = Array.isArray(value.profiles)
        ? value.profiles
            .filter((item: any) => item?.provider && item?.endpoint)
            .map((item: any) => ({
              id: String(item.id),
              name: item.name,
              provider: String(item.provider),
              endpoint: String(item.endpoint),
            }))
        : [];
    } catch {
      connections = [];
    }
  }

  async function refresh() {
    readConnections();
    const connected = await runtime.connect();
    offline = !connected;
    if (!connected) return;
    const [proactive, chain, list, cron] = await Promise.all([
      runtime.request<any>({type: 'proactive.getConfig', id: crypto.randomUUID()}),
      runtime.request<any>({type: 'proactive.chain.list', id: crypto.randomUUID(), limit: 30}),
      runtime.request<any>({type: 'proactive.list', id: crypto.randomUUID(), limit: 40}),
      runtime.request<any>({type: 'cron.getConfig', id: crypto.randomUUID()}),
    ]);
    if (proactive.type === 'proactive.config') {
      proactiveEnabled = proactive.enabled;
      paused = proactive.paused;
      bedtimeHour = proactive.bedtimeHour;
    }
    if (chain.type === 'proactive.chain.list.result') chains = chain.chains || [];
    if (list.type === 'proactive.list.result') log = list.items || [];
    if (cron.type === 'cron.config') {
      reminderCount = (cron.triggers || []).filter((t: any) => t.enabled !== false).length;
    }
  }

  onMount(() => {
    void refresh();
    return runtime.on((incoming: any) => {
      if (incoming.type === 'proactive.chain.updated') {
        chains = [incoming.chain, ...chains.filter((item) => item.id !== incoming.chain.id)];
      }
      if (incoming.type === 'proactive.inbox.updated' || incoming.type === 'proactive.impulse') {
        const item = incoming.item;
        if (item?.id) log = [item, ...log.filter((x) => x.id !== item.id)].slice(0, 40);
      }
      if (incoming.type === 'proactive.config') {
        proactiveEnabled = incoming.enabled;
        paused = incoming.paused;
        bedtimeHour = incoming.bedtimeHour;
      }
      if (incoming.type === 'cron.config') {
        reminderCount = (incoming.triggers || []).filter((t: any) => t.enabled !== false).length;
      }
    });
  });

  async function runNow() {
    if (!(await runtime.connect())) {
      notify('运行时未连接');
      return;
    }
    const response = await runtime.request<any>({
      type: 'proactive.trigger',
      id: crypto.randomUUID(),
      kind: 'manual',
      reason: '主动页面手动触发',
    });
    if (response.type === 'error') notify(response.message);
    else {
      notify('已唤醒一次 AI 主动判断');
      void refresh();
    }
  }

  async function saveSettings() {
    saved = '';
    if ((window as any).__TAURI_INTERNALS__) {
      try {
        const {invoke} = await import('@tauri-apps/api/core');
        await invoke('save_proactive_config', {
          config: {enabled: proactiveEnabled, bedtimeHour, paused},
        });
      } catch (value) {
        saved = `保存失败：${value}`;
        notify(saved);
        return;
      }
    }
    if (!(await runtime.connect())) {
      saved = '已写本地，运行时未连接';
      notify(saved);
      return;
    }
    const response = await runtime.request<any>({
      type: 'proactive.setConfig',
      id: crypto.randomUUID(),
      enabled: proactiveEnabled,
      paused,
      bedtimeHour,
    });
    if (response.type === 'proactive.config') {
      proactiveEnabled = response.enabled;
      paused = response.paused;
      bedtimeHour = response.bedtimeHour;
    } else if (response.type === 'error') {
      await runtime.request({type: 'proactive.setPaused', id: crypto.randomUUID(), paused});
    }
    saved = '已保存';
    notify('主动引擎设置已保存');
  }

  async function runChain(id: string) {
    if (!(await runtime.connect())) return;
    await runtime.request({type: 'proactive.chain.runNow', id: crypto.randomUUID(), chainId: id});
    notify('已立即运行该主动链');
  }

  async function cancelChain(id: string) {
    if (!(await runtime.connect())) return;
    await runtime.request({type: 'proactive.chain.cancel', id: crypto.randomUUID(), chainId: id});
    notify('主动链已取消');
  }

  function endpointLabel(endpoint: string) {
    return endpoint.replace(/^https?:\/\//, '').replace(/\/$/, '');
  }

  function formatLogTime(ts: number) {
    const ms = ts > 1e12 ? ts : ts * 1000;
    return new Date(ms).toLocaleString('zh-CN');
  }

  function chainStatusLabel(status: string) {
    return (
      {
        active: '进行中',
        running: '执行中',
        completed: '已完成',
        cancelled: '已取消',
        failed: '失败',
      } as Record<string, string>
    )[status] || status;
  }
</script>

<section class="view proactive-view">
  <PageHeader
    eyebrow="AI 主动关心"
    title="主动"
    subtitle="只管理「AI 会不会主动找你」。每天几点发提醒，请到「任务」页。"
  >
    <button class="quiet-button" onclick={() => void refresh()}><RefreshCw size={14} />刷新</button>
    <button class="primary-button" onclick={runNow}><Sparkles size={14} />现在关心一次</button>
  </PageHeader>

  <!-- One-line map so people know where things live -->
  <div class="scope-map" role="note">
    <article>
      <strong>本页 · AI 主动</strong>
      <span>开关、安静时间、主动链、AI 发过的消息</span>
    </article>
    <article>
      <strong>对话内提醒 / 定时</strong>
      <span>每日 HH:MM 系统提醒、循环任务、定时执行</span>
      {#if onOpenTasks}
        <button type="button" class="quiet-button tiny" onclick={onOpenTasks}>
          <BellRing size={12} />回到对话{reminderCount ? ` · ${reminderCount} 条提醒` : ''}
        </button>
      {/if}
    </article>
  </div>

  <div class="proactive-overview">
    <article>
      <span class="overview-icon green"><Play size={16} /></span>
      <div><strong>{engineLabel}</strong><small>主动引擎</small></div>
    </article>
    <article>
      <span class="overview-icon blue"><Sparkles size={16} /></span>
      <div><strong>{activeChains.length}</strong><small>条活跃主动链</small></div>
    </article>
    <article>
      <span class="overview-icon amber"><BellRing size={16} /></span>
      <div><strong>{reminderCount}</strong><small>条每日提醒</small></div>
    </article>
  </div>

  <div class="proactive-settings-grid compact-top">
    <section class="proactive-settings-card">
      <div class="card-heading">
        <div>
          <h2>引擎</h2>
          <p>控制 AI 会不会自己找你说话。不影响已经创建的系统提醒。</p>
        </div>
        <Zap size={20} />
      </div>
      <SettingRow title="启用 AI 主动" desc="关闭后不再新建 AI 关心链">
        <Toggle checked={proactiveEnabled} label="启用 AI 主动" onChange={(v) => (proactiveEnabled = v)} />
      </SettingRow>
      <SettingRow title="暂停" desc="临时停发；托盘也可切换">
        <Toggle checked={paused} label="暂停 AI 主动" onChange={(v) => (paused = v)} />
      </SettingRow>
      <SettingRow title="安静时间" desc="之后 AI 更少打扰">
        <select class="compact-select" bind:value={bedtimeHour}>
          {#each [21, 22, 23, 0, 1, 2] as hour}
            <option value={hour}>{String(hour).padStart(2, '0')}:00</option>
          {/each}
        </select>
      </SettingRow>
      <div class="proactive-settings-actions">
        <button class="primary-button" onclick={saveSettings}><Save size={14} />保存</button>
        <button class="quiet-button" onclick={runNow}><Sparkles size={14} />现在关心一次</button>
        {#if saved}<span class="save-result">{saved}</span>{/if}
      </div>
    </section>

    <section class="proactive-settings-card">
      <div class="card-heading">
        <div>
          <h2>Heartbeat</h2>
          <p>自动检查「设置 → 模型 → 接入」里的供应商是否可达。</p>
        </div>
        <RefreshCw size={20} />
      </div>
      {#if connections.length}
        <div class="connection-health-list">
          {#each connections as connection (connection.id)}
            <article>
              <span class="health-dot"></span>
              <div>
                <strong>{connection.name || connection.provider}</strong>
                <small>{connection.provider} · {endpointLabel(connection.endpoint)}</small>
              </div>
              <span class="badge dim">自动</span>
            </article>
          {/each}
        </div>
      {:else}
        <div class="proactive-empty compact">
          <p>还没有模型接入。去设置里添加供应商后，这里会自动列出。</p>
        </div>
      {/if}
    </section>
  </div>

  <div class="proactive-section-head">
    <div>
      <h2>AI 主动链</h2>
      <p>模型决定要不要说、何时再说。可手动跑一次或取消。</p>
    </div>
  </div>
  <div class="proactive-list chain-list">
    {#each chains as chain (chain.id)}
      <article>
        <div class="proactive-time">
          <strong><Sparkles size={15} /></strong>
          <span>{chainStatusLabel(chain.status)}</span>
        </div>
        <div class="proactive-message">
          <strong>{chain.purpose}</strong>
          <small>
            {chain.kind === 'required_reminder' ? '必需 · ' : '自主 · '}
            {chain.nextRunAt ? `下次 ${new Date(chain.nextRunAt).toLocaleString('zh-CN')}` : '等待下次唤醒'}
          </small>
        </div>
        <div class="proactive-item-actions">
          {#if chain.status === 'active' || chain.status === 'running'}
            <button class="quiet-button" onclick={() => runChain(chain.id)}>现在运行</button>
            <button class="quiet-button" onclick={() => cancelChain(chain.id)}>取消</button>
          {/if}
        </div>
      </article>
    {:else}
      <div class="proactive-empty">
        <Sparkles size={24} />
        <h3>还没有主动链</h3>
        <p>引擎开启后，AI 会在合适时机自己建链。也可点「现在关心一次」试跑。</p>
        <button class="primary-button" onclick={runNow}><Sparkles size={14} />现在关心一次</button>
      </div>
    {/each}
  </div>

  <div class="proactive-section-head">
    <div>
      <h2>AI 最近说了什么</h2>
      <p>只显示 AI 主动消息；系统提醒会通过通知和对话中的执行记录呈现。</p>
    </div>
  </div>
  <div class="proactive-log-list">
    {#each aiLog.slice(0, 12) as item (item.id)}
      <article>
        <span class="badge amber">AI</span>
        <div>
          <strong>{item.body}</strong>
          <small>{item.reason} · {formatLogTime(item.ts)}{item.state ? ` · ${item.state}` : ''}</small>
        </div>
      </article>
    {:else}
      <p class="settings-note">还没有 AI 主动消息。</p>
    {/each}
  </div>
</section>
