import test from 'node:test';
import assert from 'node:assert/strict';
import {buildSync} from 'esbuild';
import {mkdtempSync,rmSync,mkdirSync,writeFileSync} from 'node:fs';
import {join} from 'node:path';
import {tmpdir} from 'node:os';
import {fileURLToPath} from 'node:url';
import {createRequire} from 'node:module';

function loadChannels(dir){const outfile=join(dir,'channels.cjs');buildSync({entryPoints:[fileURLToPath(new URL('../../packages/channels/src/index.ts',import.meta.url))],bundle:true,platform:'node',format:'cjs',target:'node22',external:['nodemailer'],outfile});return createRequire(import.meta.url)(outfile)}

test('Telegram channel sends and advances filtered inbound offset',async()=>{const dir=mkdtempSync(join(tmpdir(),'pattern-channels-'));try{const requests=[];const fetcher=async(url,init)=>{requests.push({url:String(url),init});if(String(url).includes('getUpdates'))return new Response(JSON.stringify({result:[{update_id:4,message:{text:'忽略',chat:{id:'other'}}},{update_id:5,message:{text:'你好',chat:{id:'42'}}}]}),{status:200});return new Response('{}',{status:200})};const {TelegramChannel,channelMessage}=loadChannels(dir);const channel=new TelegramChannel('token','42',fetcher,3);await channel.send(channelMessage('发送'));const incoming=await channel.poll();assert.equal(incoming.length,1);assert.equal(incoming[0].text,'你好');assert.equal(channel.getOffset(),6);assert.match(requests[0].url,/sendMessage/)}finally{rmSync(dir,{recursive:true,force:true})}});

test('SMTP channel delegates through the common adapter',async()=>{const dir=mkdtempSync(join(tmpdir(),'pattern-channels-smtp-'));try{const sent=[];const {SmtpChannel,channelMessage}=loadChannels(dir);const channel=new SmtpChannel({host:'smtp.test',port:587,secure:false,username:'from@test',password:'x',recipient:'to@test'},{sendMail:async(value)=>sent.push(value)});await channel.send(channelMessage('任务完成','task'));assert.equal(sent[0].to,'to@test');assert.match(sent[0].subject,/任务/)}finally{rmSync(dir,{recursive:true,force:true})}});

test('channel plugin discovery validates manifest paths and loads only an explicit plugin', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'pattern-channel-plugins-'));
  try {
    const pluginDir = join(dir, 'plugins', 'echo'); mkdirSync(pluginDir, {recursive:true});
    writeFileSync(join(pluginDir, 'pattern.channel.json'), JSON.stringify({id:'echo.test',name:'Echo',version:'1.0.0',entry:'index.mjs'}));
    writeFileSync(join(pluginDir, 'index.mjs'), "export function createChannel({id,config}) { return {id, capabilities:{inbound:false,outbound:true,requiresApprovalForPeople:true}, config, sent:[], async send(message){this.sent.push(message)}, onMessage(){return ()=>{}}}; }");
    const {discoverChannelPlugins,loadChannelPlugin}=loadChannels(dir);
    const plugins=discoverChannelPlugins(join(dir,'plugins'));
    assert.equal(plugins.length,1);
    const channel=await loadChannelPlugin(plugins[0],{room:'test'},dir);
    assert.equal(channel.id,'echo.test');
    await channel.send({id:'m',text:'hello',type:'chat',ts:1});
    assert.equal(channel.sent[0].text,'hello');
    writeFileSync(join(pluginDir, 'escape.json'), '{}');
  } finally { rmSync(dir,{recursive:true,force:true}); }
});
