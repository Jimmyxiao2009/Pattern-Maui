/** Routing compatibility kept for the wire protocol; the UI exposes one primary agent. */
export type AgentSlot = 'companion' | 'executor';
export interface RouteDecision { slot: AgentSlot; confidence: number; reason: string; }

// Keep Chinese patterns as unicode escapes so routing stays stable across editors/shells.
const EXECUTOR_ZH = /(帮我|请|能不能|可以|试试|给我).{0,24}(打开|整理|移动|重命名|删除|下载|上传|发送|运行|设置|关闭|点击|输入|截图|按|模拟|操作|计算)/;
const EXECUTOR_EN = /\b(open|organize|move|rename|delete|download|upload|send|run|click|type|press|launch|calculate|compute)\b/i;
// Explicit computer-use / desktop-control keywords (incl. Start menu / accessibility).
const EXECUTOR_HINT = /(\u6267\u884c\u4efb\u52a1|\u7535\u8111\u64cd\u4f5c|computer[\s-]?use|\btask\b|\u7528\u8f85\u52a9\u529f\u80fd|\u901a\u8fc7\u8f85\u52a9\u529f\u80fd|\u7528\u65e0\u969c\u788d|accessibility\s*(to|and)?\s*(open|press|click)|\u6253\u5f00\u5f00\u59cb\u83dc\u5355|start menu|\bwin\s*\u952e\b|\bwindows\s*key\b)/i;
// Open app / operate UI / multi-step desktop goals (calc, notepad, browser, etc.).
const EXECUTOR_ACTION = /(打开.{0,20}(菜单|应用|窗口|文件|设置|浏览器|app|软件|程序|计算器|记事本|资源管理器|画图|截图|终端|命令行|powershell|cmd|chrome|edge|firefox)|打开\s*(计算器|记事本|浏览器|设置|画图)|按.{0,8}(键|win|enter|tab|esc)|点击|输入|模拟.{0,8}(键盘|鼠标)|用.{0,8}(键盘|鼠标|UIA|辅助功能)|随机.{0,12}打开|计算\s*\d|\d\s*[\+\-\*\/×÷]\s*\d)/i;

/** Fast local routing. A model can later override low-confidence decisions. */
export function routeUserMessage(text: string): RouteDecision {
  const input = text.trim();
  const normalized = input.toLowerCase();
  if (!input) return {slot: 'companion', confidence: 1, reason: 'empty'};
  if (/^\/(task|\u6267\u884c)\b/.test(normalized)) return {slot: 'executor', confidence: 1, reason: 'explicit command'};
  if (EXECUTOR_HINT.test(input)) return {slot: 'executor', confidence: 0.95, reason: 'task keyword'};
  if (EXECUTOR_ACTION.test(input)) return {slot: 'executor', confidence: 0.9, reason: 'desktop control request'};
  if (EXECUTOR_ZH.test(input) || EXECUTOR_EN.test(normalized)) return {slot: 'executor', confidence: 0.88, reason: 'desktop action intent'};
  return {slot: 'companion', confidence: 0.55, reason: 'ambiguous conversation/default'};
}

/** Whether the primary agent should start desktop execution for this request. */
export function shouldTransferToExecutor(text: string, minConfidence = 0.85): boolean {
  // Desktop-control phrases (open app, Start menu, press keys, accessibility) auto-start work.
  const decision = routeUserMessage(text);
  return decision.slot === 'executor' && decision.confidence >= minConfidence;
}

export function taskTitleFromText(text: string, max = 80): string {
  const cleaned = text
    .replace(/^\/(task|\u6267\u884c|goal|plan|skill|loop|remind|workflow|proactive)\b\s*/i, '')
    .trim();
  return (cleaned || text).slice(0, max);
}

/** Slash commands inspired by Grok Build (/goal /skill /loop /plan) mapped onto Pattern primitives. */
export type SlashKind = 'help' | 'goal' | 'skill' | 'loop' | 'plan' | 'task' | 'remind' | 'proactive' | 'workflow' | 'unknown';

