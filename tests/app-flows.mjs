import {chromium} from 'playwright-core';

const browser = await chromium.launch({
  headless: true,
  executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
});
const page = await browser.newPage({viewport: {width: 1280, height: 860}});
page.setDefaultTimeout(45_000);
const base = process.env.PATTERN_DESKTOP_URL || 'http://127.0.0.1:1420';

await page.goto(`${base}/?demo=1`, {waitUntil: 'domcontentloaded', timeout: 60_000});
await page.waitForLoadState('networkidle').catch(() => {});

// Global chat: recent sidebar + empty real conversation (no mock seed messages).
await page.getByRole('navigation', {name: '主导航'}).getByRole('button', {name: '对话'}).click();
await page.getByRole('complementary', {name: '最近内容'}).waitFor();
await page.getByRole('heading', {name: '最近聊天'}).waitFor();
await page.getByRole('heading', {name: '最近项目'}).waitFor();
await page.getByText('开始一段全局对话').waitFor();
await page.getByLabel('消息').fill('全局自动化对话');
await page.getByRole('button', {name: '发送'}).click();
await page.locator('.message.user p', {hasText: '全局自动化对话'}).waitFor();
// Demo mode should soft-fail instead of dumping a raw runtime exception.
await page.locator('.message.assistant', {hasText: '演示模式不会调用模型'}).waitFor();
await page.getByLabel('执行时间线').first().waitFor();

// Proactive AI messages become a brand-new global conversation.
await page.waitForFunction(() => Boolean(window.__patternTest?.ingestProactive));
await page.evaluate(() => {
  window.__patternTest.ingestProactive({
    id: crypto.randomUUID(),
    body: '这是一条主动提醒：该休息一下了。',
    type: '日程',
    reason: '主动休息提醒',
  });
});
await page.getByRole('heading', {name: '主动休息提醒'}).first().waitFor();
await page.locator('.message.assistant p', {hasText: '该休息一下了'}).waitFor();
await page.locator('.badge.amber', {hasText: '主动'}).first().waitFor();

// Create project and open Codex-like 3-pane workspace.
await page.getByRole('button', {name: '新建项目'}).click();
await page.getByRole('button', {name: '浏览文件夹'}).waitFor();
await page.getByLabel('项目名称').fill('自动化项目');
await page.getByLabel('项目路径').fill('E:/Desktop/项目/CrossPlatform/Pattern');
await page.getByRole('button', {name: '创建项目'}).click();
await page.getByRole('heading', {name: '自动化项目'}).first().waitFor();
await page.getByText('已绑定工作区').first().waitFor();
await page.getByRole('button', {name: '查看 Diff'}).waitFor();
await page.getByRole('button', {name: 'Worktree'}).waitFor();
await page.getByRole('complementary', {name: '项目对话'}).waitFor();
await page.getByRole('region', {name: '项目对话内容'}).waitFor();
await page.getByRole('complementary', {name: '项目文件'}).waitFor();
// Browser preview no longer fabricates a fake file tree.
await page.getByText('浏览器预览无法读取本地文件夹').waitFor();
await page.getByLabel('项目消息').fill('项目内自动化对话');
await page.getByRole('button', {name: '发送'}).click();
await page.locator('.message.user p', {hasText: '项目内自动化对话'}).waitFor();

// Memory page starts empty (no prefabricated demo memories); add one via UI.
await page.getByRole('button', {name: '记忆'}).click();
await page.getByRole('button', {name: '添加记忆'}).click();
await page.getByLabel('内容').fill('自动化测试记忆');
await page.getByRole('button', {name: '保存记忆'}).click();
await page.getByText('自动化测试记忆').waitFor();

await page.getByRole('button', {name: '任务'}).click();
await page.getByRole('button', {name: '创建第一个任务'}).click();
await page.getByLabel('任务名称').fill('自动化测试任务');
await page.getByRole('button', {name: '开始执行'}).click();
await page.getByText('自动化测试任务').waitFor();

await page.getByRole('button', {name: '通道'}).click();
await page.getByRole('button', {name: '配置'}).first().click();
await page.getByLabel('WebDAV 地址（可选）').fill('https://dav.example.com/pattern');
await page.getByRole('button', {name: '保存'}).click();
await page.getByText('已配置').waitFor();

await page.getByRole('button', {name: '工具'}).click();
await page.getByRole('heading', {name: 'MCP 管理'}).waitFor();
await page.getByRole('button', {name: '工作流'}).click();
await page.getByRole('button', {name: '安装技能'}).waitFor();
await page.getByRole('button', {name: '设置'}).click();
await page.getByRole('button', {name: '快捷键'}).click();
await page.getByLabel('唤起快捷窗快捷键').selectOption('ctrl-shift-space');
await page.getByRole('button', {name: '应用快捷键'}).click();
await page.getByText('浏览器预览不会注册全局快捷键').waitFor();

await page.getByRole('button', {name: '常规'}).click();
await page.getByText('关闭到托盘').waitFor();
await page.getByText('单实例运行').waitFor();

await browser.close();
console.log('Chat, project workspace, memory, task, and channel flows completed');
