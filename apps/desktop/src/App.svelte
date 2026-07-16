<script lang="ts">
  import {onMount, tick} from 'svelte';
  import {BellRing, MessageCircleMore, Layers3, Zap, Send, Settings, Plus, Paperclip, ArrowUp, Search, Minus, Maximize2, X, Trash2, MessagesSquare, Workflow, Wrench, FolderGit2} from 'lucide-svelte';
  import StatusDot from './lib/StatusDot.svelte';
  import Oobe from './lib/Oobe.svelte';
  import QuickWindow from './lib/QuickWindow.svelte';
  import ReviewWindow from './lib/ReviewWindow.svelte';
  import PageHeader from './lib/PageHeader.svelte';
  import SettingsView from './lib/SettingsView.svelte';
  import MemoryEditor from './lib/MemoryEditor.svelte';
  import TasksView from './lib/TasksView.svelte';
  import ProactiveView from './lib/ProactiveView.svelte';
  import ChannelsView from './lib/ChannelsView.svelte';
  import ConversationsView from './lib/ConversationsView.svelte';
  import WorkflowsView from './lib/WorkflowsView.svelte';
  import McpView from './lib/McpView.svelte';
  import RecentsSidebar from './lib/RecentsSidebar.svelte';
  import ProjectWorkspace from './lib/ProjectWorkspace.svelte';
  import MessageContent from './lib/MessageContent.svelte';
  import type {ChatMessage, Conversation, FileNode, MemoryCategory, MemoryItem, ModelSetup, Persona, Project, Theme, ViewId} from './lib/types';
  import {categoryFromWire, categoryToWire, importanceStars, normalizeConversation, normalizeProject} from './lib/types';
  import {formatRuntimeError, runtime} from './lib/runtime';
  import {routeUserMessage, shouldTransferToExecutor, taskTitleFromText} from '@pattern/core';

  const nav = [
    {id: 'chat', label: '对话', icon: MessageCircleMore},
    {id: 'project', label: '项目', icon: FolderGit2},
    {id: 'conversations', label: '管理', icon: MessagesSquare},
    {id: 'memory', label: '记忆', icon: Layers3},
    {id: 'tasks', label: '任务', icon: Zap},
    {id: 'proactive', label: '主动', icon: BellRing},
    {id: 'workflows', label: '技能', icon: Workflow},
    {id: 'mcp', label: '工具', icon: Wrench},
    {id: 'channels', label: '通道', icon: Send},
    {id: 'settings', label: '设置', icon: Settings},
  ] as const;

  const params = new URLSearchParams(location.search);
  const isQuick = params.get('window') === 'quick';
  const isReview = params.get('window') === 'review';
  const isDemo = params.has('demo');

  let activeView = $state<ViewId>('chat');
  let ready = $state(false);
  let persona = $state<Persona | null>(null);
  let messages = $state<ChatMessage[]>([]);
  let conversations = $state<Conversation[]>([]);
  let projects = $state<Project[]>([]);
  let activeConversationId = $state('');
  let activeProjectId = $state('');
  let draft = $state('');
  let replying = $state(false);
  let stream = $state<HTMLDivElement>();
  let query = $state('');
  let category = $state<'all' | MemoryCategory>('all');
  let theme = $state<Theme>('night');
  let toast = $state('');
  let runtimeConnected = $state(false);
  let memoryItems = $state<MemoryItem[]>([]);
  let editingMemory = $state(false);
  let memoryCount = $state(0);
  let proactiveCount = $state(0);
  let proactiveInbox = $state<Array<{id: string; body: string; type?: string; reason?: string; origin?: 'ai' | 'system'; state?: string; chainId?: string; ts?: number}>>([]);
  let proactivePreview = $state<{id: string; body: string; type?: string; reason?: string; origin?: 'ai' | 'system'; chainId?: string} | null>(null);
  let lastConsolidateAt = $state<number | null>(null);
  let memorySearchTimer: ReturnType<typeof setTimeout>;
  let taskDraft = $state<{title: string; detail: string; nonce: number} | null>(null);
  let attachmentInput = $state<HTMLInputElement>();
  let agentState = $state<'idle' | 'thinking' | 'executing' | 'paused' | 'approval'>('idle');
  let activeSlot = $state<'companion' | 'executor'>('companion');
  // When enabled, desktop-action turns may spawn/internalize a sub-agent worker.
  // When disabled, primary agent still acts (tools/desktop) but does not spawn sub-agents.
  let allowSubAgents = $state(typeof localStorage === 'undefined' ? true : localStorage.getItem('pattern-allow-sub-agents') !== '0');
  let showProjectDialog = $state(false);
  let projectNameDraft = $state('');
  let projectPathDraft = $state('');
  let fileNodes = $state<FileNode[]>([]);
  let fileLoading = $state(false);
  let fileError = $state('');
  let selectedFilePath = $state('');
  let filePreview = $state('');
  let streamingId = $state('');
  let lastUserText = $state('');
  let attachedPaths = $state<string[]>([]);
  let editingMemoryItem = $state<MemoryItem | null>(null);
  let memoryProposals = $state<Array<{id: string; text: string; category: string; importance: number; sourceConv?: string | null; reason?: string}>>([]);
  let foregroundTitle = $state('');
  let foregroundBusy = $state(false);
  let projectDiff = $state<{status: string; diff: string} | null>(null);
  let clock = $state(Date.now());
  let redefiningPersona = $state(false);
  let personaEditorMode = $state<'redefine' | 'new'>('redefine');
  let editingMessageId = $state('');

  const activeConversation = $derived(conversations.find((item) => item.id === activeConversationId) || null);
  const activeProject = $derived(projects.find((item) => item.id === activeProjectId) || null);
  const showRecents = $derived(activeView === 'chat' || activeView === 'conversations');
  const homeGreeting = $derived.by(() => {
    const hour = new Date(clock).getHours();
    const salutation = hour < 5 ? '夜深了' : hour < 12 ? '早上好' : hour < 14 ? '中午好' : hour < 18 ? '下午好' : hour < 23 ? '晚上好' : '夜深了';
    const name = persona?.userName?.trim();
    return name ? `${salutation}，${name}` : salutation;
  });
  const homeContext = $derived.by(() => {
    const date = new Date(clock);
    const weekday = '日一二三四五六'[date.getDay()];
    const unread = proactiveInbox.filter((item) => item.state !== 'read' && item.state !== 'dismissed').length;
    const extra = unread ? ` · ${unread} 条主动消息待处理` : conversations.length ? ` · ${conversations.length} 个对话在本地` : '';
    return `${date.getMonth() + 1}月${date.getDate()}日 · 星期${weekday} · 全局对话${extra}`;
  });

  async function pushTrayState(state: typeof agentState) {
    if (!(window as any).__TAURI_INTERNALS__) return;
    try {
      const {invoke} = await import('@tauri-apps/api/core');
      await invoke('set_tray_state', {state});
    } catch {
      /* tray optional */
    }
  }

  function mapMemory(item: any): MemoryItem {
    const cat = categoryFromWire[item.category] || item.category || '事实';
    const importance = Number(item.importance ?? 0.5);
    const sourceConv = item.sourceConv || item.source_conv || null;
    return {
      id: item.id,
      category: cat,
      text: item.text,
      importance: importance > 1 ? importance / 3 : importance,
      expired: !!item.expired,
      accessCount: item.accessCount,
      sourceConv,
      meta: item.meta || (sourceConv ? `来源对话 · 访问 ${item.accessCount ?? 0}` : `访问 ${item.accessCount ?? 0}`),
    };
  }

  function persistConversations() {
    localStorage.setItem('pattern-conversations', JSON.stringify(conversations));
    persistWorkspaceState();
  }

  function persistProjects() {
    localStorage.setItem('pattern-projects', JSON.stringify(projects));
    persistWorkspaceState();
    void syncProjectsToSidecar();
  }

  async function syncProjectsToSidecar() {
    try {
      if (!(await runtime.connect())) return;
      await runtime.request({
        type: 'projects.sync',
        id: crypto.randomUUID(),
        projects: projects.map((item) => ({id: item.id, name: item.name, path: item.path})),
      } as any);
    } catch (error) {
      console.error('[pattern] projects.sync failed', error);
    }
  }

  function persistWorkspaceState() {
    if (!(window as any).__TAURI_INTERNALS__) return;
    void import('@tauri-apps/api/core')
      .then(({invoke}) => invoke('save_workspace_state', {conversations, projects}))
      .catch((error) => console.error('[pattern] workspace persistence failed', error));
  }

  function pushMessageEvent(
    messageId: string,
    kind: string,
    eventText: string,
    extra: Partial<import('./lib/types').ChatMessageEvent> = {},
    conversationId = activeConversationId,
  ) {
    const conversation = conversationId && conversationId !== activeConversationId
      ? conversations.find((item) => item.id === conversationId)
      : null;
    const messageList = conversation ? conversation.messages : messages;
    const target = messageList.find((message) => message.id === messageId);
    if (!target) return;
    const event = {
      id: extra.id || crypto.randomUUID(),
      kind,
      text: eventText,
      ts: extra.ts || Date.now(),
      status: extra.status,
      action: extra.action,
      tier: extra.tier,
      receipt: extra.receipt,
      taskId: extra.taskId,
      stepId: extra.stepId,
    };
    // Dedupe by stepId when present so task step updates replace instead of stacking forever.
    if (event.stepId) {
      const existing = (target.events || []).findIndex((item) => item.stepId === event.stepId);
      if (existing >= 0) {
        const next = [...(target.events || [])];
        next[existing] = {...next[existing], ...event};
        target.events = next;
        if (conversation) {
          conversation.messages = [...messageList];
          conversation.updatedAt = Date.now();
          conversations = [...conversations];
          persistConversations();
        } else messages = [...messages];
        return;
      }
    }
    // Also collapse identical trailing status lines.
    const last = target.events?.at(-1);
    if (last && !event.stepId && last.kind === event.kind && last.text === event.text && last.status === event.status) {
      last.ts = event.ts;
      if (conversation) {
        conversation.messages = [...messageList];
        conversation.updatedAt = Date.now();
        conversations = [...conversations];
        persistConversations();
      } else messages = [...messages];
      return;
    }
    target.events = [...(target.events || []), event];
    if (conversation) {
      conversation.messages = [...messageList];
      conversation.updatedAt = Date.now();
      conversations = [...conversations];
      persistConversations();
    } else messages = [...messages];
  }

  function settleMessageEvents(messageId: string, status: 'done' | 'failed' | 'skipped') {
    const target = messages.find((message) => message.id === messageId);
    if (!target?.events?.length) return;
    // Leave task-bound live steps alone — syncTaskTimeline owns those.
    target.events = target.events.map((event) => {
      if (event.taskId && (event.status === 'running' || event.status === 'pending' || event.status === 'awaiting_approval')) {
        return event;
      }
      if (event.status === 'running' || event.status === 'pending' || !event.status) {
        return {...event, status};
      }
      return event;
    });
    messages = [...messages];
  }

  function syncTaskTimeline(task: any) {
    const conversationId = task.conversationId || activeConversationId;
    const taskConversation = conversationId && conversationId !== activeConversationId
      ? conversations.find((item) => item.id === conversationId)
      : null;
    const messageList = taskConversation ? taskConversation.messages : messages;
    const target = [...messageList].reverse().find((m) => m.role === 'assistant' && m.taskCard?.taskId === task.id);
    if (!target) return;
    target.taskCard = {...target.taskCard!, status: task.status, title: task.title, detail: task.detail || target.taskCard?.detail};
    const steps = Array.isArray(task.steps) ? task.steps : [];
    for (const step of steps) {
      pushMessageEvent(target.id, step.action === 'mcp' ? 'mcp' : 'task', step.detail || step.action || '步骤', {
        id: step.id,
        stepId: step.id,
        taskId: task.id,
        action: step.action,
        status: step.status,
        tier: step.tier,
        receipt: step.receipt,
        ts: step.ts ? (step.ts < 10_000_000_000 ? step.ts * 1000 : step.ts) : Date.now(),
      }, conversationId);
    }
    // Task-level status pulse
    pushMessageEvent(target.id, 'task', `任务状态 · ${task.status}: ${task.title}`, {
      stepId: `task:${task.id}:status`,
      taskId: task.id,
      status: ['done', 'failed', 'cancelled'].includes(task.status)
        ? task.status === 'done'
          ? 'done'
          : task.status === 'failed'
            ? 'failed'
            : 'skipped'
        : task.status === 'awaiting_approval'
          ? 'awaiting_approval'
          : task.status === 'running'
            ? 'running'
            : 'pending',
      ts: Date.now(),
    }, conversationId);
    // Agent results if present
    for (const result of task.agentResults || []) {
      pushMessageEvent(target.id, 'agent', `${result.skillId || 'agent'}: ${(result.output || '').slice(0, 180)}`, {
        stepId: `task:${task.id}:agent:${result.skillId || 'agent'}`,
        status: result.status === 'failed' ? 'failed' : 'done',
        taskId: task.id,
        receipt: result.output,
        ts: result.ts || Date.now(),
      }, conversationId);
    }
    if (['done', 'failed', 'cancelled'].includes(task.status)) {
      const lastReceipt = [...steps].reverse().find((step: any) => step.receipt)?.receipt;
      target.text = task.status === 'done'
        ? `已完成：${task.title}${lastReceipt ? `\n\n${lastReceipt}` : ''}`
        : task.status === 'failed'
          ? `执行失败：${task.title}\n\n${task.error || '没有可用的错误详情'}`
          : `已终止：${task.title}`;
    }
    if (taskConversation) {
      taskConversation.messages = [...messageList];
      taskConversation.updatedAt = Date.now();
      conversations = [...conversations];
      persistConversations();
    } else {
      messages = [...messages];
      void persistSession();
    }
  }

  async function expandDirectory(node: FileNode) {
    if (node.kind !== 'directory') return;
    let children: FileNode[] = [];
    if ((window as any).__TAURI_INTERNALS__) {
      try {
        const {invoke} = await import('@tauri-apps/api/core');
        children = await invoke<FileNode[]>('list_directory', {path: node.path, depth: 1});
      } catch (error) {
        fileError = String(error);
        return;
      }
    } else {
      fileError = '浏览器预览无法展开本地目录';
      return;
    }
    const patch = (items: FileNode[]): FileNode[] =>
      items.map((item) => {
        if (item.path === node.path) return {...item, children};
        if (item.children?.length) return {...item, children: patch(item.children)};
        return item;
      });
    fileNodes = patch(fileNodes);
  }

  async function openProjectFile(node: FileNode) {
    selectedFilePath = node.path;
    if ((window as any).__TAURI_INTERNALS__) {
      try {
        const {invoke} = await import('@tauri-apps/api/core');
        filePreview = await invoke<string>('read_text_file', {path: node.path, maxBytes: 120000});
      } catch (error) {
        filePreview = String(error);
      }
    } else {
      filePreview = '浏览器预览无法读取本地文件内容。';
    }
  }

  async function attachProjectPath(node: FileNode) {
    if (node.kind !== 'file') return;
    if (!(window as any).__TAURI_INTERNALS__) {
      notify('浏览器预览无法读取该文件');
      return;
    }
    try {
      const {invoke} = await import('@tauri-apps/api/core');
      const content = await invoke<string>('read_text_file', {path: node.path, maxBytes: 64_000});
      if (!attachedPaths.includes(node.path)) attachedPaths = [...attachedPaths, node.path];
      draft = `${draft ? `${draft}\n\n` : ''}[已附加项目文件：${node.name}]\n${content}`;
      notify(`已读取并附加 ${node.name}`);
    } catch (error) {
      notify(`附加失败：${error}`);
    }
  }

  async function inspectProjectDiff() {
    const root = activeProject?.path;
    if (!root) {
      notify('请先打开项目');
      return;
    }
    if (!(await runtime.connect())) {
      notify('运行时未连接，无法读取 Diff');
      return;
    }
    try {
      const result = await runtime.request<any>({type: 'workspace.diff', id: crypto.randomUUID(), root});
      if (result.type === 'workspace.diff.result') {
        projectDiff = {status: result.status, diff: result.diff};
        notify('已刷新项目 Diff');
      }
    } catch (error) {
      notify(`读取 Diff 失败：${error}`);
    }
  }

  async function createProjectWorktree() {
    const root = activeProject?.path;
    if (!root) {
      notify('请先打开项目');
      return;
    }
    if (!(await runtime.connect())) {
      notify('运行时未连接，无法创建 Worktree');
      return;
    }
    try {
      const result = await runtime.request<any>({
        type: 'workspace.worktree.create',
        id: crypto.randomUUID(),
        root,
        name: `pattern-${Date.now().toString(36)}`,
      });
      if (result.type === 'workspace.worktree.created') {
        notify(`Worktree 已创建：${result.path}`);
        draft = `${draft ? `${draft}
` : ''}[worktree] ${result.path} (branch ${result.branch})`;
      }
    } catch (error) {
      notify(`创建 Worktree 失败：${error}`);
    }
  }

  async function openTaskReview(taskId: string) {
    if ((window as any).__TAURI_INTERNALS__) {
      const {invoke} = await import('@tauri-apps/api/core');
      try {
        await invoke('show_review', {taskId});
        return;
      } catch {
        await invoke('show_review');
      }
    } else {
      notify(`审查任务 ${taskId.slice(0, 8)}…（桌面端可打开审查窗）`);
    }
  }

  async function pickProjectFolder() {
    if ((window as any).__TAURI_INTERNALS__) {
      try {
        const {invoke} = await import('@tauri-apps/api/core');
        const selected = await invoke<string | null>('pick_directory');
        if (selected) {
          projectPathDraft = selected;
          if (!projectNameDraft.trim()) {
            projectNameDraft = selected.split(/[\/]/).filter(Boolean).at(-1) || '';
          }
        }
        return;
      } catch (error) {
        notify(`选择文件夹失败：${error}`);
      }
    }
    const fallback = window.prompt('请输入项目绝对路径', projectPathDraft || '');
    if (fallback) projectPathDraft = fallback;
  }


  async function stopGeneration() {
    const id = streamingId;
    if (id) runtime.abortChat(id);
    replying = false;
    agentState = 'idle';
    if (id) {
      const target = messages.find((message) => message.id === id);
      if (target) {
        target.streaming = false;
        if (!target.text?.trim() && !target.error) target.text = '（已停止）';
      }
      settleMessageEvents(id, 'skipped');
      pushMessageEvent(id, 'status', '已停止生成', {status: 'skipped', stepId: `chat:${id}:status`});
    }
    streamingId = '';
    attachedPaths = [];
    await persistSession();
  }

  async function retryAssistant(messageId: string) {
    const idx = messages.findIndex((message) => message.id === messageId);
    if (idx < 0) return;
    let userText = '';
    let userIndex = -1;
    for (let i = idx - 1; i >= 0; i--) {
      if (messages[i].role === 'user') {
        userText = messages[i].text;
        userIndex = i;
        break;
      }
    }
    if (!userText || userIndex < 0) return;
    messages = messages.slice(0, userIndex);
    draft = userText;
    await sendMessage();
  }

  async function copyMessage(message: ChatMessage) {
    try {
      await navigator.clipboard.writeText(message.text);
      notify('已复制消息');
    } catch {
      notify('复制失败，请手动选择文本');
    }
  }

  function editUserMessage(message: ChatMessage) {
    if (replying || message.role !== 'user') return;
    editingMessageId = message.id;
    draft = message.text;
    notify('修改后发送，将从这条消息继续对话');
  }

  function cancelMessageEdit() {
    editingMessageId = '';
    draft = '';
  }

  function isUserBusy() {
    if (replying || agentState === 'thinking' || agentState === 'executing' || agentState === 'approval') return true;
    if (document.visibilityState === 'visible' && (activeView === 'project' || activeView === 'tasks')) return true;
    if (foregroundBusy) return true;
    return false;
  }

  async function refreshForeground() {
    try {
      if ((window as any).__TAURI_INTERNALS__) {
        const {invoke} = await import('@tauri-apps/api/core');
        const info = await invoke<{title?: string}>('get_foreground_window');
        foregroundTitle = info?.title || '';
      } else if (await runtime.connect()) {
        const res = await runtime.request<any>({type: 'runtime.foreground', id: crypto.randomUUID()});
        if (res.type === 'runtime.foreground.result') {
          foregroundTitle = res.title || '';
          foregroundBusy = !!res.busyHint;
          return;
        }
      }
      foregroundBusy = /Visual Studio Code|Code -|IntelliJ|PyCharm|terminal|Windows Terminal|cmd\.exe|powershell|Chrome|Edge|Firefox|Slack|Zoom|会议|Excel|Word|PowerPoint/i.test(foregroundTitle);
    } catch {
      /* optional */
    }
  }

  async function refreshMemoryProposals() {
    if (!(await runtime.connect())) return;
    const res = await runtime.request<any>({type: 'memory.propose.list', id: crypto.randomUUID()});
    if (res.type === 'memory.propose.list.result') memoryProposals = res.items || [];
  }

  async function acceptMemoryProposal(id: string) {
    if (!(await runtime.connect())) {
      notify('运行时未连接');
      return;
    }
    const res = await runtime.request<any>({type: 'memory.propose.accept', id: crypto.randomUUID(), proposalId: id});
    if (res.type === 'memory.propose.accept.result' && res.ok) {
      memoryProposals = memoryProposals.filter((item) => item.id !== id);
      await refreshMemories();
      notify('记忆已确认保存');
    } else {
      notify('确认失败');
    }
  }

  async function rejectMemoryProposal(id: string) {
    if (!(await runtime.connect())) {
      notify('运行时未连接');
      return;
    }
    await runtime.request<any>({type: 'memory.propose.reject', id: crypto.randomUUID(), proposalId: id});
    memoryProposals = memoryProposals.filter((item) => item.id !== id);
    notify('已忽略该候选记忆');
  }

  function openMemorySource(memory: MemoryItem) {
    const source = memory.sourceConv?.trim();
    if (!source) {
      notify('这条记忆没有可跳转的来源对话');
      return;
    }
    if (source.startsWith('filewatch:')) {
      notify('来源是文件监视事件，不是聊天对话');
      return;
    }
    const conversation = conversations.find((item) => item.id === source);
    if (!conversation) {
      notify('来源对话已不在本地列表中');
      return;
    }
    void openConversation(conversation.id);
    notify('已跳转到来源对话');
  }

  function createConversation(scope: 'global' | 'project', projectId?: string): Conversation {
    return {
      id: crypto.randomUUID(),
      title: scope === 'project' ? '项目对话' : '新对话',
      createdAt: Date.now(),
      updatedAt: Date.now(),
      messages: [],
      scope,
      projectId: scope === 'project' ? projectId : undefined,
    };
  }

  function conversationTitleFromText(text: string, fallback = '新对话') {
    const value = text.replace(/\s+/g, ' ').trim();
    return (value || fallback).slice(0, 36);
  }

  function ingestProactiveImpulse(item: {id: string; body: string; type?: string; reason?: string; origin?: 'ai' | 'system'; state?: string; chainId?: string; ts?: number}) {
    const exists = proactiveInbox.some((entry) => entry.id === item.id);
    proactiveInbox = [item, ...proactiveInbox.filter((entry) => entry.id !== item.id)].slice(0, 100);
    if (!exists) proactiveCount += 1;
    notify(`${item.origin === 'system' ? '系统提醒' : 'AI 主动消息'}已进入收件箱`);
  }

  function proactiveTitle(item: {body: string; type?: string; reason?: string; origin?: 'ai' | 'system'}) {
    return conversationTitleFromText(item.reason || item.type || item.body, item.origin === 'system' ? '提醒' : '主动消息');
  }

  async function markProactiveState(itemId: string, state: 'read' | 'dismissed' | 'replied') {
    if (state === 'dismissed') {
      proactiveInbox = proactiveInbox.filter((entry) => entry.id !== itemId);
      if (proactivePreview?.id === itemId) proactivePreview = null;
    } else {
      proactiveInbox = proactiveInbox.map((entry) => entry.id === itemId ? {...entry, state} : entry);
    }
    if (await runtime.connect()) {
      void runtime.request({type: 'proactive.inbox.mark', id: crypto.randomUUID(), itemId, state});
    }
  }

  async function dismissProactiveInboxItem(item: {id: string; chainId?: string; origin?: 'ai' | 'system'}, options: {quiet?: boolean} = {}) {
    if (item.origin === 'ai' && item.chainId && await runtime.connect()) {
      void runtime.request({type: 'proactive.chain.cancel', id: crypto.randomUUID(), chainId: item.chainId});
    }
    await markProactiveState(item.id, 'dismissed');
    const orphan = conversations.find((conversation) =>
      conversation.messages.length === 1 && conversation.messages[0]?.id === item.id,
    );
    if (orphan) {
      conversations = conversations.filter((conversation) => conversation.id !== orphan.id);
      if (activeConversationId === orphan.id) {
        activeConversationId = '';
        messages = [];
      }
      persistConversations();
    }
    if (!options.quiet) notify('已忽略这条主动消息');
  }

  async function dismissAllProactiveInbox() {
    const items = proactiveInbox.filter((item) => item.state !== 'dismissed' && item.state !== 'replied');
    if (!items.length) {
      notify('收件箱已经是空的');
      return;
    }
    for (const item of items) {
      await dismissProactiveInboxItem(item, {quiet: true});
    }
    if (proactivePreview && items.some((item) => item.id === proactivePreview?.id)) {
      proactivePreview = null;
    }
    notify(items.length === 1 ? '已忽略 1 条主动消息' : `已全部忽略 ${items.length} 条主动消息`);
  }

  async function openProactiveInboxItem(item: {id: string; body: string; type?: string; reason?: string; origin?: 'ai' | 'system'; chainId?: string}) {
    // Only reopen a real conversation if the user already replied there.
    const existing = conversations.find((conversation) =>
      conversation.messages.some((message) => message.id === item.id) &&
      conversation.messages.some((message) => message.role === 'user'),
    );
    if (existing) {
      proactivePreview = null;
      await openConversation(existing.id);
      await markProactiveState(item.id, 'read');
      return;
    }

    const mode = (localStorage.getItem('pattern-proactive-mode') || 'new_chat') as 'new_chat' | 'inline';
    const proactiveMessage = {
      id: item.id || crypto.randomUUID(),
      role: 'assistant' as const,
      text: item.body,
      time: new Date().toLocaleTimeString('zh-CN', {hour: '2-digit', minute: '2-digit'}),
      proactive: item.type || '主动',
    };

    // 轻插：插入当前/最近全局对话，不新建会话。
    if (mode === 'inline') {
      let conversation = activeConversationId
        ? conversations.find((entry) => entry.id === activeConversationId && !entry.archived)
        : undefined;
      if (!conversation) conversation = conversations.find((entry) => !entry.archived && entry.scope === 'global');
      if (conversation) {
        if (replying) await stopGeneration();
        if (!conversation.messages.some((message) => message.id === item.id)) {
          conversation.messages = [...conversation.messages, proactiveMessage];
        }
        conversation.updatedAt = Date.now();
        if (!conversation.title || conversation.title === '新对话') conversation.title = proactiveTitle(item);
        conversations = [...conversations];
        proactivePreview = null;
        activeConversationId = conversation.id;
        messages = [...conversation.messages];
        activeView = 'chat';
        persistConversations();
        await markProactiveState(item.id, 'read');
        await scrollConversationToEnd();
        return;
      }
    }

    // 新对话（默认）：懒预览，回复后才真正创建对话。
    if (replying) await stopGeneration();
    proactivePreview = item;
    activeConversationId = '';
    messages = [proactiveMessage];
    activeView = 'chat';
    await markProactiveState(item.id, 'read');
    await scrollConversationToEnd();
  }

  async function refreshMemories() {
    if (!(await runtime.connect())) {
      // Keep current list empty/offline — never inject prefabricated memories.
      return;
    }
    const res = await runtime.request<any>({
      type: 'memory.list',
      id: crypto.randomUUID(),
      query: query || null,
      category: category === 'all' ? null : categoryToWire[category] || category,
    });
    if (res.type === 'memory.list.result') memoryItems = res.items.map(mapMemory);
    const stats = await runtime.request<any>({type: 'memory.stats', id: crypto.randomUUID()});
    if (stats.type === 'memory.stats.result') {
      memoryCount = stats.count;
      lastConsolidateAt = stats.lastConsolidateAt;
    }
    const proactive = await runtime.request<any>({type: 'proactive.list', id: crypto.randomUUID(), limit: 100});
    if (proactive.type === 'proactive.list.result') {
      const key = new Date().toDateString();
      proactiveCount = proactive.items.filter((item: any) => new Date(item.ts * 1000).toDateString() === key && item.delivered).length;
      proactiveInbox = proactive.items.filter((item: any) => item.state !== 'dismissed').slice(0, 100);
    }
  }

  async function loadDirectoryTree(path: string) {
    fileLoading = true;
    fileError = '';
    fileNodes = [];
    if (!path.trim()) {
      fileLoading = false;
      fileError = '请先为项目设置本地路径';
      return;
    }
    if (!(window as any).__TAURI_INTERNALS__) {
      fileNodes = [];
      fileError = '浏览器预览无法读取本地文件夹；请使用桌面端打开项目。';
      fileLoading = false;
      return;
    }
    try {
      const {invoke} = await import('@tauri-apps/api/core');
      fileNodes = await invoke<FileNode[]>('list_directory', {path, depth: 1});
    } catch (error) {
      fileError = String(error);
      fileNodes = [];
    } finally {
      fileLoading = false;
    }
  }

  onMount(async () => {
    try {
      const channel = new BroadcastChannel('pattern-conversations');
      channel.onmessage = () => {
        try {
          const stored = JSON.parse(localStorage.getItem('pattern-conversations') || '[]') as Conversation[];
          conversations = stored.map((item) => normalizeConversation(item));
          if (!replying) {
            const current = conversations.find((item) => item.id === activeConversationId);
            if (current) messages = [...current.messages];
          }
        } catch { /* malformed local state is ignored */ }
      };
      (window as any).__patternConversationChannel?.close?.();
      (window as any).__patternConversationChannel = channel;
    } catch { /* BroadcastChannel is optional */ }
    const saved = localStorage.getItem('pattern-persona');
    try {
      const stored = JSON.parse(localStorage.getItem('pattern-conversations') || '[]') as Conversation[];
      if (Array.isArray(stored) && stored.length) {
        // Migrate empty conversations created by the pre-lazy-create startup path.
        const normalized = stored.map((item) => normalizeConversation(item));
        conversations = normalized.filter((item) => item.messages.some((message) => message.text.trim()));
        if (conversations.length !== normalized.length) persistConversations();
      }
    } catch { /* start with a clean local conversation list */ }
    try {
      const storedProjects = JSON.parse(localStorage.getItem('pattern-projects') || '[]') as Project[];
      if (Array.isArray(storedProjects) && storedProjects.length) {
        projects = storedProjects.map((item) => normalizeProject(item));
        activeProjectId = projects[0]?.id || '';
      }
    } catch { /* start with no projects */ }
    void syncProjectsToSidecar();

    if ((window as any).__TAURI_INTERNALS__) {
      try {
        const {invoke} = await import('@tauri-apps/api/core');
        persona = await invoke<Persona | null>('load_persona');
        const workspaceState = await invoke<{conversations?: Conversation[]; projects?: Project[]} | null>('load_workspace_state');
        if (workspaceState?.conversations?.length) {
          conversations = workspaceState.conversations.map((item) => normalizeConversation(item));
          localStorage.setItem('pattern-conversations', JSON.stringify(conversations));
        }
        if (workspaceState?.projects?.length) {
          projects = workspaceState.projects.map((item) => normalizeProject(item));
          activeProjectId = projects[0]?.id || '';
          localStorage.setItem('pattern-projects', JSON.stringify(projects));
        }
        const session = await invoke<ChatMessage[] | null>('load_session');
        if (session?.length && !conversations.length) {
          const conversation = createConversation('global');
          conversation.messages = session;
          conversation.updatedAt = Date.now();
          const firstUser = session.find((item) => item.role === 'user')?.text.trim();
          if (firstUser) conversation.title = firstUser.replace(/\s+/g, ' ').slice(0, 36);
          conversations = [conversation];
        }
      } catch (error) {
        console.error(error);
      }
      runtimeConnected = await runtime.connect();
      runtime.onStatus((connected) => {
        runtimeConnected = connected;
      });
      runtime.on((message) => {
        if (message.type === 'memory.changed') void refreshMemories();
        if (message.type === 'memory.proposed') memoryProposals = message.items || [];
        if (message.type === 'runtime.agent_state') agentState = message.state;
        if (message.type === 'task.updated') {
          if (message.task.status === 'running') agentState = 'executing';
          else if (message.task.status === 'paused') agentState = 'paused';
          else if (message.task.status === 'awaiting_approval') agentState = 'approval';
          else if (['done', 'failed', 'cancelled'].includes(message.task.status)) agentState = 'idle';
          syncTaskTimeline(message.task);
        }
        if (message.type === 'task.approval_required') {
          const last = [...messages].reverse().find((m) => m.role === 'assistant' && (!m.taskCard || m.taskCard.taskId === message.taskId));
          if (last) {
            pushMessageEvent(last.id, 'task', message.step?.detail || '需要审批', {
              taskId: message.taskId,
              stepId: message.step?.id,
              action: message.step?.action || 'approval',
              status: 'awaiting_approval',
              tier: message.step?.tier,
              ts: Date.now(),
            });
            void persistSession();
          }
        }
        if (message.type === 'proactive.impulse') {
          ingestProactiveImpulse(message.item);
          if ((window as any).__TAURI_INTERNALS__) {
            import('@tauri-apps/api/core').then(({invoke}) => invoke('show_quick')).catch(() => {});
          }
        }
        if (message.type === 'proactive.inbox.updated') {
          proactiveInbox = [message.item, ...proactiveInbox.filter((item) => item.id !== message.item.id)].slice(0, 100);
        }
      });
      await refreshMemories();
    } else {
      runtimeConnected = await runtime.connect();
      runtime.onStatus((connected) => {
        runtimeConnected = connected;
      });
      runtime.on((message) => {
        if (message.type === 'proactive.impulse') ingestProactiveImpulse(message.item);
        if (message.type === 'proactive.inbox.updated') proactiveInbox = [message.item, ...proactiveInbox.filter((item) => item.id !== message.item.id)].slice(0, 100);
      });
      // No prefabricated demo memories.
    }

    if (conversations.length) {
      const preferred = conversations.find((item) => !item.archived && item.scope === 'global')
        || conversations.find((item) => !item.archived)
        || conversations[0];
      activeConversationId = preferred.id;
      messages = [...preferred.messages];
      if (preferred.scope === 'project' && preferred.projectId) {
        activeProjectId = preferred.projectId;
        activeView = 'project';
      }
      void scrollConversationToEnd();
    }

    if (!persona && saved) persona = JSON.parse(saved);
    else if (!persona && isDemo)
      persona = {name: 'Pattern', userName: '你', description: '说话直接，但知道分寸。', proactive: 'free'};
    const storedTheme = localStorage.getItem('pattern-theme') as Theme | null;
    theme = storedTheme && ['night', 'day', 'ocean', 'forest', 'paper'].includes(storedTheme) ? storedTheme : 'night';
    document.documentElement.dataset.theme = theme;
    if (activeProject) void loadDirectoryTree(activeProject.path);
    if (isDemo) {
      (window as any).__patternTest = {
        ingestProactive: (item: {id: string; body: string; type?: string; reason?: string}) => ingestProactiveImpulse(item),
      };
    }
    ready = true;
    void refreshForeground();
    void refreshMemoryProposals();
    (window as any).__patternClockTimer && clearInterval((window as any).__patternClockTimer);
    (window as any).__patternClockTimer = setInterval(() => { clock = Date.now(); }, 60_000);
    const fgTimer = setInterval(() => { void refreshForeground(); }, 4000);
    // Store on window so we can clear if hot-reloaded; onMount async cannot return cleanup.
    (window as any).__patternFgTimer && clearInterval((window as any).__patternFgTimer);
    (window as any).__patternFgTimer = fgTimer;
  });

  $effect(() => {
    void pushTrayState(agentState);
  });

  $effect(() => {
    const search = query;
    const filter = category;
    if (ready && (window as any).__TAURI_INTERNALS__) {
      clearTimeout(memorySearchTimer);
      memorySearchTimer = setTimeout(() => {
        void refreshMemories();
      }, 180);
      void search;
      void filter;
    }
  });

  async function savePersona(value: Persona, model: ModelSetup) {
    const isRedefinition = redefiningPersona;
    if ((window as any).__TAURI_INTERNALS__) {
      const {invoke} = await import('@tauri-apps/api/core');
      await invoke('save_persona', {persona: value});
      if (!isRedefinition) await invoke('save_model_config', {
          config: {provider: model.provider, endpoint: model.endpoint, model: model.model},
          apiKey: model.apiKey || null,
        });
      runtimeConnected = await runtime.connect();
    }
    persona = value;
    localStorage.setItem('pattern-persona', JSON.stringify(value));
    if (isRedefinition) redefiningPersona = false;
    notify(`${value.name} 已启用`);
  }

  function setTheme(value: Theme) {
    theme = value;
    document.documentElement.dataset.theme = value;
    localStorage.setItem('pattern-theme', value);
  }

  function notify(text: string) {
    toast = text;
    setTimeout(() => {
      if (toast === text) toast = '';
    }, 2200);
  }

  function setAllowSubAgents(next: boolean) {
    allowSubAgents = next;
    try {
      localStorage.setItem('pattern-allow-sub-agents', next ? '1' : '0');
    } catch {
      /* ignore */
    }
    notify(next ? '已启用子 Agent：主 Agent 可派生子代理处理复杂工作' : '已关闭子 Agent：主 Agent 自己调用工具并执行');
  }

  function scrollToBottom(behavior: ScrollBehavior = 'smooth') {
    const el = stream;
    if (!el) return;
    const top = el.scrollHeight;
    if (behavior === 'smooth') el.scrollTo({top, behavior: 'smooth'});
    else el.scrollTop = top;
    // Fallback when the outer view is still the scroller (legacy layout / intermediate paint).
    const parent = el.parentElement;
    if (parent && parent.scrollHeight > parent.clientHeight + 8) {
      if (behavior === 'smooth') parent.scrollTo({top: parent.scrollHeight, behavior: 'smooth'});
      else parent.scrollTop = parent.scrollHeight;
    }
  }

  async function scrollConversationToEnd() {
    // Wait for messages + the active chat-stream bind:this to settle after view/conversation switches.
    await tick();
    scrollToBottom('auto');
    requestAnimationFrame(() => {
      scrollToBottom('auto');
      requestAnimationFrame(() => scrollToBottom('auto'));
    });
  }

  async function persistSession() {
    const now = Date.now();
    const current = conversations.find((item) => item.id === activeConversationId);
    if (current) {
      current.messages = [...messages];
      current.updatedAt = now;
      const firstUser = messages.find((item) => item.role === 'user')?.text.trim();
      if (firstUser) current.title = firstUser.replace(/\s+/g, ' ').slice(0, 36);
      conversations = [...conversations];
      persistConversations();
    }
    if (current?.scope === 'project' && current.projectId) {
      const project = projects.find((item) => item.id === current.projectId);
      if (project) {
        project.updatedAt = now;
        projects = [...projects];
        persistProjects();
      }
    }
    if ((window as any).__TAURI_INTERNALS__) {
      const {invoke} = await import('@tauri-apps/api/core');
      await invoke('save_session', {messages});
    }
  }

  async function newChat(scope: 'global' | 'project' = 'global', projectId?: string) {
    if (scope === 'project' && !projectId) {
      notify('请先选择一个项目');
      return;
    }
    if (replying) await stopGeneration();
    proactivePreview = null;
    activeConversationId = '';
    messages = [];
    if (scope === 'project' && projectId) {
      activeProjectId = projectId;
      activeView = 'project';
      const project = projects.find((item) => item.id === projectId);
      if (project) {
        project.updatedAt = Date.now();
        projects = [...projects];
        persistProjects();
      }
    } else {
      activeView = 'chat';
    }
    await persistSession();
    notify(scope === 'project' ? '项目对话会在你发出第一条消息时创建' : '对话会在你发出第一条消息时创建');
  }

  async function openConversation(id: string) {
    if (id === activeConversationId && !proactivePreview) return;
    if (replying) await stopGeneration();
    const conversation = conversations.find((item) => item.id === id);
    if (!conversation) return;
    proactivePreview = null;
    activeConversationId = id;
    messages = [...conversation.messages];
    if (conversation.scope === 'project' && conversation.projectId) {
      activeProjectId = conversation.projectId;
      activeView = 'project';
      const project = projects.find((item) => item.id === conversation.projectId);
      if (project) void loadDirectoryTree(project.path);
    } else {
      activeView = 'chat';
    }
    await persistSession();
    await scrollConversationToEnd();
  }

  async function openProject(id: string) {
    if (replying) await stopGeneration();
    const project = projects.find((item) => item.id === id);
    if (!project) return;
    activeProjectId = id;
    project.updatedAt = Date.now();
    projects = [...projects];
    persistProjects();
    activeView = 'project';
    const conversation = conversations.find((item) => item.scope === 'project' && item.projectId === id && !item.archived);
    activeConversationId = conversation?.id || '';
    messages = conversation ? [...conversation.messages] : [];
    await loadDirectoryTree(project.path);
    await scrollConversationToEnd();
  }

  function openNewProjectDialog() {
    projectNameDraft = '';
    projectPathDraft = '';
    showProjectDialog = true;
  }

  function createProject() {
    const name = projectNameDraft.trim();
    const path = projectPathDraft.trim();
    if (!name || !path) {
      notify('请填写项目名称和路径');
      return;
    }
    const project = normalizeProject({
      id: crypto.randomUUID(),
      name,
      path,
      createdAt: Date.now(),
      updatedAt: Date.now(),
    });
    projects = [project, ...projects];
    persistProjects();
    showProjectDialog = false;
    void openProject(project.id);
    notify(`项目 ${name} 已创建`);
  }

  function archiveConversation(id: string) {
    const conversation = conversations.find((item) => item.id === id);
    if (!conversation) return;
    conversation.archived = !conversation.archived;
    conversations = [...conversations];
    persistConversations();
  }

  async function deleteConversation(id: string) {
    conversations = conversations.filter((item) => item.id !== id);
    if (activeConversationId === id) {
      const next = conversations.find((item) => !item.archived && item.scope === 'global')
        || conversations.find((item) => !item.archived);
      if (next) {
        activeConversationId = next.id;
        messages = [...next.messages];
        if (next.scope === 'project' && next.projectId) {
          activeProjectId = next.projectId;
          activeView = 'project';
        } else {
          activeView = 'chat';
        }
      } else {
        proactivePreview = null;
        activeConversationId = '';
        messages = [];
        activeView = 'chat';
      }
    }
    persistConversations();
    await persistSession();
    await scrollConversationToEnd();
    notify('对话已删除');
  }

  async function addMemory(item: MemoryItem) {
    if (!(await runtime.connect())) { notify('运行时未连接，记忆尚未保存'); return; }
    {
      const res = await runtime.request<any>({
        type: 'memory.add',
        id: crypto.randomUUID(),
        item: {
          text: item.text,
          category: (categoryToWire[item.category] || item.category) as any,
          importance: typeof item.importance === 'number' && item.importance > 1 ? item.importance / 3 : item.importance,
        },
      });
      if (res.type !== 'memory.add.result') { notify('记忆保存失败'); return; }
      item = mapMemory(res.item);
    }
    memoryItems = [item, ...memoryItems];
    editingMemory = false;
    memoryCount += 1;
    notify('记忆已保存');
  }

  async function updateMemory(id: string, item: MemoryItem) {
    if (!(await runtime.connect())) { notify('运行时未连接，记忆尚未更新'); return; }
    {
      const res = await runtime.request<any>({
        type: 'memory.update',
        id: crypto.randomUUID(),
        memoryId: id,
        item: {
          text: item.text,
          category: (categoryToWire[item.category] || item.category) as any,
          importance: typeof item.importance === 'number' && item.importance > 1 ? item.importance / 3 : item.importance,
        },
      });
      if (res.type !== 'memory.update.result') { notify('记忆更新失败'); return; }
      item = mapMemory(res.item);
    }
    memoryItems = memoryItems.map((entry) => (entry.id === id ? {...item, id} : entry));
    editingMemory = false;
    notify('记忆已更新');
  }

  async function deleteMemory(id: string) {
    if (!(await runtime.connect())) { notify('运行时未连接，记忆尚未删除'); return; }
    const result = await runtime.request<any>({type: 'memory.expire', id: crypto.randomUUID(), memoryId: id});
    if (result.type !== 'memory.expire.result' || !result.ok) { notify('删除记忆失败'); return; }
    memoryItems = memoryItems.filter((item) => item.id !== id);
    memoryCount = Math.max(0, memoryCount - 1);
    notify('记忆已移入历史');
  }

  async function createExecutorTask(text: string, options: {announce?: boolean; openTasks?: boolean; openReview?: boolean} = {}) {
    const title = taskTitleFromText(text);
    const detail = text;
    if (options.announce !== false && !activeConversationId) {
      const scope: 'global' | 'project' = activeView === 'project' && activeProject ? 'project' : 'global';
      const conversation = createConversation(scope, scope === 'project' ? activeProject?.id : undefined);
      conversations = [conversation, ...conversations];
      activeConversationId = conversation.id;
      persistConversations();
    }
    if (!(await runtime.connect())) {
      notify('运行时未连接，主 Agent 暂时无法执行桌面操作');
      draft = text;
      return null;
    }
    // Primary agent owns the turn; executor worker runs underneath, without forcing a page switch.
    activeSlot = 'executor';
    agentState = 'executing';
    let created: any;
    try {
      created = await runtime.request<any>({
        type: 'task.create',
        id: crypto.randomUUID(),
        title,
        detail,
        conversationId: activeConversationId || undefined,
        workspace: activeConversation?.scope === 'project' ? activeProject?.path : undefined,
        projectName: activeConversation?.scope === 'project' ? activeProject?.name : undefined,
      });
    } catch (error) {
      agentState = 'idle';
      draft = text;
      notify(`主 Agent 启动执行失败：${formatRuntimeError(error, {isTauri: !!(window as any).__TAURI_INTERNALS__})}`);
      return null;
    }
    const tasks = created?.type === 'task.list.result' ? created.tasks : [];
    const createdTask = created?.createdTask || tasks.find((item: any) => item.conversationId === activeConversationId && item.title === title);
    const taskId = createdTask?.id || crypto.randomUUID();
    const status = createdTask?.status || 'queued';
    if (options.announce !== false) {
      const time = new Date().toLocaleTimeString('zh-CN', {hour: '2-digit', minute: '2-digit'});
      messages.push({id: crypto.randomUUID(), role: 'user', text, time});
      messages.push({
        id: crypto.randomUUID(),
        role: 'assistant',
        text: ['done', 'failed', 'cancelled'].includes(status)
          ? status === 'done' ? `已完成：${title}` : status === 'failed' ? `执行失败：${title}\n\n${createdTask?.error || '没有可用的错误详情'}` : `已终止：${title}`
          : `好的，我来处理：${title}\n已开始桌面执行，进度会同步在这条对话里。`,
        time,
        taskCard: {taskId, title, status, detail},
        events: [{id: crypto.randomUUID(), stepId: `task:${taskId}:status`, kind: 'task', text: `执行状态 · ${status}`, ts: Date.now(), status: status === 'running' ? 'running' : status === 'done' ? 'done' : status === 'failed' ? 'failed' : 'pending', taskId}],
      });
      await persistSession();
    }
    // Stay in chat by default. Tasks page is only for inspection/management.
    if (options.openTasks) activeView = 'tasks';
    notify('主 Agent 已开始执行');
    if (options.openReview && (window as any).__TAURI_INTERNALS__) {
      const {invoke} = await import('@tauri-apps/api/core');
      try {
        await invoke('show_review', {taskId});
      } catch {
        await invoke('show_review');
      }
    }
    return true;
  }

  async function finishLocalAssistant(id: string, textValue: string, isError = false) {
    const target = messages.find((message) => message.id === id);
    if (target) {
      if (isError) {
        target.error = textValue;
        if (!target.text) target.text = '';
      } else {
        target.text = textValue;
        target.error = undefined;
      }
      target.streaming = false;
    }
    settleMessageEvents(id, isError ? 'failed' : 'done');
    replying = false;
    agentState = 'idle';
    streamingId = '';
    attachedPaths = [];
    await persistSession();
  }

  async function sendMessage() {
    const textValue = draft.trim();
    if (!textValue || replying) return;
    if (editingMessageId) {
      const editIndex = messages.findIndex((message) => message.id === editingMessageId && message.role === 'user');
      if (editIndex >= 0) messages = messages.slice(0, editIndex);
      editingMessageId = '';
    }
    // Desktop work always belongs to the primary agent.
    // Sub-agents are optional workers that can be spawned only when enabled.
    if (allowSubAgents && shouldTransferToExecutor(textValue)) {
      draft = '';
      await createExecutorTask(textValue);
      return;
    }
    if (!activeConversationId) {
      const scope: 'global' | 'project' = activeView === 'project' && activeProject ? 'project' : 'global';
      const conversation = createConversation(scope, scope === 'project' ? activeProject?.id : undefined);
      if (proactivePreview) {
        conversation.title = proactiveTitle(proactivePreview);
        conversation.messages = [...messages];
      }
      conversations = [conversation, ...conversations];
      activeConversationId = conversation.id;
      persistConversations();
    }
    const previewItem = proactivePreview;
    if (previewItem) proactivePreview = null;
    const repliedTo = proactiveInbox.find((item) => item.id === messages.at(-1)?.id && item.state !== 'replied') || previewItem;
    if (repliedTo && await runtime.connect()) {
      void runtime.request({type:'proactive.inbox.mark', id:crypto.randomUUID(), itemId:repliedTo.id, state:'replied'});
      // A user reply becomes the new context; don't let the old autonomous objective wake again.
      if (repliedTo.origin === 'ai' && repliedTo.chainId) void runtime.request({type:'proactive.chain.cancel', id:crypto.randomUUID(), chainId:repliedTo.chainId});
      proactiveInbox = proactiveInbox.map((item) => item.id === repliedTo.id ? {...item, state:'replied'} : item);
    }
    const decision = routeUserMessage(textValue);
    activeSlot = decision.slot;
    const history = messages.map((message) => ({role: message.role, content: message.text}));
    const time = new Date().toLocaleTimeString('zh-CN', {hour: '2-digit', minute: '2-digit'});
    lastUserText = textValue;
    messages.push({id: crypto.randomUUID(), role: 'user', text: textValue, time});
    draft = '';
    replying = true;
    agentState = 'thinking';
    await tick();
    scrollToBottom();
    const id = crypto.randomUUID();
    streamingId = id;
    messages.push({id, role: 'assistant', text: '', time, streaming: true, events: []});
    const isTauri = !!(window as any).__TAURI_INTERNALS__;
    const workspace = activeConversation?.scope === 'project' ? activeProject?.path : undefined;
    const projectName = activeConversation?.scope === 'project' ? activeProject?.name : undefined;
    const attachments = attachedPaths.slice();
    if (workspace) pushMessageEvent(id, 'workspace', `工作区 ${projectName || ''} · ${workspace}`.replace(/\s+·/, ' ·').trim(), {status: 'done'});
    if (attachments.length) pushMessageEvent(id, 'status', `附加 ${attachments.length} 个路径`, {status: 'done'});
    try {
      const connected = await runtime.ensureConnected();
      runtimeConnected = connected;
      if (!connected) {
        const fallback = isDemo
          ? `已记下：「${textValue.slice(0, 80)}${textValue.length > 80 ? '…' : ''}」\n\n演示模式不会调用模型。启动桌面端并配置模型后，可获得完整回复。`
          : formatRuntimeError(new Error('Agent 运行时未连接'), {isDemo, isTauri});
        pushMessageEvent(id, 'status', isDemo ? '演示模式本地记录' : '运行时未连接', {status: isDemo ? 'done' : 'failed'});
        if (isDemo) await finishLocalAssistant(id, fallback, false);
        else await finishLocalAssistant(id, fallback, true);
        return;
      }
      await runtime.chat(
        {
          type: 'chat.send',
          id,
          text: textValue,
          history,
          sessionId: activeConversationId || 'current',
          // Primary agent stays in chat; only spawn executor worker when sub-agents are enabled.
          slot: allowSubAgents && decision.slot === 'executor' ? 'executor' : 'companion',
          allowSubAgents,
          workspace,
          projectName,
          attachments: attachments.length ? attachments : undefined,
        },
        {
          onDelta: (delta) => {
            if (streamingId !== id) return;
            const target = messages.find((message) => message.id === id);
            if (target) {
              target.text += delta;
              // Reassign so Svelte reliably re-renders streaming markdown body.
              messages = messages;
            }
            tick().then(scrollToBottom);
          },
          onEvent: (event) => {
            // Allow late events (e.g. memory) only while this stream is still active.
            if (streamingId !== id) return;
            const kind = event.kind || 'status';
            const status =
              event.status ||
              (kind === 'error' ? 'failed' : kind === 'memory' || kind === 'workspace' || kind === 'status' ? 'done' : 'running');
            pushMessageEvent(id, kind, event.text, {id: event.id, stepId: event.id, status, action: (event as any).action, receipt: (event as any).receipt, ts: event.ts || Date.now()});
          },
          onDone: () => {
            // Even if the user already stopped (streamingId cleared), still finalize the bubble.
            const target = messages.find((message) => message.id === id);
            if (target) {
              target.streaming = false;
              messages = messages;
            }
            settleMessageEvents(id, 'done');
            if (streamingId === id) {
              replying = false;
              agentState = 'idle';
              runtimeConnected = true;
              streamingId = '';
              attachedPaths = [];
            }
            void persistSession();
            setTimeout(() => void refreshMemories(), 1200);
          },
          onError: (error) => {
            const target = messages.find((message) => message.id === id);
            const msg = formatRuntimeError(error, {isDemo, isTauri: true});
            if (target) {
              if (streamingId === id || !target.text?.trim()) {
                target.error = msg;
              }
              target.streaming = false;
              messages = messages;
            }
            settleMessageEvents(id, 'failed');
            if (streamingId === id) {
              replying = false;
              agentState = 'idle';
              runtimeConnected = runtime.connected;
              streamingId = '';
              attachedPaths = [];
            }
            void persistSession();
          },
          onCancelled: () => {
            const target = messages.find((message) => message.id === id);
            if (target) {
              target.streaming = false;
              if (!target.text?.trim() && !target.error) target.text = '（已停止）';
              messages = messages;
            }
            settleMessageEvents(id, 'skipped');
            if (streamingId === id) {
              replying = false;
              agentState = 'idle';
              streamingId = '';
              attachedPaths = [];
            }
            void persistSession();
          },
        },
      );
    } catch (error) {
      await finishLocalAssistant(id, formatRuntimeError(error, {isDemo, isTauri}), true);
      runtimeConnected = runtime.connected;
    }
  }

  function keydown(event: KeyboardEvent) {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      sendMessage();
    }
  }

  async function attachFile(event: Event) {
    const file = (event.currentTarget as HTMLInputElement).files?.[0];
    if (!file) return;
    if (file.size > 64 * 1024) { notify('附件大于 64KB，请通过「文件感知」监视该目录'); return; }
    try {
      const content = await file.text();
      draft = `${draft ? `${draft}\n\n` : ''}[用户主动附上文件：${file.name}]\n${content.slice(0, 60_000)}`;
      notify(`已附加 ${file.name}`);
    } catch { notify('该文件无法以文本读取'); }
    (event.currentTarget as HTMLInputElement).value = '';
  }

  async function windowAction(action: 'minimize' | 'maximize' | 'close') {
    if (!(window as any).__TAURI_INTERNALS__) {
      notify('桌面窗口操作将在 Tauri 中生效');
      return;
    }
    try {
      const {getCurrentWindow} = await import('@tauri-apps/api/window');
      const win = getCurrentWindow();
      if (action === 'maximize') {
        // Custom titlebar: toggleMaximize is the correct API.
        await win.toggleMaximize();
        return;
      }
      if (action === 'minimize') {
        await win.minimize();
        return;
      }
      if (action === 'close') {
        await win.close();
      }
    } catch (error) {
      notify(`窗口操作失败：${error}`);
    }
  }

  async function goToView(id: ViewId) {
    if (replying && id !== activeView) await stopGeneration();
    activeView = id;
    if (id === 'project') {
      if (activeProjectId) void openProject(activeProjectId);
      else if (projects[0]) void openProject(projects[0].id);
      else openNewProjectDialog();
    }
  }

  const filtered = $derived(
    memoryItems.filter(
      (m) => (category === 'all' || m.category === category) && m.text.toLowerCase().includes(query.toLowerCase()),
    ),
  );
