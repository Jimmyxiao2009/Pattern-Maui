import {randomUUID} from 'node:crypto';
import {existsSync, mkdirSync, readFileSync, writeFileSync} from 'node:fs';
import {join} from 'node:path';
import type {ProactiveChain, ProactiveLogItem} from '@pattern/protocol';

export interface Impulse {
  type: string;
  score: number;
  payload: Record<string, unknown>;
  topicKey: string;
  reason: string;
}

export interface ProactiveConfig {
  enabled: boolean;
  paused: boolean;
  bedtimeHour: number;
  dailyQuotaEnabled: boolean;
  dailyQuota: number;
}

export type DeliverChannel = 'notification' | 'quick' | 'relay' | 'log';

const DEFAULT_CONFIG: ProactiveConfig = {
  enabled: true,
  paused: false,
  bedtimeHour: 23,
  dailyQuotaEnabled: true,
  // Keep autonomous nudges sparse; required reminders still bypass via force.
  dailyQuota: 6,
};

function dayKey(d = new Date()) {
  return `${d.getFullYear()}-${d.getMonth() + 1}-${d.getDate()}`;
}

export class ProactiveEngine {
  private config: ProactiveConfig;
  private log: ProactiveLogItem[] = [];
  private deliveredTopics = new Map<string, string>();
  private configPath: string;
  private logPath: string;
  private chainsPath: string;
  private chains: ProactiveChain[] = [];

  constructor(private dataDir: string) {
    mkdirSync(dataDir, {recursive: true});
    this.configPath = join(dataDir, 'proactive.json');
    this.logPath = join(dataDir, 'logs', 'proactive.jsonl');
    this.chainsPath = join(dataDir, 'proactive-chains.json');
    mkdirSync(join(dataDir, 'logs'), {recursive: true});
    this.config = this.loadConfig();
    this.log = this.loadLog();
    this.chains = this.loadChains();
    for (const item of this.log) {
      if (item.delivered) {
        const d = new Date(item.ts * 1000);
        this.deliveredTopics.set(item.topicKey, dayKey(d));
      }
    }
  }

  getConfig(): ProactiveConfig {
    return {...this.config};
  }

  setConfig(partial: Partial<ProactiveConfig>) {
    this.config = {
      ...this.config,
      ...partial,
      bedtimeHour: Math.min(23, Math.max(0, partial.bedtimeHour ?? this.config.bedtimeHour)),
    };
    writeFileSync(this.configPath, JSON.stringify(this.config, null, 2));
  }

  setPaused(paused: boolean) {
    this.setConfig({paused});
  }

  private loadConfig(): ProactiveConfig {
    if (!existsSync(this.configPath)) return {...DEFAULT_CONFIG};
    try {
      const raw = JSON.parse(readFileSync(this.configPath, 'utf8')) as Partial<ProactiveConfig> & {
        bedtime_hour?: number;
      };
      return {
        ...DEFAULT_CONFIG,
        ...raw,
        // Autonomous chains have a hard safety ceiling; legacy configs cannot disable it.
        dailyQuotaEnabled: true,
        dailyQuota: Math.min(8, Math.max(1, Number(raw.dailyQuota) || DEFAULT_CONFIG.dailyQuota)),
        bedtimeHour: raw.bedtimeHour ?? raw.bedtime_hour ?? DEFAULT_CONFIG.bedtimeHour,
      };
    } catch {
      return {...DEFAULT_CONFIG};
    }
  }

  private loadLog(): ProactiveLogItem[] {
    if (!existsSync(this.logPath)) return [];
    try {
      return readFileSync(this.logPath, 'utf8')
        .split('\n')
        .filter(Boolean)
        .map((line) => JSON.parse(line) as ProactiveLogItem)
        .slice(-500);
    } catch {
      return [];
    }
  }

  private appendLog(item: ProactiveLogItem) {
    this.log.push(item);
    if (this.log.length > 500) this.log = this.log.slice(-500);
    writeFileSync(this.logPath, this.log.map((x) => JSON.stringify(x)).join('\n') + '\n');
  }

