import {createHash, randomUUID} from 'node:crypto';
export const id = () => randomUUID();
export const hashPlan = p => createHash('sha256').update(JSON.stringify(p)).digest('hex');
export function validatePlan(plan, agents) {
  if (!plan || typeof plan.summary !== 'string' || !plan.summary.trim() || !Array.isArray(plan.tasks) || !plan.tasks.length || plan.tasks.length > 30) throw new Error('计划需包含摘要和 1–30 个任务');
  const ids = new Set(plan.tasks.map(t=>t.id));
  if (ids.size !== plan.tasks.length) throw new Error('任务 ID 不可重复');
  for (const t of plan.tasks) {
    if (typeof t.id !== 'string' || !/^[\w-]{1,60}$/.test(t.id) || typeof t.title !== 'string' || !t.title.trim() || typeof t.description !== 'string' || typeof t.acceptance !== 'string' || !t.acceptance.trim()) throw new Error('任务需包含有效 ID、标题、说明和验收标准');
    if (!agents.some(a=>a.id===t.agentId)) throw new Error(`任务 ${t.id} 的负责人不存在`);
    if (!Array.isArray(t.dependsOn) || t.dependsOn.some(d=>!ids.has(d) || d===t.id)) throw new Error(`任务 ${t.id} 的依赖无效`);
  }
  const done = new Set();
  while (done.size < ids.size) {
    const ready = plan.tasks.filter(t=>!done.has(t.id) && t.dependsOn.every(d=>done.has(d)));
    if (!ready.length) throw new Error('任务依赖存在循环');
    ready.forEach(t=>done.add(t.id));
  }
  return plan;
}
export function validateTeam(team, levels) {
  if (!Number.isInteger(levels) || levels < 1 || levels > 12) throw new Error('层数支持 1–12，建议 3 或 4 层');
  if (!Array.isArray(team) || team.length < 2 || team.length > 12) throw new Error('讨论团队需要 2–12 位成员');
  const ids = new Set(team.map(a=>a.id));
  if (ids.size !== team.length) throw new Error('成员 ID 重复');
  for (const a of team) {
    if (a.department != null && (typeof a.department !== "string" || a.department.length > 60)) throw new Error("职能部门须为不超过 60 字的文本");
    if (!a.id || !a.name?.trim() || !a.role?.trim() || !Number.isInteger(a.level) || a.level < 1 || a.level > levels) throw new Error('请检查成员名称、岗位和层级');
    if (a.managerId && !team.some(m=>m.id===a.managerId && m.level<a.level)) throw new Error('直属负责人必须位于更高层');
  }
}
export function parsePlan(text) {
  const cleaned = text.trim().replace(/^```(?:json)?\s*/,'').replace(/\s*```$/,'');
  try { return JSON.parse(cleaned); } catch { throw new Error('汇总模型未返回有效计划 JSON，可重新讨论或手动填写计划'); }
}
export function assertApproved(project) {
  if (!project.plan || !project.approval || project.approval.hash !== hashPlan(project.plan)) throw new Error('当前计划未经用户批准，不能执行');
}
