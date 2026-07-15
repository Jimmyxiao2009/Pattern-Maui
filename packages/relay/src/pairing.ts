import {x25519} from '@noble/curves/ed25519.js';
import {xchacha20poly1305} from '@noble/ciphers/chacha.js';
import {sha256} from '@noble/hashes/sha2.js';
import {randomBytes} from '@noble/hashes/utils.js';

const encoder=new TextEncoder();const decoder=new TextDecoder();
function b64(bytes:Uint8Array){let value='';for(const byte of bytes)value+=String.fromCharCode(byte);return btoa(value).replace(/\+/g,'-').replace(/\//g,'_').replace(/=+$/,'')}
function unb64(value:string){const padded=value.replace(/-/g,'+').replace(/_/g,'/')+'='.repeat((4-value.length%4)%4);const raw=atob(padded);return Uint8Array.from(raw,(char)=>char.charCodeAt(0))}
function encode(value:unknown){return b64(encoder.encode(JSON.stringify(value)))}
function decode<T>(value:string){return JSON.parse(decoder.decode(unb64(value))) as T}
function unwrap(code:string){const trimmed=code.trim();const marker='pattern://pair?data=';return decodeURIComponent(trimmed.startsWith(marker)?trimmed.slice(marker.length):trimmed)}
function wrap(value:unknown){return `pattern://pair?data=${encodeURIComponent(encode(value))}`}

export type SecurePairingRequest={version:2;kind:'request';deviceId:string;publicKey:string};
export type SecurePairingResponse={version:2;kind:'response';deviceId:string;publicKey:string;nonce:string;ciphertext:string};

export function createSecurePairingRequest(deviceId:string){const privateKey=x25519.utils.randomSecretKey();const request:SecurePairingRequest={version:2,kind:'request',deviceId,publicKey:b64(x25519.getPublicKey(privateKey))};return{code:wrap(request),privateKey:b64(privateKey),request}}
export function parseSecurePairingRequest(code:string){const request=decode<SecurePairingRequest>(unwrap(code));if(request.version!==2||request.kind!=='request'||!request.publicKey)throw new Error('无效的安全配对请求');return request}
export function createSecurePairingResponse(code:string,payload:unknown,desktopDeviceId:string){const request=parseSecurePairingRequest(code);const privateKey=x25519.utils.randomSecretKey();const publicKey=x25519.getPublicKey(privateKey);const shared=x25519.getSharedSecret(privateKey,unb64(request.publicKey));const key=sha256(new Uint8Array([...shared,...encoder.encode('pattern-pair-v2')]));const nonce=randomBytes(24);const cipher=xchacha20poly1305(key,nonce).encrypt(encoder.encode(JSON.stringify(payload)));const response:SecurePairingResponse={version:2,kind:'response',deviceId:desktopDeviceId,publicKey:b64(publicKey),nonce:b64(nonce),ciphertext:b64(cipher)};return{code:wrap(response),response}}
export function openSecurePairingResponse(code:string,privateKey:string){const response=decode<SecurePairingResponse>(unwrap(code));if(response.version!==2||response.kind!=='response')throw new Error('无效的安全配对响应');const shared=x25519.getSharedSecret(unb64(privateKey),unb64(response.publicKey));const key=sha256(new Uint8Array([...shared,...encoder.encode('pattern-pair-v2')]));const plain=xchacha20poly1305(key,unb64(response.nonce)).decrypt(unb64(response.ciphertext));return JSON.parse(decoder.decode(plain));}
