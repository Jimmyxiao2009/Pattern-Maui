/** Common contract and built-in adapters for optional delivery channels. */
import {existsSync, readdirSync, readFileSync, realpathSync} from 'node:fs';
import {relative, resolve} from 'node:path';
import {pathToFileURL} from 'node:url';
export interface ChannelMessage { id: string; text: string; type: 'chat' | 'proactive' | 'task'; ts: number; source?: string; }
export interface ChannelCapabilities { inbound: boolean; outbound: boolean; requiresApprovalForPeople: boolean; }
export interface Channel { readonly id: string; readonly capabilities: ChannelCapabilities; send(message: ChannelMessage): Promise<void>; onMessage(listener: (message: ChannelMessage) => void): () => void; }

/**
 * Manifest for a locally-installed third-party channel. Pattern deliberately
 * does not execute a plugin while discovering it: a user must enable it in
 * the desktop UI before its entry module is imported.
 */
export interface ChannelPluginManifest {
  id: string;
  name: string;
  version: string;
  entry: string;
  description?: string;
}

export interface DiscoveredChannelPlugin {
  manifest: ChannelPluginManifest;
  directory: string;
  entryPath: string;
}

export type ChannelPluginFactory = (input: {
  id: string;
  config: unknown;
  dataDir: string;
  log: (message: string, error?: unknown) => void;
}) => Channel | Promise<Channel>;

const PLUGIN_ID = /^[a-z0-9][a-z0-9._-]{1,63}$/;

function pluginPathInside(directory: string, entry: string) {
  if (!entry || entry.includes('\0')) return null;
  const root = realpathSync(directory);
  const candidate = resolve(root, entry);
  if (!existsSync(candidate)) return null;
  const resolvedCandidate = realpathSync(candidate);
  const rel = relative(root, resolvedCandidate);
  if (rel.startsWith('..') || rel === '' || /^[\\/]/.test(rel)) return null;
  return resolvedCandidate;
}

/** Discover manifests in `<dataDir>/plugins/<plugin>/pattern.channel.json`. */
export function discoverChannelPlugins(pluginRoot: string): DiscoveredChannelPlugin[] {
  if (!existsSync(pluginRoot)) return [];
  const seen = new Set<string>();
  const plugins: DiscoveredChannelPlugin[] = [];
  for (const name of readdirSync(pluginRoot, {withFileTypes: true})) {
    if (!name.isDirectory()) continue;
    const directory = resolve(pluginRoot, name.name);
    const manifestFile = resolve(directory, 'pattern.channel.json');
    if (!existsSync(manifestFile)) continue;
    try {
      const value = JSON.parse(readFileSync(manifestFile, 'utf8')) as Partial<ChannelPluginManifest>;
      if (!PLUGIN_ID.test(String(value.id || '')) || !value.name?.trim() || !value.version?.trim() || !value.entry?.trim()) continue;
      const id = String(value.id);
      const nameValue = value.name.trim();
      const version = value.version.trim();
      const entry = value.entry.trim();
      const entryPath = pluginPathInside(directory, entry);
      if (!entryPath || !existsSync(entryPath) || seen.has(id)) continue;
      seen.add(id);
      plugins.push({
        manifest: {id, name: nameValue, version, entry, description: value.description?.trim() || undefined},
        directory,
        entryPath,
      });
    } catch {
      // An invalid manifest must not prevent other plugins from being listed.
    }
  }
  return plugins.sort((a, b) => a.manifest.name.localeCompare(b.manifest.name));
}

/** Import and validate one previously-discovered, explicitly enabled plugin. */
export async function loadChannelPlugin(
  plugin: DiscoveredChannelPlugin,
  config: unknown,
  dataDir: string,
  log: (message: string, error?: unknown) => void = console.warn,
): Promise<Channel> {
  const loaded = await import(pathToFileURL(plugin.entryPath).href);
  const factory = (loaded.createChannel || loaded.default?.createChannel || loaded.default) as ChannelPluginFactory | undefined;
  if (typeof factory !== 'function') throw new Error(`plugin ${plugin.manifest.id} does not export createChannel`);
  const channel = await factory({id: plugin.manifest.id, config, dataDir, log});
  if (!channel || typeof channel.id !== 'string' || typeof channel.send !== 'function' || typeof channel.onMessage !== 'function' || !channel.capabilities) {
    throw new Error(`plugin ${plugin.manifest.id} returned an invalid channel`);
  }
  if (channel.id !== plugin.manifest.id) throw new Error(`plugin id mismatch: expected ${plugin.manifest.id}, got ${channel.id}`);
  return channel;
}

type FetchLike = typeof fetch;
type TelegramUpdate = {update_id:number;message?:{text?:string;chat?:{id:string|number}}};

export class TelegramChannel implements Channel {
  readonly id = 'telegram';
  readonly capabilities = {inbound:true, outbound:true, requiresApprovalForPeople:true};
  private listeners = new Set<(message:ChannelMessage)=>void>();
  private offset = 0;

