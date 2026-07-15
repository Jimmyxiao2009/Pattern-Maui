from pathlib import Path
import re

path = Path("sidecar/src/index.ts")
st = path.read_text(encoding="utf-8")

helpers = """
type SecurityPolicyState = {
  workspaceRoot: string | null;
  enforceWorkspace: boolean;
  autoApproveBelow: number;
  hardDenyAt: number;
  tierGuide: Array<{tier: number; label: string; meaning: string}>;
};
const DEFAULT_SECURITY_POLICY: SecurityPolicyState = {
  workspaceRoot: null,
  enforceWorkspace: true,
  autoApproveBelow: 2,
  hardDenyAt: 3,
  tierGuide: [
    {tier: 0, label: 'T0 只读', meaning: '读取界面/文件，不改变系统状态'},
    {tier: 1, label: 'T1 低风险', meaning: '可逆的本地操作，通常自动放行'},
    {tier: 2, label: 'T2 需审批', meaning: '破坏性、外发、安装、提交等，必须人工确认'},
    {tier: 3, label: 'T3 禁区', meaning: '银行/密码管理器等，默认拒绝并冻结'},
  ],
};
function securityPolicyFile() { return join(dataDir, 'security-policy.json'); }
function loadSecurityPolicy(): SecurityPolicyState {
  try {
    if (!existsSync(securityPolicyFile())) return {...DEFAULT_SECURITY_POLICY, tierGuide: [...DEFAULT_SECURITY_POLICY.tierGuide]};
    const raw = JSON.parse(readFileSync(securityPolicyFile(), 'utf8'));
    return {
      workspaceRoot: raw.workspaceRoot ? String(raw.workspaceRoot) : null,
      enforceWorkspace: raw.enforceWorkspace !== false,
      autoApproveBelow: Math.min(3, Math.max(0, Number(raw.autoApproveBelow ?? 2))),
      hardDenyAt: Math.min(3, Math.max(1, Number(raw.hardDenyAt ?? 3))),
      tierGuide: Array.isArray(raw.tierGuide) && raw.tierGuide.length ? raw.tierGuide : [...DEFAULT_SECURITY_POLICY.tierGuide],
    };
  } catch {
    return {...DEFAULT_SECURITY_POLICY, tierGuide: [...DEFAULT_SECURITY_POLICY.tierGuide]};
  }
}
function saveSecurityPolicy() { writeFileSync(securityPolicyFile(), JSON.stringify(securityPolicy, null, 2)); }
let securityPolicy = loadSecurityPolicy();
function normalizePathForBoundary(value: string) { return value.replace(/\\\\/g, '/').replace(/\\/+$/, '').toLowerCase(); }
function isPathInsideWorkspace(target: string, root: string) {
  const a = normalizePathForBoundary(target);
  const b = normalizePathForBoundary(root);
  return a === b || a.startsWith(b + '/');
}
function assertWorkspaceAllowed(targetPath?: string | null, action = 'access') {
  if (!securityPolicy.enforceWorkspace || !securityPolicy.workspaceRoot) return;
  if (!targetPath) return;
  if (!isPathInsideWorkspace(String(targetPath), securityPolicy.workspaceRoot)) {
    appendJournal({line: `DENIED ${action} outside workspace: ${targetPath} (root=${securityPolicy.workspaceRoot})`, tier: 2, kind: 'boundary', decision: 'denied'});
    throw new Error(`工作区隔离：拒绝访问项目外路径\\n目标：${targetPath}\\n允许根：${securityPolicy.workspaceRoot}`);
  }
}
function parseJournalLine(raw: any): {ts: number; line: string; tier?: number; kind?: string; taskId?: string; decision?: string} {
  if (typeof raw === 'string') {
    try { return parseJournalLine(JSON.parse(raw)); } catch { return {ts: 0, line: raw}; }
  }
  return {
    ts: Number(raw.ts || 0),
    line: String(raw.line || ''),
    tier: raw.tier == null ? undefined : Number(raw.tier),
    kind: raw.kind ? String(raw.kind) : undefined,
    taskId: raw.taskId ? String(raw.taskId) : undefined,
    decision: raw.decision ? String(raw.decision) : undefined,
  };
}
"""