export type SlashCommand =
  | {kind: 'help'}
  | {kind: 'goal'; action: 'set' | 'status' | 'pause' | 'resume' | 'complete' | 'clear'; text?: string}
  | {kind: 'skill'; action: 'create' | 'list' | 'run' | 'help'; name?: string; body?: string}
  | {kind: 'loop'; action: 'create' | 'list' | 'delete' | 'help'; interval?: string; prompt?: string; id?: string}
  | {kind: 'plan'; action: 'set' | 'append' | 'status' | 'clear'; text?: string}
  | {kind: 'task'; text: string}
  | {kind: 'remind'; time?: string; message: string}
  | {kind: 'proactive'; action: 'pause' | 'resume' | 'trigger' | 'help'; reason?: string}
  | {kind: 'workflow'; action: 'list' | 'run' | 'help'; id?: string; input?: string}
  | {kind: 'unknown'; raw: string};

export interface SlashCatalogItem {
  command: string;
  summary: string;
  example: string;
}

/** UI autocomplete + /help catalog (Grok Build–style surface adapted to Pattern). */
export const SLASH_CATALOG: SlashCatalogItem[] = [
  {command: '/goal', summary: '设定可验证的 run-until-done 目标（任务）', example: '/goal 让计算器算出 1+1 并验证结果'},
  {command: '/goal status', summary: '查看当前目标', example: '/goal status'},
  {command: '/goal pause', summary: '暂停当前目标', example: '/goal pause'},
  {command: '/goal resume', summary: '恢复当前目标', example: '/goal resume'},
  {command: '/goal done', summary: '将当前目标标记完成', example: '/goal done'},
  {command: '/goal clear', summary: '清除当前目标', example: '/goal clear'},
  {command: '/skill', summary: '创建技能：名称 | 描述 | 提示词', example: '/skill 代码审查 | 查风险 | 先 diff 再列问题'},
  {command: '/skill list', summary: '列出技能', example: '/skill list'},
  {command: '/skill run', summary: '按技能跑一轮任务', example: '/skill run 代码审查 检查本次改动'},
  {command: '/loop', summary: '创建循环：间隔 + 提示词', example: '/loop 30m 巡检未完成任务并提醒我'},
  {command: '/loop list', summary: '列出循环/定时', example: '/loop list'},
  {command: '/loop delete', summary: '停止一个循环', example: '/loop delete <taskId>'},
  {command: '/plan', summary: '为当前会话写入待办计划（todo 清单）', example: '/plan 1. 查代码 2. 改接口 3. 写测试'},
  {command: '/plan add', summary: '向当前会话计划追加步骤', example: '/plan add 运行验证'},
  {command: '/plan status', summary: '查看当前会话计划', example: '/plan status'},
  {command: '/plan clear', summary: '清空当前会话计划', example: '/plan clear'},
  {command: '/task', summary: '创建桌面执行任务', example: '/task 打开记事本写今天待办'},
  {command: '/execute', summary: '创建桌面执行任务（/task 别名）', example: '/execute 打开浏览器搜索天气'},
  {command: '/remind', summary: '定点提醒 HH:MM 文案', example: '/remind 21:30 该休息了'},
  {command: '/reminder', summary: '定点提醒（/remind 别名）', example: '/reminder 09:00 站会'},
  {command: '/proactive', summary: 'pause|resume|trigger 主动消息', example: '/proactive trigger 关心一下用户'},
  {command: '/workflow', summary: 'list 或 run <id> <目标>', example: '/workflow run review-and-test 检查 PR'},
  {command: '/workflow list', summary: '列出工作流', example: '/workflow list'},
  {command: '/wf', summary: '工作流快捷别名', example: '/wf run safe-refactor 重构设置页'},
  {command: '/help', summary: '显示斜杠指令帮助', example: '/help'},
];

