import {createCipheriv, createDecipheriv, createHash, randomBytes, randomUUID} from 'node:crypto';
import {existsSync, mkdirSync, readFileSync, writeFileSync, readdirSync} from 'node:fs';
import {join} from 'node:path';
import type {RelayStatus} from '@pattern/protocol';
export * from './pairing.js';

export interface RelayConfig {
  url: string;
  username: string;
  password: string;
}

export interface Envelope {
  id: string;
  from: string;
  role: 'companion' | 'user' | 'system';
  type: 'chat' | 'proactive' | 'task';
  ts: number;
  body: string;
  encrypted?: boolean;
  sig?: string;
}

export interface OutboxItem {
  id: string;
  envelope: Envelope;
  attempts: number;
  nextAttemptAt: number;
  lastError?: string;
}

function now() {
  return Math.floor(Date.now() / 1000);
}

function deriveKey(secret: string) {
  return createHash('sha256').update(secret).digest();
}

export function encryptBody(plaintext: string, secret: string): string {
  const key = deriveKey(secret);
  const iv = randomBytes(12);
  const cipher = createCipheriv('aes-256-gcm', key, iv);
  const enc = Buffer.concat([cipher.update(plaintext, 'utf8'), cipher.final()]);
  const tag = cipher.getAuthTag();
  return Buffer.concat([iv, tag, enc]).toString('base64url');
}

export function decryptBody(payload: string, secret: string): string {
  const buf = Buffer.from(payload, 'base64url');
  const iv = buf.subarray(0, 12);
  const tag = buf.subarray(12, 28);
  const data = buf.subarray(28);
  const key = deriveKey(secret);
  const decipher = createDecipheriv('aes-256-gcm', key, iv);
  decipher.setAuthTag(tag);
  return Buffer.concat([decipher.update(data), decipher.final()]).toString('utf8');
}

function basicAuth(username: string, password: string) {
  return 'Basic ' + Buffer.from(`${username}:${password}`).toString('base64');
}

function joinUrl(base: string, ...parts: string[]) {
  const root = base.replace(/\/+$/, '');
  const path = parts
    .map((p) => p.replace(/^\/+|\/+$/g, ''))
    .filter(Boolean)
    .join('/');
  return `${root}/${path}`;
}

function isFileBackend(url: string) {
  return url.startsWith('file:') || url.startsWith('local:');
}

function fileRoot(url: string) {
  if (url.startsWith('local:')) return url.slice('local:'.length);
  if (url.startsWith('file:///')) {
    // file:///C:/path -> C:/path
    return url.slice('file:///'.length);
  }
  if (url.startsWith('file://')) return url.slice('file://'.length);
  return url;
}

export class RelayClient {
  private outbox: OutboxItem[] = [];
  private outboxPath: string;
  private devicePath: string;
  private cursorPath: string;
  private lastSyncAt: number | null = null;
  private lastError: string | null = null;
  private online = false;
  private seenIds = new Set<string>();
  /** Last seen envelope timestamp (seconds) for remote/local cursor advancement. */
  private cursorTs = 0;
  deviceId: string;
  channelKey: string;

  constructor(
    private dataDir: string,
    private config: RelayConfig | null,
    deviceId?: string,
    channelKey?: string,
  ) {
    mkdirSync(dataDir, {recursive: true});
    this.outboxPath = join(dataDir, 'relay-outbox.json');
    this.devicePath = join(dataDir, 'device.json');
    this.cursorPath = join(dataDir, 'relay-cursor.json');
    const saved = this.loadDevice();
    this.deviceId = deviceId || saved.deviceId;
    this.channelKey = channelKey || saved.channelKey;
    this.persistDevice();
    this.outbox = this.loadOutbox();
    this.loadCursor();
  }

  private loadDevice() {
    if (!existsSync(this.devicePath)) {
      return {deviceId: randomUUID(), channelKey: randomBytes(32).toString('base64url')};
    }
    try {
      return JSON.parse(readFileSync(this.devicePath, 'utf8')) as {deviceId: string; channelKey: string};
    } catch {
      return {deviceId: randomUUID(), channelKey: randomBytes(32).toString('base64url')};
    }
  }

