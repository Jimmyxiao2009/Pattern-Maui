<script lang="ts">
  import {ChevronDown, ChevronRight, File, Folder, FolderOpen, Paperclip, RefreshCw} from 'lucide-svelte';
  import type {FileNode} from './types';

  let {
    rootPath,
    nodes,
    loading = false,
    error = '',
    selectedPath = '',
    onRefresh,
    onOpenFile,
    onAttachFile,
    onExpandDir,
  }: {
    rootPath: string;
    nodes: FileNode[];
    loading?: boolean;
    error?: string;
    selectedPath?: string;
    onRefresh: () => void;
    onOpenFile: (node: FileNode) => void;
    onAttachFile?: (node: FileNode) => void;
    onExpandDir?: (node: FileNode) => void | Promise<void>;
  } = $props();

  let expanded = $state<Record<string, boolean>>({});

  async function toggle(node: FileNode) {
    const next = !isExpanded(node.path);
    expanded = {...expanded, [node.path]: next};
    if (next && node.kind === 'directory' && !node.children?.length) {
      await onExpandDir?.(node);
    }
  }

  function isExpanded(path: string) {
    // First level defaults open so project files are immediately visible.
    if (!(path in expanded)) {
      const isTop = nodes.some((node) => node.path === path);
      return isTop;
    }
    return expanded[path];
  }
</script>

<aside class="file-tree-pane" aria-label="项目文件">
  <header class="file-tree-head">
    <div>
      <p class="eyebrow">项目文件夹</p>
      <strong title={rootPath}>{rootPath || '未设置路径'}</strong>
    </div>
    <button class="icon-action" type="button" aria-label="刷新文件树" title="刷新文件树" onclick={onRefresh} disabled={loading}>
      <RefreshCw size={14} />
    </button>
  </header>

  {#if loading}
    <p class="file-tree-status">正在读取目录…</p>
  {:else if error}
    <p class="file-tree-status error" role="alert">{error}</p>
  {:else if !nodes.length}
    <p class="file-tree-status">此目录为空，或当前环境无法读取本地文件。</p>
  {:else}
    <ul class="file-tree" role="tree" aria-label="文件列表">
      {#each nodes as node (node.path)}
        {@render treeNode(node, 0)}
      {/each}
    </ul>
  {/if}
</aside>

{#snippet treeNode(node: FileNode, depth: number)}
  <li role="treeitem" aria-selected={selectedPath === node.path ? 'true' : 'false'} aria-expanded={node.kind === 'directory' ? isExpanded(node.path) : undefined} style={`--depth:${depth}`}>
    {#if node.kind === 'directory'}
      <button type="button" class="file-node dir" class:active={selectedPath === node.path} onclick={() => toggle(node)}>
        {#if isExpanded(node.path)}
          <ChevronDown size={13} />
          <FolderOpen size={14} />
        {:else}
          <ChevronRight size={13} />
          <Folder size={14} />
        {/if}
        <span>{node.name}</span>
      </button>
      {#if isExpanded(node.path) && node.children?.length}
        <ul role="group">
          {#each node.children as child (child.path)}
            {@render treeNode(child, depth + 1)}
          {/each}
        </ul>
      {/if}
    {:else}
      <div class="file-node file" class:active={selectedPath === node.path}>
        <button type="button" class="file-open" onclick={() => onOpenFile(node)} title={node.path}>
          <File size={14} />
          <span>{node.name}</span>
        </button>
        {#if onAttachFile}
          <button type="button" class="icon-action file-attach" aria-label={`附加 ${node.name}`} title="附加到对话" onclick={() => onAttachFile?.(node)}>
            <Paperclip size={12} />
          </button>
        {/if}
      </div>
    {/if}
  </li>
{/snippet}
