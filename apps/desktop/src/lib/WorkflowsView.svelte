<script lang="ts">
  import {Play, Plus, ShieldCheck, Trash2, Workflow as WorkflowIcon} from 'lucide-svelte';
  import PageHeader from './PageHeader.svelte';
  import {runtime} from './runtime';

  const MAX_AGENT_COUNT = 384;

  type Skill = {
    id: string;
    name: string;
    description: string;
    kind: string;
    permissions: string[];
    prompt?: string;
    builtin?: boolean;
  };
  type Workflow = {
    id: string;
    name: string;
    description: string;
    skillIds: string[];
    mode: string;
    maxAgents: number;
    discussionRounds?: number;
  };

  let {notify, defaultWorkspace = ''}: {notify: (message: string) => void; defaultWorkspace?: string} = $props();
  let skills = $state<Skill[]>([]);
  let workflows = $state<Workflow[]>([]);
  let selected = $state<Workflow | null>(null);
  let input = $state('');
  let workspace = $state('');
  let isolatedWorktree = $state(false);
  let agentCount = $state(1);
  let running = $state(false);
  let diff = $state<{status: string; diff: string} | null>(null);
  let installing = $state(false);
  let skillName = $state('');
  let skillKind = $state<'coding' | 'research' | 'desktop'>('coding');
  let skillDesc = $state('');
  let skillPrompt = $state('');
  let skillPermissions = $state('workspace.read');

  async function refresh() {
    if (!(await runtime.connect())) return;
    const [skillResult, workflowResult] = await Promise.all([
      runtime.request<any>({type: 'skill.list', id: crypto.randomUUID()}),
      runtime.request<any>({type: 'workflow.list', id: crypto.randomUUID()}),
    ]);
    if (skillResult.type === 'skill.list.result' || skillResult.type === 'skill.updated') skills = skillResult.skills;
    if (workflowResult.type === 'workflow.list.result') workflows = workflowResult.workflows;
  }

  $effect(() => {
    void refresh();
  });
  $effect(() => {
    if (defaultWorkspace && !workspace.trim()) workspace = defaultWorkspace;
  });

  async function run() {
    if (!selected || !input.trim() || running) return;
    running = true;
    try {
      const result = await runtime.request<any>({
        type: 'workflow.run',
        id: crypto.randomUUID(),
        workflowId: selected.id,
        input: input.trim(),
        workspace: workspace.trim() || undefined,
        isolatedWorktree,
        agentCount: Math.max(1, Math.min(MAX_AGENT_COUNT, agentCount || selected.maxAgents)),
      });
      if (result.type === 'workflow.started') {
        notify(result.workspace ? `工作流已启动，隔离目录：${result.workspace}` : `工作流已启动：${selected.name}`);
        selected = null;
        input = '';
        isolatedWorktree = false;
      }
    } catch (error) {
      notify(`工作流启动失败：${error}`);
    } finally {
      running = false;
    }
  }

  async function inspectDiff() {
    if (!workspace.trim()) return;
    try {
      const result = await runtime.request<any>({type: 'workspace.diff', id: crypto.randomUUID(), root: workspace.trim()});
      if (result.type === 'workspace.diff.result') diff = {status: result.status, diff: result.diff};
    } catch (error) {
      notify(`读取 Diff 失败：${error}`);
    }
  }

  async function installSkill() {
    if (!skillName.trim() || !skillPrompt.trim()) {
      notify('请填写技能名称与提示词');
      return;
    }
    try {
      const result = await runtime.request<any>({
        type: 'skill.install',
        id: crypto.randomUUID(),
        skill: {
          id: skillName.trim().toLowerCase().replace(/[^a-z0-9_-]+/g, '-').replace(/^-|-$/g, '') || crypto.randomUUID(),
          name: skillName.trim(),
          description: skillDesc.trim() || skillName.trim(),
          kind: skillKind,
          permissions: skillPermissions
            .split(',')
            .map((item) => item.trim())
            .filter(Boolean),
          prompt: skillPrompt.trim(),
        },
      });
      if (result.type === 'skill.updated') {
        skills = result.skills;
        installing = false;
        skillName = '';
        skillDesc = '';
        skillPrompt = '';
        skillPermissions = 'workspace.read';
        notify('技能已安装');
      }
    } catch (error) {
      notify(`安装失败：${error}`);
    }
  }

  async function removeSkill(skill: Skill) {
    if (skill.builtin) {
      notify('内置技能不可删除');
      return;
    }
    try {
      const result = await runtime.request<any>({type: 'skill.remove', id: crypto.randomUUID(), skillId: skill.id});
      if (result.type === 'skill.updated') {
        skills = result.skills;
        notify(`已移除 ${skill.name}`);
      }
    } catch (error) {
      notify(`移除失败：${error}`);
    }
  }
</script>

