/**
 * Slash-command execution + pattern management helpers for the companion agent.
 * Inspired by Grok Build (/goal /skill /loop /plan) mapped onto Pattern primitives.
 */
import {randomUUID} from 'node:crypto';
import {existsSync, readFileSync, writeFileSync} from 'node:fs';
import {join} from 'node:path';
import {
  formatSlashHelp,
  intervalToMinutes,
  parseAtMentions,
  parseSlashCommand,
  taskTitleFromText,
  type SlashCommand,
} from '@pattern/core';
import type {
  CronTriggerConfig,
  GoalState,
  SessionPlan,
  SessionPlanItem,
  SessionPlanItemStatus,
  SkillDefinition,
  TaskRecord,
  TaskSchedule,
  WorkflowDefinition,
} from '@pattern/protocol';

export type {GoalState, SessionPlan, SessionPlanItem};

export type SlashDeps = {
  dataDir: string;
  allSkills: () => SkillDefinition[];
  codingSkills: SkillDefinition[];
  getCustomSkills: () => SkillDefinition[];
  setCustomSkills: (skills: SkillDefinition[]) => void;
  saveCustomSkills: (skills: SkillDefinition[]) => void;
  getWorkflows: () => WorkflowDefinition[];
  setCustomWorkflows: (items: WorkflowDefinition[]) => void;
  saveCustomWorkflows: (items: WorkflowDefinition[]) => void;
  getCronTriggers: () => CronTriggerConfig[];
  setCronTriggers: (items: CronTriggerConfig[]) => void;
  saveCronTriggers: () => void;
  getTasks: () => TaskRecord[];
  createTaskFromText: (
    title: string,
    detail?: string,
    schedule?: TaskSchedule,
    workflow?: TaskRecord['workflow'],
    plan?: TaskRecord['plan'],
    context?: {conversationId?: string; workspace?: string; projectName?: string},
  ) => Promise<TaskRecord>;
  scheduleFromText: (text: string) => TaskSchedule | undefined;
  setProactivePaused: (paused: boolean) => void;
  triggerProactive: (reason: string) => Promise<string>;
  /** Optional: broadcast session plan changes for live chat UI. */
  onSessionPlanUpdated?: (plan: SessionPlan) => void;
  onSkillsUpdated?: (skills: SkillDefinition[]) => void;
  onCronUpdated?: (triggers: CronTriggerConfig[]) => void;
  onProactiveConfigUpdated?: (paused: boolean) => void;
  onTaskChanged?: (task: TaskRecord) => void;
  conversationId?: string;
  workspace?: string;
  projectName?: string;
};

function goalsFile(dataDir: string) {
  return join(dataDir, 'goals.json');
}

export function loadGoals(dataDir: string): GoalState[] {
  try {
    if (!existsSync(goalsFile(dataDir))) return [];
    const raw = JSON.parse(readFileSync(goalsFile(dataDir), 'utf8'));
    return Array.isArray(raw) ? raw.slice(0, 50) : [];
  } catch {
    return [];
  }
}

export function saveGoals(dataDir: string, goals: GoalState[]) {
  writeFileSync(goalsFile(dataDir), JSON.stringify(goals.slice(0, 50), null, 2));
}

export function activeGoal(goals: GoalState[]): GoalState | undefined {
  return goals.find((g) => g.status === 'active' || g.status === 'paused');
}

function sessionPlansFile(dataDir: string) {
  return join(dataDir, 'session-plans.json');
}

function normalizePlanStatus(value: unknown): SessionPlanItemStatus {
  const s = String(value || 'pending').toLowerCase();
  if (s === 'in_progress' || s === 'in-progress' || s === 'running' || s === 'doing') return 'in_progress';
  if (s === 'completed' || s === 'done' || s === 'complete') return 'completed';
  if (s === 'cancelled' || s === 'canceled' || s === 'skipped') return 'cancelled';
  return 'pending';
}

function normalizePlanItem(item: Partial<SessionPlanItem> & {content?: string; title?: string; text?: string}, index: number): SessionPlanItem | null {
  const content = String(item.content || item.title || item.text || '').trim().slice(0, 400);
  if (!content) return null;
  return {
    id: String(item.id || randomUUID()).slice(0, 64),
    content,
    status: normalizePlanStatus(item.status),
  };
}

