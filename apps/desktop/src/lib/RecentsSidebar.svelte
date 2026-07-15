<script lang="ts">
  import {Bell, FolderGit2, MessageSquarePlus, Plus} from 'lucide-svelte';
  import type {Conversation, Project} from './types';

  let {
    conversations,
    projects,
    proactiveInbox,
    activeConversationId,
    activeProjectId,
    onOpenConversation,
    onOpenProject,
    onNewChat,
    onNewProject,
    onOpenProactive,
  }: {
    conversations: Conversation[];
    projects: Project[];
    proactiveInbox: Array<{id: string; body: string; type?: string; reason?: string; origin?: 'ai' | 'system'; state?: string; chainId?: string}>;
    activeConversationId: string;
    activeProjectId: string;
    onOpenConversation: (id: string) => void;
    onOpenProject: (id: string) => void;
    onNewChat: () => void;
    onNewProject: () => void;
    onOpenProactive: (item: {id: string; body: string; type?: string; reason?: string; origin?: 'ai' | 'system'; state?: string; chainId?: string}) => void;
  } = $props();

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

  const preview = (item: Conversation) =>
    item.messages.at(-1)?.text?.replace(/\s+/g, ' ').slice(0, 48) || '尚未开始';
</script>

<aside class="recents-sidebar" aria-label="最近内容">
  <div class="recents-section">
    <div class="recents-heading"><h2>主动收件箱</h2><Bell size={14} /></div>
    <div class="recents-list">
      {#each proactiveInbox.filter((item) => item.state !== 'dismissed').slice(0, 8) as item (item.id)}
        <button type="button" class="recents-item" onclick={() => onOpenProactive(item)}>
          <strong>{item.origin === 'system' ? '系统提醒' : 'AI 主动消息'}</strong>
          <span>{item.body.replace(/\s+/g, ' ').slice(0, 48)}</span>
        </button>
      {:else}
        <p class="recents-empty">没有待处理的主动消息</p>
      {/each}
    </div>
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
