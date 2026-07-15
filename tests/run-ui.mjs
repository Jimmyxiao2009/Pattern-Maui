import {spawn} from 'node:child_process';
import {setTimeout as delay} from 'node:timers/promises';
import {fileURLToPath} from 'node:url';
import {dirname, resolve} from 'node:path';
import {createServer} from 'node:net';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const servers = [];

async function freePort() {
  const server = createServer();
  await new Promise((resolveListen, rejectListen) => { server.once('error', rejectListen); server.listen(0, '127.0.0.1', resolveListen); });
  const address = server.address();
  const port = typeof address === 'object' && address ? address.port : 0;
  await new Promise((resolveClose) => server.close(resolveClose));
  if (!port) throw new Error('Unable to allocate a local test port');
  return port;
}

function startServer(directory, port) {
  const vite = resolve(root, directory, 'node_modules', 'vite', 'bin', 'vite.js');
  const child = spawn(process.execPath, [vite, '--host', '127.0.0.1', '--port', String(port), '--strictPort'], {
    cwd: resolve(root, directory), stdio: 'inherit', windowsHide: true,
  });
  servers.push(child);
  return child;
}

async function waitForServer(url, child) {
  const deadline = Date.now() + 30_000;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) throw new Error(`${url} development server exited before becoming ready`);
    try { if ((await fetch(url)).ok) return; } catch { /* Vite is still starting */ }
    await delay(250);
  }
  throw new Error(`Timed out waiting for ${url}`);
}

function runNode(file, env) {
  return new Promise((resolveRun, rejectRun) => {
    const child = spawn(process.execPath, [file], {cwd: root, stdio: 'inherit', windowsHide: true, env: {...process.env, ...env}});
    child.once('error', rejectRun);
    child.once('exit', (code) => code === 0 ? resolveRun() : rejectRun(new Error(`${file} exited with ${code}`)));
  });
}

try {
  const desktopPort = await freePort();
  let mobilePort = await freePort();
  while (mobilePort === desktopPort) mobilePort = await freePort();
  const desktopUrl = `http://127.0.0.1:${desktopPort}`;
  const mobileUrl = `http://127.0.0.1:${mobilePort}`;
  const desktop = startServer('apps/desktop', desktopPort);
  const mobile = startServer('apps/mobile', mobilePort);
  await Promise.all([waitForServer(`${desktopUrl}/`, desktop), waitForServer(`${mobileUrl}/`, mobile)]);
  const env = {PATTERN_DESKTOP_URL: desktopUrl, PATTERN_MOBILE_URL: mobileUrl};
  console.log(`UI servers ready: ${desktopUrl}, ${mobileUrl}`);
  const tests = ['tests/oobe.mjs', 'tests/app-flows.mjs', 'tests/ui-accessibility.mjs'];
  for (const test of tests) console.log(`Running ${test}`);
  for (const test of tests) await runNode(test, env);
} finally {
  for (const child of servers) if (child.exitCode === null) child.kill();
}
