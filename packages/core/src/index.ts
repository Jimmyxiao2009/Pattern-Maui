/** Routing compatibility kept for the wire protocol; the UI exposes one primary agent. */
export type AgentSlot = 'companion' | 'executor';
export interface RouteDecision { slot: AgentSlot; confidence: number; reason: string; }

// Keep Chinese patterns as unicode escapes so routing stays stable across editors/shells.
const EXECUTOR_ZH = /(帮我|请|能不能|可以|试试|给我).{0,24}(打开|整理|移动|重命名|删除|下载|上传|发送|运行|设置|关闭|点击|输入|截图|按|模拟|操作)/;
const EXECUTOR_EN = /\b(open|organize|move|rename|delete|download|upload|send|run|click|type|press|launch)\b/i;
const EXECUTOR_HINT = /(\u6267\u884c\u4efb\u52a1|\u7535\u8111\u64cd\u4f5c|computer use|\btask\b|\u7528\u8f85\u52a9\u529f\u80fd|\u901a\u8fc7\u8f85\u52a9\u529f\u80fd|\u7528\u65e0\u969c\u788d|accessibility\s*(to|and)?\s*(open|press|click)|\u6253\u5f00\u5f00\u59cb\u83dc\u5355|start menu|\bwin\s*\u952e\b|\bwindows\s*key\b)/i;
const EXECUTOR_ACTION = /(打开.{0,16}(菜单|应用|窗口|文件|设置|浏览器|app|软件|程序)|按.{0,8}(键|win|enter|tab|esc)|点击|输入|模拟.{0,8}(键盘|鼠标)|用.{0,8}(键盘|鼠标|UIA|辅助功能)|随机.{0,12}打开)/i;

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
  const cleaned = text.replace(/^\/(task|\u6267\u884c)\s*/i, '').trim();
  return (cleaned || text).slice(0, max);
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
