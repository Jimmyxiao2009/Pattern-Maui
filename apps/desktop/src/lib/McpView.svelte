<script lang="ts">
  import {Play, Plus, RefreshCw, ShieldCheck, Trash2, Wrench} from 'lucide-svelte';
  import PageHeader from './PageHeader.svelte';
  import {runtime} from './runtime';

  type ToolSchema = {name: string; description?: string; inputSchema?: unknown};
  type Server = {
    id: string;
    name: string;
    command: string;
    args: string[];
    enabled: boolean;
    permissions: string[];
    tools?: string[];
    toolSchemas?: ToolSchema[];
    lastDiscoveredAt?: number;
    error?: string;
  };

  let {notify}: {notify: (message: string) => void} = $props();
  let servers = $state<Server[]>([]);
  let editing = $state(false);
  let name = $state('');
  let command = $state('');
  let args = $state('');
  let permissions = $state('workspace.read, mcp.call');
  let calling = $state<{serverId: string; tool: string} | null>(null);
  let callArgs = $state('{}');
  let callResult = $state('');
  let callBusy = $state(false);

  async function refresh() {
    if (await runtime.connect()) {
      const result = await runtime.request<any>({type: 'mcp.list', id: crypto.randomUUID()});
      if (result.type === 'mcp.list.result') servers = result.servers;
    }
  }

  $effect(() => {
    void refresh();
  });

  async function save() {
    if (!name.trim() || !command.trim()) return;
    const server: Server = {
      id: crypto.randomUUID(),
      name: name.trim(),
      command: command.trim(),
      args: args.split(/\s+/).filter(Boolean),
      enabled: true,
      permissions: permissions
        .split(',')
        .map((item) => item.trim())
        .filter(Boolean),
    };
    try {
      const result = await runtime.request<any>({type: 'mcp.set', id: crypto.randomUUID(), servers: [...servers, server]});
      if (result.type === 'mcp.updated') servers = result.servers;
      editing = false;
      name = '';
      command = '';
      args = '';
      permissions = 'workspace.read, mcp.call';
      notify('MCP 服务已保存');
    } catch (error) {
      notify(`保存失败：${error}`);
    }
  }

  async function discover(server: Server) {
    try {
      const result = await runtime.request<any>({type: 'mcp.discover', id: crypto.randomUUID(), serverId: server.id});
      if (result.type === 'mcp.updated') servers = result.servers;
      notify(result.servers?.find((item: Server) => item.id === server.id)?.error ? '工具发现失败' : '已发现 MCP 工具');
    } catch (error) {
      notify(`发现失败：${error}`);
    }
  }

  async function remove(server: Server) {
    const result = await runtime.request<any>({
      type: 'mcp.set',
      id: crypto.randomUUID(),
      servers: servers.filter((item) => item.id !== server.id),
    });
    if (result.type === 'mcp.updated') servers = result.servers;
  }

  function openCall(server: Server, tool: string) {
    calling = {serverId: server.id, tool};
    callArgs = '{}';
    callResult = '';
  }

  async function runCall() {
    if (!calling || callBusy) return;
    let parsed: Record<string, unknown> = {};
    try {
      parsed = callArgs.trim() ? JSON.parse(callArgs) : {};
      if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error('参数必须是 JSON 对象');
    } catch (error) {
      notify(`参数无效：${error}`);
      return;
    }
    callBusy = true;
    callResult = '';
    try {
      const result = await runtime.request<any>({
        type: 'mcp.call',
        id: crypto.randomUUID(),
        serverId: calling.serverId,
        tool: calling.tool,
        arguments: parsed,
      });
      if (result.type === 'mcp.call.result') {
        callResult = JSON.stringify(result.result, null, 2);
        notify(`已调用 ${calling.tool}`);
      } else {
        callResult = JSON.stringify(result, null, 2);
      }
    } catch (error) {
      callResult = String(error);
      notify(`调用失败：${error}`);
    } finally {
      callBusy = false;
    }
  }

  function toolNames(server: Server) {
    if (server.toolSchemas?.length) return server.toolSchemas.map((tool) => tool.name);
    return server.tools || [];
  }
</script>

