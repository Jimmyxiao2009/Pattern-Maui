/**
 * Drives real @pattern/core exports used by the desktop chat routing + tool loop.
 * No re-implementation of the unit under test.
 */
import test from 'node:test';
import assert from 'node:assert/strict';
import {
  formatSlashHelp,
  intervalToMinutes,
  normalizeCompanionToolName,
  parseAtMentions,
  parseSlashCommand,
  routeUserMessage,
  shouldTransferToExecutor,
} from '@pattern/core';

test('routeUserMessage: open calculator / multi-step UI → executor', () => {
  for (const text of ['打开计算器', '打开计算器并计算1+1', '随机打开一个app', 'open calculator', '用辅助功能打开开始菜单']) {
    const decision = routeUserMessage(text);
    assert.equal(decision.slot, 'executor', `expected executor for: ${text} (got ${decision.slot}/${decision.confidence})`);
    assert.ok(decision.confidence >= 0.85, `confidence too low for: ${text}`);
    assert.equal(shouldTransferToExecutor(text), true, `shouldTransfer for: ${text}`);
  }
});

test('routeUserMessage: chatty / explanatory → companion (not forced executor)', () => {
  for (const text of ['你好', '请解释一下什么是辅助功能', 'what is accessibility', '讲个笑话']) {
    const decision = routeUserMessage(text);
    assert.equal(decision.slot, 'companion', `expected companion for: ${text} (got ${decision.slot}/${decision.reason})`);
    assert.equal(shouldTransferToExecutor(text), false, `must not transfer: ${text}`);
  }
});

test('normalizeCompanionToolName: launch / desktop.launch / desktop:launch → launch', () => {
  const expected = 'launch';
  for (const raw of ['launch', 'desktop.launch', 'desktop:launch', 'Desktop:Launch', ' DESKTOP.launch ', 'os bridge:launch', 'OS Bridge.launch']) {
    assert.equal(normalizeCompanionToolName(raw), expected, `normalize(${JSON.stringify(raw)})`);
  }
  assert.equal(normalizeCompanionToolName('computer_use'), 'computer_use');
  assert.equal(normalizeCompanionToolName('desktop:computer_use'), 'computer_use');
  assert.equal(normalizeCompanionToolName('accessibility_tree'), 'accessibility_tree');
});

test('shouldTransferToExecutor threshold respects minConfidence', () => {
  // 0.88 path (desktop action intent with 帮我) still transfers at default 0.85
  assert.equal(shouldTransferToExecutor('帮我打开设置'), true);
  // explicit high bar rejects ambiguous companion
  assert.equal(shouldTransferToExecutor('你好', 0.99), false);
});

test('parseSlashCommand covers goal/skill/loop/plan/remind (Grok Build–style)', () => {
  assert.equal(parseSlashCommand('/help')?.kind, 'help');
  const st = parseSlashCommand('/goal status');
  assert.equal(st?.kind, 'goal');
  if (st?.kind === 'goal') assert.equal(st.action, 'status');
  const goal = parseSlashCommand('/goal 让所有测试通过');
  assert.equal(goal?.kind, 'goal');
  if (goal?.kind === 'goal') {
    assert.equal(goal.action, 'set');
    assert.match(goal.text || '', /测试/);
  }
  const skill = parseSlashCommand('/skill 代码审查 | 查风险 | 先看 diff');
  assert.equal(skill?.kind, 'skill');
  if (skill?.kind === 'skill') {
    assert.equal(skill.action, 'create');
    assert.equal(skill.name, '代码审查');
  }
  const loop = parseSlashCommand('/loop 30m 巡检未完成任务');
  assert.equal(loop?.kind, 'loop');
  if (loop?.kind === 'loop') {
    assert.equal(loop.action, 'create');
    assert.equal(loop.interval, '30m');
  }
  const plan = parseSlashCommand('/plan 重构记忆模块');
  assert.equal(plan?.kind, 'plan');
  const remind = parseSlashCommand('/remind 21:30 该休息了');
  assert.equal(remind?.kind, 'remind');
  if (remind?.kind === 'remind') assert.equal(remind.time, '21:30');
  assert.ok(formatSlashHelp().includes('/goal'));
  assert.equal(intervalToMinutes('2h'), 120);
});

test('parseAtMentions extracts skill/workflow refs', () => {
  const mentions = parseAtMentions('请用 @skill:代码审查 和 @workflow:review-and-test 处理');
  assert.ok(mentions.some((m) => m.type === 'skill' && m.id.includes('代码审查')));
  assert.ok(mentions.some((m) => m.type === 'workflow' && m.id === 'review-and-test'));
});
