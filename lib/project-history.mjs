import {randomUUID} from 'node:crypto';
const fields=['name','goal','constraints','mode','levels','team','plan','results','discussion','review','commands','coordinatorId','reviewerId'];
export function checkpoint(p,label) {
  const data=Object.fromEntries(fields.filter(k=>p[k]!==undefined).map(k=>[k,structuredClone(p[k])]));
  const entry={id:randomUUID(),at:new Date().toISOString(),label,revision:p.revision,data};
  if(JSON.stringify(entry).length>4000000)throw new Error('Project snapshot exceeds local history size limit; export the project before changing it.');
  const history=[entry,...(p.history||[])].slice(0,10);
  while(JSON.stringify(history).length>4000000&&history.length>1)history.pop();
  p.history=history;
  return entry;
}
export function restoreCheckpoint(p,snapshotId) {
  const source=p.history?.find(s=>s.id===snapshotId);
  if(!source)throw new Error('Snapshot not found');
  const restored=structuredClone(source.data);
  checkpoint(p,'Before restore');
  for(const key of fields)if(key in restored)p[key]=restored[key];else delete p[key];
  for(const c of p.commands||[])if(['queued','running'].includes(c.status)){c.status='failed';c.error='Restored history; send again to resume.';}
  for(const r of Object.values(p.results||{}))if(r.status==='running')r.status='pending';
  p.approval=null;p.revision++;p.status=p.plan?'awaiting_approval':'draft';p.error=null;
}
