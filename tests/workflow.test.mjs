import {test} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {createApp} from '../server.mjs';
import {callProvider,renderTemplate} from '../lib/providers.mjs';
import {validatePlan,validateTeam,conversationFor} from '../lib/domain.mjs';

test('message intent controls task context without changing approved scope',async t=>{
  const prompts=[];
  const f=await fixture(t,async(provider,messages)=>{prompts.push(messages.at(-1).content);return {text:'Acknowledged'};});
  const {id:projectId}=await f.post('projects/create',{goal:'Synthetic context test'});
  const provider=await f.post('providers/save',{name:'Mock',model:'mock',endpoint:'https://example.invalid/chat',protocol:'chat'});
  let p=(await f.state()).projects[0];
  await f.post('projects/save',{projectId,mode:'live',team:p.team.map(a=>({...a,providerId:provider.id}))});
  const plan={summary:'Approved scope',risks:'',tasks:[{id:'A',title:'Draft',description:'Write text',acceptance:'Text exists',agentId:p.team[0].id,dependsOn:[]}]};
  await f.post('projects/plan',{projectId,plan});await f.post('projects/approve',{projectId});
  const approval=(await f.state()).projects[0].approval.hash;
  for(const [intent,text] of [['question','QUESTION_ONLY'],['plan_change','CHANGE_ONLY'],['instruction','TASK_REQUIREMENT']]){
    const receipt=await f.post('projects/command',{projectId,intent,text,targetId:p.team[0].id});
    assert.equal(receipt.planChanged,false);assert.equal(receipt.intent,intent);await f.idle();
  }
  assert.equal((await f.post('projects/command',{projectId,intent:'auto_approve',text:'invalid'})).status,400);
  p=(await f.state()).projects[0];assert.equal(p.approval.hash,approval);assert.deepEqual(p.plan,plan);
  prompts.length=0;await f.post('projects/execute',{projectId});p=(await f.idle()).projects[0];
  assert.match(prompts[0],/TASK_REQUIREMENT/);assert.doesNotMatch(prompts[0],/QUESTION_ONLY|CHANGE_ONLY/);
  assert.equal(p.commands[0].contextUses.length,0);assert.equal(p.commands[1].contextUses.length,0);assert.equal(p.commands[2].contextUses.length,1);
  assert.equal(p.commands[2].contextUses[0].agentId,p.team[0].id);
  assert.deepEqual(conversationFor({commands:[{text:'failed',status:'failed'},{text:'other',targetId:'other',status:'done'}]},'a'),[]);
});

test('provider failures have actionable diagnostics and never echo secret response bodies',async()=>{
  const p={name:'Mock',model:'mock',protocol:'chat',endpoint:'https://example.invalid/chat',apiKey:'test-only-secret'};
  await assert.rejects(callProvider(p,[],{fetchImpl:async()=>new Response('test-only-secret',{status:429})}),/HTTP 429/);
  await assert.rejects(callProvider(p,[],{fetchImpl:async()=>new Response('test-only-secret')}),error=>/JSON/.test(error.message)&&!error.message.includes('test-only-secret'));
  await assert.rejects(callProvider(p,[],{fetchImpl:async()=>{throw new TypeError('test-only-secret',{cause:{code:'ECONNRESET'}});}}),/网络.*ECONNRESET/);
  await assert.rejects(callProvider(p,[],{fetchImpl:async()=>new Response(JSON.stringify({choices:[{finish_reason:'length',message:{content:'partial'}}]}))}),/长度上限/);
});

test('English demo and targeted instructions preserve approval and persist replies',async t=>{
  const f=await fixture(t),created=await f.post('projects/create',{name:'English demo',goal:'Write a report',locale:'en'}),projectId=created.id;
  let p=(await f.state()).projects[0];assert.equal(p.team[0].name,'Project lead');
  await f.post('projects/save',{projectId,team:p.team.map((a,i)=>({...a,department:i===2?'Quality':'Research'}))});
  p=(await f.state()).projects[0];assert.equal(p.team[2].department,'Quality');
  assert.equal((await f.post('projects/command',{projectId,targetId:'missing',text:'Hello'})).status,400);
  await f.post('projects/discuss',{projectId});
  await f.post('projects/command',{projectId,targetId:p.team[1].id,text:'List assumptions explicitly.'});
  p=(await f.idle()).projects[0];assert.equal(p.commands[0].status,'done');assert.equal(p.commands[0].replies.length,1);assert.equal(p.commands[0].replies[0].agentId,p.team[1].id);
  assert.match(p.plan.summary,/Demo plan/);assert.equal(Object.keys(p.results).length,0);
  await f.post('projects/approve',{projectId});p=(await f.state()).projects[0];const hash=p.approval.hash;
  await f.post('projects/command',{projectId,text:'Review the plan, do not execute.'});p=(await f.idle()).projects[0];
  assert.equal(p.commands[1].replies.length,3);assert.equal(p.approval.hash,hash);assert.equal(p.status,'approved');assert.equal(Object.keys(p.results).length,0);
  assert.equal(JSON.parse(fs.readFileSync(path.join(f.dataDir,'workspace.json'))).projects[0].commands.length,2);
});