  private persistDevice() {
    writeFileSync(this.devicePath, JSON.stringify({deviceId: this.deviceId, channelKey: this.channelKey}, null, 2));
  }

  private loadOutbox(): OutboxItem[] {
    if (!existsSync(this.outboxPath)) return [];
    try {
      return JSON.parse(readFileSync(this.outboxPath, 'utf8')) as OutboxItem[];
    } catch {
      return [];
    }
  }

  private saveOutbox() {
    writeFileSync(this.outboxPath, JSON.stringify(this.outbox, null, 2));
  }

  private loadCursor() {
    if (!existsSync(this.cursorPath)) return;
    try {
      const data = JSON.parse(readFileSync(this.cursorPath, 'utf8')) as {
        cursorTs?: number;
        seenIds?: string[];
      };
      this.cursorTs = Number(data.cursorTs ?? 0) || 0;
      for (const id of data.seenIds ?? []) this.seenIds.add(id);
    } catch {
      /* ignore */
    }
  }

  private saveCursor() {
    // Cap seen set so the file stays small
    const seenIds = [...this.seenIds];
    if (seenIds.length > 5000) seenIds.splice(0, seenIds.length - 5000);
    writeFileSync(
      this.cursorPath,
      JSON.stringify({cursorTs: this.cursorTs, seenIds, updatedAt: now()}, null, 2),
    );
  }

  updateConfig(config: RelayConfig | null) {
    this.config = config;
  }

  status(): RelayStatus {
    return {
      configured: !!(this.config?.url),
      online: this.online,
      lastSyncAt: this.lastSyncAt,
      outboxCount: this.outbox.length,
      error: this.lastError,
    };
  }

  createEnvelope(input: Omit<Envelope, 'id' | 'ts' | 'from'> & {from?: string}): Envelope {
    return {
      id: randomUUID().replace(/-/g, '').slice(0, 26),
      from: input.from ?? this.deviceId,
      role: input.role,
      type: input.type,
      ts: now(),
      body: input.body,
      encrypted: true,
    };
  }

  private headers() {
    if (!this.config) throw new Error('WebDAV 未配置');
    return {
      authorization: basicAuth(this.config.username, this.config.password),
      'content-type': 'application/json',
    };
  }

  private localPatternDir() {
    if (!this.config?.url) throw new Error('WebDAV 未配置');
    const root = fileRoot(this.config.url);
    const dir = join(root, 'pattern');
    for (const part of ['', 'devices', 'mailbox', 'cursors', 'state']) {
      mkdirSync(part ? join(dir, part) : dir, {recursive: true});
    }
    return dir;
  }

  private async ensureDirs() {
    if (!this.config?.url) return;
    if (isFileBackend(this.config.url)) {
      this.localPatternDir();
      return;
    }
    for (const part of ['', 'devices', 'mailbox', 'cursors', 'state']) {
      const url = joinUrl(this.config.url, 'pattern', part);
      try {
        await fetch(url, {method: 'MKCOL', headers: this.headers()});
      } catch {
        /* exists or unsupported */
      }
    }
  }

  private async put(pathParts: string[], body: string) {
    if (!this.config?.url) throw new Error('WebDAV 未配置');
    if (isFileBackend(this.config.url)) {
      const file = join(this.localPatternDir(), ...pathParts);
      mkdirSync(join(file, '..'), {recursive: true});
      writeFileSync(file, body);
      return;
    }
    const url = joinUrl(this.config.url, 'pattern', ...pathParts);
    const res = await fetch(url, {method: 'PUT', headers: this.headers(), body});
    if (!res.ok) throw new Error(`WebDAV PUT ${res.status}`);
  }

  private async get(pathParts: string[]): Promise<string | null> {
    if (!this.config?.url) return null;
    if (isFileBackend(this.config.url)) {
      const file = join(this.localPatternDir(), ...pathParts);
      if (!existsSync(file)) return null;
      return readFileSync(file, 'utf8');
    }
    const url = joinUrl(this.config.url, 'pattern', ...pathParts);
    const res = await fetch(url, {method: 'GET', headers: this.headers()});
    if (res.status === 404) return null;
    if (!res.ok) throw new Error(`WebDAV GET ${res.status}`);
    return await res.text();
  }

