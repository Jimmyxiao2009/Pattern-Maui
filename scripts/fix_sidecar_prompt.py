from pathlib import Path

path = Path(r'E:/Desktop/项目/CrossPlatform/Pattern/sidecar/src/index.ts')
text = path.read_text(encoding='utf-8')
start = text.find('function buildSystemPrompt')
end = text.find('function cleanupJournal()')
if start < 0 or end < 0:
    raise SystemExit(f'bounds missing {start=} {end=}')

fixed = r'''function buildSystemPrompt(memHits: MemoryRecord[], slot: 'companion' | 'executor' = 'companion') {
  const useExecutor = slot === 'executor' && config?.executorPersona?.description;
  const persona = useExecutor
    ? config!.executorPersona!.description
    : (config?.persona || 'You are Pattern, a desktop companion defined by the user.');
  const name = useExecutor
    ? (config!.executorPersona!.name || config?.personaName || 'Pattern')
    : (config?.personaName || 'Pattern');
  const user = useExecutor
    ? (config!.executorPersona!.userName || config?.userName || 'User')
    : (config?.userName || 'User');
  const index = memory.buildIndex();
  const details = memHits.length
    ? memHits.map((m) => `- (${categoryLabel(m.category)}, imp=${m.importance.toFixed(2)}) ${m.text}`).join('\n')
    : '(no extra retrieval hits this turn)';
  const now = new Date();
  const env = `Local time: ${now.toLocaleString('zh-CN')}. You are a resident desktop companion, not a website chatbot.`;
  const role = slot === 'executor'
    ? 'You are currently in the executor slot: prefer concrete desktop actions and tool use over chit-chat.'
    : 'You are currently in the companion slot: conversation, memory, and measured initiative.';
  return `${persona}

[Identity]
- Your name: ${name}
- User address: ${user}
- Active slot: ${slot}
- ${role}

[MEMORY-INDEX | always know what you remember]
${index}

[Retrieved memory details]
${details}

[Environment]
${env}

[Rules]
- Use memories naturally; do not recite entry ids.
- If a memory conflicts with the user's latest statement, prefer the latest statement.
- Never claim computer-use success without tool receipts.`;
}

function listJournal(limit = 80) {
  try {
    const file = join(dataDir, 'journal', 'actions.jsonl');
    if (!existsSync(file)) return [] as Array<{ts: number; line: string}>;
    const lines = readFileSync(file, 'utf8').split(/\r?\n/).filter(Boolean);
    const items: Array<{ts: number; line: string}> = [];
    for (const raw of lines.slice(-Math.max(1, limit))) {
      try {
        const parsed = JSON.parse(raw) as {ts?: number; line?: string};
        items.push({ts: Number(parsed.ts || 0), line: String(parsed.line || raw)});
      } catch {
        items.push({ts: 0, line: raw});
      }
    }
    return items.reverse();
  } catch {
    return [] as Array<{ts: number; line: string}>;
  }
}

'''

text = text[:start] + fixed + text[end:]
path.write_text(text, encoding='utf-8', newline='\n')
content = path.read_text(encoding='utf-8')
for i, line in enumerate(content.splitlines()[168:176], start=169):
    print(i, repr(line))
for i, line in enumerate(content.splitlines()[203:208], start=204):
    print(i, repr(line))
print('join_ok', ".join('\\n')" in content)
print('split_ok', 'split(/\\r?\\n/)' in content)