export function parseSlashCommand(text: string): SlashCommand | null {
  const input = text.trim();
  if (!input.startsWith('/')) return null;
  const body = input.slice(1).trim();
  if (!body) return {kind: 'help'};
  const [head, ...restParts] = body.split(/\s+/);
  const cmd = (head || '').toLowerCase();
  const rest = restParts.join(' ').trim();
  const fullRest = body.slice(head.length).trim();

  if (cmd === 'help' || cmd === '?') return {kind: 'help'};

  if (cmd === 'goal') {
    const sub = (restParts[0] || '').toLowerCase();
    if (!rest || sub === 'status') return {kind: 'goal', action: rest && sub !== 'status' ? 'set' : 'status', text: sub === 'status' ? undefined : rest || undefined};
    if (sub === 'pause') return {kind: 'goal', action: 'pause'};
    if (sub === 'resume') return {kind: 'goal', action: 'resume'};
    if (sub === 'done' || sub === 'complete') return {kind: 'goal', action: 'complete'};
    if (sub === 'clear' || sub === 'cancel') return {kind: 'goal', action: 'clear'};
    return {kind: 'goal', action: 'set', text: fullRest};
  }

  if (cmd === 'skill') {
    const sub = (restParts[0] || '').toLowerCase();
    if (!rest || sub === 'help') return {kind: 'skill', action: 'help'};
    if (sub === 'list' || sub === 'ls') return {kind: 'skill', action: 'list'};
    if (sub === 'run') {
      const runBody = restParts.slice(1).join(' ').trim();
      const [name, ...more] = runBody.split(/\s+/);
      return {kind: 'skill', action: 'run', name: name || undefined, body: more.join(' ').trim() || undefined};
    }
    // /skill Name | description | prompt
    const parts = fullRest.split('|').map((p) => p.trim()).filter(Boolean);
    if (parts.length >= 2) {
      return {
        kind: 'skill',
        action: 'create',
        name: parts[0],
        body: parts.length >= 3 ? `${parts[1]}\n\n${parts.slice(2).join(' | ')}` : parts[1],
      };
    }
    return {kind: 'skill', action: 'create', name: fullRest.slice(0, 40), body: fullRest};
  }

  if (cmd === 'loop') {
    const sub = (restParts[0] || '').toLowerCase();
    if (!rest || sub === 'help') return {kind: 'loop', action: 'help'};
    if (sub === 'list' || sub === 'ls') return {kind: 'loop', action: 'list'};
    if (sub === 'delete' || sub === 'rm' || sub === 'cancel') {
      return {kind: 'loop', action: 'delete', id: restParts[1]};
    }
    // /loop 30m prompt...  or /loop every 30m prompt
    const intervalMatch = fullRest.match(/^(?:every\s+)?(\d+)\s*([mhd]|min|mins|minute|minutes|h|hr|hour|hours|d|day|days)\b\s*(.*)$/i)
      || fullRest.match(/^(\d+[mhd])\b\s*(.*)$/i);
    if (intervalMatch) {
      const n = intervalMatch[1];
      const unit = (intervalMatch[2] || 'm').toLowerCase();
      const prompt = (intervalMatch[3] || '').trim();
      let interval = `${n}m`;
      if (/^\d+$/.test(n)) {
        if (unit.startsWith('h')) interval = `${n}h`;
        else if (unit.startsWith('d')) interval = `${n}d`;
        else interval = `${n}m`;
      } else {
        interval = n.toLowerCase();
      }
      return {kind: 'loop', action: 'create', interval, prompt: prompt || fullRest};
    }
    return {kind: 'loop', action: 'create', interval: '60m', prompt: fullRest};
  }

  if (cmd === 'plan' || cmd === 'view-plan' || cmd === 'show-plan' || cmd === 'plan-view') {
    if (cmd !== 'plan' || !fullRest || restParts[0]?.toLowerCase() === 'status' || restParts[0]?.toLowerCase() === 'view') {
      return {kind: 'plan', action: 'status'};
    }
    const sub = (restParts[0] || '').toLowerCase();
    if (sub === 'clear' || sub === 'reset' || sub === 'cancel') return {kind: 'plan', action: 'clear'};
    if (sub === 'add' || sub === 'append') return {kind: 'plan', action: 'append', text: restParts.slice(1).join(' ').trim()};
    return {kind: 'plan', action: 'set', text: fullRest};
  }

  if (cmd === 'task' || cmd === 'execute' || cmd === '\u6267\u884c') {
    if (!fullRest) return {kind: 'unknown', raw: input};
    return {kind: 'task', text: fullRest};
  }

  if (cmd === 'remind' || cmd === 'reminder' || cmd === '\u63d0\u9192') {
    const m = fullRest.match(/^([01]?\d|2[0-3])[:：]([0-5]\d)\s+(.+)$/);
    if (m) return {kind: 'remind', time: `${m[1].padStart(2, '0')}:${m[2]}`, message: m[3].trim()};
    return {kind: 'remind', message: fullRest};
  }

  if (cmd === 'proactive') {
    const sub = (restParts[0] || '').toLowerCase();
    if (!rest || sub === 'help') return {kind: 'proactive', action: 'help'};
    if (sub === 'pause') return {kind: 'proactive', action: 'pause'};
    if (sub === 'resume') return {kind: 'proactive', action: 'resume'};
    if (sub === 'trigger' || sub === 'ping') {
      return {kind: 'proactive', action: 'trigger', reason: restParts.slice(1).join(' ').trim() || '用户手动触发'};
    }
    return {kind: 'proactive', action: 'trigger', reason: fullRest};
  }

  if (cmd === 'workflow' || cmd === 'wf') {
    const sub = (restParts[0] || '').toLowerCase();
    if (!rest || sub === 'help') return {kind: 'workflow', action: 'help'};
    if (sub === 'list' || sub === 'ls') return {kind: 'workflow', action: 'list'};
    if (sub === 'run') {
      const id = restParts[1];
      const input = restParts.slice(2).join(' ').trim();
      return {kind: 'workflow', action: 'run', id, input: input || undefined};
    }
    // /workflow <id> <input>
    return {kind: 'workflow', action: 'run', id: restParts[0], input: restParts.slice(1).join(' ').trim() || undefined};
  }

  // /skill-name shorthand → run skill by name (Grok Build style /skill-name)
  if (cmd && !['help'].includes(cmd)) {
    return {kind: 'skill', action: 'run', name: head, body: rest || undefined};
  }

  return {kind: 'unknown', raw: input};
}