</script>

{#if isQuick}
  <QuickWindow />
{:else if isReview}
  <ReviewWindow />
{:else}
  {#if ready && !persona}<Oobe onComplete={savePersona} />{/if}
  {#if ready && redefiningPersona}<Oobe mode={personaEditorMode} initialPersona={personaEditorMode === 'redefine' ? persona : null} onComplete={savePersona} onCancel={() => (redefiningPersona = false)} />{/if}
  <main class="app-shell" inert={ready && !persona} aria-hidden={ready && !persona ? 'true' : undefined}>
    <header class="titlebar" data-tauri-drag-region>
      <div class="brand" data-tauri-drag-region>
        <StatusDot active={agentState !== 'idle'} />
        <strong data-tauri-drag-region>{persona?.name || 'Pattern'}</strong>
        <span data-tauri-drag-region>{nav.find((n) => n.id === activeView)?.label}</span>
      </div>
      <div class="window-actions">
        <button aria-label="最小化" onclick={() => windowAction('minimize')}><Minus size={15} /></button>
        <button aria-label="最大化" onclick={() => windowAction('maximize')}><Maximize2 size={14} /></button>
        <button class="close" aria-label="关闭" onclick={() => windowAction('close')}><X size={15} /></button>
      </div>
    </header>
    <div class="workspace">
      <nav class="rail" aria-label="主导航">
        {#each nav as item}
          <button class:active={activeView === item.id} class:bottom={item.id === 'settings'} aria-label={item.label} title={item.label} onclick={() => goToView(item.id)}>
            <item.icon size={19} />
            <small>{item.label}</small>
          </button>
        {/each}
      </nav>
      {#if showRecents}
        <RecentsSidebar
          {conversations}
          {projects}
          {proactiveInbox}
          {activeConversationId}
          {activeProjectId}
          activeProactiveId={proactivePreview?.id || ''}
          onOpenConversation={openConversation}
          onOpenProject={openProject}
          onNewChat={() => newChat('global')}
          onNewProject={openNewProjectDialog}
          onOpenProactive={openProactiveInboxItem}
          onDismissProactive={dismissProactiveInboxItem}
          onDismissAllProactive={dismissAllProactiveInbox}
          onDeleteConversation={deleteConversation}
          onArchiveConversation={archiveConversation}
        />
      {/if}
      {#if activeView === 'chat'}
        <section class="view">
          <div class="conversation-head">
            <div class="conversation-head-copy">
              <p class="eyebrow">主 Agent · 全局</p>
              <h1>{activeConversation?.title || (proactivePreview ? proactiveTitle(proactivePreview) : homeGreeting)}</h1>
              {#if proactivePreview && !activeConversation}
                <p class="conversation-context">主动消息预览 · 回复后才会创建对话 · 可忽略</p>
              {:else if !activeConversation}
                <p class="conversation-context">{homeContext}</p>
              {/if}
            </div>
            <div class="conversation-head-actions">
              {#if proactivePreview}
                <button class="quiet-button" onclick={() => proactivePreview && dismissProactiveInboxItem(proactivePreview)}><Trash2 size={14} />忽略</button>
              {/if}
              <button class="quiet-button" onclick={() => newChat('global')}><Plus size={14} />新对话</button>
            </div>
          </div>
          <div class="chat-stream" bind:this={stream}>
            <div class="day-divider"><span>{new Date().toLocaleDateString('zh-CN', {month: 'long', day: 'numeric', weekday: 'short'})}</span></div>
            {#if !messages.length}
              <div class="blank-state compact">
                <div class="blank-mark">⌁</div>
                <h3>开始一段全局对话</h3>
                <p>这里是跨项目的主 Agent 上下文，不会绑定某个工作区。</p>
              </div>
            {/if}
            {#each messages as message (message.id)}
              <article class="message" class:user={message.role === 'user'} class:assistant={message.role === 'assistant'} class:proactive={!!message.proactive}>
                {#if message.role === 'assistant'}
                  <div class="message-meta">
                    <StatusDot size="small" active={!!message.streaming} />
                    <strong>{persona?.name || 'Pattern'}</strong>
                    {#if message.proactive}<span class="badge amber">主动 · {message.proactive}</span>{/if}
                    {#if message.streaming && !message.text}
                      <span>正在想</span>
                    {:else}
                      <time>{message.time}</time>
                    {/if}
                  </div>
                {/if}
                <MessageContent {message} onOpenTask={openTaskReview} />
                {#if message.role === 'user'}<time>{message.time}</time>{/if}
                {#if !message.streaming}
                  <div class="message-actions">
                    <button type="button" onclick={() => copyMessage(message)}>复制</button>
                    {#if message.role === 'user'}<button type="button" disabled={replying} onclick={() => editUserMessage(message)}>编辑</button>{/if}
                    {#if message.role === 'assistant' && !message.proactive}<button type="button" disabled={replying} onclick={() => retryAssistant(message.id)}>{message.error ? '重试' : '重新生成'}</button>{/if}
                  </div>
                {/if}
              </article>
            {/each}
          </div>
          <form
            class="composer"
            onsubmit={(e) => {
              e.preventDefault();
              sendMessage();
            }}
          >
            {#if editingMessageId}
              <div class="composer-editing"><span>正在编辑较早的消息，发送后将从这里继续。</span><button type="button" onclick={cancelMessageEdit}>取消</button></div>
            {/if}
            <textarea aria-label="消息" bind:value={draft} onkeydown={keydown} rows="2" placeholder={`和 ${persona?.name || 'Pattern'} 说点什么……`}></textarea>
            <div class="composer-actions">
              <button
                type="button"
                class="text-action subagent-toggle"
                class:active={allowSubAgents}
                aria-pressed={allowSubAgents}
                title={allowSubAgents ? '子 Agent 已启用：复杂任务可派生子代理' : '子 Agent 已关闭：主 Agent 自己调用工具'}
                onclick={() => setAllowSubAgents(!allowSubAgents)}
              >
                {allowSubAgents ? '子 Agent · 开' : '子 Agent · 关'}
              </button>
              <input aria-label="添加文件" bind:this={attachmentInput} class="file-input" type="file" onchange={attachFile} />
              <button type="button" class="icon-action" title="添加文件（最大 64KB）" aria-label="添加文件" onclick={() => attachmentInput?.click()}><Paperclip size={14} /></button>
              <span>{allowSubAgents ? '主 Agent · 可派生子代理' : '主 Agent · 直接工具'}</span>
              {#if replying}<button type="button" class="quiet-button" aria-label="停止生成" onclick={stopGeneration}>停止</button>{/if}
              <button class="send-button" aria-label="发送" disabled={!draft.trim() || replying}><ArrowUp size={18} /></button>
            </div>
          </form>
        </section>
      {:else if activeView === 'project'}
        {#if activeProject}
          <ProjectWorkspace
            project={activeProject}
            {conversations}
            {activeConversationId}
            {messages}
            bind:draft
            {replying}
            personaName={persona?.name || 'Pattern'}
            {activeSlot}
            {allowSubAgents}
            onToggleSubAgents={setAllowSubAgents}
            {fileNodes}
            {fileLoading}
            {fileError}
            {selectedFilePath}
            {filePreview}
            bind:stream
            onSelectConversation={openConversation}
            onNewConversation={() => newChat('project', activeProject.id)}
            onSend={sendMessage}
            onStop={stopGeneration}
            onRetry={retryAssistant}
            onCopy={copyMessage}
            onEdit={editUserMessage}
            editingMessageId={editingMessageId}
            onCancelEdit={cancelMessageEdit}
            onKeydown={keydown}
            onAttach={attachFile}
            onRefreshFiles={() => loadDirectoryTree(activeProject.path)}
            onOpenFile={openProjectFile}
            onAttachPath={attachProjectPath}
            onExpandDir={expandDirectory}
            projectDiff={projectDiff}
            onInspectDiff={inspectProjectDiff}
            onCreateWorktree={createProjectWorktree}
            onOpenTask={openTaskReview}
          />
        {:else}
          <section class="view">
            <div class="blank-state">
              <div class="blank-mark">⌁</div>
              <h3>还没有项目</h3>
              <p>创建一个项目工作区，即可在左侧对话、中间聊天、右侧浏览文件夹。</p>
              <button class="primary-button" type="button" onclick={openNewProjectDialog}><Plus size={14} />新建项目</button>
            </div>
          </section>
        {/if}
      {:else if activeView === 'memory'}
        <section class="view">
          <PageHeader eyebrow="长期记忆" title="记得的事" subtitle="检索命中会提升访问计数；对话结束后自动提取候选记忆。">
            <button class="quiet-button" onclick={async () => { if(await runtime.connect()){ await runtime.request({type:'memory.consolidate', id:crypto.randomUUID()}); await refreshMemories(); notify('已执行记忆固化'); } }}>固化</button>
            <button class="primary-button" onclick={() => (editingMemory = true)}><Plus size={14} />添加记忆</button>
          </PageHeader>
          <div class="toolbar">
            <label class="search-box"><Search size={15} /><input bind:value={query} placeholder="搜索记忆" /></label>
            <div class="filters">
              {#each ['all', '事实', '偏好', '事件', '反馈'] as item}
                <button class:active={category === item} onclick={() => (category = item as typeof category)}>{item === 'all' ? '全部' : item}</button>
              {/each}
            </div>
          </div>
          <div class="consolidation">
            <span>✦</span>
            <div>
              <strong>{lastConsolidateAt ? '最近固化' : '尚未固化'}</strong>
              <p>
                {#if lastConsolidateAt}
                  {new Date(lastConsolidateAt * 1000).toLocaleString('zh-CN')} 完成衰减与容量整理。当前有效记忆 {memoryCount} 条。
                {:else}
                  系统会在凌晨自动整理；你也可以继续对话让她记住新事实。当前有效记忆 {memoryCount} 条。
                {/if}
              </p>
            </div>
            <time>{lastConsolidateAt ? new Date(lastConsolidateAt * 1000).toLocaleTimeString('zh-CN', {hour: '2-digit', minute: '2-digit'}) : '--'}</time>
          </div>
          {#if memoryProposals.length}
            <div class="memory-proposals" aria-label="待确认记忆">
              <div class="memory-proposals-head">
                <strong>待确认记忆</strong>
                <span class="badge amber">{memoryProposals.length}</span>
              </div>
              {#each memoryProposals as proposal}
                <article class="memory-card proposal">
                  <div>
                    <span class="badge blue">{categoryFromWire[proposal.category] || proposal.category}</span>
                    <span class="badge dim">{proposal.reason || '提取'}</span>
                  </div>
                  <p>{proposal.text}</p>
                  <footer>
                    <button class="primary-button" type="button" onclick={() => acceptMemoryProposal(proposal.id)}>确认记住</button>
                    <button class="quiet-button" type="button" onclick={() => rejectMemoryProposal(proposal.id)}>忽略</button>
                  </footer>
                </article>
              {/each}
            </div>
          {/if}
          <div class="memory-grid">
            {#each filtered as memory}
              <article class="memory-card" class:expired={memory.expired}>
                <div>
                  <span class="badge" class:amber={memory.category === '事件'} class:blue={memory.category === '事实'} class:green={memory.category === '反馈'} class:dim={memory.category === '偏好'}>
                    {memory.category}{memory.expired ? ' · 已取代' : ''}
                  </span>
                  <span class="importance">{'● '.repeat(importanceStars(memory.importance))}{'○ '.repeat(3 - importanceStars(memory.importance))}</span>
                </div>
                <p>{memory.text}</p>
                <footer>
                  <span>{memory.meta}</span>
                  {#if memory.sourceConv}<button aria-label="查看来源对话" title="查看来源对话" class="text-action" onclick={() => openMemorySource(memory)}>来源</button>{/if}
                  <button aria-label="编辑记忆" title="编辑记忆" class="text-action" onclick={() => { editingMemoryItem = memory; editingMemory = true; }}>编辑</button>
                  <button aria-label="删除记忆" title="删除记忆" onclick={() => deleteMemory(memory.id)}><Trash2 size={13} /></button>
                </footer>
              </article>
            {:else}
              <div class="empty-state">没有找到匹配的记忆</div>
            {/each}
          </div>
        </section>
      {:else if activeView === 'conversations'}
        <ConversationsView
          {conversations}
          activeId={activeConversationId}
          onOpen={openConversation}
          onCreate={() => newChat('global')}
          onArchive={archiveConversation}
          onDelete={deleteConversation}
        />
      {:else if activeView === 'tasks'}
        <TasksView {notify} initialDraft={taskDraft} onDraftConsumed={() => taskDraft = null} />
      {:else if activeView === 'proactive'}
        <ProactiveView {notify} />
      {:else if activeView === 'workflows'}
        <WorkflowsView {notify} defaultWorkspace={activeProject?.path || projects[0]?.path || ''} />
      {:else if activeView === 'mcp'}
        <McpView {notify} />
      {:else if activeView === 'channels'}
        <ChannelsView {notify} />
      {:else}
        <SettingsView {persona} {theme} onTheme={setTheme} onRedefine={(mode = 'redefine') => { personaEditorMode = mode; redefiningPersona = true; }} onPersonaChange={(value) => { persona = value; localStorage.setItem('pattern-persona', JSON.stringify(value)); notify(`已切换为 ${value.name}`); }} />
      {/if}
    </div>
    <footer class="statusbar">
      <span title={runtimeConnected ? 'Node Sidecar 已连接' : isDemo ? '当前是浏览器演示预览，不会启动 Node Sidecar' : '桌面端未能连接 Node Sidecar'}>
        <StatusDot size="tiny" active={runtimeConnected} />
        {runtimeConnected ? '运行时已连接' : isDemo ? '演示模式 · 不连接运行时' : '运行时未连接'}
      </span>
      <span>今日主动 <code>{proactiveCount}</code> 次</span>
      {#if foregroundTitle}<span title={foregroundTitle}>前台 <code>{foregroundTitle.slice(0, 24)}{foregroundTitle.length > 24 ? '…' : ''}</code>{foregroundBusy ? ' · 忙' : ''}</span>{/if}
      <span>记忆 <code>{memoryCount}</code> 条</span>
      <i></i>
            <code>{activeConversation?.scope || 'global'} · primary · {agentState}</code>
    </footer>
  </main>
  {#if toast}<div class="toast show" role="status" aria-live="polite">{toast}</div>{/if}
  {#if editingMemory}<MemoryEditor initial={editingMemoryItem || undefined} onClose={() => { editingMemory = false; editingMemoryItem = null; }} onSave={async (item) => { if (editingMemoryItem) await updateMemory(editingMemoryItem.id, item); else await addMemory(item); editingMemoryItem = null; }} />{/if}
  {#if showProjectDialog}
    <div class="modal-backdrop" role="presentation">
      <form
        class="memory-editor project-editor"
        aria-label="新建项目"
        onsubmit={(event) => {
          event.preventDefault();
          createProject();
        }}
      >
        <header>
          <div>
            <p class="eyebrow">工作区</p>
            <h2>新建项目</h2>
          </div>
          <button type="button" aria-label="关闭" onclick={() => (showProjectDialog = false)}><X size={15} /></button>
        </header>
        <label>项目名称
          <input aria-label="项目名称" bind:value={projectNameDraft} placeholder="Pattern" required />
        </label>
        <label>本地路径
          <div class="path-row">
            <input aria-label="项目路径" bind:value={projectPathDraft} placeholder="E:\Desktop\项目\CrossPlatform\Pattern" required />
            <button type="button" class="quiet-button" onclick={pickProjectFolder}>浏览文件夹</button>
          </div>
        </label>
        <p class="field-help">项目对话会绑定该工作区；右侧文件树读取此路径。可浏览选择或手动粘贴绝对路径。</p>
        <footer>
          <button type="button" class="quiet-button" onclick={() => (showProjectDialog = false)}>取消</button>
          <button type="submit" class="primary-button">创建项目</button>
        </footer>
      </form>
    </div>
  {/if}
{/if}