if "function loadSecurityPolicy" not in st:
    st = st.replace(
        "function appendJournal(line: string)",
        helpers + "\nfunction appendJournal(entry: string | {line: string; tier?: number; kind?: string; taskId?: string; decision?: string})",
        1,
    )

# rewrite appendJournal
start = st.find("function appendJournal(")
end = st.find("function bridgeReady()", start)
if start < 0 or end < 0:
    raise SystemExit("appendJournal range missing")
st = (
    st[:start]
    + """function appendJournal(entry: string | {line: string; tier?: number; kind?: string; taskId?: string; decision?: string}) {
  try {
    const file = join(dataDir, 'journal', 'actions.jsonl');
    mkdirSync(join(dataDir, 'journal'), {recursive: true});
    const payload = typeof entry === 'string'
      ? {ts: Math.floor(Date.now()/1000), line: entry, decision: 'info'}
      : {ts: Math.floor(Date.now()/1000), line: entry.line, tier: entry.tier, kind: entry.kind, taskId: entry.taskId, decision: entry.decision || 'info'};
    writeFileSync(file, `${existsSync(file) ? readFileSync(file, 'utf8') : ''}${JSON.stringify(payload)}\\n`);
  } catch {
    /* ignore journal failures */
  }
}
"""
    + st[end:]
)

# rewrite listJournal
start = st.find("function listJournal(")
end = st.find("\nfunction ", start + 10)
if end < 0:
    end = st.find("\nasync function ", start + 10)
if start < 0 or end < 0:
    raise SystemExit(f"listJournal range missing {start} {end}")
st = (
    st[:start]
    + """function listJournal(limit = 80, query?: string | null) {
  try {
    const file = join(dataDir, 'journal', 'actions.jsonl');
    if (!existsSync(file)) return [] as Array<{ts: number; line: string; tier?: number; kind?: string; taskId?: string; decision?: string}>;
    const lines = readFileSync(file, 'utf8').split(/\\r?\\n/).filter(Boolean);
    let items = lines.map((raw) => {
      try { return parseJournalLine(JSON.parse(raw)); } catch { return parseJournalLine(raw); }
    });
    if (query && query.trim()) {
      const q = query.trim().toLowerCase();
      items = items.filter((item) =>
        item.line.toLowerCase().includes(q)
        || String(item.kind || '').toLowerCase().includes(q)
        || String(item.decision || '').toLowerCase().includes(q)
        || String(item.taskId || '').toLowerCase().includes(q)
        || (item.tier != null && `t${item.tier}` === q)
      );
    }
    return items.slice(-Math.max(1, limit)).reverse();
  } catch {
    return [];
  }
}
"""
    + st[end + 1 :]
)

# MCP boundary
if "mcp path boundary" not in st:
    m = re.search(r"async function callMcpTool\([^\)]*\)\s*\{", st)
    if not m:
        raise SystemExit("callMcpTool missing")
    st = (
        st[: m.end()]
        + """
  // mcp path boundary
  if (securityPolicy.enforceWorkspace && securityPolicy.workspaceRoot) {
    const args = (arguments[2] || {}) as any;
    for (const key of ['path', 'root', 'cwd', 'file', 'directory', 'target']) {
      if (typeof args?.[key] === 'string') assertWorkspaceAllowed(args[key], `mcp:${String(arguments[1] || 'tool')}`);
    }
  }
"""
        + st[m.end() :]
    )

# tier thresholds
st = st.replace(
    "if (tier >= 3) {\n        await bridgeCall('/freeze', {frozen:true}, true);\n        throw new Error(`T3 动作已拦截：${action.reason}`);\n      }",
    "if (tier >= securityPolicy.hardDenyAt) {\n        await bridgeCall('/freeze', {frozen:true}, true);\n        appendJournal({line: `${task.id} DENIED T${tier} ${action.type} ${action.reason}`, tier, kind: 'approval', taskId: task.id, decision: 'denied'});\n        throw new Error(`T${tier} 动作已拦截（硬拒绝阈值 T${securityPolicy.hardDenyAt}）：${action.reason}`);\n      }",
)
st = st.replace(
    "if (tier >= 2) {\n        step.status = 'awaiting_approval';\n        task.status = 'awaiting_approval';\n        setAgentState('approval');",
    "if (tier >= securityPolicy.autoApproveBelow) {\n        step.status = 'awaiting_approval';\n        task.status = 'awaiting_approval';\n        setAgentState('approval');\n        appendJournal({line: `${task.id} APPROVAL_REQUIRED T${tier} ${action.type} ${action.reason}`, tier, kind: 'approval', taskId: task.id, decision: 'info'});",
)

