import {chromium} from 'playwright-core';

const browser = await chromium.launch({
  headless: true,
  executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
});

async function inspect(url, setup) {
  const page = await browser.newPage({viewport: {width: 1280, height: 860}});
  page.setDefaultTimeout(45_000);
  const errors = [];
  page.on('console', (message) => {
    if (message.type() === 'error' && !message.text().startsWith('Failed to load resource:')) {
      errors.push(message.text());
    }
  });
  await page.goto(url, {waitUntil: 'domcontentloaded', timeout: 60_000});
  await page.waitForLoadState('networkidle').catch(() => {});
  if (setup) await setup(page);

  const unnamed = await page.locator('button').evaluateAll((buttons) =>
    buttons.filter((button) => !(button.getAttribute('aria-label') || button.textContent?.trim() || button.getAttribute('title'))).length,
  );
  if (unnamed) throw new Error(`${url}: ${unnamed} unnamed buttons`);

  const inputs = await page.locator('input,textarea,select').evaluateAll((controls) =>
    controls.filter((input) => !input.closest('label') && !input.getAttribute('aria-label') && !input.getAttribute('aria-labelledby')).length,
  );
  if (inputs) throw new Error(`${url}: ${inputs} unlabeled form controls`);

  await page.keyboard.press('Tab');
  const focused = await page.evaluate(() => document.activeElement?.tagName);
  if (!focused || focused === 'BODY') throw new Error(`${url}: keyboard focus did not enter UI`);

  if (errors.length) throw new Error(`${url}: console errors: ${errors.join('; ')}`);
  await page.close();
}

const desktop = process.env.PATTERN_DESKTOP_URL || 'http://127.0.0.1:1420';
const mobile = process.env.PATTERN_MOBILE_URL || 'http://127.0.0.1:1421';

await inspect(`${desktop}/?demo=1`);
await inspect(`${desktop}/?demo=1`, async (page) => {
  await page.getByRole('button', {name: '新建项目'}).click();
  await page.getByLabel('项目名称').fill('无障碍项目');
  await page.getByLabel('项目路径').fill('E:/Desktop/项目/CrossPlatform/Pattern');
  await page.getByRole('button', {name: '创建项目'}).click();
  await page.getByRole('complementary', {name: '项目文件'}).waitFor();
  // Browser preview no longer fabricates a fake tree; empty/error state is fine for a11y.
});
await inspect(`${desktop}/?window=quick`);
await inspect(`${mobile}/`);

await browser.close();
console.log('Desktop and mobile accessibility smoke checks passed');
