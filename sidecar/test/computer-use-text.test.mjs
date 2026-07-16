import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {spawn} from 'node:child_process';
import {createInterface} from 'node:readline';
import {mkdtempSync, readFileSync, rmSync, writeFileSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {WebSocket} from 'ws';

function json(res, value) {
  res.writeHead(200, {'content-type': 'application/json'});
  res.end(JSON.stringify(value));
}

test('accessibility-only Computer Use serializes detached AgentOS transactions', async () => {
  const modelRequests = [];
  const modelCalls = new Map();
  const recoveryCalls = [];
  const transactionIds = new Map();
  let recoveryAvailable = true;
  let accessibilityActionCalls = 0;
  const server = createServer((req, res) => {
    let raw = '';
    req.on('data', (chunk) => (raw += chunk));
    req.on('end', () => {
      if (req.url === '/v1/chat/completions') {
        const modelRequest = JSON.parse(raw);
        modelRequests.push(modelRequest);
        const prompt = String(modelRequest.messages.at(-1).content);
        const taskKey = prompt.includes('无恢复') ? 'unprotected' : prompt.includes('第二个') ? 'second' : 'first';
        const call = (modelCalls.get(taskKey) || 0) + 1;
        modelCalls.set(taskKey, call);
        const content = call === 1
          ? '{"type":"uiaInvoke","ref":"uia-1","reason":"点击测试控件","tier":1}'
          : '{"type":"done","reason":"目标控件树已显示完成","tier":0}';
        return json(res, {choices: [{message: {content}}]});
      }
      if (req.url === '/foreground') return json(res, {title: '测试窗口'});
      if (req.url === '/screenshot') return json(res, {path: 'test.png', pngBase64: 'iVBORw0KGgo='});
      if (req.url === '/accessibility/tree') return json(res, {supported: true, controls: [{ref: 'uia-1', name: '完成', controlType: 'button', enabled: true}]});
      if (req.url === '/accessibility/action') { accessibilityActionCalls++; return json(res, {ok: true}); }
      if (req.url === '/recovery/capabilities') return json(res, {available: recoveryAvailable, platform: 'windows'});
      if (req.url?.startsWith('/recovery/')) {
        const operation = req.url.split('/').at(-1);
        recoveryCalls.push(operation);
        const body = raw ? JSON.parse(raw) : {};
        if (operation === 'begin' && !transactionIds.has(body.taskId)) {
          transactionIds.set(body.taskId, String(transactionIds.size + 1).padStart(32, '0'));
        }
        const transactionId = body.transactionId || transactionIds.get(body.taskId) || '';
        const state = {begin: 'Active', prepare: 'Prepared', commit: 'Committed', rollback: 'RolledBack'}[operation];
        return json(res, {ok: true, exitCode: 0, transaction: {id: transactionId, state}});
      }
      res.writeHead(404);
      res.end();
    });
  });
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  const port = server.address().port;
  const dir = mkdtempSync(join(tmpdir(), 'pattern-text-cu-'));
  const child = spawn(process.execPath, ['dist/index.cjs'], {
    cwd: new URL('..', import.meta.url),
    stdio: ['pipe', 'pipe', 'inherit'],
    env: {...process.env, PATTERN_DATA_DIR: dir},
  });
  try {
    const lines = createInterface({input: child.stdout});
    const handshake = await new Promise((resolve) => lines.once('line', (line) => resolve(JSON.parse(line))));
    child.stdin.write(`${JSON.stringify({method: 'runtime.configure', params: {
      provider: 'OpenAI Compatible', endpoint: `http://127.0.0.1:${port}/v1`, model: 'text-model', apiKey: 'test', persona: 'test',
      executor: {provider: 'OpenAI Compatible', endpoint: `http://127.0.0.1:${port}/v1`, model: 'text-model', apiKey: 'test', vision: false},
      bridgeUrl: `http://127.0.0.1:${port}`, bridgeToken: 'bridge-test',
    }})}\n`);
    const ws = new WebSocket(`ws://127.0.0.1:${handshake.port}/ws?token=${handshake.token}`);
    await new Promise((resolve, reject) => { ws.once('open', resolve); ws.once('error', reject); });
    ws.send(JSON.stringify({type: 'task.create', id: 'create-first', title: '点击第一个测试控件', detail: '执行一次可逆操作', workspace: dir}));
    ws.send(JSON.stringify({type: 'task.create', id: 'create-second', title: '点击第二个测试控件', detail: '执行一次可逆操作', workspace: dir}));
    let done = [];
    for (let i = 0; i < 80; i++) {
      await new Promise((resolve) => setTimeout(resolve, 100));
      done = await new Promise((resolve) => {
        const id = `list-${i}`;
        const listener = (rawMessage) => {
          const message = JSON.parse(rawMessage);
          if (message.id === id) { ws.off('message', listener); resolve(message.tasks); }
        };
        ws.on('message', listener);
        ws.send(JSON.stringify({type: 'task.list', id}));
      });
      const relevant = done.filter((task) => task.title.includes('测试控件'));
      if (relevant.length === 2 && relevant.every((task) => task.status === 'done' || task.status === 'failed')) break;
    }
    const relevant = done.filter((task) => task.title.includes('测试控件'));
    assert.equal(relevant.length, 2);
    for (const task of relevant) {
      assert.equal(task.status, 'done', task.error);
      assert.equal(task.recovery.state, 'committed');
      assert.match(task.recovery.transactionId, /^[0-9]{32}$/);
    }
    assert.notEqual(relevant[0].recovery.transactionId, relevant[1].recovery.transactionId);
    assert.deepEqual(recoveryCalls, ['list', 'begin', 'prepare', 'commit', 'gc', 'begin', 'prepare', 'commit', 'gc']);
    assert.equal(accessibilityActionCalls, 2);
    assert.equal(modelRequests.length, 4);
    for (const modelRequest of modelRequests) {
      const content = modelRequest.messages.at(-1).content;
      assert.equal(typeof content, 'string');
      assert.match(content, /accessibility-only mode/);
      assert.doesNotMatch(JSON.stringify(modelRequest), /image_url|pngBase64/);
    }
    recoveryAvailable = false;
    ws.send(JSON.stringify({type: 'task.create', id: 'create-unprotected', title: '无恢复保护写入', detail: '验证 fail-closed', workspace: dir}));
    let blocked;
    for (let i = 0; i < 40; i++) {
      await new Promise((resolve) => setTimeout(resolve, 50));
      const tasks = await new Promise((resolve) => {
        const id = `blocked-list-${i}`;
        const listener = (rawMessage) => {
          const message = JSON.parse(rawMessage);
          if (message.id === id) { ws.off('message', listener); resolve(message.tasks); }
        };
        ws.on('message', listener);
        ws.send(JSON.stringify({type: 'task.list', id}));
      });
      blocked = tasks.find((task) => task.title === '无恢复保护写入');
      if (blocked?.status === 'failed') break;
    }
    assert.equal(blocked.status, 'failed');
    assert.equal(blocked.recovery.state, 'unavailable');
    assert.match(blocked.error, /工作区写入已阻止/);
    assert.equal(accessibilityActionCalls, 2);
    assert.deepEqual(recoveryCalls, ['list', 'begin', 'prepare', 'commit', 'gc', 'begin', 'prepare', 'commit', 'gc']);
    ws.close();
  } finally {
    child.kill();
    server.close();
    await new Promise((resolve) => setTimeout(resolve, 200));
    rmSync(dir, {recursive: true, force: true});
  }
});

