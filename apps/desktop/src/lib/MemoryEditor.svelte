<script lang="ts">
  import {X} from 'lucide-svelte';
  import type {MemoryCategory, MemoryItem} from './types';

  let {
    onClose,
    onSave,
    initial,
  }: {
    onClose: () => void;
    onSave: (item: MemoryItem) => void | Promise<void>;
    initial?: MemoryItem;
  } = $props();

  const seed = initial;
  let category = $state<MemoryCategory>((seed?.category as MemoryCategory) || '事实');
  let text = $state(seed?.text || '');
  let importance = $state<1 | 2 | 3>(
    seed ? ((seed.importance >= 0.75 || seed.importance >= 3 ? 3 : seed.importance >= 0.4 || seed.importance >= 2 ? 2 : 1) as 1 | 2 | 3) : 2,
  );
  let error = $state('');

  function save() {
    if (!text.trim()) {
      error = '请写下要记住的内容';
      return;
    }
    const mapped = importance === 1 ? 0.33 : importance === 3 ? 0.9 : 0.66;
    void onSave({
      id: seed?.id || crypto.randomUUID(),
      category,
      text: text.trim(),
      importance: mapped,
      meta: seed?.meta || '刚刚 · 手动添加',
    });
  }
</script>

<div
  class="modal-backdrop"
  role="presentation"
  onclick={(event) => {
    if (event.target === event.currentTarget) onClose();
  }}
>
  <div class="memory-editor" role="dialog" aria-modal="true" aria-labelledby="memory-editor-title">
    <header>
      <div>
        <p class="eyebrow">手动记忆</p>
        <h2 id="memory-editor-title">{seed ? '编辑记忆' : '添加一件要记住的事'}</h2>
      </div>
      <button aria-label="关闭" onclick={onClose}><X size={16} /></button>
    </header>
    <label
      >类别<select bind:value={category}><option>事实</option><option>偏好</option><option>事件</option><option>反馈</option></select></label
    >
    <label>内容<textarea bind:value={text} rows="5" placeholder="例如：每周三和周日更新小说"></textarea></label>
    <label
      >重要性<div class="segmented">
        {#each [1, 2, 3] as level}
          <button class:active={importance === level} onclick={() => (importance = level as 1 | 2 | 3)}
            >{level === 1 ? '普通' : level === 2 ? '重要' : '长期保留'}</button
          >
        {/each}
      </div></label
    >
    {#if error}<p class="validation-error">{error}</p>{/if}
    <footer>
      <button onclick={onClose}>取消</button>
      <button class="primary-button" onclick={save}>{seed ? '保存修改' : '保存记忆'}</button>
    </footer>
  </div>
</div>
