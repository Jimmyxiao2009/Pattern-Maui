<script lang="ts">
  import {renderMarkdown} from './markdown';
  import TaskCard from './TaskCard.svelte';
  import ExecutionTimeline from './ExecutionTimeline.svelte';
  import type {ChatMessage} from './types';

  let {
    message,
    onOpenTask,
  }: {
    message: ChatMessage;
    onOpenTask?: (taskId: string) => void;
  } = $props();
  const html = $derived(message.role === 'assistant' ? renderMarkdown(message.text) : '');
</script>

{#if message.events?.length}
  <ExecutionTimeline events={message.events} streaming={!!message.streaming} />
{/if}

{#if message.role === 'assistant'}
  {#if message.text}
    <div class="md-body">{@html html}</div>
  {:else if message.streaming && !message.error}
    <div class="typing" aria-label="正在生成"><i></i><i></i><i></i></div>
  {/if}
  {#if message.taskCard}
    <TaskCard card={message.taskCard} onOpen={onOpenTask} />
  {/if}
  {#if message.error}
    <p class="message-error" role="alert">{message.error}</p>
  {/if}
{:else}
  <p>{message.text}</p>
{/if}
