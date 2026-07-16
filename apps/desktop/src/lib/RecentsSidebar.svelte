<script lang="ts">
  import {Bell, ChevronDown, ChevronRight, FolderGit2, MessageSquarePlus, Plus, Trash2} from 'lucide-svelte';
  import type {Conversation, Project} from './types';
  import ContextMenu, {type ContextMenuItem} from './ContextMenu.svelte';

  type ProactiveItem = {
    id: string;
    body: string;
    type?: string;
    reason?: string;
    origin?: 'ai' | 'system';
    state?: string;
    chainId?: string;
  };

  let {
    conversations,
    projects,
    proactiveInbox,
    activeConversationId,
    activeProjectId,
    activeProactiveId = '',
    onOpenConversation,
    onOpenProject,
    onNewChat,
    onNewProject,
    onOpenProactive,
    onDismissProactive,
    onDismissAllProactive,
    onDeleteConversation,
    onArchiveConversation,
  }: {
    conversations: Conversation[];
    projects: Project[];
    proactiveInbox: ProactiveItem[];
    activeConversationId: string;
    activeProjectId: string;
    activeProactiveId?: string;
    onOpenConversation: (id: string) => void;
    onOpenProject: (id: string) => void;
    onNewChat: () => void;
    onNewProject: () => void;
    onOpenProactive: (item: ProactiveItem) => void;
    onDismissProactive: (item: ProactiveItem) => void;
    onDismissAllProactive: () => void;
    onDeleteConversation: (id: string) => void;
    onArchiveConversation: (id: string) => void;
  } = $props();

  let menu = $state<{
    open: boolean;
    x: number;
    y: number;
    items: ContextMenuItem[];
    kind: 'proactive' | 'conversation' | null;
    targetId: string;
  }>({open: false, x: 0, y: 0, items: [], kind: null, targetId: ''});

  let inboxExpanded = $state(
    typeof localStorage === 'undefined'
      ? true
      : localStorage.getItem('pattern-proactive-inbox-expanded') !== '0',
  );

  const recentChats = $derived(
    [...conversations]
      .filter((item) => !item.archived && item.scope === 'global')
      .sort((a, b) => b.updatedAt - a.updatedAt)
      .slice(0, 12),
  );
  const recentProjects = $derived(
    [...projects]
      .sort((a, b) => b.updatedAt - a.updatedAt)
      .slice(0, 12),
  );
  const inbox = $derived(proactiveInbox.filter((item) => item.state !== 'dismissed').slice(0, 20));
  const inboxCount = $derived(inbox.length);

  const preview = (item: Conversation) =>
    item.messages.at(-1)?.text?.replace(/\s+/g, ' ').slice(0, 48) || '尚未开始';

  function setInboxExpanded(next: boolean) {
    inboxExpanded = next;
    try {
      localStorage.setItem('pattern-proactive-inbox-expanded', next ? '1' : '0');
    } catch {
      /* ignore quota / private mode */
    }
  }

  function toggleInbox() {
    setInboxExpanded(!inboxExpanded);
  }

  function closeMenu() {
    menu = {...menu, open: false, kind: null, targetId: '', items: []};
  }

  function openProactiveMenu(event: MouseEvent, item: ProactiveItem) {
    event.preventDefault();
    event.stopPropagation();
    menu = {
      open: true,
      x: event.clientX,
      y: event.clientY,
      kind: 'proactive',
      targetId: item.id,
      items: [
        {id: 'open', label: '打开'},
        {id: 'dismiss', label: '忽略这条', danger: true},
      ],
    };
  }

  function openConversationMenu(event: MouseEvent, conversation: Conversation) {
    event.preventDefault();
    event.stopPropagation();
    menu = {
      open: true,
      x: event.clientX,
      y: event.clientY,
      kind: 'conversation',
      targetId: conversation.id,
      items: [
        {id: 'open', label: '打开'},
        {id: 'archive', label: conversation.archived ? '取消归档' : '归档'},
        {id: 'delete', label: '删除对话', danger: true, disabled: conversations.length <= 1},
      ],
    };
  }

  function onSelect(id: string) {
    if (menu.kind === 'proactive') {
      const item = proactiveInbox.find((entry) => entry.id === menu.targetId);
      if (!item) return;
      if (id === 'open') onOpenProactive(item);
      if (id === 'dismiss') onDismissProactive(item);
      return;
    }
    if (menu.kind === 'conversation') {
      if (id === 'open') onOpenConversation(menu.targetId);
      if (id === 'archive') onArchiveConversation(menu.targetId);
      if (id === 'delete') onDeleteConversation(menu.targetId);
    }
  }