<section class="view">
  <PageHeader eyebrow="Skills / 技能" title="技能" subtitle="技能是主 Agent 可以调用的能力说明；需要定时、多步执行时，请到定时任务里编排。">
    <button class="quiet-button" onclick={() => (installing = true)}><Plus size={14} />安装技能</button>
  </PageHeader>
  <div class="workflow-layout">
    <div class="workflow-list">
      <h2>使用方式</h2>
      <p class="settings-note">在聊天中直接告诉主 Agent 目标；它会按需调用技能，必要时派生子代理。多步自动化和执行时间统一在“定时”页面管理。</p>
      <div class="skill-usage-note"><WorkflowIcon size={17} /><strong>技能不等于工作流</strong><small>技能描述“怎么做”，定时任务描述“什么时候做”和“按哪些步骤做”。</small></div>
    </div>
    <div class="skill-list">
      <h2>技能库</h2>
      {#each skills as skill}
        <article>
          <div>
            <strong>{skill.name}</strong>
            <span class="badge">{skill.kind}</span>
            {#if skill.builtin !== false && !skill.id.startsWith('custom-')}<span class="badge dim">内置</span>{/if}
          </div>
          <p>{skill.description}</p>
          <small><ShieldCheck size={12} />{skill.permissions.join(' · ')}</small>
          {#if skill.builtin === false}
            <button class="text-action" type="button" onclick={() => removeSkill(skill)}><Trash2 size={12} />移除</button>
          {/if}
        </article>
      {/each}
    </div>
  </div>
</section>

{#if selected}
  <div class="modal-backdrop" role="presentation" onclick={(event) => { if (event.target === event.currentTarget) selected = null; }}>
    <div class="memory-editor" role="dialog" aria-modal="true" aria-labelledby="workflow-title">
      <header>
        <div>
          <p class="eyebrow">Workflow</p>
          <h2 id="workflow-title">{selected.name}</h2>
        </div>
        <button aria-label="关闭" onclick={() => (selected = null)}>×</button>
      </header>
      <p class="field-help">{selected.description}</p>
      <label>目标<input bind:value={input} placeholder="例如：审查本次提交并运行相关测试" /></label>
      <label>工作区（可选，默认当前项目）<input bind:value={workspace} placeholder={defaultWorkspace || '未绑定项目时请手填路径'} /></label>
      <label>子 Agent 数量<input type="number" min="1" max={MAX_AGENT_COUNT} bind:value={agentCount} /></label>
      {#if agentCount > 64}<p class="settings-note">高并发提示：{agentCount} 个 Agent 会同时请求模型，可能触发供应商限流；运行时会保留每个 Agent 的独立回执。</p>{/if}
      {#if workspace.trim()}<button class="quiet-button" onclick={inspectDiff}>查看当前 Diff</button>{/if}
      {#if diff}<div class="diff-preview"><strong>工作区状态</strong><pre>{diff.status || '无未提交文件'}</pre><strong>变更摘要</strong><pre>{diff.diff || '无 Diff'}</pre></div>{/if}
      <label class="task-schedule-toggle"><input type="checkbox" bind:checked={isolatedWorktree} /> 在 Git Worktree 中隔离执行（需填写 Git 工作区）</label>
      <p class="settings-note">
        执行模式：{selected.mode === 'peer-discussion'
          ? `平权研讨（${selected.discussionRounds || 1} 轮观点与质询 + 主模型主持汇总）`
          : selected.mode === 'parallel-read'
            ? '只读并行'
            : '串行'}；默认 {selected.maxAgents} 个 Agent，单次最高 {MAX_AGENT_COUNT} 个，可在此调整。
      </p>
      <footer>
        <button onclick={() => (selected = null)}>取消</button>
        <button class="primary-button" disabled={!input.trim() || running} onclick={run}><Play size={14} />{running ? '启动中…' : '运行工作流'}</button>
      </footer>
    </div>
  </div>
{/if}

{#if installing}
  <div class="modal-backdrop" role="presentation" onclick={(event) => { if (event.target === event.currentTarget) installing = false; }}>
    <div class="memory-editor" role="dialog" aria-modal="true" aria-label="安装技能">
      <header>
        <div>
          <p class="eyebrow">Skill</p>
          <h2>安装自定义技能</h2>
        </div>
        <button aria-label="关闭" onclick={() => (installing = false)}>×</button>
      </header>
      <label>名称<input aria-label="技能名称" bind:value={skillName} placeholder="例如：依赖审计" /></label>
      <label>类型
        <select aria-label="技能类型" bind:value={skillKind}>
          <option value="coding">coding</option>
          <option value="research">research</option>
          <option value="desktop">desktop</option>
        </select>
      </label>
      <label>简介<input aria-label="技能简介" bind:value={skillDesc} placeholder="一句话说明用途" /></label>
      <label>权限（逗号分隔）<input aria-label="技能权限" bind:value={skillPermissions} placeholder="workspace.read, process.test" /></label>
      <label>提示词<textarea aria-label="技能提示词" bind:value={skillPrompt} rows="5" placeholder="告诉子 Agent 如何完成这项工作…"></textarea></label>
      <p class="settings-note">自定义技能会保存在本地 skills.json；内置技能不可覆盖或删除。</p>
      <footer>
        <button onclick={() => (installing = false)}>取消</button>
        <button class="primary-button" onclick={installSkill}>安装</button>
      </footer>
    </div>
  </div>
{/if}
