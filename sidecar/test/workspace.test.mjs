import test from 'node:test';
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createInterface} from 'node:readline';
import {mkdtempSync, rmSync, writeFileSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';

test('workspace list/read is policy-aware and bounded', async (t) => {
  const dataDir = mkdtempSync(join(tmpdir(), 'pattern-workspace-sidecar-'));
  const workspace = mkdtempSync(join(tmpdir(), 'pattern-workspace-root-'));
  writeFileSync(join(workspace, 'README.md'), '# Pattern\nhello');
  writeFileSync(join(workspace, 'secret.bin'), Buffer.from([0, 1, 2]));
  const child = spawn(process.execPath, ['dist/index.cjs', '--stdio'], {
    cwd: new URL('..', import.meta.url), stdio: ['pipe', 'pipe', 'pipe'],
    env: {...process.env, PATTERN_DATA_DIR: dataDir},
  });
  const lines = createInterface({input: child.stdout});
  const pending = new Map();
  lines.on('line', (line) => {
    try {
      const value = JSON.parse(line);
      if (value.id && pending.has(value.id)) { pending.get(value.id)(value); pending.delete(value.id); }
    } catch { /* startup/diagnostic lines are handled by the handshake below */ }
  });
  const waitFor = (id) => new Promise((resolve, reject) => {
    const timer = setTimeout(() => { pending.delete(id); reject(new Error(`timeout waiting for ${id}`)); }, 5000);
    pending.set(id, (value) => { clearTimeout(timer); resolve(value); });
  });
  await new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('stdio handshake timeout')), 5000);
    const onLine = (line) => { try { const value = JSON.parse(line); if (value.transport === 'stdio') { clearTimeout(timer); lines.off('line', onLine); resolve(value); } } catch {} };
    lines.on('line', onLine);
  });
  t.after(async () => { lines.close(); if (!child.killed) child.kill(); await new Promise((resolve) => child.once('exit', resolve)); rmSync(dataDir, {recursive:true, force:true}); rmSync(workspace, {recursive:true, force:true}); });

  child.stdin.write(`${JSON.stringify({type:'workspace.list', id:'list', root:workspace, depth:1})}\n`);
  const listed = await waitFor('list');
  assert.equal(listed.type, 'workspace.list.result');
  assert.ok(listed.nodes.some((node) => node.name === 'README.md'));
  child.stdin.write(`${JSON.stringify({type:'workspace.read', id:'read', path:join(workspace, 'README.md'), maxBytes:20})}\n`);
  const read = await waitFor('read');
  assert.equal(read.content, '# Pattern\nhello');
  child.stdin.write(`${JSON.stringify({type:'workspace.read', id:'binary', path:join(workspace, 'secret.bin')})}\n`);
  const binary = await waitFor('binary');
  assert.equal(binary.type, 'error');
});
