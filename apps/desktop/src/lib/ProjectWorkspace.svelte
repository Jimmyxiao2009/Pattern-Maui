<script lang="ts">
  import {ArrowUp, GitBranch, MessageSquarePlus, Paperclip, Plus, Square} from 'lucide-svelte';
  import StatusDot from './StatusDot.svelte';
  import FileTree from './FileTree.svelte';
  import MessageContent from './MessageContent.svelte';
  import SessionAgentDocks from './SessionAgentDocks.svelte';
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
    allowSubAgents = true,
    onToggleSubAgents,
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
    onCopy,
    onEdit,
    editingMessageId = '',
    onCancelEdit,
    onKeydown,
    onAttach,
    composerDragOver = false,
    onDragOver,
    onDragLeave,
    onDrop,
    attachedPaths = [],
    onRemoveAttachment,
    onRefreshFiles,
    onOpenFile,
    onAttachPath,
    onExpandDir,
    onInspectDiff,
    onCreateWorktree,
    onOpenTask,
    sessionPlanCollapsed = false,
    sessionGoalCollapsed = false,
    sessionLoopCollapsed = false,
    sessionRemindCollapsed = false,
    onToggleSessionPlan,
    onToggleSessionGoal,
    onToggleSessionLoop,
    onToggleSessionRemind,
    onOpenGoals,
    onOpenTasks,
    onOpenProactive,
  }: {
    project: Project;
    conversations: Conversation[];
    activeConversationId: string;
    messages: ChatMessage[];
    draft: string;
    replying: boolean;
    personaName: string;
    activeSlot: 'companion' | 'executor';
    allowSubAgents?: boolean;
    onToggleSubAgents?: (value: boolean) => void;
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
    onCopy: (message: ChatMessage) => void | Promise<void>;
    onEdit: (message: ChatMessage) => void;
    editingMessageId?: string;
    onCancelEdit: () => void;
    onKeydown: (event: KeyboardEvent) => void;
    onAttach: (event: Event) => void;
    composerDragOver?: boolean;
    sessionPlanCollapsed?: boolean;
    sessionGoalCollapsed?: boolean;
    sessionLoopCollapsed?: boolean;
    sessionRemindCollapsed?: boolean;
    onToggleSessionPlan?: () => void;
    onToggleSessionGoal?: () => void;
    onToggleSessionLoop?: () => void;
    onToggleSessionRemind?: () => void;
    onOpenGoals?: () => void;
    onOpenTasks?: () => void;
    onOpenProactive?: () => void;
    onDragOver?: (event: DragEvent) => void;
    onDragLeave?: (event: DragEvent) => void;
    onDrop?: (event: DragEvent) => void;
    attachedPaths?: string[];
    onRemoveAttachment?: (path: string) => void;
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
        <p class="eyebrow">主 Agent · 项目</p>
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
    <SessionAgentDocks
      conversationId={activeConversationId}
      planCollapsed={sessionPlanCollapsed}
      goalCollapsed={sessionGoalCollapsed}
      loopCollapsed={sessionLoopCollapsed}
      remindCollapsed={sessionRemindCollapsed}
      onTogglePlan={() => onToggleSessionPlan?.()}
      onToggleGoal={() => onToggleSessionGoal?.()}
      onToggleLoop={() => onToggleSessionLoop?.()}
      onToggleRemind={() => onToggleSessionRemind?.()}
      onOpenGoals={() => onOpenGoals?.()}
      onOpenTasks={() => onOpenTasks?.()}
      onOpenProactive={() => onOpenProactive?.()}
    />
    <div class="chat-stream" bind:this={stream}>
      <div class="day-divider"><span>{project.name}</span></div>
      {#each messages as message (message.id)}
        <article class="message" class:user={message.role === 'user'} class:assistant={message.role === 'assistant'} class:proactive={!!message.proactive}>
          {#if message.role === 'assistant'}
            <div class="message-meta">
              <StatusDot size="small" active={!!message.streaming} />
              <strong>{personaName}</strong>
              {#if message.proactive}<span class="badge amber">主动 · {message.proactive}</span>{/if}
              {#if message.streaming && !message.text}
                <span>正在想</span>
              {:else}
                <time>{message.time}</time>
              {/if}
            </div>
          {/if}
          <MessageContent {message} onOpenTask={(id) => onOpenTask?.(id)} />
          {#if message.role === 'user'}<time>{message.time}</time>{/if}
          {#if !message.streaming}
            <div class="message-actions">
              <button type="button" onclick={() => onCopy(message)}>复制</button>
              {#if message.role === 'user'}<button type="button" disabled={replying} onclick={() => onEdit(message)}>编辑</button>{/if}
              {#if message.role === 'assistant' && !message.proactive}<button type="button" disabled={replying} onclick={() => onRetry(message.id)}>{message.error ? '重试' : '重新生成'}</button>{/if}
            </div>
          {/if}
        </article>
      {/each}
    </div>
    <form
      class="composer"
      class:drag-over={composerDragOver}
      onsubmit={(e) => {
        e.preventDefault();
        onSend();
      }}
      ondragenter={(e) => onDragOver?.(e)}
      ondragover={(e) => onDragOver?.(e)}
      ondragleave={(e) => onDragLeave?.(e)}
      ondrop={(e) => onDrop?.(e)}
    >
      {#if composerDragOver}
        <div class="composer-drop-hint" aria-hidden="true">松开以附加文件到对话</div>
      {/if}
      {#if editingMessageId}
        <div class="composer-editing"><span>正在编辑较早的消息，发送后将从这里继续。</span><button type="button" onclick={onCancelEdit}>取消</button></div>
      {/if}
      {#if attachedPaths?.length}
        <div class="composer-attachments" aria-label="已附加路径">
          {#each attachedPaths as path (path)}
            <span class="composer-chip" title={path}>
              {path.split(/[/\\]/).pop() || path}
              <button type="button" aria-label={`移除 ${path}`} onclick={() => onRemoveAttachment?.(path)}>×</button>
            </span>
          {/each}
        </div>
      {/if}
      <textarea aria-label="项目消息" bind:value={draft} onkeydown={onKeydown} rows="2" placeholder={`在 ${project.name} 中和 ${personaName} 讨论……（可拖入文件）`}></textarea>
      <div class="composer-actions">
        <button
          type="button"
          class="text-action subagent-toggle"
          class:active={allowSubAgents}
          aria-pressed={allowSubAgents}
          title={allowSubAgents ? '子 Agent 已启用：复杂任务可派生子代理' : '子 Agent 已关闭：主 Agent 自己调用工具'}
          onclick={() => onToggleSubAgents?.(!allowSubAgents)}
        >
          {allowSubAgents ? '子 Agent · 开' : '子 Agent · 关'}
        </button>
        <input aria-label="添加文件" bind:this={attachmentInput} class="file-input" type="file" onchange={onAttach} />
        <button type="button" class="icon-action" title="添加文件（最大 64KB）" aria-label="添加文件" onclick={() => attachmentInput?.click()}><Paperclip size={14} /></button>
        <span>{allowSubAgents ? '项目 · 可派生子代理' : '项目 · 直接工具'}</span>
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