# handlers
old_journal = "case 'journal.list': {\n        send(socket, {type: 'journal.list.result', id: message.id, items: listJournal(message.limit ?? 80)});\n        break;\n      }"
new_journal = """case 'journal.list': {
        send(socket, {type: 'journal.list.result', id: message.id, items: listJournal(message.limit ?? 80, (message as any).query)});
        break;
      }
      case 'security.policy.get': {
        send(socket, {type: 'security.policy', id: message.id, policy: securityPolicy});
        break;
      }
      case 'security.policy.set': {
        const next = (message as any).policy || {};
        if ('workspaceRoot' in next) securityPolicy.workspaceRoot = next.workspaceRoot ? String(next.workspaceRoot) : null;
        if ('enforceWorkspace' in next) securityPolicy.enforceWorkspace = next.enforceWorkspace !== false;
        if ('autoApproveBelow' in next) securityPolicy.autoApproveBelow = Math.min(3, Math.max(0, Number(next.autoApproveBelow)));
        if ('hardDenyAt' in next) securityPolicy.hardDenyAt = Math.min(3, Math.max(1, Number(next.hardDenyAt)));
        saveSecurityPolicy();
        appendJournal({line: `security.policy updated enforce=${securityPolicy.enforceWorkspace} root=${securityPolicy.workspaceRoot || '-'} auto<${securityPolicy.autoApproveBelow} deny>=${securityPolicy.hardDenyAt}`, kind: 'policy', decision: 'info'});
        send(socket, {type: 'security.policy', id: message.id, policy: securityPolicy});
        break;
      }"""
if "case 'security.policy.get'" not in st:
    if old_journal not in st:
        raise SystemExit("journal.list case missing")
    st = st.replace(old_journal, new_journal, 1)

# workspace bind + event text
m = re.search(r"if \(message\.workspace\) \{[\s\S]*?const system = buildSystemPrompt", st)
if m:
    st = (
        st[: m.start()]
        + """if (message.workspace) {
      if (securityPolicy.enforceWorkspace) {
        securityPolicy.workspaceRoot = message.workspace;
        saveSecurityPolicy();
      }
      send(socket, {
        type: 'chat.event',
        id: message.id,
        event: {
          kind: 'workspace',
          text: ('工作区隔离已绑定 · ' + (message.projectName || '') + ' · ' + message.workspace).replace(/\\s+·/g, ' ·').trim(),
          ts: Date.now(),
        },
      });
    }
    const system = buildSystemPrompt"""
        + st[m.end() :]
    )

if "assertWorkspaceAllowed((message as any).root, 'workspace.diff')" not in st and "assertWorkspaceAllowed(message.root, 'workspace.diff')" not in st:
    st = st.replace("case 'workspace.diff': {", "case 'workspace.diff': {\n        assertWorkspaceAllowed((message as any).root, 'workspace.diff');", 1)
if "assertWorkspaceAllowed((message as any).root, 'workspace.worktree')" not in st and "assertWorkspaceAllowed(message.root, 'workspace.worktree')" not in st:
    st = st.replace("case 'workspace.worktree.create': {", "case 'workspace.worktree.create': {\n        assertWorkspaceAllowed((message as any).root, 'workspace.worktree');", 1)

path.write_text(st, encoding="utf-8")
st = path.read_text(encoding="utf-8")
checks = [
    "function loadSecurityPolicy",
    "function assertWorkspaceAllowed",
    "case 'security.policy.get'",
    "securityPolicy.hardDenyAt",
    "securityPolicy.autoApproveBelow",
    "listJournal(limit = 80, query",
    "工作区隔离已绑定",
]
for c in checks:
    print(c, c in st)
print("lines", len(st.splitlines()))
