<script lang="ts">
  import {
    AlertTriangle,
    CheckCircle2,
    ChevronDown,
    ChevronRight,
    CircleDot,
    FolderGit2,
    Loader2,
    MemoryStick,
    Sparkles,
    Terminal,
    Wrench,
    XCircle,
  } from 'lucide-svelte';
  import type {ChatMessageEvent} from './types';

  let {
    events = [],
  }: {
    events?: ChatMessageEvent[];
  } = $props();

  let open = $state(true);
  let expanded = $state<Record<string, boolean>>({});

  const items = $derived(
    [...(events || [])].sort((a, b) => (a.ts || 0) - (b.ts || 0)),
  );

  const summary = $derived.by(() => {
    const total = items.length;
    const running = items.filter((item) => item.status === 'running' || item.status === 'pending').length;
    const failed = items.filter((item) => item.status === 'failed' || item.kind === 'error').length;
    const done = items.filter((item) => item.status === 'done').length;
    if (!total) return '无执行步骤';
    if (running) return `${total} 步 · ${running} 进行中`;
    if (failed) return `${total} 步 · ${failed} 失败`;
    if (done) return `${total} 步 · 已完成`;
    return `${total} 个事件`;
  });

  function kindLabel(kind: string) {
    const map: Record<string, string> = {
      status: '状态',
      workspace: '工作区',
      tool: '工具',
      task: '任务',
      mcp: 'MCP',
      memory: '记忆',
      error: '错误',
      agent: 'Agent',
    };
    return map[kind] || kind;
  }

  function statusLabel(status?: string) {
    const map: Record<string, string> = {
      pending: '等待',
      running: '进行中',
      done: '完成',
      failed: '失败',
      skipped: '跳过',
      awaiting_approval: '待审批',
    };
    return status ? map[status] || status : '';
  }

  function iconFor(event: ChatMessageEvent) {
    if (event.status === 'running' || event.status === 'pending') return Loader2;
    if (event.status === 'failed' || event.kind === 'error') return XCircle;
    if (event.status === 'awaiting_approval') return AlertTriangle;
    if (event.status === 'done') return CheckCircle2;
    if (event.kind === 'workspace') return FolderGit2;
    if (event.kind === 'mcp' || event.kind === 'tool') return Wrench;
    if (event.kind === 'memory') return MemoryStick;
    if (event.kind === 'agent') return Sparkles;
    if (event.kind === 'task') return Terminal;
    return CircleDot;
  }

  function timeText(ts?: number) {
    if (!ts) return '';
    return new Date(ts).toLocaleTimeString('zh-CN', {hour: '2-digit', minute: '2-digit', second: '2-digit'});
  }

  function toggleReceipt(id: string) {
    expanded = {...expanded, [id]: !expanded[id]};
  }
</script>

{#if items.length}
  <div class="exec-timeline" aria-label="执行时间线">
    <button type="button" class="exec-timeline-toggle" aria-expanded={open} onclick={() => (open = !open)}>
      {#if open}<ChevronDown size={14} />{:else}<ChevronRight size={14} />{/if}
      <strong>执行时间线</strong>
      <span class="badge dim">{summary}</span>
    </button>
    {#if open}
      <ol class="exec-timeline-list">
        {#each items as event (event.id)}
          {@const Icon = iconFor(event)}
          <li class="exec-timeline-item" data-kind={event.kind} data-status={event.status || ''}>
            <div class="exec-timeline-rail" aria-hidden="true">
              <span class="exec-timeline-dot" class:spin={event.status === 'running'}>
                <Icon size={12} />
              </span>
            </div>
            <div class="exec-timeline-body">
              <div class="exec-timeline-meta">
                <span class="badge" class:amber={event.status === 'running' || event.status === 'awaiting_approval' || event.kind === 'memory'} class:green={event.status === 'done'} class:dim={!event.status || event.status === 'pending'} class:danger={event.status === 'failed' || event.kind === 'error'}>
                  {kindLabel(event.kind)}
                </span>
                {#if event.status}
                  <span class="exec-status">{statusLabel(event.status)}</span>
                {/if}
                {#if typeof event.tier === 'number'}
                  <span class="badge dim">T{event.tier}</span>
                {/if}
                {#if event.ts}
                  <time>{timeText(event.ts)}</time>
                {/if}
              </div>
              <p class="exec-timeline-text">{event.action ? `${event.action} · ${event.text}` : event.text}</p>
              {#if event.receipt}
                <button type="button" class="text-action" onclick={() => toggleReceipt(event.id)}>
                  {expanded[event.id] ? '收起回执' : '查看回执'}
                </button>
                {#if expanded[event.id]}
                  <pre class="exec-timeline-receipt">{event.receipt}</pre>
                {/if}
              {/if}
            </div>
          </li>
        {/each}
      </ol>
    {/if}
  </div>
{/if}
