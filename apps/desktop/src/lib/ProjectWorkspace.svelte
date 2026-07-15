<script lang="ts">
  import {ArrowUp, GitBranch, MessageSquarePlus, Paperclip, Plus, Square} from 'lucide-svelte';
  import StatusDot from './StatusDot.svelte';
  import FileTree from './FileTree.svelte';
  import MessageContent from './MessageContent.svelte';
  import type {ChatMessage, Conversation, FileNode, Project} from './types';

  let {
    project,
    conversations,
    activeConversationId,
    messages,
    draft = $bindable(''),
    replying,
    personaName,
    activeSlot,
    fileNodes,
    fileLoading,
    fileError,
    selectedFilePath = '',
    filePreview = '',
    projectDiff = null,
    stream = $bindable(),
    onSelectConversation,
    onNewConversation,
    onSend,
    onStop,
    onRetry,
    onKeydown,
    onTransfer,
    onAttach,
    onRefreshFiles,
    onOpenFile,
    onAttachPath,
    onExpandDir,
    onInspectDiff,
    onCreateWorktree,
    onOpenTask,
  }: {
    project: Project;
    conversations: Conversation[];
    activeConversationId: string;
    messages: ChatMessage[];
    draft: string;
    replying: boolean;
    personaName: string;
    activeSlot: 'companion' | 'executor';
    fileNodes: FileNode[];
    fileLoading: boolean;
    fileError: string;
    selectedFilePath?: string;
    filePreview?: string;
    projectDiff?: {status: string; diff: string} | null;
    stream?: HTMLDivElement;
    onSelectConversation: (id: string) => void;
    onNewConversation: () => void;
    onSend: () => void;
    onStop: () => void;
    onRetry: (messageId: string) => void;
    onKeydown: (event: KeyboardEvent) => void;
    onTransfer: () => void;
    onAttach: (event: Event) => void;
    onRefreshFiles: () => void;
    onOpenFile: (node: FileNode) => void;
    onAttachPath: (node: FileNode) => void;
    onExpandDir: (node: FileNode) => void | Promise<void>;
    onInspectDiff: () => void | Promise<void>;
    onCreateWorktree: () => void | Promise<void>;
    onOpenTask?: (taskId: string) => void;
  } = $props();

  let attachmentInput = $state<HTMLInputElement>();

  const projectChats = $derived(
    [...conversations]
      .filter((item) => !item.archived && item.scope === 'project' && item.projectId === project.id)
      .sort((a, b) => b.updatedAt - a.updatedAt),
  );

  const preview = (item: Conversation) =>
    item.messages.at(-1)?.text?.replace(/\s+/g, ' ').slice(0, 40) || '尚未开始';
</script>

