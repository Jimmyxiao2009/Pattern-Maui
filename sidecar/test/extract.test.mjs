import test from 'node:test';
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createInterface} from 'node:readline';
import {mkdtempSync, rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {createServer} from 'node:http';
import {WebSocket} from 'ws';

test('chat injects memory and heuristic extraction remembers pet fact', async () => {
  const dataDir = mkdtempSync(join(tmpdir(), 'pattern-extract-'));
  const mock = createServer((req, res) => {
    // always stream a short reply that mentions the pet if system has it
    let body = '';
    req.on('data', (c) => (body += c));
    req.on('end', () => {
      const hasCat = body.includes('黑猫') || body.includes('黄眼睛');
      const text = hasCat ? '我记得你的黑猫。' : '收到。';
      res.writeHead(200, {'content-type': 'text/event-stream'});
      res.write(`data: {"choices":[{"delta":{"content":"${text}"}}]}\n\n`);
      res.end('data: [DONE]\n\n');
    });
  });
  await new Promise((r) => mock.listen(0, '127.0.0.1', r));
  const port = mock.address().port;

  const child = spawn(process.execPath, ['dist/index.cjs'], {
    cwd: new URL('..', import.meta.url),
    stdio: ['pipe', 'pipe', 'inherit'],
    env: {...process.env, PATTERN_DATA_DIR: dataDir},
  });
  try {
    const handshake = await new Promise((resolve) =>
      createInterface({input: child.stdout}).once('line', (line) => resolve(JSON.parse(line))),
    );
    child.stdin.write(
      `${JSON.stringify({
        method: 'runtime.configure',
        params: {
          provider: 'OpenAI Compatible',
          endpoint: `http://127.0.0.1:${port}/v1`,
          model: 'test',
          apiKey: 'sk',
          persona: '测试',
        },
      })}\n`,
    );
    const ws = new WebSocket(`ws://127.0.0.1:${handshake.port}/ws?token=${encodeURIComponent(handshake.token)}`);
    await new Promise((res, rej) => {
      ws.once('open', res);
      ws.once('error', rej);
    });

    const waitDone = (id) =>
      new Promise((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error('chat timeout')), 8000);
        let out = '';
        ws.on('message', (raw) => {
          const msg = JSON.parse(raw.toString());
          if (msg.id !== id) return;
          if (msg.type === 'chat.delta') out += msg.delta;
          if (msg.type === 'chat.done') {
            clearTimeout(timer);
            resolve(out);
          }
          if (msg.type === 'chat.error') {
            clearTimeout(timer);
            reject(new Error(msg.message));
          }
        });
      });

    // first message stores memory via heuristic extract
    const id1 = 'c1';
    const p1 = waitDone(id1);
    let proposalIdPromise = new Promise((resolve) => {
      const listener = (raw) => {
        const msg = JSON.parse(raw.toString());
        if (msg.type === 'memory.proposed' && msg.items.length > 0) {
          ws.removeListener('message', listener);
          resolve(msg.items[0].id);
        }
      };
      ws.on('message', listener);
    });
    ws.send(JSON.stringify({type: 'chat.send', id: id1, text: '我养了一只黄眼睛的黑猫。', history: []}));
    await p1;

    // Accept the proposed memory
    const proposalId = await proposalIdPromise;
    await new Promise((resolve, reject) => {
      const id = 'accept1';
      const timer = setTimeout(() => reject(new Error('accept timeout')), 5000);
      const listener = (raw) => {
        const msg = JSON.parse(raw.toString());
        if (msg.id === id && msg.type === 'memory.propose.accept.result') {
          clearTimeout(timer);
          ws.removeListener('message', listener);
          resolve(msg.ok);
        }
      };
      ws.on('message', listener);
      ws.send(JSON.stringify({type: 'memory.propose.accept', id, proposalId}));
    });
    await new Promise((r) => setTimeout(r, 300));

    const list = await new Promise((resolve, reject) => {
      const id = 'list1';
      const timer = setTimeout(() => reject(new Error('list timeout')), 5000);
      ws.on('message', (raw) => {
        const msg = JSON.parse(raw.toString());
        if (msg.id === id && msg.type === 'memory.list.result') {
          clearTimeout(timer);
          resolve(msg.items);
        }
      });
      ws.send(JSON.stringify({type: 'memory.list', id, query: '猫'}));
    });
    assert.ok(list.some((item) => item.text.includes('猫')), JSON.stringify(list));

    // second chat should inject memory into system and model sees 黑猫
    const id2 = 'c2';
    const p2 = waitDone(id2);
    ws.send(JSON.stringify({type: 'chat.send', id: id2, text: '我养了什么？', history: []}));
    const reply = await p2;
    assert.match(reply, /黑猫/);

    ws.close();
  } finally {
    child.kill();
    mock.close();
    await new Promise((r) => setTimeout(r, 200));
    try {
      rmSync(dataDir, {recursive: true, force: true});
    } catch {
      /* ignore */
    }
  }
});
