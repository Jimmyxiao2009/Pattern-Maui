import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {spawn} from 'node:child_process';
import {createInterface} from 'node:readline';
import {mkdtempSync, rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {WebSocket} from 'ws';

test('streams an OpenAI-compatible response over the authenticated websocket', async (t) => {
  const mock = createServer((request, response) => {
    assert.equal(request.url, '/v1/chat/completions');
    response.writeHead(200, {'content-type': 'text/event-stream'});
    response.write('data: {"choices":[{"delta":{"content":"你"}}]}\n\n');
    response.write('data: {"choices":[{"delta":{"content":"好"}}]}\n\n');
    response.end('data: [DONE]\n\n');
  });
  await new Promise((resolve) => mock.listen(0, '127.0.0.1', resolve));
  t.after(() => mock.close());
  const address = mock.address();
  assert.ok(address && typeof address !== 'string');

  const tmp = mkdtempSync(join(tmpdir(), 'pattern-sidecar-'));
  const child = spawn(process.execPath, ['dist/index.cjs'], {
    cwd: new URL('..', import.meta.url),
    stdio: ['pipe', 'pipe', 'inherit'],
    env: {...process.env, PATTERN_DATA_DIR: tmp},
  });

  const lines = createInterface({input: child.stdout});
  const handshake = await new Promise((resolve) => lines.once('line', (line) => resolve(JSON.parse(line))));
  child.stdin.write(
    `${JSON.stringify({
      method: 'runtime.configure',
      params: {
        provider: 'OpenAI Compatible',
        endpoint: `http://127.0.0.1:${address.port}/v1`,
        model: 'test',
        apiKey: 'sk-test',
        persona: 'test',
      },
    })}\n`,
  );

  const socket = new WebSocket(`ws://127.0.0.1:${handshake.port}/ws?token=${encodeURIComponent(handshake.token)}`);
  await new Promise((resolve, reject) => {
    socket.once('open', resolve);
    socket.once('error', reject);
  });

  let output = '';
  const complete = new Promise((resolve, reject) =>
    socket.on('message', (raw) => {
      const message = JSON.parse(raw.toString());
      if (message.type === 'chat.delta') output += message.delta;
      if (message.type === 'chat.done') resolve(undefined);
      if (message.type === 'chat.error') reject(new Error(message.message));
    }),
  );
  socket.send(JSON.stringify({type: 'chat.send', id: 'test-request', text: '打招呼', history: []}));
  await complete;
  assert.equal(output, '你好');

  socket.close();
  child.kill();
  await new Promise((resolve) => setTimeout(resolve, 300));
  try {
    rmSync(tmp, {recursive: true, force: true});
  } catch {
    /* ignore Windows sqlite lock */
  }
});