export function loadSessionPlans(dataDir: string): Record<string, SessionPlan> {
  try {
    if (!existsSync(sessionPlansFile(dataDir))) return {};
    const raw = JSON.parse(readFileSync(sessionPlansFile(dataDir), 'utf8'));
    if (!raw || typeof raw !== 'object') return {};
    const out: Record<string, SessionPlan> = {};
    for (const [key, value] of Object.entries(raw as Record<string, any>)) {
      const conversationId = String(value?.conversationId || key).slice(0, 120);
      if (!conversationId) continue;
      const items = Array.isArray(value?.items)
        ? value.items.map((item: any, i: number) => normalizePlanItem(item, i)).filter(Boolean).slice(0, 40)
        : [];
      out[conversationId] = {
        conversationId,
        title: value?.title ? String(value.title).slice(0, 120) : undefined,
        items: items as SessionPlanItem[],
        updatedAt: Number(value?.updatedAt) || Date.now(),
      };
    }
    return out;
  } catch {
    return {};
  }
}

export function saveSessionPlans(dataDir: string, plans: Record<string, SessionPlan>) {
  // Keep last 80 conversations' plans.
  const entries = Object.entries(plans)
    .sort((a, b) => (b[1].updatedAt || 0) - (a[1].updatedAt || 0))
    .slice(0, 80);
  const out: Record<string, SessionPlan> = {};
  for (const [key, plan] of entries) out[key] = plan;
  writeFileSync(sessionPlansFile(dataDir), JSON.stringify(out, null, 2));
}

export function getSessionPlan(dataDir: string, conversationId: string): SessionPlan | null {
  if (!conversationId) return null;
  return loadSessionPlans(dataDir)[conversationId] || null;
}

export function setSessionPlan(
  dataDir: string,
  conversationId: string,
  items: Array<Partial<SessionPlanItem> & {content?: string; title?: string; text?: string}>,
  options?: {title?: string; merge?: boolean},
): SessionPlan {
  const plans = loadSessionPlans(dataDir);
  const existing = plans[conversationId];
  const nextItems = items
    .map((item, i) => normalizePlanItem(item, i))
    .filter(Boolean)
    .slice(0, 40) as SessionPlanItem[];

  let merged = nextItems;
  if (options?.merge && existing?.items?.length) {
    const byId = new Map(existing.items.map((item) => [item.id, {...item}]));
    for (const item of nextItems) {
      const prev = byId.get(item.id);
      if (prev) byId.set(item.id, {...prev, content: item.content || prev.content, status: item.status});
      else byId.set(item.id, item);
    }
    // Preserve order: existing order first, then new ids.
    const order: string[] = [];
    for (const item of existing.items) if (byId.has(item.id) && !order.includes(item.id)) order.push(item.id);
    for (const item of nextItems) if (!order.includes(item.id)) order.push(item.id);
    merged = order.map((id) => byId.get(id)!).filter(Boolean).slice(0, 40);
  }

  const plan: SessionPlan = {
    conversationId,
    title: options?.title?.trim().slice(0, 120) || existing?.title,
    items: merged,
    updatedAt: Date.now(),
  };
  plans[conversationId] = plan;
  saveSessionPlans(dataDir, plans);
  return plan;
}

export function clearSessionPlan(dataDir: string, conversationId: string): SessionPlan {
  const plan: SessionPlan = {conversationId, items: [], updatedAt: Date.now()};
  const plans = loadSessionPlans(dataDir);
  plans[conversationId] = plan;
  saveSessionPlans(dataDir, plans);
  return plan;
}

export function formatSessionPlan(plan: SessionPlan | null): string {
  if (!plan || !plan.items.length) return '当前会话还没有计划。用 `/plan` 写几步待办，或让我在推进任务时维护清单。';
  const done = plan.items.filter((i) => i.status === 'completed').length;
  const total = plan.items.filter((i) => i.status !== 'cancelled').length;
  const icon = (status: SessionPlanItemStatus) =>
    status === 'completed' ? '✓' : status === 'in_progress' ? '›' : status === 'cancelled' ? '×' : '○';
  const lines = [
    `### 当前会话计划${plan.title ? ` · ${plan.title}` : ''}`,
    '',
    `进度 **${done}/${total}**`,
    '',
    ...plan.items.map((item, i) => `${icon(item.status)} ${i + 1}. ${item.content}${item.status === 'in_progress' ? ' _(进行中)_' : ''}`),
  ];
  return lines.join('\n');
}

