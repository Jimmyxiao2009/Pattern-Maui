import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync, rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {createRequire} from 'node:module';
import {buildSync} from 'esbuild';

function loadMemory(dir) {
  const entry = fileURLToPath(new URL('../../packages/memory/src/index.ts', import.meta.url));
  const outfile = join(dir, 'mem.cjs');
  buildSync({
    entryPoints: [entry],
    bundle: true,
    platform: 'node',
    format: 'cjs',
    target: 'node22',
    outfile,
  });
  const require = createRequire(import.meta.url);
  return require(outfile);
}

test('memory engine add/search/expire via node sqlite', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'pattern-memory-'));
  try {
    const {MemoryEngine} = loadMemory(dir);
    const engine = new MemoryEngine(dir);
    const item = await engine.add({text: '用户养了一只黄眼睛的黑猫', category: 'fact', importance: 0.8});
    assert.equal(item.expired, false);
    const hits = await engine.search('黑猫', 3);
    assert.ok(hits.some((h) => h.id === item.id), 'search should hit 黑猫');
    engine.touch([item.id]);
    const again = engine.list('黑猫');
    assert.ok(again[0].accessCount >= 1);
    engine.expire(item.id);
    const after = engine.list('黑猫');
    assert.equal(after.length, 0);
    engine.close();
  } finally {
    rmSync(dir, {recursive: true, force: true});
  }
});

test('FTS path finds Chinese substring among distractors', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'pattern-memory-fts-'));
  try {
    const {MemoryEngine, buildFtsMatchQuery, ftsIndexText} = loadMemory(dir);
    assert.ok(buildFtsMatchQuery('黑猫').includes('黑猫') || buildFtsMatchQuery('黑猫').includes('黑'));
    assert.ok(ftsIndexText('黄眼睛的黑猫').includes('黑猫'));

    const engine = new MemoryEngine(dir);
    await engine.add({text: '用户喜欢喝美式咖啡', category: 'preference', importance: 0.6});
    await engine.add({text: '上周搬家到杭州西湖边', category: 'event', importance: 0.7});
    const target = await engine.add({text: '用户养了一只黄眼睛的黑猫，叫煤球', category: 'fact', importance: 0.9});
    await engine.add({text: '服务器监控地址是 https://status.example.com', category: 'reference', importance: 0.5});

    const hits = await engine.search('黑猫', 5);
    assert.ok(hits.length >= 1);
    assert.equal(hits[0].id, target.id, `top hit should be cat memory, got ${hits[0]?.text}`);

    const move = await engine.search('搬家', 3);
    assert.ok(move.some((h) => h.text.includes('杭州')));

    // custom embedder still works (dim mismatch falls back inside scorer)
    engine.setEmbedder(async (text) => {
      const v = new Float32Array(8);
      for (let i = 0; i < 8; i++) v[i] = (text.length + i) % 7;
      return v;
    });
    const again = await engine.search('煤球', 3);
    assert.ok(again.some((h) => h.id === target.id));
    engine.close();
  } finally {
    rmSync(dir, {recursive: true, force: true});
  }
});
