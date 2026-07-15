import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync, rmSync, writeFileSync, mkdirSync, readFileSync, existsSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {createRequire} from 'node:module';
import {buildSync} from 'esbuild';
import http from 'node:http';
import {webcrypto} from 'node:crypto';

function loadRelay(dir) {
  const entry = fileURLToPath(new URL('../../packages/relay/src/index.ts', import.meta.url));
  const outfile = join(dir, 'relay.cjs');
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

function loadMobileRelay(dir) {
  const entry = fileURLToPath(new URL('../../apps/mobile/src/lib/relay.ts', import.meta.url));
  const outfile = join(dir, 'mobile-relay.cjs');
  buildSync({entryPoints:[entry], bundle:true, platform:'browser', format:'cjs', target:'es2022', outfile});
  return createRequire(import.meta.url)(outfile);
}

test('desktop and mobile AES-GCM wire formats interoperate', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'pattern-relay-crypto-'));
  try {
    globalThis.crypto ??= webcrypto;
    const desktop = loadRelay(dir);
    const mobile = loadMobileRelay(dir);
    const secret = 'cross-device-test-key';
    assert.equal(await mobile.decrypt(desktop.encryptBody('desktop to mobile', secret), secret), 'desktop to mobile');
    assert.equal(desktop.decryptBody(await mobile.encrypt('mobile to desktop', secret), secret), 'mobile to desktop');
  } finally {
    rmSync(dir, {recursive:true, force:true});
  }
});

test('X25519 pairing wraps credentials with XChaCha20-Poly1305',()=>{const dir=mkdtempSync(join(tmpdir(),'pattern-pairing-'));try{const {createSecurePairingRequest,createSecurePairingResponse,openSecurePairingResponse}=loadRelay(dir);const request=createSecurePairingRequest('mobile-1');const payload={webdavUrl:'https://dav.test',username:'u',password:'p',channelKey:'secret'};const response=createSecurePairingResponse(request.code,payload,'desktop-1');assert.deepEqual(openSecurePairingResponse(response.code,request.privateKey),payload);assert.equal(response.code.includes('secret'),false);assert.throws(()=>openSecurePairingResponse(response.code,createSecurePairingRequest('attacker').privateKey));}finally{rmSync(dir,{recursive:true,force:true})}});

test('local relay publish/list/pull with cursor dedupe', async () => {
  const dataDir = mkdtempSync(join(tmpdir(), 'pattern-relay-data-'));
  const root = mkdtempSync(join(tmpdir(), 'pattern-relay-root-'));
  try {
    const {RelayClient} = loadRelay(dataDir);
    const a = new RelayClient(dataDir, {url: `local:${root}`, username: '', password: ''});
    const env = a.createEnvelope({role: 'user', type: 'chat', body: 'hello from A'});
    // forge from another device
    env.from = 'device-b';
    const pub = await a.publish(env);
    assert.equal(pub.queued, false);

    const listed = await a.listMailbox();
    assert.equal(listed.length, 1);
    assert.equal(listed[0].body, 'hello from A');

    const first = await a.pullIncoming();
    assert.equal(first.length, 1);
    assert.equal(first[0].body, 'hello from A');

    const second = await a.pullIncoming();
    assert.equal(second.length, 0, 'cursor should suppress duplicates');

    assert.ok(existsSync(join(dataDir, 'relay-cursor.json')));
    assert.ok(existsSync(join(root, 'pattern', 'cursors', `${a.deviceId}.json`)));
  } finally {
    rmSync(dataDir, {recursive: true, force: true});
    rmSync(root, {recursive: true, force: true});
  }
});

test('extractMailboxHrefs parses PROPFIND multistatus', () => {
  const dir = mkdtempSync(join(tmpdir(), 'pattern-relay-href-'));
  try {
    const {extractMailboxHrefs} = loadRelay(dir);
    const xml = `<?xml version="1.0"?>
<d:multistatus xmlns:d="DAV:">
  <d:response><d:href>/dav/files/u/pattern/mailbox/</d:href></d:response>
  <d:response><d:href>/dav/files/u/pattern/mailbox/abc123.json</d:href></d:response>
  <d:response><d:href>/dav/files/u/pattern/mailbox/def%2Ejson</d:href></d:response>
</d:multistatus>`;
    const names = extractMailboxHrefs(xml, 'https://example.com/dav/files/u/pattern/mailbox/');
    assert.ok(names.includes('abc123.json'));
  } finally {
    rmSync(dir, {recursive: true, force: true});
  }
});

test('remote listMailbox uses PROPFIND + GET', async () => {
  const dataDir = mkdtempSync(join(tmpdir(), 'pattern-relay-remote-'));
  const store = new Map();
  // pre-seed one encrypted-looking envelope via local client crypto helpers
  const {RelayClient, encryptBody, extractMailboxHrefs} = loadRelay(dataDir);
  assert.ok(typeof extractMailboxHrefs === 'function');

  const channelKey = 'test-channel-key-please-ignore';
  const bodyCipher = encryptBody('ping remote', channelKey);
  const envId = 'envremote001';
  store.set(`/pattern/mailbox/${envId}.json`, JSON.stringify({
    id: envId,
    from: 'other-device',
    role: 'user',
    type: 'chat',
    ts: Math.floor(Date.now() / 1000),
    body: bodyCipher,
    encrypted: true,
  }));

  const server = http.createServer((req, res) => {
    const url = new URL(req.url || '/', 'http://127.0.0.1');
    if (req.method === 'MKCOL') {
      res.writeHead(201);
      res.end();
      return;
    }
    if (req.method === 'PROPFIND' && url.pathname.replace(/\/$/, '').endsWith('/mailbox')) {
      const xml = `<?xml version="1.0"?>
<d:multistatus xmlns:d="DAV:">
  <d:response><d:href>${url.pathname}</d:href></d:response>
  <d:response><d:href>${url.pathname.replace(/\/$/, '')}/${envId}.json</d:href></d:response>
</d:multistatus>`;
      res.writeHead(207, {'content-type': 'application/xml'});
      res.end(xml);
      return;
    }
    if (req.method === 'GET') {
      const key = url.pathname;
      // normalize to /pattern/...
      const hit = [...store.entries()].find(([k]) => key.endsWith(k) || key.endsWith(k.slice(1)));
      if (!hit) {
        res.writeHead(404);
        res.end();
        return;
      }
      res.writeHead(200, {'content-type': 'application/json'});
      res.end(hit[1]);
      return;
    }
    if (req.method === 'PUT') {
      let data = '';
      req.on('data', (c) => (data += c));
      req.on('end', () => {
        store.set(url.pathname, data);
        res.writeHead(201);
        res.end();
      });
      return;
    }
    res.writeHead(405);
    res.end();
  });

  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  const {port} = server.address();
  try {
    const client = new RelayClient(
      dataDir,
      {url: `http://127.0.0.1:${port}`, username: 'u', password: 'p'},
      'desktop-1',
      channelKey,
    );
    const items = await client.listMailbox();
    assert.equal(items.length, 1);
    assert.equal(items[0].body, 'ping remote');
    const incoming = await client.pullIncoming();
    assert.equal(incoming.length, 1);
  } finally {
    server.close();
    rmSync(dataDir, {recursive: true, force: true});
  }
});
