<script lang="ts">
  import {onMount} from 'svelte';
  import {KeyRound,ShieldCheck,UserRound,ArrowLeft,ArrowRight,Upload} from 'lucide-svelte';
  import StatusDot from './StatusDot.svelte';
  import type {ModelSetup,Persona} from './types';
  let{onComplete,mode='setup',initialPersona=null,onCancel}:{onComplete:(persona:Persona,model:ModelSetup)=>Promise<void>;mode?:'setup'|'redefine';initialPersona?:Persona|null;onCancel?:()=>void}=$props();
  let step=$state(1);let provider=$state('OpenAI Compatible');let endpoint=$state('https://api.openai.com/v1');let model=$state('gpt-4.1-mini');let apiKey=$state('');
  let name=$state('');let userName=$state('');let description=$state('');let proactive=$state<'free'|'quiet'>('free');let validation=$state('');let permissionStatus=$state('');let completed=$state(false);let saving=$state(false);let importInput=$state<HTMLInputElement>();let seeded=$state(false);
  $effect.pre(() => {
    if (seeded) return;
    seeded = true;
    if (mode === 'redefine') {
      step = 3;
      name = initialPersona?.name || '';
      userName = initialPersona?.userName || '';
      description = initialPersona?.description || '';
      proactive = initialPersona?.proactive || 'free';
    }
  });
  type PermissionStatus={platform:'windows'|'macos'|'other';notifications:boolean;accessibility:boolean;screenRecording:boolean};
  let permissions=$state<PermissionStatus>({platform:navigator.userAgent.includes('Windows')?'windows':'other',notifications:false,accessibility:false,screenRecording:false});
  const canContinue=()=>step===1?endpoint.trim().length>0&&model.trim().length>0:step===2||name.trim().length>0&&description.trim().length>0;
  function providerChanged(){if(provider==='Anthropic'){endpoint='https://api.anthropic.com/v1';model='claude-sonnet-5';}else{endpoint='https://api.openai.com/v1';model='gpt-5.6';}}
  async function next(){if(!canContinue()){validation=step===1?'请填写 API 地址和模型名称':'请给 TA 一个名字，并写下性格与说话方式';return;}validation='';if(step<3){step+=1;return;}saving=true;try{await onComplete({name:name.trim(),userName:userName.trim(),description:description.trim(),proactive},{provider,endpoint:endpoint.trim(),model:model.trim(),apiKey});completed=true;}catch(value){validation=`无法完成设置：${value}`;}finally{saving=false;}}
  async function importCard(event:Event){const file=(event.currentTarget as HTMLInputElement).files?.[0];if(!file)return;try{const content=await file.text();const match=content.match(/^---\s*\n([\s\S]*?)\n---\s*\n?([\s\S]*)$/);const front=match?.[1]||'';const value=(key:string)=>front.match(new RegExp(`^${key}:\\s*(.+)$`,'m'))?.[1]?.trim()||'';name=value('name')||file.name.replace(/\.md$/i,'');userName=value('user_name');description=(match?.[2]||content).trim();const mode=value('proactive');proactive=mode==='quiet'?'quiet':'free';validation='\u5df2\u5bfc\u5165\u4eba\u683c\u5361\uff0c\u53ef\u7ee7\u7eed\u4fee\u6539\u3002';}catch{validation='\u4eba\u683c\u5361\u8bfb\u53d6\u5931\u8d25';}(event.currentTarget as HTMLInputElement).value='';}
  async function checkPermissions(){if(!(window as any).__TAURI_INTERNALS__){permissionStatus='浏览器预览无法检查系统权限';return}try{const{invoke}=await import('@tauri-apps/api/core');permissions=await invoke<PermissionStatus>('permission_status');permissionStatus=permissions.platform==='windows'?'Windows 使用 UI 自动化，不需要单独开启辅助功能；首次视觉任务会验证屏幕捕获。':permissions.accessibility&&permissions.screenRecording?'所需权限已经就绪':'仍有权限未授予，请打开对应系统设置';}catch(value){permissionStatus=`权限检查失败：${value}`}}
  async function openPermission(kind:'notifications'|'accessibility'|'screen'){if(!(window as any).__TAURI_INTERNALS__){permissionStatus='浏览器预览无法打开系统设置';return}try{const{invoke}=await import('@tauri-apps/api/core');await invoke('open_permission_settings',{kind});}catch(value){permissionStatus=`无法打开系统设置：${value}`}}
  onMount(()=>{void checkPermissions();});
