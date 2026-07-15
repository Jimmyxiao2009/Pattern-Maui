<script lang="ts">
  import {onMount} from 'svelte';
  import {MessageCircle, Settings, RefreshCw, Send, CheckCircle2, WifiOff, ListTodo, Camera, Plus} from 'lucide-svelte';
  import QrScanner from 'qr-scanner';
  import QRCode from 'qrcode';
  import {createSecurePairingRequest,openSecurePairingResponse} from '@pattern/relay/pairing';
  import {loadSettings, saveSettings, newDeviceId, publish, pull, parsePairingCode, type RelayEnvelope, type RelaySettings} from './lib/relay';

  const storedSettings=loadSettings();
  const storedMessages=(()=>{try{return JSON.parse(localStorage.getItem('pattern-mobile-messages')||'[]') as RelayEnvelope[]}catch{return []}})();
  let settings=$state<RelaySettings | null>(storedSettings);
  let tab=$state<'chat'|'tasks'|'settings'>('chat'); let draft=$state(''); let busy=$state(false); let error=$state(''); let messages=$state<RelayEnvelope[]>(storedMessages); let lastSync=$state<number|null>(null);
  let taskTitle=$state(''); let taskDetail=$state(''); let pairingCode=$state('');
  let pairingRequestCode=$state('');let pairingRequestQr=$state('');let pairingPrivateKey=$state(sessionStorage.getItem('pattern-pair-private')||'');
  type TaskSummary={id:string;title:string;detail?:string;status:string;error?:string;riskTier?:number};
  let form=$state<RelaySettings>(storedSettings||{url:'',username:'',password:'',channelKey:'',deviceId:newDeviceId()});
  function persistMessages(){localStorage.setItem('pattern-mobile-messages',JSON.stringify(messages.slice(-2000)))}
  async function sync(){if(!settings)return;busy=true;error='';try{messages=[...messages,...await pull(settings)].filter((item,index,all)=>all.findIndex(x=>x.id===item.id)===index);persistMessages();lastSync=Date.now()}catch(value){error=String(value)}finally{busy=false}}
  async function send(){if(!settings||!draft.trim())return;busy=true;try{const item=await publish(settings,{role:'user',type:'chat',body:draft.trim()});messages=[...messages,item];persistMessages();draft='';await sync()}catch(value){error=String(value)}finally{busy=false}}
  async function createTask(){if(!settings||!taskTitle.trim())return;busy=true;try{const item=await publish(settings,{role:'user',type:'task',body:JSON.stringify({version:1,action:'create',title:taskTitle.trim(),detail:taskDetail.trim()})});messages=[...messages,item];persistMessages();taskTitle='';taskDetail='';await sync()}catch(value){error=String(value)}finally{busy=false}}
  function save(){if(!form.url.trim()||!form.channelKey.trim()){error='请填写 WebDAV 地址和配对密钥';return}settings={...form,url:form.url.trim(),channelKey:form.channelKey.trim()};saveSettings(settings);tab='chat';void sync()}
  function applyPairing(raw:string){try{let value:any;if(pairingPrivateKey){try{value=openSecurePairingResponse(raw,pairingPrivateKey)}catch{value=parsePairingCode(raw)}}else value=parsePairingCode(raw);form={...form,url:value.webdavUrl,username:value.username,password:value.password,channelKey:value.channelKey};pairingCode='';pairingPrivateKey='';sessionStorage.removeItem('pattern-pair-private');error='';save()}catch(value){error=`配对码无效：${value}`}}
  async function generatePairingRequest(){try{const result=createSecurePairingRequest(form.deviceId);pairingPrivateKey=result.privateKey;pairingRequestCode=result.code;sessionStorage.setItem('pattern-pair-private',result.privateKey);pairingRequestQr=await QRCode.toDataURL(result.code,{width:280,margin:2,errorCorrectionLevel:'M'});error='';}catch(value){error=`无法生成安全配对请求：${value}`}}
  async function copyRequest(){if(pairingRequestCode)await navigator.clipboard.writeText(pairingRequestCode)}
  async function scanPairing(event:Event){const file=(event.currentTarget as HTMLInputElement).files?.[0];if(!file)return;busy=true;try{const result=await QrScanner.scanImage(file,{returnDetailedScanResult:true});applyPairing(result.data)}catch(value){error=`无法识别二维码：${value}`}finally{busy=false}}
  function taskData(message:RelayEnvelope):{task?:TaskSummary;updatedAt?:number}|null{try{return JSON.parse(message.body) as {task?:TaskSummary;updatedAt?:number};}catch{return null}}
  const taskMessages=$derived.by(()=>{const latest=new Map<string,{task:TaskSummary;updatedAt:number}>();for(const item of messages){const data=taskData(item);if(!data?.task)continue;const previous=latest.get(data.task.id);const updatedAt=data.updatedAt??item.ts*1000;if(!previous||updatedAt>=previous.updatedAt)latest.set(data.task.id,{task:data.task,updatedAt})}return [...latest.values()].sort((a,b)=>b.updatedAt-a.updatedAt).map((item)=>item.task)});
  onMount(()=>{let timer:number;let stopped=false;const schedule=()=>{if(stopped)return;timer=window.setTimeout(async()=>{await sync();schedule()},document.hidden?60_000:10_000)};const wake=()=>{clearTimeout(timer);void sync().finally(schedule)};void sync().finally(schedule);document.addEventListener('visibilitychange',wake);window.addEventListener('online',wake);return()=>{stopped=true;clearTimeout(timer);document.removeEventListener('visibilitychange',wake);window.removeEventListener('online',wake)}})