async function fixture(t,modelCall){
  const dataDir=fs.mkdtempSync(path.join(os.tmpdir(),'cofoundry-test-'));
  const app=createApp({dataDir,modelCall});await new Promise(resolve=>app.server.listen(0,'127.0.0.1',resolve));
  const origin=`http://127.0.0.1:${app.server.address().port}`;
  t.after(async()=>{for(const c of [...app.active.values(),...app.commandWorkers.values()])c.abort();await idle();await new Promise(resolve=>app.server.close(resolve));fs.rmSync(dataDir,{recursive:true,force:true});});
  const post=async(route,body={})=>{const r=await fetch(origin+'/api/'+route,{method:'POST',headers:{'Content-Type':'application/json','X-Cofoundry':'local'},body:JSON.stringify(body)});return {status:r.status,...await r.json()};};
  const state=async()=>await (await fetch(origin+'/api/state')).json();
  const idle=async()=>{for(let n=0;n<300;n++){const s=await state();if(!s.active.length)return s;await new Promise(r=>setTimeout(r,20));}throw new Error('timeout');};
  return {...app,origin,post,state,idle,dataDir};
}
test('live instructions respect recipients, call limits and pause queued delivery',async t=>{
  const calls=[];
  const f=await fixture(t,async(provider,messages)=>{calls.push({model:provider.model,prompt:messages.at(-1).content});return {text:'Acknowledged'};});
  const {id:projectId}=await f.post('projects/create',{goal:'Test instructions'});
  let p=(await f.state()).projects[0];
  await f.post('projects/discuss',{projectId});
  await f.post('projects/command',{projectId,targetId:p.team[1].id,text:'Queued before pause'});
  await f.post('projects/pause',{projectId});p=(await f.idle()).projects[0];
  assert.equal(p.commands[0].status,'failed');assert.deepEqual(p.commands[0].replies,[]);
  const team=[];
  for(let i=0;i<p.team.length;i++){
    const provider=await f.post('providers/save',{name:'Member '+i,model:'model-'+i,endpoint:'https://example.invalid/api',protocol:'chat'});
    team.push({...p.team[i],providerId:provider.id});
  }
  await f.post('projects/save',{projectId,mode:'live',team,maxCalls:1});
  await f.post('projects/command',{projectId,targetId:team[1].id,text:'Only the solution lead should receive this.'});
  p=(await f.idle()).projects[0];assert.equal(p.commands[1].status,'done');
  assert.equal(calls.length,1);assert.equal(calls[0].model,'model-1');assert.match(calls[0].prompt,/Only the solution lead/);
  await f.post('projects/command',{projectId,targetId:team[0].id,text:'A separate instruction'});
  p=(await f.idle()).projects[0];assert.equal(p.commands[2].status,'failed');assert.equal(calls.length,1);assert.equal(p.calls,1);
  await f.post('projects/save',{projectId,maxCalls:2});
  await f.post('projects/command',{projectId,targetId:team[0].id,text:'Try again'});await f.idle();
  assert.equal(calls.length,2);assert.doesNotMatch(calls[1].prompt,/Only the solution lead/);
});
test('approval gate, discussion, editing, DAG execution and persisted local secrets',async t=>{
  const f=await fixture(t);
  const made=await f.post('projects/create',{name:'Test',goal:'Generate a report'}),projectId=made.id;
  assert.equal((await f.post('projects/execute',{projectId})).status,400);
  await f.post('projects/discuss',{projectId});let s=await f.idle(),p=s.projects[0];
  assert.equal(p.status,'awaiting_approval');assert.equal(p.discussion.length,6);assert.equal(Object.keys(p.results).length,0);
  await f.post('projects/approve',{projectId});
  const altered=structuredClone(p.plan);altered.summary='Edited by human';
  await f.post('projects/plan',{projectId,plan:altered});
  assert.equal((await f.post('projects/execute',{projectId})).status,400);
  await f.post('projects/approve',{projectId});await f.post('projects/execute',{projectId});
  s=await f.idle();p=s.projects[0];assert.equal(p.status,'delivered');assert.equal(Object.values(p.results).filter(r=>r.status==='done').length,4);
  assert.ok(new Date(p.results.T4.startedAt)>=new Date(p.results.T2.finishedAt));
  assert.equal((await f.post('projects/accept',{projectId})).status,200);
  await f.post('providers/save',{name:'Example',model:'arbitrary-model',endpoint:'https://example.invalid/api',protocol:'chat',apiKey:'secret-test-key'});
  assert.ok(!JSON.stringify(await f.state()).includes('secret-test-key'));
  assert.ok(!fs.readFileSync(path.join(f.dataDir,'workspace.json'),'utf8').includes('secret-test-key'));
  assert.equal(f.db.decrypt(f.db.state.providers[0].secret),'secret-test-key');
});
test('live API workflow uses configured arbitrary model and never executes before approval',async t=>{
  let modelCalls=0;
  const f=await fixture(t,async(provider,messages)=>{
    assert.equal(provider.model,'my-model');assert.equal(provider.apiKey,'test-key');modelCalls++;
    const prompt=messages.at(-1).content;
    if(prompt.includes('只输出合法 JSON')) {
      const roster=JSON.parse(prompt.split('团队：')[1].split('\n讨论内容：')[0]);
      return {text:JSON.stringify({summary:'Real adapter test',risks:'',tasks:[{id:'A',title:'Draft',description:'Write',acceptance:'Text present',agentId:roster[0].id,dependsOn:[]}]})};
    }
    return {text:'Mock API response',usage:{total_tokens:10}};
  });
  const provider=await f.post('providers/save',{name:'API',model:'my-model',endpoint:'https://example.invalid/api',protocol:'chat',apiKey:'test-key'});
  const project=await f.post('projects/create',{name:'Live',goal:'Write a report'});const p=(await f.state()).projects[0];
  await f.post('projects/save',{projectId:project.id,mode:'live',team:p.team.map(a=>({...a,providerId:provider.id}))});
  await f.post('projects/discuss',{projectId:project.id});let s=await f.idle();
  assert.equal(modelCalls,7);assert.equal(s.projects[0].status,'awaiting_approval');
  await f.post('projects/execute',{projectId:project.id});assert.equal(modelCalls,7);
  await f.post('projects/approve',{projectId:project.id});await f.post('projects/execute',{projectId:project.id});s=await f.idle();
  assert.equal(modelCalls,9);assert.equal(s.projects[0].status,'delivered');assert.equal(s.projects[0].calls,9);
});
test('pause preserves approval and can resume pending work',async t=>{
  const f=await fixture(t);const {id:projectId}=await f.post('projects/create',{goal:'Test'});
  await f.post('projects/discuss',{projectId});await f.idle();await f.post('projects/approve',{projectId});
  await f.post('projects/execute',{projectId});await f.post('projects/pause',{projectId});
  assert.equal((await f.idle()).projects[0].status,'paused');
  await f.post('projects/execute',{projectId});assert.equal((await f.idle()).projects[0].status,'delivered');
});
test('request origin and client marker are checked',async t=>{
  const f=await fixture(t);
  let r=await fetch(f.origin+'/api/projects/create',{method:'POST',body:'{}'});assert.equal(r.status,403);
  r=await fetch(f.origin+'/api/state',{headers:{origin:'https://untrusted.invalid'}});assert.equal(r.status,403);
});
test('task and organization graphs reject cycles and invalid owners',()=>{
  const agents=[{id:'a',name:'A',role:'Lead',level:1},{id:'b',name:'B',role:'Worker',level:2}];
  assert.throws(()=>validateTeam([{...agents[0],managerId:'b'},agents[1]],3));
  validateTeam(agents.map(a=>({...a,department:'Engineering'})),3);
  assert.throws(()=>validateTeam(agents.map(a=>({...a,department:42})),3));
  assert.throws(()=>validateTeam(agents.map(a=>({...a,department:'x'.repeat(61)})),3));
  const task=(id,dependsOn)=>({id,title:id,description:'Work',acceptance:'Check',agentId:'a',dependsOn});
  assert.throws(()=>validatePlan({summary:'S',tasks:[task('a',['b']),task('b',['a'])]},agents));
  assert.throws(()=>validatePlan({summary:'S',tasks:[task('a',['missing'])]},agents));
});
test('API protocols map requests and responses without model vendor assumptions',async()=>{
  const base={name:'Custom service',model:'anything',endpoint:'http://127.0.0.1:9999/test',apiKey:'key'};
  const messages=[{role:'system',content:'system'},{role:'user',content:'quoted " text'}];
  for(const protocol of ['chat','anthropic','custom']){
    const p={...base,protocol,bodyTemplate:'{"input":"{{messages}}","model":"{{model}}"}',headerTemplate:'{"X-Key":"{{apiKey}}"}',responsePath:'data.0.answer'};
    const result=await callProvider(p,messages,{fetchImpl:async(url,options)=>{
      const body=JSON.parse(options.body);
      assert.equal(body.model,'anything');
      if(protocol==='chat'){assert.deepEqual(body.messages,messages);assert.equal(options.headers.Authorization,'Bearer key');}
      if(protocol==='anthropic'){assert.equal(body.system,'system');assert.equal(body.messages.length,1);}
      if(protocol==='custom'){assert.deepEqual(body.input,messages);assert.equal(options.headers['X-Key'],'key');}
      return new Response(JSON.stringify({choices:[{message:{content:'OK'}}],content:[{type:'text',text:'OK'}],data:[{answer:'OK'}]}));
    }});assert.equal(result.text,'OK');
  }
  assert.deepEqual(renderTemplate('{{messages}}',{messages}),messages);
});