  private async putDirect(pathParts: string[], body: string) {
    await this.ensureDirs();
    await this.put(pathParts, body);
  }

  /** Publish without touching outbox on success; caller manages outbox. */
  private async publishOnce(envelope: Envelope) {
    if (!this.config?.url) throw new Error('WebDAV 未配置');
    const wire = {
      ...envelope,
      body: encryptBody(envelope.body, this.channelKey),
      encrypted: true,
    };
    await this.putDirect(['mailbox', `${envelope.id}.json`], JSON.stringify(wire));
    await this.putDirect(
      ['devices', `${this.deviceId}.json`],
      JSON.stringify({id: this.deviceId, ts: now(), role: 'desktop-master'}),
    );
    await this.putDirect(['lock.json'], JSON.stringify({holder: this.deviceId, ts: now(), ttl: 30}));
  }

  async publish(envelope: Envelope) {
    if (!this.config?.url) {
      this.enqueue(envelope, '未配置 WebDAV');
      this.online = false;
      this.lastError = '未配置 WebDAV';
      return {queued: true};
    }
    try {
      await this.publishOnce(envelope);
      this.online = true;
      this.lastError = null;
      this.lastSyncAt = now();
      // remove from outbox if previously queued
      this.outbox = this.outbox.filter((item) => item.id !== envelope.id);
      this.saveOutbox();
      return {queued: false};
    } catch (error) {
      this.online = false;
      this.lastError = error instanceof Error ? error.message : String(error);
      this.enqueue(envelope, this.lastError);
      return {queued: true};
    }
  }

  private enqueue(envelope: Envelope, lastError?: string) {
    const existing = this.outbox.find((x) => x.id === envelope.id);
    if (existing) {
      existing.attempts += 1;
      existing.nextAttemptAt = now() + Math.min(3600, 2 ** Math.min(existing.attempts, 8));
      existing.lastError = lastError;
    } else {
      this.outbox.push({
        id: envelope.id,
        envelope,
        attempts: 0,
        nextAttemptAt: now(),
        lastError,
      });
    }
    this.saveOutbox();
  }

  async flushOutbox() {
    if (!this.config?.url || !this.outbox.length) return;
    const remain: OutboxItem[] = [];
    for (const item of this.outbox) {
      if (item.nextAttemptAt > now()) {
        remain.push(item);
        continue;
      }
      try {
        await this.publishOnce(item.envelope);
      } catch (error) {
        remain.push({
          ...item,
          attempts: item.attempts + 1,
          nextAttemptAt: now() + Math.min(3600, 2 ** Math.min(item.attempts, 8)),
          lastError: error instanceof Error ? error.message : String(error),
        });
      }
    }
    this.outbox = remain;
    this.saveOutbox();
    if (!remain.length) {
      this.online = true;
      this.lastError = null;
      this.lastSyncAt = now();
    }
  }

  private parseEnvelope(raw: string): Envelope | null {
    try {
      const env = JSON.parse(raw) as Envelope;
      if (env.encrypted) env.body = decryptBody(env.body, this.channelKey);
      return env;
    } catch {
      return null;
    }
  }

  /** PROPFIND Depth:1 listing for remote WebDAV mailbox/*.json */
  private async listRemoteMailboxNames(): Promise<string[]> {
    if (!this.config?.url) return [];
    const url = joinUrl(this.config.url, 'pattern', 'mailbox');
    // Ensure trailing slash — some providers require it for collections
    const collection = url.endsWith('/') ? url : `${url}/`;
    const res = await fetch(collection, {
      method: 'PROPFIND',
      headers: {
        ...this.headers(),
        depth: '1',
        'content-type': 'application/xml; charset=utf-8',
      },
      body: `<?xml version="1.0" encoding="utf-8"?>
<d:propfind xmlns:d="DAV:">
  <d:prop><d:displayname/><d:getcontentlength/><d:resourcetype/></d:prop>
</d:propfind>`,
    });
    if (res.status === 404) return [];
    if (!res.ok) throw new Error(`WebDAV PROPFIND ${res.status}`);
    const xml = await res.text();
    return extractMailboxHrefs(xml, collection);
  }

