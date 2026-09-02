// Minimal MCP streamable-HTTP client for the Unity MCP server running on 127.0.0.1:8080.
// usage: node mcpcall.js list
//        node mcpcall.js call <toolName> '<json-args>'
const URL_ = process.env.UNITY_MCP_URL || 'http://127.0.0.1:8080/mcp';
const http = require('http');

function post(body, session) {
  return new Promise((resolve, reject) => {
    const data = JSON.stringify(body);
    const u = new URL(URL_);
    const req = http.request({
      host: u.hostname, port: u.port, path: u.pathname, method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Accept': 'application/json, text/event-stream',
        'Content-Length': Buffer.byteLength(data),
        ...(session ? { 'Mcp-Session-Id': session } : {}),
      },
    }, res => {
      let buf = '';
      res.setEncoding('utf8');
      res.on('data', c => { buf += c; });
      res.on('end', () => {
        const sid = res.headers['mcp-session-id'] || session;
        if (res.statusCode === 202) return resolve({ sid, msg: null });
        const ct = res.headers['content-type'] || '';
        let msg = null;
        if (ct.includes('text/event-stream')) {
          for (const line of buf.split('\n')) {
            if (line.startsWith('data:')) {
              try { const m = JSON.parse(line.slice(5).trim()); if (m.id !== undefined || m.error) msg = m; } catch (_) {}
            }
          }
        } else if (buf.trim()) {
          try { msg = JSON.parse(buf); } catch (_) { msg = { raw: buf }; }
        }
        if (res.statusCode >= 400) return reject(new Error(`HTTP ${res.statusCode}: ${buf.slice(0, 400)}`));
        resolve({ sid, msg });
      });
    });
    req.on('error', reject);
    req.write(data);
    req.end();
  });
}

(async () => {
  const [mode, tool, argsJson] = process.argv.slice(2);
  let { sid } = await post({ jsonrpc: '2.0', id: 1, method: 'initialize', params: { protocolVersion: '2025-03-26', capabilities: {}, clientInfo: { name: 'gnp-cli', version: '0.1' } } });
  await post({ jsonrpc: '2.0', method: 'notifications/initialized' }, sid);
  let out;
  if (mode === 'list') {
    out = (await post({ jsonrpc: '2.0', id: 2, method: 'tools/list', params: {} }, sid)).msg;
    const tools = out?.result?.tools || [];
    for (const t of tools) console.log(t.name + ' — ' + (t.description || '').split('\n')[0].slice(0, 140));
    if (!tools.length) console.log(JSON.stringify(out, null, 2));
  } else if (mode === 'call') {
    const args = argsJson ? JSON.parse(argsJson) : {};
    out = (await post({ jsonrpc: '2.0', id: 3, method: 'tools/call', params: { name: tool, arguments: args } }, sid)).msg;
    const content = out?.result?.content;
    if (content) for (const c of content) console.log(c.type === 'text' ? c.text : JSON.stringify(c).slice(0, 2000));
    else console.log(JSON.stringify(out, null, 2));
  } else {
    console.log('usage: node mcpcall.js list | call <tool> <json>');
  }
})().catch(e => { console.error('ERR', e.message); process.exit(1); });