  private loadChains(): ProactiveChain[] {
    if (!existsSync(this.chainsPath)) return [];
    try {
      const value = JSON.parse(readFileSync(this.chainsPath, 'utf8'));
      if (!Array.isArray(value)) return [];
      const normalized = value.filter((item): item is ProactiveChain => !!item && typeof item.id === 'string' && typeof item.purpose === 'string')
        .map((item) => ({
          ...item,
          kind: item.kind === 'required_reminder' ? 'required_reminder' : 'autonomous',
          status: ['active', 'running', 'completed', 'cancelled', 'failed'].includes(item.status) ? item.status : 'active',
          nextRunAt: typeof item.nextRunAt === 'number' ? item.nextRunAt : null,
          timezone: item.timezone || 'UTC',
          consecutiveSilentRuns: Number(item.consecutiveSilentRuns) || 0,
          failureCount: Number(item.failureCount) || 0,
          createdAt: Number(item.createdAt) || Date.now(),
          updatedAt: Number(item.updatedAt) || Date.now(),
        }))
        .sort((a, b) => b.updatedAt - a.updatedAt);
      // Keep live chains, but trim historical failed/completed noise so UI/data don't balloon.
      const live = normalized.filter((item) => item.status === 'active' || item.status === 'running');
      const history = normalized.filter((item) => item.status !== 'active' && item.status !== 'running').slice(0, 20);
      const pruned = [...live, ...history].slice(0, 80);
      if (pruned.length !== normalized.length) {
        writeFileSync(this.chainsPath, JSON.stringify(pruned, null, 2));
      }
      return pruned;
    } catch { return []; }
  }

  private saveChains() { writeFileSync(this.chainsPath, JSON.stringify(this.chains, null, 2)); }

  listChains(limit = 50): ProactiveChain[] { return [...this.chains].sort((a, b) => b.updatedAt - a.updatedAt).slice(0, limit); }

  createChain(input: Omit<ProactiveChain, 'id' | 'status' | 'consecutiveSilentRuns' | 'failureCount' | 'createdAt' | 'updatedAt' | 'lastRunAt'> & {id?: string}): ProactiveChain {
    const now = Date.now();
    const chain: ProactiveChain = {
      id: input.id || randomUUID(), kind: input.kind, status: 'active', purpose: input.purpose.slice(0, 500),
      context: input.context?.slice(0, 4000), nextRunAt: input.nextRunAt, timezone: input.timezone || 'UTC',
      sourceConversationId: input.sourceConversationId || null, recurrence: input.recurrence || null,
      consecutiveSilentRuns: 0, failureCount: 0, createdAt: now, updatedAt: now, lastRunAt: null,
    };
    const next = [chain, ...this.chains.filter((item) => item.id !== chain.id)];
    const live = next.filter((item) => item.status === 'active' || item.status === 'running');
    const history = next.filter((item) => item.status !== 'active' && item.status !== 'running').slice(0, 20);
    this.chains = [...live, ...history].slice(0, 80);
    this.saveChains();
    return chain;
  }

  cancelChain(id: string): ProactiveChain | null {
    const chain = this.chains.find((item) => item.id === id);
    if (!chain) return null;
    chain.status = 'cancelled'; chain.nextRunAt = null; chain.updatedAt = Date.now();
    this.saveChains(); return {...chain};
  }

  markDue(now = Date.now()): ProactiveChain[] {
    const due = this.chains.filter((item) => item.status === 'active' && item.nextRunAt !== null && item.nextRunAt <= now);
    for (const chain of due) { chain.status = 'running'; chain.lastRunAt = now; chain.updatedAt = now; }
    if (due.length) this.saveChains();
    return due.map((item) => ({...item}));
  }

  runNow(id: string): ProactiveChain | null {
    const chain = this.chains.find((item) => item.id === id && item.status === 'active');
    if (!chain) return null;
    chain.status = 'running'; chain.nextRunAt = null; chain.lastRunAt = Date.now(); chain.updatedAt = Date.now();
    this.saveChains(); return {...chain};
  }

  finishChainRun(id: string, result: {nextRunAt?: number | null; emitted?: boolean; failed?: boolean}) : ProactiveChain | null {
    const chain = this.chains.find((item) => item.id === id);
    if (!chain) return null;
    const now = Date.now();
    if (result.failed) chain.failureCount += 1;
    else chain.failureCount = 0;
    if (result.emitted) chain.consecutiveSilentRuns = 0;
    else if (!result.failed) chain.consecutiveSilentRuns += 1;
    const next = typeof result.nextRunAt === 'number' ? result.nextRunAt : null;
    if (result.failed && chain.failureCount >= 3) { chain.status = 'failed'; chain.nextRunAt = null; }
    else if (result.failed) {
      const retryMinutes = [5, 30, 120][Math.min(chain.failureCount - 1, 2)];
      chain.status = 'active'; chain.nextRunAt = now + retryMinutes * 60_000;
    }
    else if (next && next > now) { chain.status = 'active'; chain.nextRunAt = next; }
    else if (chain.recurrence?.kind === 'daily') { chain.status = 'active'; chain.nextRunAt = nextDaily(chain.recurrence.time, chain.timezone, now); }
    else { chain.status = 'completed'; chain.nextRunAt = null; }
    if (chain.consecutiveSilentRuns >= 10) { chain.status = 'completed'; chain.nextRunAt = null; }
    chain.updatedAt = now; this.saveChains(); return {...chain};
  }

