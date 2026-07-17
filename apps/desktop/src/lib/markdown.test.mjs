import test from 'node:test';
import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {dirname, join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {readFileSync, writeFileSync, unlinkSync} from 'node:fs';
const here = dirname(fileURLToPath(import.meta.url));
const require = createRequire(import.meta.url);
// Prefer monorepo esbuild (sidecar)
let transformSync;
try {
  ({transformSync} = require(join(here, '../../../../sidecar/node_modules/esbuild')));
} catch {
  ({transformSync} = require(join(here, '../../../../node_modules/esbuild')));
}
const source = readFileSync(join(here, 'markdown.ts'), 'utf8');
const {code} = transformSync(source, {loader: 'ts', format: 'esm'});
const tmp = join(here, `.markdown.generated.${process.pid}.mjs`);
writeFileSync(tmp, code);
try {
  const {renderMarkdown} = await import(pathToFileUrl(tmp));
  run(renderMarkdown);
} finally {
  try { unlinkSync(tmp); } catch { /* ignore */ }
}

function pathToFileUrl(p) {
  return 'file:///' + p.replace(/\\/g, '/');
}

function run(renderMarkdown) {
  test('renders bold italic code links headings lists', () => {
    const html = renderMarkdown('## 标题\n\n**粗体** 和 *斜体* 以及 `code`\n\n- 一项\n- 二项\n\n[链接](https://example.com)');
    assert.match(html, /<h2 class="md-h">/);
    assert.match(html, /<strong>粗体<\/strong>/);
    assert.match(html, /<em>斜体<\/em>/);
    assert.match(html, /<code class="md-inline">code<\/code>/);
    assert.match(html, /<ul class="md-list">/);
    assert.match(html, /href="https:\/\/example.com"/);
  });

  test('renders fenced code blocks without double-escaping', () => {
    const html = renderMarkdown('```js\nconst a = 1 < 2\n```');
    assert.match(html, /<pre class="md-code"><code(?: class="[^"]*")?>/);
    assert.match(html, /&lt; <span class="hljs-number">2<\/span>/);
    assert.match(html, /hljs-keyword/);
    assert.doesNotMatch(html, /&amp;lt;/);
  });

  test('renders inline and display math with KaTeX', () => {
    const html = renderMarkdown('行内公式 $E=mc^2$\n\n$$\\frac{a}{b}$$');
    assert.match(html, /katex/);
    assert.match(html, /katex-display/);
    assert.doesNotMatch(html, /PATTERN_MATH/);
  });

  test('renders gfm tables and task lists', () => {
    const table = renderMarkdown('| 名称 | 值 |\n| --- | --- |\n| a | 1 |\n| b | 2 |');
    assert.match(table, /<table class="md-table">/);
    assert.match(table, /<th>名称<\/th>/);
    assert.match(table, /<td>a<\/td>/);

    const tasks = renderMarkdown('- [x] 完成\n- [ ] 待办');
    assert.match(tasks, /md-task/);
    assert.match(tasks, /checked/);
  });

  test('escapes raw HTML from model output', () => {
    const html = renderMarkdown('hello <script>alert(1)</script>');
    assert.doesNotMatch(html, /<script>/);
    assert.match(html, /&lt;script&gt;/);
  });
}