function parsePlanTextToItems(text: string): SessionPlanItem[] {
  return text
    .split(/\n+/)
    .map((line) => line.replace(/^\d+[.)、]\s*/, '').replace(/^[-*•]\s+/, '').replace(/^\[[ xX]\]\s*/, '').trim())
    .filter(Boolean)
    .slice(0, 40)
    .map((content) => ({
      id: randomUUID(),
      content: content.slice(0, 400),
      status: 'pending' as const,
    }));
}

function slugId(name: string): string {
  return String(name || 'item')
    .toLowerCase()
    .replace(/[^a-z0-9\u4e00-\u9fff]+/gi, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 48) || randomUUID().slice(0, 8);
}

export function workflowsFile(dataDir: string) {
  return join(dataDir, 'workflows.json');
}

export function loadCustomWorkflows(dataDir: string): WorkflowDefinition[] {
  try {
    if (!existsSync(workflowsFile(dataDir))) return [];
    const raw = JSON.parse(readFileSync(workflowsFile(dataDir), 'utf8'));
    if (!Array.isArray(raw)) return [];
    return raw
      .map((item: any) => ({
        id: String(item.id || '').slice(0, 80),
        name: String(item.name || '').slice(0, 80),
        description: String(item.description || '').slice(0, 400),
        skillIds: Array.isArray(item.skillIds) ? item.skillIds.map(String).slice(0, 20) : [],
        mode: (['serial', 'parallel-read', 'peer-discussion'].includes(item.mode) ? item.mode : 'serial') as WorkflowDefinition['mode'],
        maxAgents: Math.max(1, Math.min(32, Number(item.maxAgents) || 1)),
        discussionRounds: item.discussionRounds ? Math.max(1, Math.min(4, Number(item.discussionRounds) || 1)) : undefined,
      }))
      .filter((item: WorkflowDefinition) => item.id && item.name && item.skillIds.length)
      .slice(0, 50);
  } catch {
    return [];
  }
}

export function saveCustomWorkflows(dataDir: string, items: WorkflowDefinition[]) {
  writeFileSync(workflowsFile(dataDir), JSON.stringify(items, null, 2));
}

function intervalSchedule(interval: string): TaskSchedule {
  return {
    kind: 'interval',
    intervalMinutes: intervalToMinutes(interval),
    enabled: true,
  };
}

