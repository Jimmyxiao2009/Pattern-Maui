import {readFileSync, writeFileSync} from 'node:fs';

const path = 'sidecar/src/index.ts';
let t = readFileSync(path, 'utf8');

const heStart = t.indexOf('function heuristicExtract');
const heEnd = t.indexOf('async function extractMemories');
if (heStart < 0 || heEnd < 0) throw new Error('heuristic markers');

const heuristic = [
  'function heuristicExtract(userText: string): Array<{text: string; category: string; importance: number}> {',
  '  const text = userText.trim();',
  '  if (!text) return [];',
  "  const rules: Array<{re: RegExp; category: string; importance: number; toText?: (m: RegExpMatchArray) => string}> = [",
  "    {re: /\\u6211(?:\\u517b\\u4e86|\\u6709)(.+?)(?:[\\u3002\\uff01\\uff1f.!?]|$)/, category: 'fact', importance: 0.8, toText: (m) => `\\u7528\\u6237\\u517b\\u4e86/\\u6709${m[1].trim()}`},",
  "    {re: /\\u6211\\u4f4f\\u5728(.+?)(?:[\\u3002\\uff01\\uff1f.!?]|$)/, category: 'fact', importance: 0.8, toText: (m) => `\\u7528\\u6237\\u4f4f\\u5728${m[1].trim()}`},",
  "    {re: /\\u6211(?:\\u559c\\u6b22|\\u7231)(.+?)(?:[\\u3002\\uff01\\uff1f.!?]|$)/, category: 'preference', importance: 0.7, toText: (m) => `\\u7528\\u6237\\u559c\\u6b22${m[1].trim()}`},",
  "    {re: /\\u6211(?:\\u4e0d\\u559c\\u6b22|\\u8ba8\\u538c)(.+?)(?:[\\u3002\\uff01\\uff1f.!?]|$)/, category: 'preference', importance: 0.7, toText: (m) => `\\u7528\\u6237\\u4e0d\\u559c\\u6b22${m[1].trim()}`},",
  "    {re: /\\u6211\\u53eb(.+?)(?:[\\u3002\\uff01\\uff1f.!?]|$)/, category: 'fact', importance: 0.9, toText: (m) => `\\u7528\\u6237\\u540d\\u5b57\\u662f${m[1].trim()}`},",
  '  ];',
  '  const out: Array<{text: string; category: string; importance: number}> = [];',
  '  for (const rule of rules) {',
  '    const m = text.match(rule.re);',
  '    if (m) out.push({text: rule.toText ? rule.toText(m) : m[0], category: rule.category, importance: rule.importance});',
  '  }',
  '  return out;',
  '}',
  '',
  '',
].join('\n');
t = t.slice(0, heStart) + heuristic + t.slice(heEnd);

const spStart = t.indexOf('function buildSystemPrompt');
const spEnd = t.indexOf('async function callEmbedding');
if (spStart < 0 || spEnd < 0) throw new Error('system prompt markers');
const systemPrompt = [
  'function buildSystemPrompt(memHits: MemoryRecord[]) {',
  "  const persona = config?.persona || 'You are Pattern, a desktop companion defined by the user.';",
  "  const name = config?.personaName || 'Pattern';",
  "  const user = config?.userName || 'User';",
  '  const index = memory.buildIndex();',
  '  const details = memHits.length',
  "    ? memHits.map((m) => `- (${categoryLabel(m.category)}, imp=${m.importance.toFixed(2)}) ${m.text}`).join('\\n')",
  "    : '(no extra retrieval hits this turn)';",
  '  const now = new Date();',
  "  const env = `Local time: ${now.toLocaleString('zh-CN')}. You are a resident desktop companion, not a website chatbot.`;",
  '  return `${persona}',
  '',
  '[Identity]',
  '- Your name: ${name}',
  '- User address: ${user}',
  '',
  '[MEMORY-INDEX | always know what you remember]',
  '${index}',
  '',
  '[Retrieved memory details]',
  '${details}',
  '',
  '[Environment]',
  '${env}',
  '',
  '[Rules]',
  '- Use memories naturally; do not recite entry ids.',
  "- If a memory conflicts with the user's latest statement, prefer the latest statement.",
  '- Never claim computer-use success without tool receipts.`;',
  '}',
  '',
  '',
].join('\n');
t = t.slice(0, spStart) + systemPrompt + t.slice(spEnd);

t = t.replace(
  /function classifyTaskTier\([\s\S]*?\n\}/,
  [
    'function classifyTaskTier(title: string, detail: string): number {',
    '  const text = `${title}\\n${detail}`.toLowerCase();',
    "  if (/(\\u5220\\u9664|\\u6e05\\u7a7a|format|rm\\s+-rf|\\u652f\\u4ed8|\\u8f6c\\u8d26|\\u5bc6\\u7801|\\u94f6\\u884c)/i.test(text)) return 2;",
    "  if (/(\\u53d1\\u9001|\\u90ae\\u4ef6|\\u6d88\\u606f|\\u4e0a\\u4f20|\\u5b89\\u88c5|\\u5378\\u8f7d)/i.test(text)) return 2;",
    "  if (/(\\u6574\\u7406|\\u79fb\\u52a8|\\u91cd\\u547d\\u540d|\\u70b9\\u51fb|\\u6253\\u5f00)/i.test(text)) return 1;",
    '  return 1;',
    '}',
  ].join('\n'),
);