  async listMailbox(): Promise<Envelope[]> {
    if (!this.config?.url) return [];
    if (isFileBackend(this.config.url)) {
      const dir = join(this.localPatternDir(), 'mailbox');
      if (!existsSync(dir)) return [];
      const files = readdirSync(dir).filter((f) => f.endsWith('.json'));
      const items: Envelope[] = [];
      for (const file of files) {
        const env = this.parseEnvelope(readFileSync(join(dir, file), 'utf8'));
        if (env) items.push(env);
      }
      return items.sort((a, b) => a.ts - b.ts || a.id.localeCompare(b.id));
    }

    const names = await this.listRemoteMailboxNames();
    const items: Envelope[] = [];
    for (const name of names) {
      const text = await this.get(['mailbox', name]);
      if (!text) continue;
      const env = this.parseEnvelope(text);
      if (env) items.push(env);
    }
    return items.sort((a, b) => a.ts - b.ts || a.id.localeCompare(b.id));
  }

  async pullIncoming(): Promise<Envelope[]> {
    const all = await this.listMailbox();
    const fresh = all.filter(
      (env) => env.from !== this.deviceId && !this.seenIds.has(env.id) && env.ts >= this.cursorTs,
    );
    let maxTs = this.cursorTs;
    for (const env of fresh) {
      this.seenIds.add(env.id);
      if (env.ts > maxTs) maxTs = env.ts;
    }
    // Also mark our own published ids as seen so re-list doesn't thrash
    for (const env of all) {
      if (env.from === this.deviceId) this.seenIds.add(env.id);
      if (env.ts > maxTs) maxTs = env.ts;
    }
    if (fresh.length || maxTs > this.cursorTs) {
      this.cursorTs = maxTs;
      this.saveCursor();
      await this.pushRemoteCursor().catch(() => {
        /* remote cursor is best-effort */
      });
    }
    return fresh;
  }

  private async pushRemoteCursor() {
    if (!this.config?.url) return;
    const payload = JSON.stringify({
      deviceId: this.deviceId,
      cursorTs: this.cursorTs,
      updatedAt: now(),
    });
    await this.putDirect(['cursors', `${this.deviceId}.json`], payload);
  }

  async sync(): Promise<Envelope[]> {
    if (!this.config?.url) {
      this.lastError = '未配置 WebDAV';
      this.online = false;
      return [];
    }
    try {
      await this.ensureDirs();
      await this.flushOutbox();
      await this.putDirect(
        ['devices', `${this.deviceId}.json`],
        JSON.stringify({id: this.deviceId, ts: now(), role: 'desktop-master'}),
      );
      await this.putDirect(['state', 'agenda.json'], JSON.stringify({ts: now(), eyes: 'idle'}));
      const incoming = await this.pullIncoming();
      this.online = true;
      this.lastError = null;
      this.lastSyncAt = now();
      return incoming;
    } catch (error) {
      this.online = false;
      this.lastError = error instanceof Error ? error.message : String(error);
      return [];
    }
  }

  async fetchEnvelopeRaw(id: string): Promise<Envelope | null> {
    const text = await this.get(['mailbox', `${id}.json`]);
    if (!text) return null;
    const env = JSON.parse(text) as Envelope;
    if (env.encrypted) env.body = decryptBody(env.body, this.channelKey);
    return env;
  }
}


/** Parse PROPFIND multistatus XML and return mailbox JSON filenames. */
export function extractMailboxHrefs(xml: string, collectionUrl: string): string[] {
  const hrefs = [...xml.matchAll(/<[^>]*:?href[^>]*>([^<]+)<\//gi)].map((m) =>
    decodeURIComponent(m[1].trim()),
  );
  const names = new Set<string>();
  const collectionPath = (() => {
    try {
      return new URL(collectionUrl).pathname.replace(/\/+$/, '');
    } catch {
      return collectionUrl.replace(/\/+$/, '');
    }
  })();

  for (const href of hrefs) {
    // Skip the collection itself
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
