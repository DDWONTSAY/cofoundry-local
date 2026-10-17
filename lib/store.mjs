import fs from 'node:fs';
import path from 'node:path';
import {randomBytes,createCipheriv,createDecipheriv} from 'node:crypto';
export function createStore(directory) {
  fs.mkdirSync(directory,{recursive:true});
  const keyFile = path.join(directory,'local.key');
  if (!fs.existsSync(keyFile)) fs.writeFileSync(keyFile,randomBytes(32),{mode:0o600});
  const key = fs.readFileSync(keyFile);
  const file = path.join(directory,'workspace.json');
  const state = fs.existsSync(file) ? JSON.parse(fs.readFileSync(file,'utf8')) : {providers:[],projects:[]};
  const save = () => { fs.writeFileSync(file+'.tmp',JSON.stringify(state,null,2),{mode:0o600}); fs.renameSync(file+'.tmp',file); };
  function encrypt(text) { const iv=randomBytes(12); const c=createCipheriv('aes-256-gcm',key,iv); const data=Buffer.concat([c.update(text,'utf8'),c.final()]); return [iv,c.getAuthTag(),data].map(b=>b.toString('base64')).join('.'); }
  function decrypt(text) { if(!text) return ''; const [iv,tag,data]=text.split('.').map(x=>Buffer.from(x,'base64')); const c=createDecipheriv('aes-256-gcm',key,iv); c.setAuthTag(tag); return Buffer.concat([c.update(data),c.final()]).toString('utf8'); }
  const publicState = () => ({...state,providers:state.providers.map(({secret,...p})=>({...p,hasKey:!!secret}))});
  return {state,save,encrypt,decrypt,publicState};
}