</script>
{#if !completed}
<div class="oobe-backdrop">
  <div class="oobe-window" role="dialog" aria-modal="true" aria-label={mode==='redefine'?'重新定义人格':'首次启动设置'}>
    <aside class="oobe-side">
      <div class="oobe-mark"><StatusDot size="large" off={step<3}/></div>
      {#if mode==='setup'}<div class="oobe-steps">
        <div class:current={step===1} class:done={step>1}><i>{step>1?'✓':'1'}</i><KeyRound size={15}/>接入模型</div>
        <div class:current={step===2} class:done={step>2}><i>{step>2?'✓':'2'}</i><ShieldCheck size={15}/>授予权限</div>
        <div class:current={step===3}><i>3</i><UserRound size={15}/>定义人格</div>
      </div>{:else}<div class="oobe-steps"><div class="current"><i>3</i><UserRound size={15}/>重新定义人格</div></div>{/if}
      <p>{mode==='redefine'?'只修改人格，不会重置模型接入和系统权限。':step===1?'Pattern 不托管密钥，它只会存入系统凭据管理器。':step===2?'权限随时可以在系统设置中收回。':'没有模板。你写下什么，TA 就是什么样。'}</p>
    </aside>
    <div class="oobe-content">
      <header><span>{mode==='redefine'?'人格设置':'首次启动'}</span><strong>0{step} / 03</strong></header>
      {#if step===1}
        <div class="oobe-form"><div><p class="eyebrow">接入模型</p><h1>先让 TA 能开口</h1><p class="subtitle">配置主 Agent 使用的模型。明确要求干活时，主 Agent 会派生子代理执行。</p></div><label>服务商<select bind:value={provider} onchange={providerChanged}><option>OpenAI Compatible</option><option>Anthropic</option><option>OpenAI</option></select></label><label>API 地址<input bind:value={endpoint} placeholder="https://api.openai.com/v1"></label><label>模型<input bind:value={model} placeholder="gpt-4.1-mini"></label><label>API Key（可选）<input bind:value={apiKey} type="password" placeholder="留空，稍后在设置中填写"></label></div>
      {:else if step===2}
        <div class="oobe-form"><div><p class="eyebrow">系统权限</p><h1>决定 TA 能看到什么</h1><p class="subtitle">通知用于主动消息。Computer Use 会读取控件并按需截屏；权限随时可以收回。</p></div><div class="permission-row"><ShieldCheck size={22}/><div><strong>系统通知</strong><p>主动消息、日程提醒和任务完成通知</p></div><span class="badge" class:green={permissions.notifications}>{permissions.notifications?'已就绪':'待检查'}</span><button class="quiet-button" onclick={()=>openPermission('notifications')}>设置</button></div>{#if permissions.platform==='windows'}<div class="permission-row"><ShieldCheck size={22}/><div><strong>辅助功能（Windows UIA）</strong><p>Windows 的 UI 自动化无需单独授权。若目标程序以管理员身份运行，Pattern 也需要以管理员身份运行。</p></div><span class="badge green">无需设置</span></div><div class="permission-row"><ShieldCheck size={22}/><div><strong>屏幕捕获</strong><p>仅在视觉 Computer Use 模式截取屏幕。可在 Windows“隐私和安全性 → 屏幕捕获”中管理桌面应用访问。</p></div><span class="badge green">系统管理</span><button class="quiet-button" onclick={()=>openPermission('screen')}>打开设置</button></div><p class="permission-platform-note">部分 Windows 版本不显示“屏幕捕获”页面；这表示桌面屏幕捕获由系统默认允许，开始视觉任务时会实际验证。</p>{:else}<div class="permission-row"><ShieldCheck size={22}/><div><strong>辅助功能</strong><p>通过 UIA/AX 读取并操作控件，也支持无视觉模型。</p></div><span class="badge" class:green={permissions.accessibility} class:amber={!permissions.accessibility}>{permissions.accessibility?'已就绪':'需授权'}</span><button class="quiet-button" onclick={()=>openPermission('accessibility')}>设置</button></div><div class="permission-row"><ShieldCheck size={22}/><div><strong>屏幕录制</strong><p>仅在视觉 Computer Use 模式向执行模型提供截屏。</p></div><span class="badge" class:green={permissions.screenRecording} class:amber={!permissions.screenRecording}>{permissions.screenRecording?'已就绪':'需授权'}</span><button class="quiet-button" onclick={()=>openPermission('screen')}>设置</button></div>{/if}{#if permissionStatus}<p class="permission-status" role="status">{permissionStatus}</p>{/if}<button class="quiet-button" type="button" onclick={checkPermissions}>重新检查权限</button></div>
      {:else}
        <div class="oobe-form"><div><p class="eyebrow">定义人格</p><h1>TA 是谁，由你来写</h1><p class="subtitle">人格是数据，不是预设。完成后会保存为本地人格配置。</p></div><div class="form-grid"><label>名字<input bind:value={name} placeholder="还没有名字"></label><label>TA 怎么称呼你<input bind:value={userName} placeholder="留空让 TA 自己决定"></label></div><label>性格与说话方式<textarea bind:value={description} rows="5" placeholder="说话直接点，不用客套。我熬夜要管，写作的时候别打断……"></textarea></label><span class="field-label">主动性</span><div class="segmented wide"><button class:active={proactive==='free'} onclick={()=>proactive='free'}>随 TA，想说就说</button><button class:active={proactive==='quiet'} onclick={()=>proactive='quiet'}>安静一些</button></div><input aria-label="导入人格卡文件" bind:this={importInput} class="file-input" type="file" accept=".md,text/markdown,text/plain" onchange={importCard}/><button class="import-button" onclick={()=>importInput?.click()}><Upload size={15}/>导入人格卡</button></div>
      {/if}
      <footer>{#if mode==='redefine'}<button class="quiet-button" type="button" onclick={()=>onCancel?.()}>取消</button>{:else if step>1}<button class="quiet-button" onclick={()=>{validation='';step-=1;}}><ArrowLeft size={15}/>上一步</button>{/if}{#if validation}<em class="validation-error" role="alert">{validation}</em>{/if}<span></span><button class="primary-button" onclick={next} disabled={saving}>{saving?'正在保存…':mode==='redefine'?'保存人格':step===3?'完成设置':'继续'}<ArrowRight size={15}/></button></footer>
    </div>
  </div>
</div>
{/if}