  list(limit = 50): ProactiveLogItem[] {
    return this.log.slice(-limit).reverse();
  }

  todayCount() {
    const key = dayKey();
    return this.log.filter((item) => {
      const d = new Date(item.ts * 1000);
      return dayKey(d) === key && item.delivered;
    }).length;
  }

  admit(impulse: Impulse, opts?: {force?: boolean}): Impulse | null {
    if (opts?.force) return impulse;
    if (!this.config.enabled || this.config.paused) return null;
    const today = dayKey();
    if (this.deliveredTopics.get(impulse.topicKey) === today) return null;
    if (this.config.dailyQuotaEnabled && this.todayCount() >= this.config.dailyQuota) return null;
    if (impulse.score < 0.2) return null;
    return impulse;
  }

  markDelivered(impulse: Impulse, body: string, channel: DeliverChannel, extra: Partial<ProactiveLogItem> = {}): ProactiveLogItem {
    const item: ProactiveLogItem = {
      id: randomUUID(),
      type: impulse.type,
      topicKey: impulse.topicKey,
      reason: impulse.reason,
      body,
      channel,
      ts: Math.floor(Date.now() / 1000),
      delivered: true,
      origin: extra.origin || 'ai', state: extra.state || 'unread', chainId: extra.chainId,
    };
    this.deliveredTopics.set(impulse.topicKey, dayKey());
    this.appendLog(item);
    return item;
  }

  markInboxState(id: string, state: 'read' | 'dismissed' | 'replied'): ProactiveLogItem | null {
    const item = this.log.find((entry) => entry.id === id);
    if (!item) return null;
    item.state = state;
    writeFileSync(this.logPath, this.log.map((x) => JSON.stringify(x)).join('\n') + '\n');
    return {...item};
  }

  evaluateTriggers(input: {
    hour: number;
    idleSeconds: number;
    lastUserActivityAt: number;
    now: number;
  }): Impulse[] {
    const impulses: Impulse[] = [];
    const bedtime = this.config.bedtimeHour;
    const pastBed =
      bedtime <= 6 ? input.hour >= bedtime && input.hour < 8 : input.hour >= bedtime || input.hour < 5;

    if (pastBed) {
      const recentlyActive = input.now - input.lastUserActivityAt < 30 * 60;
      // if idle API unavailable (0 forever) still allow bedtime once user was active recently or always soft-fire
      const notIdleLong = input.idleSeconds < 20 * 60;
      if (recentlyActive || notIdleLong || input.idleSeconds === 0) {
        impulses.push({
          type: 'bedtime',
          score: 0.85,
          topicKey: `bedtime:${dayKey()}`,
          reason: `已过 ${String(bedtime).padStart(2, '0')}:00，检测到你可能还在活动`,
          payload: {hour: input.hour, idleSeconds: input.idleSeconds},
        });
      }
    }

    // One rest nudge per day. Hourly buckets previously spawned duplicate chains.
    if (input.idleSeconds < 60 && input.now - input.lastUserActivityAt > 50 * 60) {
      impulses.push({
        type: 'focus_break',
        score: 0.35,
        topicKey: `focus_break:${dayKey()}`,
        reason: '连续活动较久，可考虑短暂休息',
        payload: {},
      });
    }

    return impulses;
  }

  manualImpulse(input: {type: string; reason: string; topicKey?: string; payload?: Record<string, unknown>}): Impulse {
    return {
      type: input.type,
      score: 1,
      topicKey: input.topicKey || `manual:${input.type}:${Date.now()}`,
      reason: input.reason,
      payload: input.payload || {},
    };
  }
}

function nextDaily(time: string, _timezone: string, now: number) {
  const match = /^(\d{2}):(\d{2})$/.exec(time);
  const date = new Date(now);
  if (!match) return now + 24 * 60 * 60 * 1000;
  date.setHours(Number(match[1]), Number(match[2]), 0, 0);
  if (date.getTime() <= now) date.setDate(date.getDate() + 1);
  return date.getTime();
}