export async function executeSlashCommand(cmd: SlashCommand, deps: SlashDeps): Promise<string> {
  if (cmd.kind === 'help' || cmd.kind === 'unknown') {
    return formatSlashHelp() + (cmd.kind === 'unknown' ? `\n\n未识别：\`${cmd.raw}\`` : '');
  }

  if (cmd.kind === 'goal') {
    let goals = loadGoals(deps.dataDir);
    const current = activeGoal(goals);
    if (cmd.action === 'status') {
      if (!current) return '当前没有进行中的目标。用 `/goal <目标>` 创建一个。';
      return [
        `### 当前目标 · ${current.status}`,
        '',
        current.objective,
        current.taskId ? `\n关联任务：\`${current.taskId}\`` : '',
        current.blockedReason ? `\n阻塞：${current.blockedReason}` : '',
        current.progress.length ? `\n最近进度：\n${current.progress.slice(-5).map((p) => `- ${p}`).join('\n')}` : '',
      ].filter(Boolean).join('\n');
    }
    if (cmd.action === 'pause') {
      if (!current) return '没有可暂停的目标。';
      current.status = 'paused';
      current.updatedAt = Date.now();
      saveGoals(deps.dataDir, goals);
      return `已暂停目标：${current.objective}`;
    }
    if (cmd.action === 'resume') {
      if (!current || current.status !== 'paused') return '没有已暂停的目标可恢复。';
      current.status = 'active';
      current.updatedAt = Date.now();
      saveGoals(deps.dataDir, goals);
      return `已恢复目标：${current.objective}`;
    }
    if (cmd.action === 'complete') {
      if (!current) return '没有进行中的目标可标记完成。';
      current.status = 'done';
      current.updatedAt = Date.now();
      current.progress = [...(current.progress || []), `已标记完成 · ${new Date().toLocaleString('zh-CN')}`].slice(-20);
      saveGoals(deps.dataDir, goals);
      return `已完成目标：${current.objective}`;
    }
    if (cmd.action === 'clear') {
      if (!current) return '没有需要清除的目标。';
      current.status = 'cleared';
      current.updatedAt = Date.now();
      saveGoals(deps.dataDir, goals);
      return `已清除目标：${current.objective}`;
    }
    // set
    const objective = (cmd.text || '').trim();
    if (!objective) return '用法：`/goal <可验证的目标>`';
    // clear previous active
    goals = goals.map((g) =>
      g.status === 'active' || g.status === 'paused' ? {...g, status: 'cleared' as const, updatedAt: Date.now()} : g,
    );
    const task = await deps.createTaskFromText(
      taskTitleFromText(objective),
      `[Goal]\n${objective}\n\n完成条件：目标被验证达成后，用 pattern.update_goal 标记 completed。`,
      undefined,
      undefined,
      undefined,
      {conversationId: deps.conversationId, workspace: deps.workspace, projectName: deps.projectName},
    );
    const goal: GoalState = {
      id: randomUUID(),
      objective,
      status: 'active',
      createdAt: Date.now(),
      updatedAt: Date.now(),
      taskId: task.id,
      conversationId: deps.conversationId,
      progress: [`已创建并排队执行 · ${new Date().toLocaleString('zh-CN')}`],
    };
    goals.unshift(goal);
    saveGoals(deps.dataDir, goals);
    return [
      `### 已进入 Goal 模式`,
      '',
      objective,
      '',
      `关联任务 \`${task.id}\` 已排队。我会持续推进直到可验证完成。`,
      '',
      '管理：`/goal status` · `/goal pause` · `/goal resume` · `/goal clear`',
    ].join('\n');
  }

  if (cmd.kind === 'skill') {
    if (cmd.action === 'help') {
      return '用法：\n- `/skill list`\n- `/skill 名称 | 描述 | 提示词`\n- `/skill run 名称 [目标]`\n- `/代码审查`（按技能名快捷运行）';
    }
    if (cmd.action === 'list') {
      const skills = deps.allSkills();
      if (!skills.length) return '还没有技能。';
      return ['### 技能列表', '', ...skills.map((s) => `- **${s.name}** (\`${s.id}\`) · ${s.kind} — ${s.description}`)].join('\n');
    }
    if (cmd.action === 'create') {
      const name = (cmd.name || '未命名技能').slice(0, 80);
      const parts = (cmd.body || '').split('\n\n');
      const description = (parts[0] || name).slice(0, 400);
      const prompt = (parts.slice(1).join('\n\n') || cmd.body || name).slice(0, 4000);
      const skill: SkillDefinition = {
        id: slugId(name),
        name,
        description,
        kind: 'coding',
        permissions: ['workspace.read'],
        prompt,
      };
      if (deps.codingSkills.some((s) => s.id === skill.id)) return '不能覆盖内置技能 id，请换名称。';
      const next = [skill, ...deps.getCustomSkills().filter((s) => s.id !== skill.id)].slice(0, 100);
      deps.setCustomSkills(next);
      deps.saveCustomSkills(next);
      deps.onSkillsUpdated?.(deps.allSkills());
      return `已创建技能 **${skill.name}** (\`${skill.id}\`)。\n\n描述：${skill.description}\n\n用 \`/skill run ${skill.name} <目标>\` 运行。可在侧栏「技能」页管理。`;
    }
    if (cmd.action === 'run') {
      const name = cmd.name || '';
      const skill = deps.allSkills().find(
        (s) => s.id === name || s.name === name || s.name.toLowerCase() === name.toLowerCase(),
      );
      if (!skill) return `未找到技能：${name}。用 \`/skill list\` 查看。`;
      const goal = cmd.body || skill.description;
      const task = await deps.createTaskFromText(
        `${skill.name} · ${taskTitleFromText(goal)}`,
        `[Skill: ${skill.name}]\n${skill.prompt}\n\n用户目标：${goal}\n权限：${skill.permissions.join(', ')}`,
        undefined,
        undefined,
        undefined,
        {conversationId: deps.conversationId, workspace: deps.workspace, projectName: deps.projectName},
      );
      return `已用技能 **${skill.name}** 创建任务 \`${task.id}\`：${goal}`;
    }
  }

  if (cmd.kind === 'loop') {
    if (cmd.action === 'help') {
      return '用法：\n- `/loop 30m 巡检未完成任务`\n- `/loop list`\n- `/loop delete <taskId>`';
    }
    if (cmd.action === 'list') {
      const loops = deps.getTasks().filter((t) => t.schedule?.kind === 'interval' && t.schedule.enabled !== false);
      const crons = deps.getCronTriggers().filter((c) => c.enabled);
      const lines = ['### 循环 / 定时', ''];
      if (!loops.length && !crons.length) lines.push('（空）');
      for (const t of loops) {
        lines.push(`- 任务循环 \`${t.id}\` · 每 ${t.schedule?.intervalMinutes || '?'} 分钟 — ${t.title}`);
      }
      for (const c of crons) {
        lines.push(`- 每日提醒 \`${c.id}\` · ${c.time} — ${c.message}`);
      }
      return lines.join('\n');
    }
    if (cmd.action === 'delete') {
      const id = (cmd.id || '').trim();
      if (!id) return '用法：`/loop delete <taskId>`';
      const task = deps.getTasks().find((t) => t.id === id || t.id.startsWith(id));
      if (!task) return `未找到循环任务：${id}`;
      task.status = 'cancelled';
      if (task.schedule) task.schedule.enabled = false;
      deps.onTaskChanged?.(task);
      return `已取消循环 **${task.title}**（\`${task.id}\`）。`;
    }
    const prompt = (cmd.prompt || '').trim();
    if (!prompt) return '用法：`/loop 30m <每次要做的事>`';
    const schedule = intervalSchedule(cmd.interval || '60m');
    const task = await deps.createTaskFromText(
      taskTitleFromText(prompt),
      `[Loop]\n每隔 ${schedule.intervalMinutes} 分钟执行：\n${prompt}`,
      schedule,
      undefined,
      undefined,
      {conversationId: deps.conversationId, workspace: deps.workspace, projectName: deps.projectName},
    );
    return `已创建循环任务 **${task.title}**（每 ${schedule.intervalMinutes} 分钟）。\n\n\`${task.id}\``;
  }

  if (cmd.kind === 'plan') {
    const conversationId = deps.conversationId?.trim();
    if (!conversationId) {
      return '需要先有一个会话才能挂载计划。请先发一条消息创建对话，再使用 `/plan`。';
    }
    if (cmd.action === 'status') {
      return formatSessionPlan(getSessionPlan(deps.dataDir, conversationId));
    }
    if (cmd.action === 'clear') {
      const plan = clearSessionPlan(deps.dataDir, conversationId);
      deps.onSessionPlanUpdated?.(plan);
      return '已清空当前会话计划。';
    }
    const text = (cmd.text || '').trim();
    if (!text) return '用法：`/plan <待办步骤>`（每行一步）· `/plan status` · `/plan clear`';
    const items = parsePlanTextToItems(text);
    if (!items.length) return '没有解析到有效的计划步骤。';
    const plan = setSessionPlan(deps.dataDir, conversationId, items, {
      title: taskTitleFromText(text),
      merge: cmd.action === 'append',
    });
    deps.onSessionPlanUpdated?.(plan);
    return [
      `### 已写入当前会话计划`,
      '',
      formatSessionPlan(plan),
      '',
      '这是**本对话**里的待办清单（不是定时任务）。推进时我会用 `pattern.update_plan` 勾选进度；侧栏也能看到。',
    ].join('\n');
  }

  if (cmd.kind === 'task') {
    const task = await deps.createTaskFromText(
      taskTitleFromText(cmd.text),
      cmd.text,
      deps.scheduleFromText(cmd.text),
      undefined,
      undefined,
      {conversationId: deps.conversationId, workspace: deps.workspace, projectName: deps.projectName},
    );
    return `已创建任务 **${task.title}**（\`${task.id}\`，${task.status}）。`;
  }

  if (cmd.kind === 'remind') {
    const message = cmd.message.trim();
    if (!message) return '用法：`/remind 21:30 该休息了`';
    let time = cmd.time;
    if (!time) {
      const inferred = message.match(/([01]?\d|2[0-3])[:：]([0-5]\d)/);
      if (inferred) time = `${inferred[1].padStart(2, '0')}:${inferred[2]}`;
    }
    if (!time || !/^([01]\d|2[0-3]):[0-5]\d$/.test(time)) {
      // fall back to interval task in 60m
      const task = await deps.createTaskFromText(
        taskTitleFromText(message),
        `[Reminder]\n${message}`,
        {kind: 'once', at: Date.now() + 60 * 60_000, enabled: true},
        undefined,
        undefined,
        {conversationId: deps.conversationId},
      );
      return `未识别 HH:MM，已创建约 1 小时后的一次性提醒任务 \`${task.id}\`：${message}`;
    }
    const trigger: CronTriggerConfig = {
      id: randomUUID(),
      time,
      message,
      enabled: true,
    };
    const next = [trigger, ...deps.getCronTriggers().filter((t) => t.id !== trigger.id)].slice(0, 30);
    deps.setCronTriggers(next);
    deps.saveCronTriggers();
    deps.onCronUpdated?.(next);
    return `已设置每日 **${time}** 提醒：${message}\n\n可在侧栏「主动」页或聊天顶栏 Remind 条管理。`;
  }

  if (cmd.kind === 'proactive') {
    if (cmd.action === 'help') return '用法：`/proactive pause|resume|trigger [原因]`';
    if (cmd.action === 'pause') {
      deps.setProactivePaused(true);
      deps.onProactiveConfigUpdated?.(true);
      return '已暂停主动消息。';
    }
    if (cmd.action === 'resume') {
      deps.setProactivePaused(false);
      deps.onProactiveConfigUpdated?.(false);
      return '已恢复主动消息。';
    }
    const note = await deps.triggerProactive(cmd.reason || '用户手动触发');
    return note;
  }

  if (cmd.kind === 'workflow') {
    if (cmd.action === 'help') {
      return '用法：\n- `/workflow list`\n- `/workflow run <id> <目标>`';
    }
    const workflows = deps.getWorkflows();
    if (cmd.action === 'list') {
      return ['### 工作流', '', ...workflows.map((w) => `- **${w.name}** (\`${w.id}\`) — ${w.description}`)].join('\n');
    }
    if (cmd.action === 'run') {
      const wf = workflows.find((w) => w.id === cmd.id || w.name === cmd.id);
      if (!wf) return `未找到工作流：${cmd.id}。用 \`/workflow list\` 查看。`;
      const input = cmd.input || wf.description;
      const skillText = wf.skillIds
        .map((id) => deps.allSkills().find((s) => s.id === id))
        .filter(Boolean)
        .map((s) => `${s!.name}：${s!.prompt}`)
        .join('\n');
      const task = await deps.createTaskFromText(
        `${wf.name} · ${taskTitleFromText(input)}`,
        `[Workflow: ${wf.name}]\n${wf.description}\n\n用户目标：${input}\n\n技能：\n${skillText}`,
        undefined,
        {id: wf.id, name: wf.name, stepCount: wf.maxAgents, currentStep: 0, workspace: deps.workspace, agents: wf.maxAgents},
        undefined,
        {conversationId: deps.conversationId, workspace: deps.workspace, projectName: deps.projectName},
      );
      return `已启动工作流 **${wf.name}** → 任务 \`${task.id}\``;
    }
  }

  return formatSlashHelp();
}

