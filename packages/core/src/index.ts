/** Routing compatibility kept for the wire protocol; the UI exposes one primary agent. */
export type AgentSlot = 'companion' | 'executor';
export interface RouteDecision { slot: AgentSlot; confidence: number; reason: string; }

const EXECUTOR_ZH = /(\u5e2e\u6211|\u8bf7).{0,14}(\u6253\u5f00|\u6574\u7406|\u79fb\u52a8|\u91cd\u547d\u540d|\u5220\u9664|\u4e0b\u8f7d|\u4e0a\u4f20|\u53d1\u9001|\u8fd0\u884c|\u8bbe\u7f6e|\u5173\u95ed|\u70b9\u51fb|\u8f93\u5165|\u622a\u56fe)/;
const EXECUTOR_EN = /^(open|organize|move|rename|delete|download|upload|send|run|click|type)\b/i;
const EXECUTOR_HINT = /(\u6267\u884c\u4efb\u52a1|\u7535\u8111\u64cd\u4f5c|computer use|\btask\b)/i;

/** Fast local routing. A model can later override low-confidence decisions. */
export function routeUserMessage(text: string): RouteDecision {
  const input = text.trim();
  const normalized = input.toLowerCase();
  if (!input) return {slot: 'companion', confidence: 1, reason: 'empty'};
  if (/^\/(task|\u6267\u884c)\b/.test(normalized)) return {slot: 'executor', confidence: 1, reason: 'explicit command'};
  if (EXECUTOR_HINT.test(input)) return {slot: 'executor', confidence: 0.9, reason: 'task keyword'};
  if (EXECUTOR_ZH.test(input) || EXECUTOR_EN.test(normalized)) return {slot: 'executor', confidence: 0.88, reason: 'desktop action intent'};
  return {slot: 'companion', confidence: 0.55, reason: 'ambiguous conversation/default'};
}

/** Whether the primary agent should delegate this explicit action to a child agent. */
export function shouldTransferToExecutor(text: string, minConfidence = 0.8): boolean {
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
  if (/(password manager|1password|bitwarden|keepass|\u5bc6\u7801\u7ba1\u7406|banking|\u94f6\u884c|\u94f6\u884c卡)/i.test(text)) {
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