test('startup reconciliation, status, and manual recovery preserve transaction semantics', async () => {
  const taskId = 'restart-task';
  const committedTaskId = 'committed-task';
  const orphanTaskId = 'orphan-task';
  const runId = 'restart-run';
  const committedRunId = 'committed-run';
  const orphanRunId = 'orphan-run';
  const transactionId = 'fedcba9876543210fedcba9876543210';
  const committedTransactionId = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';
  const recoveryCalls = [];
  const server = createServer((req, res) => {
    let raw = '';
    req.on('data', (chunk) => (raw += chunk));
    req.on('end', () => {
      if (req.url === '/recovery/capabilities') return json(res, {available: true, platform: 'windows', store: 'C:\\recovery-store'});
      if (req.url === '/recovery/list') {
        recoveryCalls.push('list');
        return json(res, {ok: true, transaction: [
          {id: transactionId, command: `detached:${taskId}`, state: 'Active', createdAt: '2026-07-16T01:00:00Z', fileScopes: ['C:\\workspace']},
          {id: 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb', command: `detached:${committedTaskId}`, state: 'RolledBack', createdAt: '2026-07-15T01:00:00Z', fileScopes: ['C:\\committed-workspace']},
          {id: committedTransactionId, command: `detached:${committedTaskId}`, state: 'Committed', createdAt: '2026-07-16T01:00:00Z', fileScopes: ['C:\\committed-workspace']},
        ]});
      }
      if (req.url === '/recovery/recover' || req.url === '/recovery/rollback') {
        const operation = req.url.split('/').at(-1);
        recoveryCalls.push(operation);
        return json(res, {ok: true, transaction: {id: JSON.parse(raw).transactionId, state: 'RolledBack'}});
      }
      res.writeHead(404);
      res.end();
    });
  });
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  const port = server.address().port;
  const dir = mkdtempSync(join(tmpdir(), 'pattern-recovery-restart-'));
  writeFileSync(join(dir, 'tasks.json'), JSON.stringify([
    {
      id: taskId,
      title: '重启恢复测试',
      detail: '模拟执行中进程退出',
      status: 'running',
      createdAt: new Date().toISOString(),
      workspace: 'C:\\workspace',
      steps: [],
      runs: [{id: runId, startedAt: Date.now(), status: 'running'}],
      activeRunId: runId,
    },
    {
      id: committedTaskId,
      title: '已提交恢复测试',
      detail: '模拟用户从任务页恢复已完成任务',
      status: 'running',
      createdAt: new Date().toISOString(),
      workspace: 'C:\\committed-workspace',
      runs: [{id: committedRunId, startedAt: Date.now(), status: 'running'}],
      activeRunId: committedRunId,
    },
    {
      id: orphanTaskId,
      title: '无事务中断测试',
      detail: '模拟第一个写动作前退出',
      status: 'running',
      createdAt: new Date().toISOString(),
      runs: [{id: orphanRunId, startedAt: Date.now(), status: 'running'}],
      activeRunId: orphanRunId,
    },
  ]));
  const child = spawn(process.execPath, ['dist/index.cjs'], {
    cwd: new URL('..', import.meta.url),
    stdio: ['pipe', 'pipe', 'inherit'],
    env: {...process.env, PATTERN_DATA_DIR: dir},
  });
  try {
    const lines = createInterface({input: child.stdout});
    const handshake = await new Promise((resolve) => lines.once('line', (line) => resolve(JSON.parse(line))));
    const configure = {method: 'runtime.configure', params: {
      provider: 'OpenAI Compatible', endpoint: `http://127.0.0.1:${port}/v1`, model: 'text-model', apiKey: 'test', persona: 'test',
      bridgeUrl: `http://127.0.0.1:${port}`, bridgeToken: 'bridge-test',
    }};
    child.stdin.write(`${JSON.stringify(configure)}\n`);
    const ws = new WebSocket(`ws://127.0.0.1:${handshake.port}/ws?token=${handshake.token}`);
    await new Promise((resolve, reject) => { ws.once('open', resolve); ws.once('error', reject); });
    let recovered;
    for (let i = 0; i < 30; i++) {
      await new Promise((resolve) => setTimeout(resolve, 50));
      recovered = await new Promise((resolve) => {
        const id = `restart-list-${i}`;
        const listener = (rawMessage) => {
          const message = JSON.parse(rawMessage);
          if (message.id === id) { ws.off('message', listener); resolve(message.tasks.find((task) => task.id === taskId)); }
        };
        ws.on('message', listener);
        ws.send(JSON.stringify({type: 'task.list', id}));
      });
      if (recovered?.recovery?.state === 'recovery_required') break;
    }
    assert.equal(recovered.status, 'failed');
    assert.equal(recovered.recovery.transactionId, transactionId);
    assert.equal(recovered.recovery.state, 'recovery_required');
    assert.deepEqual(recovered.recovery.fileScopes, ['C:\\workspace']);
    assert.match(recovered.error, /restarted before/i);
    assert.equal(recovered.activeRunId, undefined);
    assert.equal(recovered.runs[0].status, 'failed');

    const request = (message) => new Promise((resolve) => {
      const listener = (rawMessage) => {
        const response = JSON.parse(rawMessage);
        if (response.id === message.id) { ws.off('message', listener); resolve(response); }
      };
      ws.on('message', listener);
      ws.send(JSON.stringify(message));
    });
    const stableTasks = await request({type: 'task.list', id: 'stable-tasks'});
    const committed = stableTasks.tasks.find((task) => task.id === committedTaskId);
    assert.equal(committed.status, 'done');
    assert.equal(committed.recovery.state, 'committed');
    assert.equal(committed.activeRunId, undefined);
    assert.equal(committed.runs[0].status, 'done');
    const orphan = stableTasks.tasks.find((task) => task.id === orphanTaskId);
    assert.equal(orphan.status, 'failed');
    assert.match(orphan.error, /stable outcome/i);
    assert.equal(orphan.activeRunId, undefined);
    assert.equal(orphan.runs[0].status, 'failed');
    child.stdin.write(`${JSON.stringify(configure)}\n`);
    await new Promise((resolve) => setTimeout(resolve, 100));
    assert.deepEqual(recoveryCalls, ['list']);
    const status = await request({type: 'recovery.status', id: 'recovery-status'});
    assert.deepEqual(status, {
      type: 'recovery.status.result',
      id: 'recovery-status',
      available: true,
      store: 'C:\\recovery-store',
      transactionCount: 3,
      openCount: 1,
    });
    const unconfirmedRollback = await request({type: 'task.recovery.rollback', id: 'recover-unconfirmed', taskId});
    assert.equal(unconfirmedRollback.type, 'error');
    assert.match(unconfirmedRollback.message, /显式确认/);
    const interruptedRollback = await request({type: 'task.recovery.rollback', id: 'recover-interrupted', taskId, assumeExclusive: true});
    assert.equal(interruptedRollback.type, 'task.list.result');
    assert.equal(interruptedRollback.tasks.find((task) => task.id === taskId).recovery.state, 'rolled_back');
    const committedRollback = await request({type: 'task.recovery.rollback', id: 'rollback-committed', taskId: committedTaskId});
    assert.equal(committedRollback.type, 'task.list.result');
    assert.equal(committedRollback.tasks.find((task) => task.id === committedTaskId).recovery.state, 'rolled_back');
    assert.deepEqual(recoveryCalls, ['list', 'list', 'recover', 'rollback']);
    const persisted = JSON.parse(readFileSync(join(dir, 'tasks.json'), 'utf8'));
    assert.equal(persisted.find((task) => task.id === taskId).recovery.state, 'rolled_back');
    assert.equal(persisted.find((task) => task.id === committedTaskId).recovery.state, 'rolled_back');
    ws.close();
  } finally {
    child.kill();
    server.close();
    await new Promise((resolve) => setTimeout(resolve, 200));
    rmSync(dir, {recursive: true, force: true});
  }
});