<section class="view">
  <PageHeader eyebrow="工具连接" title="MCP 管理" subtitle="仅启动你明确配置的本地 MCP 进程；可发现工具并在此试调调用。">
    <button class="quiet-button" onclick={refresh}><RefreshCw size={14} />刷新</button>
    <button class="primary-button" onclick={() => (editing = true)}><Plus size={14} />添加 MCP</button>
  </PageHeader>
  <div class="mcp-list">
    {#each servers as server}
      <article>
        <div class="mcp-icon"><Wrench size={15} /></div>
        <div class="mcp-copy">
          <div>
            <strong>{server.name}</strong>
            <span class="badge" class:green={server.enabled} class:dim={!server.enabled}>{server.enabled ? '已启用' : '已停用'}</span>
          </div>
          <code>{server.command} {server.args.join(' ')}</code>
          <p><ShieldCheck size={12} />{server.permissions.join(' · ') || '未声明权限'}</p>
          {#if toolNames(server).length}
            <div class="mcp-tools">
              {#each toolNames(server) as tool}
                <button type="button" class="quiet-button" onclick={() => openCall(server, tool)}><Play size={12} />{tool}</button>
              {/each}
            </div>
          {:else}
            <small>尚未发现工具，可先点「发现工具」</small>
          {/if}
          {#if server.error}<p class="validation-error">{server.error}</p>{/if}
        </div>
        <div class="mcp-actions">
          <button title="发现工具" aria-label="发现工具" onclick={() => discover(server)}><RefreshCw size={14} /></button>
          <button title="删除" aria-label="删除" onclick={() => remove(server)}><Trash2 size={14} /></button>
        </div>
      </article>
    {:else}
      <div class="blank-state">
        <div class="blank-mark">⌘</div>
        <h3>还没有 MCP 服务</h3>
        <p>添加本地 stdio MCP 后可发现工具并试调；执行授权仍由工作流权限控制。</p>
      </div>
    {/each}
  </div>
</section>

{#if editing}
  <div
    class="modal-backdrop"
    role="presentation"
    onclick={(event) => {
      if (event.target === event.currentTarget) editing = false;
    }}
  >
    <div class="memory-editor" role="dialog" aria-modal="true">
      <header>
        <div>
          <p class="eyebrow">MCP</p>
          <h2>添加本地服务</h2>
        </div>
        <button aria-label="关闭" onclick={() => (editing = false)}>×</button>
      </header>
      <label>名称<input bind:value={name} placeholder="例如：Filesystem" /></label>
      <label>启动命令<input bind:value={command} placeholder="npx / python / uvx" /></label>
      <label>参数（空格分隔）<input bind:value={args} placeholder="-y @modelcontextprotocol/server-filesystem C:\\Work" /></label>
      <label>权限（逗号分隔）<input bind:value={permissions} placeholder="workspace.read, mcp.call, mcp.write" /></label>
      <p class="settings-note">Pattern 只会启动已保存的命令；不要把密钥写进参数，优先使用环境变量。写入、删除和命令执行类工具还必须声明 mcp.write。</p>
      <footer>
        <button onclick={() => (editing = false)}>取消</button>
        <button class="primary-button" disabled={!name.trim() || !command.trim()} onclick={save}>保存</button>
      </footer>
    </div>
  </div>
{/if}

{#if calling}
  <div
    class="modal-backdrop"
    role="presentation"
    onclick={(event) => {
      if (event.target === event.currentTarget) calling = null;
    }}
  >
    <div class="memory-editor" role="dialog" aria-modal="true" aria-label="MCP 试调">
      <header>
        <div>
          <p class="eyebrow">试调</p>
          <h2>{calling.tool}</h2>
        </div>
        <button aria-label="关闭" onclick={() => (calling = null)}>×</button>
      </header>
      <label>参数 JSON<textarea aria-label="MCP 参数 JSON" bind:value={callArgs} rows="6" spellcheck="false"></textarea></label>
      {#if callResult}
        <div class="mcp-result-block"><span class="eyebrow">结果</span><pre class="mcp-call-result" aria-label="MCP 调用结果">{callResult}</pre></div>
      {/if}
      <footer>
        <button onclick={() => (calling = null)}>关闭</button>
        <button class="primary-button" disabled={callBusy} onclick={runCall}>{callBusy ? '调用中…' : '调用工具'}</button>
      </footer>
    </div>
  </div>
{/if}
