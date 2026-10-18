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
  let response;
  try{response = await fetchImpl(provider.endpoint, {method:'POST',headers,body:JSON.stringify(body),signal:combined,redirect:'error'});}
  catch(error){
    if(signal?.aborted)throw new Error('运行已暂停');
    if(combined.aborted)throw new Error('模型请求超时，请检查网络或减少任务范围');
    const code=error.cause?.code||error.code;
    const safeCodes=['ECONNRESET','ECONNREFUSED','ENOTFOUND','ETIMEDOUT','UND_ERR_CONNECT_TIMEOUT','CERT_HAS_EXPIRED','UNABLE_TO_VERIFY_LEAF_SIGNATURE'];
    throw new Error('无法连接模型接口，请检查网络、代理或证书'+(safeCodes.includes(code)?` (${code})`:''));
  }
  if (!response.ok) {
    const reasons={401:'认证失败，请检查密钥',402:'账户余额不足，请检查余额',403:'无访问权限，请检查模型权限',404:'接口或模型不存在，请检查地址和模型 ID',429:'请求受限，请稍后重试或降低并发数',500:'模型服务异常，请稍后重试',502:'模型服务暂时不可用，请稍后重试',503:'模型服务繁忙，请稍后重试'};
    throw new Error(`HTTP ${response.status}: ${reasons[response.status]||'模型请求失败，请检查连接配置'}`);
  }
  let data;try{data=await response.json();}catch{throw new Error('模型接口未返回有效 JSON，请检查地址和协议');}
  if(data.choices?.[0]?.finish_reason==='length')throw new Error('模型输出达到长度上限，未获得完整结果；请减少任务范围或调整输出上限');
  const result = provider.protocol === 'chat' ? data.choices?.[0]?.message?.content : provider.protocol === 'anthropic' ? data.content?.filter(x=>x.type==='text').map(x=>x.text).join('\n') : getPath(data, provider.responsePath);
  if (typeof result !== 'string' || !result.trim()) throw new Error('接口未返回文本，请检查协议或响应路径');
  if (result.length > 250000) throw new Error('响应文本超过 250000 字符上限');
  return {text:result,usage:data.usage || null};
}
