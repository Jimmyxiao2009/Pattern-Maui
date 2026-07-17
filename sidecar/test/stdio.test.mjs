import test from 'node:test';
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createInterface} from 'node:readline';
import {mkdtempSync, rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';

test('native stdio transport starts, handshakes, and answers a ping', async (t) => {
  const dataDir = mkdtempSync(join(tmpdir(), 'pattern-sidecar-stdio-'));
  const child = spawn(process.execPath, ['dist/index.cjs', '--stdio'], {
    cwd: new URL('..', import.meta.url),
    stdio: ['pipe', 'pipe', 'pipe'],
    env: {...process.env, PATTERN_DATA_DIR: dataDir},
  });
  const lines = createInterface({input: child.stdout});
  const messages = [];
  const nextMessage = () => new Promise((resolve, reject) => {
    const onLine = (line) => {
      try { resolve(JSON.parse(line)); }
      catch (error) { reject(error); }
    };
    lines.once('line', onLine);
  });
  t.after(async () => {
    lines.close();
    if (!child.killed) child.kill();
    await new Promise((resolve) => child.once('exit', resolve));
    try { rmSync(dataDir, {recursive: true, force: true}); } catch { /* sqlite may still release a handle */ }
  });

  const startup = await nextMessage();
  assert.equal(startup.transport, 'stdio');
  child.stdin.write(`${JSON.stringify({type: 'runtime.ping', id: 'stdio-test'})}\n`);
  while (messages.length === 0 || messages.at(-1)?.type !== 'runtime.status') messages.push(await nextMessage());
  assert.equal(messages.at(-1).id, 'stdio-test');
  assert.equal(messages.at(-1).sidecar, 'connected');

  child.stdin.write(`${JSON.stringify({type: 'memory.list', id: 'memory-test', limit: 5})}\n`);
  while (messages.length === 0 || messages.at(-1)?.id !== 'memory-test') messages.push(await nextMessage());
  assert.equal(messages.at(-1).type, 'memory.list.result');
  assert.ok(Array.isArray(messages.at(-1).items));

  child.stdin.write(`${JSON.stringify({type: 'channel.plugins.list', id: 'plugin-test'})}\n`);
  while (messages.length === 0 || messages.at(-1)?.id !== 'plugin-test') messages.push(await nextMessage());
  assert.equal(messages.at(-1).type, 'channel.plugins');
  assert.ok(Array.isArray(messages.at(-1).plugins));
});