export function formatSlashHelp(): string {
  const lines = [
    '### Pattern 斜杠指令（参考 Grok Build）',
    '',
    '可在输入框用 `/` 唤起；也支持自然语言（如「帮我创建一个每天 21:30 的提醒」）。',
    '',
    '| 指令 | 说明 | 示例 |',
    '| --- | --- | --- |',
    ...SLASH_CATALOG.map((item) => `| \`${item.command}\` | ${item.summary} | \`${item.example}\` |`),
    '',
    '### @ 引用',
    '',
    '- `@skill:代码审查` / `@skill:code-review`',
    '- `@workflow:review-and-test`',
    '- `@task:打开记事本`（按标题模糊匹配最近任务）',
    '- `@project:本项目`（当前项目工作区）',
    '',
    '自然语言创建 skill / 任务 / workflow / 提醒 / 主动 / 目标 / 会话计划时，主 Agent 会调用 `pattern.*` 工具落盘（与斜杠指令等价）。',
    '',
    '### 自然语言 → 工具',
    '',
    '| 意图 | pattern 工具 |',
    '| --- | --- |',
    '| 设定/暂停/完成目标 | `create_goal` / `control_goal` / `update_goal` |',
    '| 会话待办计划 | `create_plan` / `update_plan` / `get_plan` / `clear_plan` |',
    '| 技能 | `create_skill` / `list_skills` / `run_skill` |',
    '| 循环 | `create_loop` / `list_loops` / `delete_loop` |',
    '| 任务 | `create_task` / `list_tasks` |',
    '| 每日提醒 | `create_reminder` / `list_reminders` / `delete_reminder` |',
    '| 主动消息 | `trigger_proactive` |',
    '| 工作流 | `create_workflow` / `list_workflows` / `run_workflow` |',
  ];
  return lines.join('\n');
}

