export type RelayEnvelope = {id:string;from:string;role:'companion'|'user'|'system';type:'chat'|'proactive'|'task';ts:number;body:string;encrypted?:boolean};
export type RelaySettings = {url:string;username:string;password:string;channelKey:string;deviceId:string};
export type PairingPayload = {version:1;webdavUrl:string;username:string;password:string;channelKey:string};

const encoder = new TextEncoder();
const decoder = new TextDecoder();
const keyName = 'pattern-mobile-relay';
const cursorName = 'pattern-mobile-cursor';

export function loadSettings(): RelaySettings | null { try{return JSON.parse(localStorage.getItem(keyName)||'null')}catch{return null} }
export function saveSettings(value:RelaySettings){localStorage.setItem(keyName,JSON.stringify(value))}
export function newDeviceId(){return crypto.randomUUID().replace(/-/g,'')}

function b64url(bytes:Uint8Array){let raw='';for(const byte of bytes)raw+=String.fromCharCode(byte);return btoa(raw).replace(/\+/g,'-').replace(/\//g,'_').replace(/=+$/,'')}
function fromB64url(input:string){const padded=input.replace(/-/g,'+').replace(/_/g,'/')+'='.repeat((4-input.length%4)%4);const raw=atob(padded);return Uint8Array.from(raw,c=>c.charCodeAt(0))}
async function key(secret:string){return crypto.subtle.importKey('raw',await crypto.subtle.digest('SHA-256',encoder.encode(secret)),{name:'AES-GCM'},false,['encrypt','decrypt'])}
// Wire format matches packages/relay: iv (12) + auth tag (16) + ciphertext.
export async function encrypt(plain:string,secret:string){const iv=crypto.getRandomValues(new Uint8Array(12));const sealed=new Uint8Array(await crypto.subtle.encrypt({name:'AES-GCM',iv},await key(secret),encoder.encode(plain)));const tag=sealed.slice(-16);const cipher=sealed.slice(0,-16);return b64url(new Uint8Array([...iv,...tag,...cipher]))}
export async function decrypt(payload:string,secret:string){const value=fromB64url(payload);if(value.length<28)throw new Error('无效的中继密文');const iv=value.slice(0,12);const tag=value.slice(12,28);const cipher=value.slice(28);const sealed=new Uint8Array([...cipher,...tag]);return decoder.decode(await crypto.subtle.decrypt({name:'AES-GCM',iv},await key(secret),sealed))}

export function parsePairingCode(raw:string):PairingPayload {const value=raw.trim();const encoded=value.startsWith('pattern://pair?data=')?value.slice('pattern://pair?data='.length):value;const parsed=JSON.parse(decoder.decode(fromB64url(decodeURIComponent(encoded)))) as PairingPayload;if(parsed.version!==1||!parsed.webdavUrl||!parsed.channelKey)throw new Error('不支持或不完整的配对码');return parsed}

function root(settings:RelaySettings){return `${settings.url.replace(/\/+$/,'')}/pattern`}
function headers(settings:RelaySettings, extra:Record<string,string>={}){return {...extra,authorization:`Basic ${btoa(`${settings.username}:${settings.password}`)}`}}
async function ensure(settings:RelaySettings){for(const part of ['', 'devices','mailbox','cursors','state']) await fetch(`${root(settings)}/${part}`,{method:'MKCOL',headers:headers(settings)}).catch(()=>undefined)}
function cursor(){try{return new Set<string>(JSON.parse(localStorage.getItem(cursorName)||'[]'))}catch{return new Set<string>()}}
function saveCursor(ids:Set<string>){localStorage.setItem(cursorName,JSON.stringify([...ids].slice(-5000)))}

export async function publish(settings:RelaySettings,input:Omit<RelayEnvelope,'id'|'from'|'ts'|'encrypted'>){await ensure(settings);const envelope:RelayEnvelope={...input,id:crypto.randomUUID().replace(/-/g,'').slice(0,26),from:settings.deviceId,ts:Math.floor(Date.now()/1000),encrypted:true};const wire={...envelope,body:await encrypt(envelope.body,settings.channelKey)};const response=await fetch(`${root(settings)}/mailbox/${envelope.id}.json`,{method:'PUT',headers:headers(settings,{'content-type':'application/json'}),body:JSON.stringify(wire)});if(!response.ok)throw new Error(`WebDAV PUT ${response.status}`);await fetch(`${root(settings)}/devices/${settings.deviceId}.json`,{method:'PUT',headers:headers(settings,{'content-type':'application/json'}),body:JSON.stringify({id:settings.deviceId,ts:envelope.ts,role:'mobile-client'})});return envelope}

function names(xml:string){return [...xml.matchAll(/<[^>]*:?href[^>]*>([^<]+)<\//gi)].map(x=>decodeURIComponent(x[1])).filter(x=>x.toLowerCase().endsWith('.json')).map(x=>x.split('/').pop()!).filter(Boolean)}
export async function pull(settings:RelaySettings){await ensure(settings);const collection=`${root(settings)}/mailbox/`;const listed=await fetch(collection,{method:'PROPFIND',headers:headers(settings,{depth:'1','content-type':'application/xml'}),body:'<?xml version="1.0"?><d:propfind xmlns:d="DAV:"><d:prop><d:displayname/></d:prop></d:propfind>'});if(!listed.ok)throw new Error(`WebDAV PROPFIND ${listed.status}`);const seen=cursor();const out:RelayEnvelope[]=[];for(const name of names(await listed.text())){const response=await fetch(`${collection}${encodeURIComponent(name)}`,{headers:headers(settings)});if(!response.ok)continue;try{const wire=await response.json() as RelayEnvelope;if(wire.from===settings.deviceId||seen.has(wire.id))continue;if(wire.encrypted)wire.body=await decrypt(wire.body,settings.channelKey);seen.add(wire.id);out.push(wire)}catch{}}
saveCursor(seen);await fetch(`${root(settings)}/cursors/${settings.deviceId}.json`,{method:'PUT',headers:headers(settings,{'content-type':'application/json'}),body:JSON.stringify({deviceId:settings.deviceId,updatedAt:Math.floor(Date.now()/1000)})});return out.sort((a,b)=>a.ts-b.ts||a.id.localeCompare(b.id))}
