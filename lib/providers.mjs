const getPath = (value, path) => path.split('.').reduce((v, k) => v?.[k], value);
export function renderTemplate(value, variables) {
  if (typeof value === 'string') {
    const exact = value.match(/^\{\{(\w+)\}\}$/);
    if (exact) {
      if (!(exact[1] in variables)) throw new Error(`未知模板变量：${exact[1]}`);
      return variables[exact[1]];
    }
    return value.replace(/\{\{(\w+)\}\}/g, (_, key) => String(variables[key] ?? ''));
  }
  if (Array.isArray(value)) return value.map(v => renderTemplate(v, variables));
  if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).map(([k,v]) => [k, renderTemplate(v, variables)]));
  return value;
}
export function validateProvider(p) {
  if (!p.name?.trim() || !p.model?.trim()) throw new Error('请填写连接名称和模型 ID');
  const url = new URL(p.endpoint);
  if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password) throw new Error('接口须为 HTTP(S) 地址，认证信息请放入密钥栏');
  if (!['chat', 'anthropic', 'custom'].includes(p.protocol)) throw new Error('未知接口协议');
  if (p.protocol === 'custom') {
    JSON.parse(p.bodyTemplate);
    JSON.parse(p.headerTemplate || '{}');
    if (!p.responsePath?.trim()) throw new Error('请填写响应文本路径');
  }
}
export async function callProvider(provider, messages, {signal, fetchImpl = fetch, maxTokens = 4096} = {}) {
  validateProvider(provider);
  let headers = {'Content-Type':'application/json'}, body;
  if (provider.protocol === 'chat') {
    if (provider.apiKey) headers.Authorization = `Bearer ${provider.apiKey}`;
    body = {model: provider.model, messages, stream:false, max_tokens:maxTokens};
  } else if (provider.protocol === 'anthropic') {
    headers['x-api-key'] = provider.apiKey || '';
    headers['anthropic-version'] = '2023-06-01';
    body = {model:provider.model, max_tokens:maxTokens, system:messages.filter(m=>m.role==='system').map(m=>m.content).join('\n'), messages:messages.filter(m=>m.role!=='system')};
  } else {
    const vars = {model:provider.model, apiKey:provider.apiKey || '', messages, system:messages.filter(m=>m.role==='system').map(m=>m.content).join('\n'), prompt:messages.filter(m=>m.role!=='system').map(m=>m.content).join('\n'), maxTokens};
    headers = {...headers, ...renderTemplate(JSON.parse(provider.headerTemplate || '{}'), vars)};
    body = renderTemplate(JSON.parse(provider.bodyTemplate), vars);
  }
  const combined = AbortSignal.any([AbortSignal.timeout(120000), ...(signal ? [signal] : [])]);
  const response = await fetchImpl(provider.endpoint, {method:'POST',headers,body:JSON.stringify(body),signal:combined,redirect:'error'});
  if (!response.ok) throw new Error(`模型接口返回 HTTP ${response.status}，请检查地址、模型 ID、密钥或配额`);
  const data = await response.json();
  const result = provider.protocol === 'chat' ? data.choices?.[0]?.message?.content : provider.protocol === 'anthropic' ? data.content?.filter(x=>x.type==='text').map(x=>x.text).join('\n') : getPath(data, provider.responsePath);
  if (typeof result !== 'string' || !result.trim()) throw new Error('接口未返回文本，请检查协议或响应路径');
  if (result.length > 250000) throw new Error('响应文本超过 250000 字符上限');
  return {text:result,usage:data.usage || null};
}
