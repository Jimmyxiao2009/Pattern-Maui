import test from 'node:test';
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createInterface} from 'node:readline';
import {mkdtempSync, rmSync, readFileSync, existsSync, readdirSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {WebSocket} from 'ws';
import {createRequire} from 'node:module';
import {fileURLToPath} from 'node:url';
import {buildSync} from 'esbuild';

async function startSidecar(dataDir) {
  const child = spawn(process.execPath, ['dist/index.cjs'], {
    cwd: new URL('..', import.meta.url),
    stdio: ['pipe', 'pipe', 'inherit'],
    env: {...process.env, PATTERN_DATA_DIR: dataDir},
  });
  const lines = createInterface({input: child.stdout});
  const handshake = await new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('sidecar handshake timeout')), 5000);
    lines.once('line', (line) => {
      clearTimeout(timer);
      resolve(JSON.parse(line));
    });
  });
  return {child, handshake};
}

function request(ws, message, timeoutMs = 8000) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`timeout ${message.type}`)), timeoutMs);
    const onMessage = (raw) => {
      const msg = JSON.parse(raw.toString());
      if (msg.id === message.id || (message.type === 'task.create' && msg.type === 'task.list.result' && msg.id === message.id)) {
        clearTimeout(timer);
        ws.off('message', onMessage);
        resolve(msg);
      }
      if (msg.type === 'error' && msg.id === message.id) {
        clearTimeout(timer);
        ws.off('message', onMessage);
        reject(new Error(msg.message));
      }
    };
    ws.on('message', onMessage);
    ws.send(JSON.stringify(message));
  });
}