</script>

<main>
  <header><div><span class="eye"></span><strong>Pattern</strong><small>Mobile</small></div><button aria-label="同步" onclick={sync} disabled={busy}><RefreshCw size={17}/></button></header>
  {#if tab==='chat'}
    <section class="chat"><div class="chat-head"><div><p>远程对话</p><h1>{settings?'Desktop 主控':'尚未配对'}</h1></div>{#if settings}<span class:offline={!!error}>{#if error}<WifiOff size={14}/>{:else}<CheckCircle2 size={14}/>{/if} {error?'离线':'已连接'}</span>{/if}</div>
      {#if settings}<div class="stream">{#each messages as message}<article class:user={message.role==='user'} class:proactive={message.type==='proactive'}><small>{message.role==='user'?'你':'Pattern'} · {new Date(message.ts*1000).toLocaleTimeString('zh-CN',{hour:'2-digit',minute:'2-digit'})}</small><p>{message.body}</p></article>{:else}<div class="empty">等待桌面端通过 WebDAV 发来消息。</div>{/each}</div><div class="composer"><textarea bind:value={draft} rows="2" placeholder="给 Pattern 留言……" onkeydown={(event)=>{if(event.key==='Enter'&&!event.shiftKey){event.preventDefault();void send()}}}></textarea><button onclick={send} disabled={busy||!draft.trim()}><Send size={18}/></button></div>{:else}<div class="empty big"><MessageCircle size={28}/><p>先在「配对」中填入桌面端的 WebDAV 中继信息。</p><button onclick={()=>tab='settings'}>开始配对</button></div>{/if}
    </section>
  {:else if tab==='tasks'}
    <section class="tasks"><div class="chat-head"><div><p>执行槽</p><h1>远程任务</h1></div></div>
      {#if settings}<div class="task-create"><label>任务名称<input bind:value={taskTitle} placeholder="例如：整理下载目录" /></label><label>补充说明<textarea bind:value={taskDetail} rows="2"></textarea></label><button class="primary" onclick={createTask} disabled={busy||!taskTitle.trim()}><Plus size={16}/>下达任务</button></div>{/if}
      <div class="task-list">{#each taskMessages as task}<article><div><strong>{task.title}</strong><span>{task.status}{#if task.riskTier!==undefined} · T{task.riskTier}{/if}</span></div>{#if task.detail}<p>{task.detail}</p>{/if}{#if task.error}<p class="error">{task.error}</p>{/if}</article>{:else}<div class="empty">尚无从桌面同步的任务状态。</div>{/each}</div>
    </section>
  {:else}
    <section class="settings"><p>配对</p><h1>连接桌面主控</h1><label>WebDAV 地址<input bind:value={form.url} placeholder="https://dav.example.com/..."/></label><label>用户名<input bind:value={form.username}/></label><label>密码<input bind:value={form.password} type="password"/></label><label>配对密钥<input bind:value={form.channelKey} type="password" placeholder="从桌面端导出"/></label><label>本设备 ID<input bind:value={form.deviceId}/></label><p class="hint">密钥仅保存在本设备。WebDAV 中的消息内容为 AES-GCM 密文。</p>{#if error}<p class="error">{error}</p>{/if}<button class="primary" onclick={save}>保存并连接</button>
      <div class="pair-tools"><button class="primary" onclick={generatePairingRequest}>1. 生成 X25519 配对请求</button>{#if pairingRequestQr}<img class="request-qr" src={pairingRequestQr} alt="手机安全配对请求二维码"/><label>请求码<textarea readonly value={pairingRequestCode} rows="4"></textarea></label><button onclick={copyRequest}>复制请求码到桌面</button>{/if}<label class="scan-button"><Camera size={16}/>2. 扫描桌面加密响应<input type="file" accept="image/*" capture="environment" onchange={scanPairing}/></label><label>或粘贴加密响应码<textarea bind:value={pairingCode} rows="3" placeholder="pattern://pair?data=…"></textarea></label><button onclick={()=>applyPairing(pairingCode)} disabled={!pairingCode.trim()}>完成安全配对</button></div>
    </section>
  {/if}
  <nav><button class:active={tab==='chat'} onclick={()=>tab='chat'}><MessageCircle size={19}/>对话</button><button class:active={tab==='tasks'} onclick={()=>tab='tasks'}><ListTodo size={19}/>任务</button><button class:active={tab==='settings'} onclick={()=>tab='settings'}><Settings size={19}/>配对</button></nav>
</main>