  constructor(private token:string, private chatId:string, private fetcher:FetchLike=fetch, offset=0) { this.offset=offset; }
  update(token:string, chatId:string) { this.token=token; this.chatId=chatId; }
  getOffset() { return this.offset; }
  onMessage(listener:(message:ChannelMessage)=>void) { this.listeners.add(listener); return ()=>this.listeners.delete(listener); }
  async send(message:ChannelMessage) {
    if (!this.token || !this.chatId) throw new Error('Telegram 未配置');
    const response=await this.fetcher(`https://api.telegram.org/bot${this.token}/sendMessage`,{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify({chat_id:this.chatId,text:message.text})});
    if(!response.ok)throw new Error(`Telegram ${response.status}`);
  }
  async poll() {
    if (!this.token || !this.chatId) return [] as ChannelMessage[];
    const response=await this.fetcher(`https://api.telegram.org/bot${this.token}/getUpdates?offset=${this.offset}&timeout=0`);
    if(!response.ok)throw new Error(`Telegram ${response.status}`);
    const payload=await response.json() as {result?:TelegramUpdate[]};
    const messages:ChannelMessage[]=[];
    for(const update of payload.result??[]){
      this.offset=Math.max(this.offset,update.update_id+1);
      const text=update.message?.text?.trim();
      if(!text||String(update.message?.chat?.id)!==String(this.chatId))continue;
      const message={id:`telegram:${update.update_id}`,text,type:'chat' as const,ts:Math.floor(Date.now()/1000),source:'telegram'};
      messages.push(message); for(const listener of this.listeners)listener(message);
    }
    return messages;
  }
}

export interface SmtpConfig {host:string;port:number;secure:boolean;username:string;password:string;recipient:string;}
export type MailTransport = {sendMail(input:{from:string;to:string;subject:string;text:string}):Promise<unknown>};

export class SmtpChannel implements Channel {
  readonly id='smtp';
  readonly capabilities={inbound:false,outbound:true,requiresApprovalForPeople:true};
  constructor(private config:SmtpConfig, private transport:MailTransport) {}
  update(config:SmtpConfig, transport:MailTransport) { this.config=config; this.transport=transport; }
  onMessage() { return ()=>undefined; }
  async send(message:ChannelMessage) {
    await this.transport.sendMail({from:this.config.username,to:this.config.recipient,subject:message.type==='task'?'Pattern · 任务消息':'Pattern · 主动消息',text:message.text});
  }
}

export async function createSmtpChannel(config:SmtpConfig) {
  const nodemailer=await import('nodemailer');
  const transport=nodemailer.createTransport({host:config.host,port:config.port||587,secure:config.secure,auth:{user:config.username,pass:config.password}}) as MailTransport;
  return new SmtpChannel(config,transport);
}

export interface ImapConfig {host:string;port:number;secure:boolean;username:string;password:string;mailbox?:string;}
export class ImapChannel implements Channel {
  readonly id='imap';
  readonly capabilities={inbound:true,outbound:false,requiresApprovalForPeople:false};
  private listeners=new Set<(message:ChannelMessage)=>void>();
  constructor(private config:ImapConfig) {}
  onMessage(listener:(message:ChannelMessage)=>void){this.listeners.add(listener);return()=>this.listeners.delete(listener);}
  async send(){throw new Error('IMAP 通道不支持出站');}
  async poll(){
    const [{ImapFlow},{simpleParser}]=await Promise.all([import('imapflow'),import('mailparser')]);
    const client=new ImapFlow({host:this.config.host,port:this.config.port||993,secure:this.config.secure,auth:{user:this.config.username,pass:this.config.password},logger:false});
    const messages:ChannelMessage[]=[];
    await client.connect();
    try{
      const lock=await client.getMailboxLock(this.config.mailbox||'INBOX');
      try{
        for await(const item of client.fetch({seen:false},{uid:true,envelope:true,source:true})){
          const parsed=await simpleParser(item.source);
          const sender=parsed.from?.text||item.envelope?.from?.[0]?.address||'unknown';
          const subject=parsed.subject||item.envelope?.subject||'(无主题)';
          const body=(parsed.text||parsed.html||'').toString().trim();
          const message={id:`imap:${item.uid}`,text:`邮件来自 ${sender}\n主题：${subject}\n\n${body}`.slice(0,20000),type:'chat' as const,ts:Math.floor(Date.now()/1000),source:'imap'};
          messages.push(message);for(const listener of this.listeners)listener(message);
          await client.messageFlagsAdd(item.uid,['\\Seen'],{uid:true});
        }
      }finally{lock.release();}
    }finally{await client.logout().catch(()=>undefined);}
    return messages;
  }
}

export function channelMessage(text:string,type:ChannelMessage['type']='chat',source?:string):ChannelMessage{return{id:crypto.randomUUID(),text,type,ts:Math.floor(Date.now()/1000),source}}