export interface AtMention {
  raw: string;
  type: 'skill' | 'workflow' | 'task' | 'project' | 'memory' | 'unknown';
  id: string;
}

/** Parse @skill:name @workflow:id style mentions from free text. */
export function parseAtMentions(text: string): AtMention[] {
  const out: AtMention[] = [];
  const re = /@([a-zA-Z\u4e00-\u9fff][\w\u4e00-\u9fff-]*)(?::([^\s@]+))?/g;
  let m: RegExpExecArray | null;
  while ((m = re.exec(text)) !== null) {
    const head = m[1].toLowerCase();
    const id = (m[2] || m[1]).trim();
    let type: AtMention['type'] = 'unknown';
    if (['skill', 'skills', '\u6280\u80fd'].includes(head) || m[2]) {
      if (['skill', 'skills', '\u6280\u80fd'].includes(head)) type = 'skill';
      else if (['workflow', 'wf', '\u5de5\u4f5c\u6d41'].includes(head)) type = 'workflow';
      else if (['task', '\u4efb\u52a1'].includes(head)) type = 'task';
      else if (['project', 'proj', '\u9879\u76ee'].includes(head)) type = 'project';
      else if (['memory', 'mem', '\u8bb0\u5fc6'].includes(head)) type = 'memory';
      else {
        // bare @name → treat as skill shorthand
        type = 'skill';
      }
    } else {
      type = 'skill';
    }
    out.push({raw: m[0], type, id});
  }
  return out;
}

/** Interval like 30m / 2h / 1d → minutes */
export function intervalToMinutes(interval: string): number {
  const m = String(interval || '').trim().match(/^(\d+)\s*([mhd])?/i);
  if (!m) return 60;
  const n = Math.max(1, Number(m[1]) || 60);
  const u = (m[2] || 'm').toLowerCase();
  if (u === 'h') return n * 60;
  if (u === 'd') return n * 60 * 24;
  return n;
}

/**
 * Models often copy catalog labels like desktop:launch / desktop.launch into the tool field.
 * Shared by the sidecar companion tool loop so unit tests can drive the real normalizer.
 */
export function normalizeCompanionToolName(tool: string): string {
  return String(tool || '')
    .trim()
    .replace(/^desktop[.:]/i, '')
    .replace(/^os\s*bridge[.:]/i, '')
    .trim()
    .toLowerCase();
}

export interface AgentCore {
  reply(input: {slot: AgentSlot; text: string; sessionId?: string}): AsyncIterable<string>;
}

export type RiskTier = 0 | 1 | 2 | 3;
export interface SafetyDecision { tier: RiskTier; blocked: boolean; requiresApproval: boolean; reason: string; }

/** Central guard shared by every transport and future plugin tool. */
export function assessSafety(input: string, kind: 'task' | 'action' = 'task'): SafetyDecision {
  const text = input.toLowerCase();
  if (/(password manager|1password|bitwarden|keepass|\u5bc6\u7801\u7ba1\u7406|banking|\u94f6\u884c|\u94f6\u884c\u5361)/i.test(text)) {
    return {tier: 3, blocked: true, requiresApproval: false, reason: 'T3 sensitive application or credential surface'};
  }
  if (/(delete|remove|format|rm\s+-rf|pay|transfer|send|submit|publish|upload|install|uninstall|\u5220\u9664|\u6e05\u7a7a|\u652f\u4ed8|\u8f6c\u8d26|\u53d1\u9001|\u63d0\u4ea4|\u4e0a\u4f20|\u5b89\u88c5|\u5378\u8f7d)/i.test(text)) {
    return {tier: 2, blocked: false, requiresApproval: true, reason: 'T2 external, destructive, or consequential action'};
  }
  if (kind === 'action' && /(click|type|key|scroll|uia)/i.test(text)) {
    return {tier: 1, blocked: false, requiresApproval: false, reason: 'T1 reversible desktop action'};
  }
  return {
    tier: kind === 'task' ? 1 : 0,
    blocked: false,
    requiresApproval: false,
    reason: kind === 'task' ? 'T1 task needs desktop interaction' : 'T0 read-only action',
  };
}