test('memory/proactive/task/relay e2e over websocket', async () => {
  const dataDir = mkdtempSync(join(tmpdir(), 'pattern-e2e-'));
  const relayRoot = mkdtempSync(join(tmpdir(), 'pattern-relay-'));
  const {child, handshake} = await startSidecar(dataDir);
  try {
    child.stdin.write(
      `${JSON.stringify({
        method: 'runtime.configure',
        params: {
          provider: 'OpenAI Compatible',
          endpoint: 'http://127.0.0.1:9/v1',
          model: 'test',
          apiKey: 'sk-test',
          persona: '你是测试人格',
          personaName: '测试',
          proactive: {enabled: true, paused: false, bedtimeHour: 23},
          webdav: {url: `local:${relayRoot}`, username: '', password: ''},
        },
      })}\n`,
    );

    const ws = new WebSocket(`ws://127.0.0.1:${handshake.port}/ws?token=${encodeURIComponent(handshake.token)}`);
    await new Promise((resolve, reject) => {
      ws.once('open', resolve);
      ws.once('error', reject);
    });

    const added = await request(ws, {
      type: 'memory.add',
      id: 'm1',
      item: {text: '用户养了一只黄眼睛的黑猫', category: 'fact', importance: 0.9},
    });
    assert.equal(added.type, 'memory.add.result');

    const listed = await request(ws, {type: 'memory.list', id: 'm2', query: '黑猫'});
    assert.equal(listed.type, 'memory.list.result');
    assert.ok(listed.items.some((item) => item.text.includes('黑猫')));

    const stats = await request(ws, {type: 'memory.stats', id: 'm3'});
    assert.equal(stats.type, 'memory.stats.result');
    assert.ok(stats.count >= 1);

    const cons = await request(ws, {type: 'memory.consolidate', id: 'm4'});
    assert.equal(cons.type, 'memory.consolidate.result');

    const impulse = await request(ws, {
      type: 'proactive.trigger',
      id: 'p1',
      kind: 'manual',
      reason: '测试主动消息：该休息了',
    });
    assert.equal(impulse.type, 'proactive.list.result');
    assert.ok(impulse.items.length >= 1);

    const task = await request(ws, {
      type: 'task.create',
      id: 't1',
      title: '打开记事本检查状态',
      detail: '低风险只读任务',
    });
    assert.equal(task.type, 'task.list.result');
    assert.ok(task.tasks[0]);

    // Production safety: never simulate success when the native OS bridge is absent.
    let finalTask = task.tasks[0];
    for (let i = 0; i < 20; i++) {
      await new Promise((r) => setTimeout(r, 100));
      const listedTasks = await request(ws, {type: 'task.list', id: `tlist-${i}`});
      finalTask = listedTasks.tasks.find((item) => item.id === finalTask.id) || finalTask;
      if (finalTask.status === 'done' || finalTask.status === 'failed' || finalTask.status === 'awaiting_approval') break;
    }
    assert.equal(finalTask.status, 'failed', `task status=${finalTask.status} error=${finalTask.error}`);
    assert.match(finalTask.error, /OS Bridge/);

    const danger = await request(ws, {
      type: 'task.create',
      id: 't2',
      title: '删除临时文件夹',
      detail: '高风险测试',
    });
    let dangerTask = danger.tasks[0];
    for (let i = 0; i < 20; i++) {
      await new Promise((r) => setTimeout(r, 100));
      const listedTasks = await request(ws, {type: 'task.list', id: `td-${i}`});
      dangerTask = listedTasks.tasks.find((item) => item.id === dangerTask.id) || dangerTask;
      if (dangerTask.status === 'failed') break;
    }
    assert.equal(dangerTask.status, 'failed');

    const watchConfig = await request(ws, {type:'filewatch.setConfig', id:'fw1', config:{enabled:false,paths:[dataDir],extensions:['.md'],maxBytes:4096}});
    assert.equal(watchConfig.type, 'filewatch.config');
    assert.deepEqual(watchConfig.config.paths, [dataDir]);

    const relay = await request(ws, {type: 'relay.syncNow', id: 'r1'});
    assert.equal(relay.type, 'relay.status.result');
    assert.equal(relay.status.configured, true);
    assert.equal(relay.status.online, true);

    const mailbox = join(relayRoot, 'pattern', 'mailbox');
    assert.ok(existsSync(mailbox));
    assert.ok(readdirSync(mailbox).some((f) => f.endsWith('.json')));

    // encrypt roundtrip via package
    const entry = fileURLToPath(new URL('../../packages/relay/src/index.ts', import.meta.url));
    const outfile = join(dataDir, 'relay.cjs');
    buildSync({entryPoints: [entry], bundle: true, platform: 'node', format: 'cjs', target: 'node22', outfile});
    const require = createRequire(import.meta.url);
    const {decryptBody} = require(outfile);
    const device = JSON.parse(readFileSync(join(dataDir, 'device.json'), 'utf8'));
    const sample = JSON.parse(readFileSync(join(mailbox, readdirSync(mailbox)[0]), 'utf8'));
    assert.equal(sample.encrypted, true);
    const plain = decryptBody(sample.body, device.channelKey);
    assert.ok(typeof plain === 'string' && plain.length > 0);

    // Second device on same local relay root should list + pull the proactive envelope
    const {RelayClient} = require(outfile);
    const peerDir = mkdtempSync(join(tmpdir(), 'pattern-e2e-peer-'));
    try {
      const peer = new RelayClient(peerDir, {url: `local:${relayRoot}`, username: '', password: ''}, 'peer-device', device.channelKey);
      const peerList = await peer.listMailbox();
      assert.ok(peerList.length >= 1);
      const pulled = await peer.pullIncoming();
      assert.ok(pulled.length >= 1);
      const pulled2 = await peer.pullIncoming();
      assert.equal(pulled2.length, 0);
    } finally {
      try { rmSync(peerDir, {recursive: true, force: true}); } catch { /* ignore */ }
    }

    ws.close();
  } finally {
    child.kill();
    await new Promise((r) => setTimeout(r, 300));
    try {
      rmSync(dataDir, {recursive: true, force: true});
      rmSync(relayRoot, {recursive: true, force: true});
    } catch {
      /* windows locks */
    }
  }
});
