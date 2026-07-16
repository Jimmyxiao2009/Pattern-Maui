<script lang="ts">
  import {onMount} from 'svelte';
  import {Monitor, Smartphone, Radio, AtSign, Save, X, RefreshCw} from 'lucide-svelte';
  import PageHeader from './PageHeader.svelte';
  import Toggle from './Toggle.svelte';
  import {runtime} from './runtime';
  import QRCode from 'qrcode';
  import {createSecurePairingResponse} from '@pattern/relay/pairing';
  import QrScanner from 'qr-scanner';

  let {notify}: {notify: (message: string) => void} = $props();
  let configured = $state(false);
  let editing = $state(false);
  let webdavUrl = $state('');
  let username = $state('');
  let password = $state('');
  let error = $state('');
  let relayOnline = $state(false);
  let outboxCount = $state(0);
  let lastSyncAt = $state<number | null>(null);
  let relayError = $state<string | null>(null);
  let telegramEnabled = $state(false);
  let telegramChatId = $state('');
  let telegramToken = $state('');
  let emailEnabled = $state(false);
  let emailHost = $state('');
  let emailPort = $state(587);
  let emailSecure = $state(false);
  let emailUsername = $state('');
  let emailRecipient = $state('');
  let smtpPassword = $state('');
  let emailImapEnabled = $state(false);
  let emailImapHost = $state('');
  let emailImapPort = $state(993);
  let emailImapSecure = $state(true);
  let pairing = $state<{webdavUrl:string;username:string;password:string;channelKey:string}|null>(null);
  let pairingCode = $state('');
  let pairingQr = $state('');
  let pairingRequest = $state('');
  type PluginConfig = {id:string;enabled:boolean;config?:unknown};
  type PluginInfo = {id:string;name:string;version:string;description:string};
  let availablePlugins = $state<PluginInfo[]>([]);
  let pluginConfigs = $state<Record<string, PluginConfig>>({});
  let pluginConfigText = $state<Record<string, string>>({});

  async function refreshRelay() {
    if (!(await runtime.connect())) return;
    const res = await runtime.request<any>({type: 'relay.status', id: crypto.randomUUID()});
    if (res.type === 'relay.status.result') {
      configured = res.status.configured;
      relayOnline = res.status.online;
      outboxCount = res.status.outboxCount;
      lastSyncAt = res.status.lastSyncAt;
      relayError = res.status.error ?? null;
    }
  }

  onMount(async () => {
    if ((window as any).__TAURI_INTERNALS__) {
      try {
        const {invoke} = await import('@tauri-apps/api/core');
        const config = await invoke<{webdavUrl: string; username: string; telegramEnabled?: boolean; telegramChatId?: string; emailEnabled?:boolean; emailHost?:string; emailPort?:number; emailSecure?:boolean; emailUsername?:string; emailRecipient?:string;emailImapEnabled?:boolean;emailImapHost?:string;emailImapPort?:number;emailImapSecure?:boolean;plugins?:PluginConfig[]} | null>('load_channel_config');
        if (config) {
          webdavUrl = config.webdavUrl;
          username = config.username;
          configured = !!config.webdavUrl;
          telegramEnabled = !!config.telegramEnabled;
          telegramChatId = config.telegramChatId || '';
          emailEnabled = !!config.emailEnabled; emailHost = config.emailHost || ''; emailPort = config.emailPort || 587; emailSecure = !!config.emailSecure; emailUsername = config.emailUsername || ''; emailRecipient = config.emailRecipient || '';
          emailImapEnabled=!!config.emailImapEnabled;emailImapHost=config.emailImapHost||'';emailImapPort=config.emailImapPort||993;emailImapSecure=config.emailImapSecure!==false;
          pluginConfigs=Object.fromEntries((config.plugins||[]).map((plugin)=>[plugin.id,plugin]));
          pluginConfigText=Object.fromEntries((config.plugins||[]).map((plugin)=>[plugin.id,JSON.stringify(plugin.config??{},null,2)]));
        }
        availablePlugins = await invoke<PluginInfo[]>('list_channel_plugins');
        for (const plugin of availablePlugins) if (!pluginConfigText[plugin.id]) pluginConfigText = {...pluginConfigText, [plugin.id]: '{}'};
      } catch (value) {
        console.error(value);
      }
    }
    void refreshRelay();
  });

  function isPluginEnabled(id:string){ return pluginConfigs[id]?.enabled===true; }
  function togglePlugin(id:string, enabled:boolean){
    pluginConfigs={...pluginConfigs,[id]:{id,enabled,config:pluginConfigs[id]?.config??{}}};
    if(!pluginConfigText[id])pluginConfigText={...pluginConfigText,[id]:'{}'};
  }

  async function save() {
    let plugins: PluginConfig[];
    try {
      plugins = availablePlugins.map((plugin) => {
        const current = pluginConfigs[plugin.id] || {id:plugin.id,enabled:false};
        if (!current.enabled) return {id:plugin.id, enabled:false};
        const value = JSON.parse(pluginConfigText[plugin.id] || '{}');
        if (!value || Array.isArray(value) || typeof value !== 'object') throw new Error(`${plugin.name} 的配置必须是 JSON 对象`);
        return {id:plugin.id, enabled:true, config:value};
      });
    } catch (value) { error=`插件配置无效：${value instanceof Error ? value.message : String(value)}`; return; }
    if (!webdavUrl.trim() && !(telegramEnabled && telegramChatId.trim()) && !(emailEnabled && emailHost.trim() && emailRecipient.trim()) && !plugins.some((plugin) => plugin.enabled)) { error = '至少配置 WebDAV、Telegram、SMTP 或一个本地插件'; return; }
    if (!(window as any).__TAURI_INTERNALS__) {
      configured = true;
      editing = false;
      notify('浏览器预览不会保存通道配置');
      return;
    }
    try {
      const {invoke} = await import('@tauri-apps/api/core');
      await invoke('save_channel_config', {
        config: {webdavUrl: webdavUrl.trim(), username: username.trim(), telegramEnabled, telegramChatId: telegramChatId.trim(), emailEnabled, emailHost:emailHost.trim(), emailPort, emailSecure, emailUsername:emailUsername.trim(), emailRecipient:emailRecipient.trim(),emailImapEnabled,emailImapHost:emailImapHost.trim(),emailImapPort,emailImapSecure,plugins},
        password: password || null,
        telegramToken: telegramToken || null,
        smtpPassword: smtpPassword || null,
      });
      configured = true;
      editing = false;
      password = '';
      telegramToken = '';
      smtpPassword = '';
      notify('Pattern Mobile 中继配置已保存');
      await refreshRelay();
    } catch (value) {
      error = `保存失败：${value}`;
    }
  }

  async function syncNow() {
    if (!(await runtime.connect())) {
      notify('运行时未连接');
      return;
    }
    await runtime.request({type: 'relay.syncNow', id: crypto.randomUUID()});
    await refreshRelay();
    notify('已触发同步');
  }

  async function showPairing() {
    if (!(window as any).__TAURI_INTERNALS__) { notify('请在桌面应用中查看配对信息'); return; }
    let plugins: PluginConfig[];
    try {
      plugins = availablePlugins.map((plugin) => {
        const current = pluginConfigs[plugin.id] || {id:plugin.id,enabled:false};
        if (!current.enabled) return {id:plugin.id, enabled:false};
        const value = JSON.parse(pluginConfigText[plugin.id] || '{}');
        if (!value || Array.isArray(value) || typeof value !== 'object') throw new Error(`${plugin.name} 的配置必须是 JSON 对象`);
        return {id:plugin.id, enabled:true, config:value};
      });
    } catch (value) { error=`插件配置无效：${value instanceof Error ? value.message : String(value)}`; return; }
    try {
      const {invoke} = await import('@tauri-apps/api/core');
      pairing = await invoke('relay_pairing_info');
      pairingRequest='';pairingCode='';pairingQr='';
    }
    catch (value) { error = `读取配对信息失败：${value}`; }
  }

  async function createSecureResponse(){
    if(!pairing||!pairingRequest.trim())return;
    try{const result=createSecurePairingResponse(pairingRequest,{version:2,webdavUrl:pairing.webdavUrl,username:pairing.username,password:pairing.password,channelKey:pairing.channelKey},crypto.randomUUID());pairingCode=result.code;pairingQr=await QRCode.toDataURL(pairingCode,{width:280,margin:2,errorCorrectionLevel:'M'});error='';}
    catch(value){error=`安全配对请求无效：${value}`;}
  }
  async function scanPairingRequest(event:Event){const file=(event.currentTarget as HTMLInputElement).files?.[0];if(!file)return;try{const result=await QrScanner.scanImage(file,{returnDetailedScanResult:true});pairingRequest=result.data;await createSecureResponse();}catch(value){error=`无法识别手机请求二维码：${value}`;}}