</script>

<aside class="recents-sidebar" aria-label="最近内容">
  <div class="recents-section inbox-section" class:collapsed={!inboxExpanded}>
    <div class="recents-heading inbox-heading">
      <button
        type="button"
        class="inbox-toggle"
        aria-expanded={inboxExpanded}
        aria-controls="proactive-inbox-list"
        title={inboxExpanded ? '折叠主动收件箱' : '展开主动收件箱'}
        onclick={toggleInbox}
      >
        {#if inboxExpanded}
          <ChevronDown size={14} />
        {:else}
          <ChevronRight size={14} />
        {/if}
        <h2>主动收件箱</h2>
        {#if inboxCount}
          <span class="inbox-count">{inboxCount}</span>
        {/if}
      </button>
      <div class="inbox-actions">
        {#if inboxCount}
          <button
            type="button"
            class="icon-action danger-quiet"
            aria-label="全部忽略"
            title="全部忽略"
            onclick={(event) => {
              event.stopPropagation();
              onDismissAllProactive();
            }}
          >
            <Trash2 size={13} />
          </button>
        {/if}
        <Bell size={14} />
      </div>
    </div>
    {#if inboxExpanded}
      <div class="recents-list" id="proactive-inbox-list">
        {#each inbox as item (item.id)}
          <button
            type="button"
            class="recents-item"
            class:active={activeProactiveId === item.id}
            aria-current={activeProactiveId === item.id ? 'page' : undefined}
            onclick={() => onOpenProactive(item)}
            oncontextmenu={(event) => openProactiveMenu(event, item)}
          >
            <strong>{item.origin === 'system' ? '系统提醒' : 'AI 主动消息'}</strong>
            <span>{item.body.replace(/\s+/g, ' ').slice(0, 48)}</span>
          </button>
        {:else}
          <p class="recents-empty">没有待处理的主动消息</p>
        {/each}
      </div>
    {:else if inboxCount}
      <p class="recents-empty compact">已折叠 · {inboxCount} 条待处理</p>
    {/if}
  </div>

  <div class="recents-section">
    <div class="recents-heading">
      <h2>最近聊天</h2>
      <button class="icon-action" type="button" aria-label="新建全局对话" title="新建全局对话" onclick={onNewChat}>
        <MessageSquarePlus size={14} />
      </button>
    </div>
    <div class="recents-list">
      {#each recentChats as conversation (conversation.id)}
        <button
          type="button"
          class="recents-item"
          class:active={conversation.id === activeConversationId}
          aria-current={conversation.id === activeConversationId ? 'page' : undefined}
          onclick={() => onOpenConversation(conversation.id)}
          oncontextmenu={(event) => openConversationMenu(event, conversation)}
        >
          <strong>{conversation.title || '新对话'}</strong>
          <span>{preview(conversation)}</span>
        </button>
      {:else}
        <p class="recents-empty">还没有全局对话</p>
      {/each}
    </div>
  </div>

  <div class="recents-section">
    <div class="recents-heading">
      <h2>最近项目</h2>
      <button class="icon-action" type="button" aria-label="新建项目" title="新建项目" onclick={onNewProject}>
        <Plus size={14} />
      </button>
    </div>
    <div class="recents-list">
      {#each recentProjects as project (project.id)}
        <button
          type="button"
          class="recents-item project"
          class:active={project.id === activeProjectId}
          aria-current={project.id === activeProjectId ? 'page' : undefined}
          onclick={() => onOpenProject(project.id)}
        >
          <strong><FolderGit2 size={13} />{project.name}</strong>
          <span>{project.path}</span>
        </button>
      {:else}
        <p class="recents-empty">还没有项目，点 + 添加工作区</p>
      {/each}
    </div>
  </div>
</aside>

<ContextMenu
  open={menu.open}
  x={menu.x}
  y={menu.y}
  items={menu.items}
  onSelect={onSelect}
  onClose={closeMenu}
/>