<section class="project-workspace" aria-label={`项目 ${project.name}`}>
  <aside class="project-sidebar" aria-label="项目对话">
    <div class="project-sidebar-head">
      <div>
        <p class="eyebrow">项目</p>
        <h1>{project.name}</h1>
        <small title={project.path}>{project.path}</small>
      </div>
      <button class="quiet-button" type="button" onclick={onNewConversation}>
        <MessageSquarePlus size={14} />新对话
      </button>
    </div>
    <div class="project-chat-list">
      {#each projectChats as conversation (conversation.id)}
        <button
          type="button"
          class="recents-item"
          class:active={conversation.id === activeConversationId}
          aria-current={conversation.id === activeConversationId ? 'page' : undefined}
          onclick={() => onSelectConversation(conversation.id)}
        >
          <strong>{conversation.title || '新对话'}</strong>
          <span>{preview(conversation)}</span>
        </button>
      {:else}
        <div class="blank-state compact">
          <div class="blank-mark">⌁</div>
          <h3>项目内还没有对话</h3>
          <p>在项目上下文中开始第一轮讨论。</p>
          <button class="primary-button" type="button" onclick={onNewConversation}><Plus size={14} />新建项目对话</button>
        </div>
      {/each}
    </div>
  </aside>

  <section class="project-chat view" aria-label="项目对话内容">
    <div class="conversation-head">
      <div>
        <p class="eyebrow">{activeSlot === 'executor' ? '执行槽 · 项目' : '陪伴槽 · 项目'}</p>
        <h1>{projectChats.find((item) => item.id === activeConversationId)?.title || '项目对话'}</h1>
        <p class="context-chip">已绑定工作区 · {project.path}</p>
      </div>
      <div class="project-head-actions">
        <button type="button" class="quiet-button" onclick={onInspectDiff}>查看 Diff</button>
        <button type="button" class="quiet-button" onclick={onCreateWorktree}><GitBranch size={14} />Worktree</button>
      </div>
    </div>
    {#if projectDiff}
      <div class="project-diff-panel" aria-label="项目 Diff">
        <strong>工作区状态</strong>
        <pre>{projectDiff.status || '无未提交文件'}</pre>
        <strong>变更摘要</strong>
        <pre>{projectDiff.diff || '无 Diff'}</pre>
      </div>
    {/if}
    <div class="chat-stream" bind:this={stream}>
      <div class="day-divider"><span>{project.name}</span></div>
      {#each messages as message (message.id)}
        <article class="message" class:user={message.role === 'user'} class:assistant={message.role === 'assistant'} class:proactive={!!message.proactive}>
          {#if message.role === 'assistant'}
            <div class="message-meta">
              <StatusDot size="small" />
              <strong>{personaName}</strong>
              {#if message.proactive}<span class="badge amber">主动 · {message.proactive}</span>{/if}
              <time>{message.time}</time>
            </div>
          {/if}
          <MessageContent {message} onOpenTask={(id) => onOpenTask?.(id)} />
          {#if message.role === 'user'}<time>{message.time}</time>{/if}
          {#if message.error}
            <button type="button" class="text-action" onclick={() => onRetry(message.id)}>重试</button>
          {/if}
        </article>
      {/each}
      {#if replying}
        <article class="message assistant">
          <div class="message-meta">
            <StatusDot size="small" active={true} />
            <strong>{personaName}</strong>
            <span>正在想</span>
          </div>
          <div class="typing"><i></i><i></i><i></i></div>
        </article>
      {/if}
    </div>
    <form
      class="composer"
      onsubmit={(e) => {
        e.preventDefault();
        onSend();
      }}
    >
      <textarea aria-label="项目消息" bind:value={draft} onkeydown={onKeydown} rows="2" placeholder={`在 ${project.name} 中和 ${personaName} 讨论……`}></textarea>
      <div class="composer-actions">
        <button type="button" class="text-action" onclick={onTransfer}>⇧ 转交执行</button>
        <input aria-label="添加文件" bind:this={attachmentInput} class="file-input" type="file" onchange={onAttach} />
        <button type="button" class="icon-action" title="添加文件（最大 64KB）" aria-label="添加文件" onclick={() => attachmentInput?.click()}><Paperclip size={14} /></button>
        <span>项目上下文已注入</span>
        {#if replying}
          <button type="button" class="quiet-button" aria-label="停止生成" onclick={onStop}><Square size={14} />停止</button>
        {/if}
        <button class="send-button" aria-label="发送" disabled={!draft.trim() || replying}><ArrowUp size={18} /></button>
      </div>
    </form>
    {#if filePreview}
      <div class="file-preview-panel" aria-label="文件预览">
        <header>
          <strong title={selectedFilePath}>{selectedFilePath}</strong>
        </header>
        <pre>{filePreview}</pre>
      </div>
    {/if}
  </section>

  <FileTree
    rootPath={project.path}
    nodes={fileNodes}
    loading={fileLoading}
    error={fileError}
    selectedPath={selectedFilePath}
    onRefresh={onRefreshFiles}
    onOpenFile={onOpenFile}
    onAttachFile={onAttachPath}
    onExpandDir={onExpandDir}
  />
</section>
