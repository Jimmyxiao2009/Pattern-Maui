#!/usr/bin/env node
/**
 * Second-device pull helper for WebDAV / local mailbox validation.
 *
 * Usage:
 *   node scripts/relay-pull.mjs --url https://dav.example/remote.php/dav/files/u --user u --pass p --key <channelKey> --id <envelopeId>
 *   node scripts/relay-pull.mjs --url local:C:\path\to\relay-root --key <channelKey> --list
 *   node scripts/relay-pull.mjs --url https://... --user u --pass p --key <channelKey> --list
 *
 * channelKey is stored in %LOCALAPPDATA%/pattern/device.json on the desktop host.
 */
import {createDecipheriv, createHash} from 'node:crypto';
import {existsSync, readdirSync, readFileSync} from 'node:fs';
import {join} from 'node:path';

function arg(name, fallback = '') {
  const idx = process.argv.indexOf(`--${name}`);
  if (idx >= 0) return process.argv[idx + 1] || fallback;
  return fallback;
}

function hasFlag(name) {
  return process.argv.includes(`--${name}`);
}

const url = arg('url');
const user = arg('user');
const pass = arg('pass');
const key = arg('key');
const id = arg('id');
const list = hasFlag('list');

if (!url || !key || (!id && !list)) {
  console.error('Required: --url --key (--id <envelopeId> | --list) [--user --pass]');
  process.exit(1);
}

function deriveKey(secret) {
  return createHash('sha256').update(secret).digest();
}

function decryptBody(payload, secret) {
  const buf = Buffer.from(payload, 'base64url');
  const iv = buf.subarray(0, 12);
  const tag = buf.subarray(12, 28);
  const data = buf.subarray(28);
  const decipher = createDecipheriv('aes-256-gcm', deriveKey(secret), iv);
  decipher.setAuthTag(tag);
  return Buffer.concat([decipher.update(data), decipher.final()]).toString('utf8');
}

function maybeDecrypt(env) {
  if (env.encrypted) env.body = decryptBody(env.body, key);
  return env;
}

function isFileBackend(u) {
  return u.startsWith('file:') || u.startsWith('local:');
}

function fileRoot(u) {
  if (u.startsWith('local:')) return u.slice('local:'.length);
  if (u.startsWith('file:///')) return u.slice('file:///'.length);
  if (u.startsWith('file://')) return u.slice('file://'.length);
  return u;
}

function joinUrl(base, ...parts) {
  const root = base.replace(/\/+$/, '');
  const path = parts.map((p) => p.replace(/^\/+|\/+$/g, '')).filter(Boolean).join('/');
  return `${root}/${path}`;
}

function authHeaders() {
  return user ? {authorization: 'Basic ' + Buffer.from(`${user}:${pass}`).toString('base64')} : {};
}

function extractMailboxHrefs(xml, collectionUrl) {
  const hrefs = [...xml.matchAll(/<[^>]*:?href[^>]*>([^<]+)<\//gi)].map((m) => decodeURIComponent(m[1].trim()));
  const names = new Set();
  let collectionPath = collectionUrl;
  try {
    collectionPath = new URL(collectionUrl).pathname.replace(/\/+$/, '');
  } catch {
    collectionPath = collectionUrl.replace(/\/+$/, '');
  }
  for (const href of hrefs) {
    let path = href;
    try {
      if (/^https?:\/\//i.test(href)) path = new URL(href).pathname;
    } catch {
      /* keep */
    }
    path = path.replace(/\/+$/, '');
    if (!path || path === collectionPath) continue;
    const base = path.split('/').pop() || '';
    if (base.toLowerCase().endsWith('.json')) names.add(base);
  }
  return [...names].sort();
}

if (list) {
  if (isFileBackend(url)) {
    const dir = join(fileRoot(url), 'pattern', 'mailbox');
    if (!existsSync(dir)) {
      console.log('[]');
      process.exit(0);
    }
    const items = [];
    for (const file of readdirSync(dir).filter((f) => f.endsWith('.json'))) {
      try {
        items.push(maybeDecrypt(JSON.parse(readFileSync(join(dir, file), 'utf8'))));
      } catch (error) {
        console.error('skip', file, error.message);
      }
    }
    items.sort((a, b) => (a.ts || 0) - (b.ts || 0));
    console.log(JSON.stringify(items, null, 2));
    process.exit(0);
  }

  const collection = joinUrl(url, 'pattern', 'mailbox');
  const coll = collection.endsWith('/') ? collection : `${collection}/`;
  const res = await fetch(coll, {
    method: 'PROPFIND',
    headers: {
      ...authHeaders(),
      depth: '1',
      'content-type': 'application/xml; charset=utf-8',
    },
    body: `<?xml version="1.0" encoding="utf-8"?>
<d:propfind xmlns:d="DAV:"><d:prop><d:displayname/></d:prop></d:propfind>`,
  });
  if (!res.ok) {
    console.error('PROPFIND failed', res.status, await res.text());
    process.exit(2);
  }
  const names = extractMailboxHrefs(await res.text(), coll);
  const items = [];
  for (const name of names) {
    const target = joinUrl(url, 'pattern', 'mailbox', name);
    const get = await fetch(target, {headers: authHeaders()});
    if (!get.ok) continue;
    try {
      items.push(maybeDecrypt(await get.json()));
    } catch {
      /* skip */
    }
  }
  console.log(JSON.stringify(items, null, 2));
  process.exit(0);
}

// single id
if (isFileBackend(url)) {
  const file = join(fileRoot(url), 'pattern', 'mailbox', `${id}.json`);
  if (!existsSync(file)) {
    console.error('not found', file);
    process.exit(2);
  }
  const env = maybeDecrypt(JSON.parse(readFileSync(file, 'utf8')));
  console.log(JSON.stringify(env, null, 2));
  process.exit(0);
}

const target = joinUrl(url, 'pattern', 'mailbox', `${id}.json`);
const res = await fetch(target, {headers: authHeaders()});
if (!res.ok) {
  console.error('GET failed', res.status, await res.text());
  process.exit(2);
}
const env = maybeDecrypt(await res.json());
console.log(JSON.stringify(env, null, 2));