const planStart = t.indexOf('const hasBridge = bridgeReady();');
const planEnd = t.indexOf('try {\n    for (const stepPlan of plan)');
if (planStart > 0 && planEnd > planStart) {
  const plan = [
    'const hasBridge = bridgeReady();',
    '  const plan: Array<{action: string; detail: string; tier: number; exec?: () => Promise<void>}> = [',
    '    {',
    "      action: 'screenshot',",
    "      detail: hasBridge ? 'Capture current screen' : 'OS Bridge offline; simulated screenshot',",
    '      tier: 0,',
    '      exec: async () => {',
    "        await bridgeCall('/screenshot', {}, true);",
    '      },',
    '    },',
    '    {',
    "      action: 'inspect',",
    '      detail: `Analyze task: ${task.title}`,',
    '      tier: 0,',
    '    },',
    '    {',
    "      action: tier >= 2 ? 'dangerous_action' : 'prepare',",
    '      detail: tier >= 2',
    '        ? `High-risk step needs approval: ${task.detail || task.title}`',
    '        : hasBridge',
    '          ? `Prepare: ${task.detail || task.title}`',
    '          : `No bridge; record plan only: ${task.detail || task.title}`,',
    '      tier,',
    '      exec: tier >= 2',
    '        ? undefined',
    '        : async () => {',
    "            await bridgeCall('/input', {type: 'move', x: 10, y: 10, relative: true}, true);",
    '          },',
    '    },',
    '    {',
    "      action: 'verify',",
    "      detail: hasBridge ? 'Verify with another screenshot' : 'No bridge; record verify step',",
    '      tier: 0,',
    '      exec: async () => {',
    "        await bridgeCall('/screenshot', {}, true);",
    '      },',
    '    },',
    '  ];',
    '',
    '  ',
  ].join('\n');
  t = t.slice(0, planStart) + plan + t.slice(planEnd);
}

t = t.replace(/if \(!config\) throw new Error\('[^']*'\);/g, "if (!config) throw new Error('runtime is not configured');");
t = t.replace(
  /send\(socket, \{type: 'chat\.error', id: message\.id, message: '[^']*'\}\);/g,
  "send(socket, {type: 'chat.error', id: message.id, message: 'runtime is not configured'});",
);
t = t.replace(/if \(!response\.body\) throw new Error\('[^']*'\);/g, "if (!response.body) throw new Error('model API returned empty body');");
t = t.replace(
  /if \(!response\.ok\) throw new Error\(`[^`]+`\);/g,
  'if (!response.ok) throw new Error(`model API status ${response.status}: ${(await response.text()).slice(0, 300)}`);',
);
t = t.replace(/task\.error = '[^']*';/g, "task.error = 'rejected or timed out';");
t = t.replace(
  /await notify\(config\?\.personaName \|\| 'Pattern', `[^`]*\$\{task\.title\}`\);/g,
  "await notify(config?.personaName || 'Pattern', `Task done: ${task.title}`);",
);
t = t.replace(/const reason = message\.reason \|\| '[^']*';/g, "const reason = message.reason || 'manual proactive trigger';");
t = t.replace(
  /if \(!item\) \{\r?\n\s*send\(socket, \{type: 'error', id: message\.id, message: '[^']*'\}\);/g,
  "if (!item) {\n          send(socket, {type: 'error', id: message.id, message: 'proactive is paused or disabled'});",
);
t = t.replace(
  /send\(socket, \{type: 'error', id: \(message as any\)\.id \|\| 'unknown', message: '[^']*'\}\);/g,
  "send(socket, {type: 'error', id: (message as any).id || 'unknown', message: 'unknown message type'});",
);
t = t.replace(/throw new Error\('[^']*sidecar[^']*'\)/g, "throw new Error('cannot read sidecar port')");
t = t.replace(/reason: `[^`]*\$\{env\.from\}`/g, 'reason: `from device ${env.from}`');
t = t.replace(/throw new Error\('OS Bridge[^']*'\);/g, "throw new Error('OS Bridge not ready');");
t = t.replace(
  /impulse\.type === 'bedtime'\s*\r?\n\s*\? '[^']*'\s*\r?\n\s*: impulse\.reason;/,
  "impulse.type === 'bedtime'\n      ? 'It is late. Save your work and get some rest.'\n      : impulse.reason;",
);
t = t.replace(
  /text: `[^`]*\$\{impulse\.reason\}[^`]*`,/,
  'text: `A proactive trigger fired. Reason: ${impulse.reason}. Reply in-character in 1-3 short sentences. Do not mention the trigger system.`,',
);
// task not found specifically near task.control
t = t.replace(
  /if \(!task\) \{\r?\n\s*send\(socket, \{type: 'error', id: message\.id, message: '[^']*'\}\);/g,
  "if (!task) {\n          send(socket, {type: 'error', id: message.id, message: 'task not found'});",
);

writeFileSync(path, t, 'utf8');
const out = readFileSync(path, 'utf8');
console.log('heuristic', out.includes('\\u6211(?:\\u517b\\u4e86'));
console.log('memory-index', out.includes('MEMORY-INDEX'));
console.log('delete tier', out.includes('\\u5220\\u9664'));
console.log('runtime msg', out.includes('runtime is not configured'));
// ensure no replacement chars
console.log('fffd', out.includes('\uFFFD'));