</script>

<section class="view">
  <PageHeader eyebrow="消息中继" title="通道" subtitle="在电脑之外，也能收到消息和下达任务。">
    <button class="quiet-button" onclick={syncNow}><RefreshCw size={14} />立即同步</button>
  </PageHeader>
  <div class="channel-list">
    <article>
      <div class="channel-icon"><Monitor size={19} /></div>
      <div>
        <h3>系统通知</h3>
        <p>本机主动提醒与任务结果</p>
      </div>
      <span class="online">本机可用</span>
      <Toggle checked={true} disabled={true} label="系统通知已启用" />
    </article>
    <article>
      <div class="channel-icon"><Smartphone size={19} /></div>
      <div>
        <h3>Pattern Mobile / WebDAV</h3>
        <p>
          {configured ? webdavUrl : '通过 WebDAV 进行端到端中继'}
          {#if configured}
            <br />
            <small>
              {relayOnline ? '在线' : '离线/未探测'} · outbox {outboxCount}
              {#if lastSyncAt} · 上次 {new Date(lastSyncAt * 1000).toLocaleString('zh-CN')}{/if}
              {#if relayError} · {relayError}{/if}
            </small>
          {/if}
        </p>
      </div>
      <span class:online={configured && relayOnline}>{configured ? (relayOnline ? '已同步' : '已配置') : '未配置'}</span>
      <div class="channel-actions"><button class="quiet-button" onclick={showPairing} disabled={!configured}>手动配对</button><button class="quiet-button" onclick={() => (editing = true)}>{configured ? '管理' : '配置'}</button></div>
    </article>
    <article>
      <div class="channel-icon"><Radio size={19} /></div>
      <div>
        <h3>Telegram</h3>
        <p>{telegramEnabled ? `已绑定 chat ${telegramChatId}` : '备用远程消息通道'}</p>
      </div>
      <span class:online={telegramEnabled}>{telegramEnabled ? '已启用' : '未配置'}</span>
      <button class="quiet-button" onclick={() => editing = true}>{telegramEnabled ? '管理' : '配置'}</button>
    </article>
    <article>
      <div class="channel-icon"><AtSign size={19} /></div>
      <div>
        <h3>邮件</h3>
        <p>{emailEnabled ? `SMTP → ${emailRecipient}` : '日报与低频正式消息'}</p>
      </div>
      <span class:online={emailEnabled}>{emailEnabled ? '已启用' : '未配置'}</span>
      <button class="quiet-button" onclick={() => editing = true}>{emailEnabled ? '管理' : '配置'}</button>
    </article>
  </div>
</section>

{#if editing}
  <div class="modal-backdrop" role="presentation" onclick={(event) => { if (event.target === event.currentTarget) editing = false; }}>
    <div class="memory-editor" role="dialog" aria-modal="true" aria-labelledby="channel-title">
      <header>
        <div>
          <p class="eyebrow">消息通道</p>
          <h2 id="channel-title">配置远程消息</h2>
        </div>
        <button aria-label="关闭" onclick={() => (editing = false)}><X size={16} /></button>
      </header>
      <label>WebDAV 地址（可选）<input bind:value={webdavUrl} placeholder="https://dav.example.com/remote.php/dav/files/user" /></label>
      <label>用户名<input bind:value={username} /></label>
      <label>密码<input type="password" bind:value={password} placeholder="留空则保持现有密码" /></label>
      <p class="field-help">密码写入系统凭据管理器。消息 body 使用本地 channel key 加密后上传。开发验收可用 local:C:\path\to\relay-root 作为本地目录后端。</p>
      <div class="telegram-enable"><Toggle checked={telegramEnabled} label="启用 Telegram Bot" onChange={(value) => telegramEnabled = value} />启用 Telegram Bot</div>
      {#if telegramEnabled}
        <label>Chat ID<input bind:value={telegramChatId} placeholder="与 Bot 开始对话后填写 chat id" /></label>
        <label>Bot Token<input type="password" bind:value={telegramToken} placeholder="留空则保留现有 Token" /></label>
        <p class="field-help">仅用于主动提醒和任务结果外发；Token 写入系统凭据管理器。</p>
      {/if}
      <div class="telegram-enable"><Toggle checked={emailEnabled} label="启用 SMTP 邮件通知" onChange={(value) => emailEnabled = value} />启用 SMTP 邮件通知</div>
      {#if emailEnabled}
        <label>SMTP 主机<input bind:value={emailHost} placeholder="smtp.example.com" /></label>
        <label>SMTP 端口<input type="number" bind:value={emailPort} /></label>
        <label>用户名/发件人<input bind:value={emailUsername} type="email" /></label>
        <label>收件人<input bind:value={emailRecipient} type="email" /></label>
        <label>SMTP 密码或应用专用密码<input type="password" bind:value={smtpPassword} placeholder="留空则保留现有密码" /></label>
        <div class="telegram-enable"><Toggle checked={emailSecure} label="使用隐式 TLS" onChange={(value) => emailSecure = value} />使用隐式 TLS（通常为 465 端口）</div>
        <div class="telegram-enable"><Toggle checked={emailImapEnabled} label="启用 IMAP 入站邮件" onChange={(value) => emailImapEnabled = value} />启用 IMAP 入站邮件</div>
        {#if emailImapEnabled}
          <label>IMAP 主机<input bind:value={emailImapHost} placeholder="imap.example.com" /></label>
          <label>IMAP 端口<input type="number" bind:value={emailImapPort} /></label>
          <div class="telegram-enable"><Toggle checked={emailImapSecure} label="IMAP 使用 TLS" onChange={(value) => emailImapSecure = value} />使用 TLS（通常为 993 端口）</div>
          <p class="field-help">轮询未读邮件并交给主 Agent 回复到配置的收件地址。请使用专用邮箱或应用密码。</p>
        {/if}
      {/if}
      <div class="plugin-settings"><p class="field-label">本地通道插件</p>
        {#if availablePlugins.length}
          {#each availablePlugins as plugin}
            <div class="plugin-row"><div class="telegram-enable"><Toggle checked={isPluginEnabled(plugin.id)} label={`启用 ${plugin.name}`} onChange={(value) => togglePlugin(plugin.id,value)} />启用 {plugin.name} <small>v{plugin.version}</small></div>
            {#if plugin.description}<p class="field-help">{plugin.description}</p>{/if}
            {#if isPluginEnabled(plugin.id)}<label>插件配置（JSON，不要填写密钥）<textarea bind:value={pluginConfigText[plugin.id]} rows="4" spellcheck="false"></textarea></label>{/if}</div>
          {/each}
        {:else}
          <p class="field-help">将插件文件夹放入 `%LOCALAPPDATA%/pattern/plugins/&lt;插件名&gt;/`，并提供 `pattern.channel.json` 后，重新打开此窗口即可启用。</p>
        {/if}
      </div>
      {#if error}<p class="validation-error">{error}</p>{/if}
      <footer>
        <button onclick={() => (editing = false)}>取消</button>
        <button class="primary-button" onclick={save}><Save size={14} />保存</button>
      </footer>
    </div>
  </div>
{/if}

{#if pairing}
  <div class="modal-backdrop" role="presentation" onclick={(event) => { if (event.target === event.currentTarget) pairing = null; }}>
    <div class="memory-editor" role="dialog" aria-modal="true" aria-labelledby="pairing-title">
      <header><div><p class="eyebrow">移动端配对</p><h2 id="pairing-title">手动连接信息</h2></div><button aria-label="关闭" onclick={() => pairing = null}><X size={16}/></button></header>
      <p class="field-help">先在手机端生成安全配对请求并粘贴到这里。桌面使用 X25519 协商密钥，再以 XChaCha20-Poly1305 加密 WebDAV 凭据和中继密钥。</p>
      <label>手机配对请求<textarea bind:value={pairingRequest} rows="4" placeholder="pattern://pair?data=…"></textarea></label>
      <label class="quiet-button">扫描手机请求二维码图片<input class="file-input" type="file" accept="image/*" onchange={scanPairingRequest}/></label>
      <button class="primary-button" onclick={createSecureResponse} disabled={!pairingRequest.trim()}>生成加密响应二维码</button>
      {#if pairingQr}<img class="pairing-qr" src={pairingQr} alt="Pattern Mobile 配对二维码" />{/if}
      {#if pairingCode}<label>加密响应码<textarea readonly rows="4" value={pairingCode}></textarea></label>{/if}
      <footer><button class="primary-button" onclick={() => pairing = null}>完成</button></footer>
    </div>
  </div>
{/if}
