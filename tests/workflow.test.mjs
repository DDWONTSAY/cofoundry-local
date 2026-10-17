import {test} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {createApp} from '../server.mjs';
import {callProvider,renderTemplate} from '../lib/providers.mjs';
import {validatePlan,validateTeam} from '../lib/domain.mjs';

async function fixture(t,modelCall){
  const dataDir=fs.mkdtempSync(path.join(os.tmpdir(),'cofoundry-test-'));
  const app=createApp({dataDir,modelCall});await new Promise(resolve=>app.server.listen(0,'127.0.0.1',resolve));
  const origin=`http://127.0.0.1:${app.server.address().port}`;
  t.after(async()=>{for(const c of app.active.values())c.abort();await new Promise(resolve=>app.server.close(resolve));fs.rmSync(dataDir,{recursive:true,force:true});});
  const post=async(route,body={})=>{const r=await fetch(origin+'/api/'+route,{method:'POST',headers:{'Content-Type':'application/json','X-Cofoundry':'local'},body:JSON.stringify(body)});return {status:r.status,...await r.json()};};
  const state=async()=>await (await fetch(origin+'/api/state')).json();
  const idle=async()=>{for(let n=0;n<300;n++){const s=await state();if(!s.active.length)return s;await new Promise(r=>setTimeout(r,20));}throw new Error('timeout');};
  return {...app,origin,post,state,idle,dataDir};
}
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