export function enrichWithMentions(text: string, deps: Pick<SlashDeps, 'allSkills' | 'getWorkflows' | 'getTasks' | 'projectName' | 'workspace'>): string {
  const mentions = parseAtMentions(text);
  if (!mentions.length) return '';
  const lines = ['[Referenced entities]'];
  for (const m of mentions) {
    if (m.type === 'skill') {
      const skill = deps.allSkills().find((s) => s.id === m.id || s.name === m.id || s.name.toLowerCase() === m.id.toLowerCase());
      lines.push(skill ? `- skill ${skill.name} (${skill.id}): ${skill.prompt.slice(0, 300)}` : `- skill ${m.id}: (not found)`);
    } else if (m.type === 'workflow') {
      const wf = deps.getWorkflows().find((w) => w.id === m.id || w.name === m.id);
      lines.push(wf ? `- workflow ${wf.name} (${wf.id}): ${wf.description}` : `- workflow ${m.id}: (not found)`);
    } else if (m.type === 'task') {
      const task = deps.getTasks().find((t) => t.id.startsWith(m.id) || t.title.includes(m.id));
      lines.push(task ? `- task ${task.title} (${task.id}) status=${task.status}` : `- task ${m.id}: (not found)`);
    } else if (m.type === 'project') {
      lines.push(`- project ${deps.projectName || m.id}: ${deps.workspace || '(no workspace)'}`);
    } else {
      lines.push(`- ${m.type} ${m.id}`);
    }
  }
  return lines.join('\n');
}

export {parseSlashCommand, formatSlashHelp, parseAtMentions};
