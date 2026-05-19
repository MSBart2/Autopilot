import json, os

with open('tools/triage-output.json', 'r', encoding='utf-8') as f:
    raw_text = json.load(f)

js_literal = json.dumps(raw_text)

html = f"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>Output Sandbox — Cyberpilot</title>
<style>
* {{ box-sizing: border-box; margin: 0; padding: 0; }}
body {{ font-family: system-ui, sans-serif; background: #0d1117; color: #d1d5db; height: 100vh; display: flex; flex-direction: column; overflow: hidden; }}
header {{ background: #161b22; border-bottom: 1px solid #30363d; padding: .6rem 1rem; display: flex; align-items: center; gap: 1rem; flex-shrink: 0; flex-wrap: wrap; }}
header h1 {{ font-size: .9rem; font-weight: 700; color: #f8fafc; letter-spacing: .04em; white-space: nowrap; }}
.controls {{ display: flex; gap: .5rem; align-items: center; flex-wrap: wrap; margin-left: auto; }}
.controls label {{ display: flex; align-items: center; gap: .3rem; font-size: .72rem; color: #94a3b8; cursor: pointer; white-space: nowrap; }}
.controls input[type=checkbox] {{ accent-color: #7c3aed; }}
.btn {{ padding: .28rem .7rem; border-radius: 5px; border: 1px solid #30363d; background: #21262d; color: #e2e8f0; font-size: .72rem; cursor: pointer; }}
.btn:hover {{ background: #30363d; }}
.main {{ flex: 1; display: grid; grid-template-columns: 1fr 1fr; overflow: hidden; min-height: 0; }}
.pane {{ display: flex; flex-direction: column; overflow: hidden; border-right: 1px solid #21262d; }}
.pane:last-child {{ border-right: none; }}
.pane-header {{ background: #161b22; border-bottom: 1px solid #21262d; padding: .35rem .7rem; font-size: .68rem; font-weight: 700; letter-spacing: .1em; color: #64748b; text-transform: uppercase; flex-shrink: 0; }}
.pane-body {{ flex: 1; overflow: auto; padding: .75rem; min-height: 0; }}
#raw-input {{ width: 100%; height: 100%; background: transparent; border: none; color: #94a3b8; font-family: 'Cascadia Code', 'Fira Code', Consolas, monospace; font-size: .7rem; line-height: 1.6; resize: none; outline: none; }}
/* ── Rendered output styles (matching production) ── */
.output-markdown h3 {{ margin: .6rem 0 .25rem; color: #f8fafc; font-size: .85rem; font-weight: 700; border-bottom: 1px solid rgba(148,163,184,.15); padding-bottom: .2rem; }}
.output-markdown p {{ margin: .25rem 0; color: #d1d5db; line-height: 1.6; font-size: .8rem; }}
.output-markdown ul {{ margin: .25rem 0 .45rem; padding-left: 1.1rem; }}
.output-markdown li {{ color: #d1d5db; font-size: .8rem; line-height: 1.55; }}
.output-markdown li + li {{ margin-top: .18rem; }}
.output-table {{ width: 100%; margin: .45rem 0; border-collapse: collapse; font-size: .74rem; }}
.output-table th, .output-table td {{ padding: .38rem .5rem; border: 1px solid rgba(148,163,184,.18); vertical-align: top; }}
.output-table th {{ color: #f8fafc; background: rgba(148,163,184,.18); }}
.output-table td {{ color: #d1d5db; background: rgba(255,255,255,.04); }}
.prose-code {{ padding: .1rem .3rem; border-radius: 3px; background: rgba(148,163,184,.15); color: #e2e8f0; font-family: 'Cascadia Code', Consolas, monospace; font-size: .72rem; }}
.output-narrative {{ border-bottom: 1px solid rgba(148,163,184,.14); margin-bottom: .55rem; padding-bottom: .45rem; }}
.output-summary {{ display: grid; gap: .55rem; }}
.output-verdict {{ display: flex; align-items: center; justify-content: space-between; gap: .7rem; padding: .6rem .7rem; border-radius: 7px; background: rgba(15,23,42,.78); border: 1px solid rgba(148,163,184,.22); }}
.output-verdict strong {{ color: #f8fafc; font-size: .82rem; letter-spacing: .08em; }}
.output-verdict span {{ color: #cbd5e1; font-size: .73rem; }}
.output-verdict.status-go {{ border-color: rgba(34,197,94,.45); background: rgba(20,83,45,.38); }}
.output-verdict.status-stop {{ border-color: rgba(245,158,11,.55); background: rgba(120,53,15,.45); }}
.output-field-grid {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(150px,1fr)); gap: .45rem; }}
.output-field {{ padding: .5rem .55rem; border-radius: 6px; background: rgba(255,255,255,.055); border: 1px solid rgba(148,163,184,.16); }}
.output-field-label {{ display: block; margin-bottom: .2rem; color: #94a3b8; font-size: .62rem; font-weight: 800; letter-spacing: .12em; text-transform: uppercase; }}
.output-chip-row {{ display: flex; flex-wrap: wrap; gap: .3rem; }}
.output-chip {{ display: inline-flex; align-items: center; padding: .16rem .45rem; border-radius: 999px; color: #e2e8f0; background: rgba(148,163,184,.18); border: 1px solid rgba(148,163,184,.22); font-size: .68rem; }}
.output-details summary {{ cursor: pointer; color: #93c5fd; font-size: .72rem; font-weight: 700; margin-top: .4rem; display: block; }}
.output-raw {{ margin: .35rem 0 0; padding: .55rem .65rem; border-radius: 6px; background: #020617; color: #6b7280; font-family: 'Cascadia Code', Consolas, monospace; font-size: .68rem; line-height: 1.55; white-space: pre-wrap; word-break: break-word; }}
.diff-removed {{ background: rgba(239,68,68,.1); border-left: 3px solid rgba(239,68,68,.5); padding: .05rem .4rem; color: #f87171; font-size: .7rem; font-family: monospace; margin: .08rem 0; display: block; }}
.diff-kept {{ padding: .05rem .4rem; font-size: .7rem; font-family: monospace; margin: .08rem 0; display: block; color: #4ade80; }}
.diff-blank {{ color: #334155; font-size: .65rem; font-family: monospace; display: block; padding: 0 .4rem; }}
#info {{ padding: .4rem .75rem; font-size: .7rem; color: #64748b; background: #161b22; border-top: 1px solid #21262d; flex-shrink: 0; letter-spacing: .02em; }}
</style>
</head>
<body>

<header>
  <h1>🔬 Output Rendering Sandbox</h1>
  <div class="controls">
    <label><input type="checkbox" id="chk-clean" checked> Clean noise</label>
    <label><input type="checkbox" id="chk-narrative" checked> Narrative</label>
    <label><input type="checkbox" id="chk-verdict" checked> Verdict card</label>
    <label><input type="checkbox" id="chk-raw-collapse" checked> Raw collapsible</label>
    <label><input type="checkbox" id="chk-diff"> Diff view</label>
    <button class="btn" onclick="resetInput()">↩ Reset</button>
    <button class="btn" onclick="copyClean()">📋 Copy cleaned</button>
  </div>
</header>

<div class="main">
  <div class="pane">
    <div class="pane-header">Raw Agent Output (editable)</div>
    <div class="pane-body" style="padding:.5rem;">
      <textarea id="raw-input" spellcheck="false"></textarea>
    </div>
  </div>
  <div class="pane">
    <div class="pane-header">Rendered Output</div>
    <div class="pane-body" id="render-pane"></div>
  </div>
</div>

<div id="info">Loading…</div>

<script>
const HARDCODED = {js_literal};

function escapeHtml(s) {{
  return String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;').replace(/'/g,'&#39;');
}}

function inlineMarkdown(text) {{
  let s = escapeHtml(text);
  s = s.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');
  s = s.replace(/`([^`]+)`/g, '<code class="prose-code">$1</code>');
  return s;
}}

function cleanAgentOutput(text) {{
  // Remove fenced json code blocks
  let out = text.replace(/```(?:json)?\\s*\\n[\\s\\S]*?\\n```/gi, '');

  // Ensure ## headers start on their own line
  out = out.replace(/([^\\n])(#{{2,4}} )/g, '$1\\n\\n$2');

  // Split common tool-call interjections onto their own lines when concatenated
  out = out.replace(/(?<=[^\\n])(Good\\.|Great\\.|Perfect\\.|Right\\.|Excellent\\.|Noted\\.|Let me |Now let me )/g, '\\n$1');

  // Drop everything before first ## header
  const firstHeader = out.search(/^#{{2,4}} /m);
  if (firstHeader > 0) out = out.slice(firstHeader);

  // Filter short tool-call interjection lines
  out = out.split('\\n').filter(line => {{
    const t = line.trim();
    if (!t) return true;
    if (t.startsWith('#')) return true;
    if (t.startsWith('|')) return true;
    if (/^[-*]\\s/.test(t)) return true;
    if (t.startsWith('`')) return true;
    if (t.length < 120 && /^(Good\\.?|Great\\.?|Perfect\\.?|Right\\.?|Excellent\\.?|Noted\\.?|Let me|Now let me|Moving on|I'll now|I can see|I need to|Looking at|Starting with|Checking|Next,|First,?\\s+I)/i.test(t)) return false;
    return true;
  }}).join('\\n');

  return out.trim();
}}

function renderMarkdownLite(text) {{
  const lines = text.split(/\\r?\\n/);
  let inList = false;
  const html = [];
  for (let i = 0; i < lines.length; i++) {{
    const t = lines[i].trim();
    if (!t) {{ if (inList) {{ html.push('</ul>'); inList = false; }} continue; }}
    const heading = /^(#{{2,4}})\\s+(.+)$/.exec(t);
    if (heading) {{
      if (inList) {{ html.push('</ul>'); inList = false; }}
      html.push(`<h3>${{inlineMarkdown(heading[2])}}</h3>`);
      continue;
    }}
    if (t.startsWith('|') && t.endsWith('|')) {{
      const tls = [];
      while (i < lines.length) {{
        const tl = lines[i].trim();
        if (!tl.startsWith('|') || !tl.endsWith('|')) break;
        tls.push(tl); i++;
      }}
      i--;
      if (inList) {{ html.push('</ul>'); inList = false; }}
      const rows = tls
        .filter(tl => !/^\\|\\s*:?-{{3,}}:?\\s*(\\|\\s*:?-{{3,}}:?\\s*)+\\|?$/.test(tl))
        .map(tl => tl.slice(1,-1).split('|').map(c => c.trim()));
      if (rows.length > 0) {{
        const [header, ...body] = rows;
        html.push('<table class="output-table"><thead><tr>');
        html.push(header.map(c => `<th>${{inlineMarkdown(c)}}</th>`).join(''));
        html.push('</tr></thead><tbody>');
        html.push(body.map(r => `<tr>${{r.map(c => `<td>${{inlineMarkdown(c)}}</td>`).join('')}}</tr>`).join(''));
        html.push('</tbody></table>');
      }}
      continue;
    }}
    const bullet = /^[-*]\\s+(.+)$/.exec(t);
    if (bullet) {{
      if (!inList) {{ html.push('<ul>'); inList = true; }}
      html.push(`<li>${{inlineMarkdown(bullet[1])}}</li>`);
      continue;
    }}
    if (inList) {{ html.push('</ul>'); inList = false; }}
    html.push(`<p>${{inlineMarkdown(t)}}</p>`);
  }}
  if (inList) html.push('</ul>');
  return `<div class="output-markdown">${{html.join('')}}</div>`;
}}

function tryReadSummaryObject(text) {{
  const fenced = /```(?:json)?\\s*\\n(\\{{[\\s\\S]*?\\}})\\s*\\n```/i.exec(text);
  if (fenced) {{ try {{ const o = JSON.parse(fenced[1]); if (o?.status) return o; }} catch {{}} }}
  const blocks = text.split(/\\n\\s*\\n/);
  for (let i = blocks.length - 1; i >= 0; i--) {{
    const b = blocks[i].trim();
    if (b.startsWith('{{') && b.endsWith('}}')) {{ try {{ const o = JSON.parse(b); if (o?.status) return o; }} catch {{}} }}
  }}
  const s = text.indexOf('{{'), e = text.lastIndexOf('}}');
  if (s >= 0 && e > s) {{ try {{ const o = JSON.parse(text.slice(s, e+1)); if (o?.status) return o; }} catch {{}} }}
  return null;
}}

function labelize(k) {{ return k.replaceAll('_', ' ').replace(/\\b\\w/g, c => c.toUpperCase()); }}

function renderValue(v) {{
  if (Array.isArray(v)) {{
    if (!v.length) return '<span style="color:#64748b">none</span>';
    return `<div class="output-chip-row">${{v.map(i => `<span class="output-chip">${{escapeHtml(String(i))}}</span>`).join('')}}</div>`;
  }}
  const s = String(v ?? '');
  if (s.length > 140) return `<span style="color:#94a3b8" title="${{escapeHtml(s)}}">${{escapeHtml(s.slice(0,140))}}&hellip;</span>`;
  return `<span>${{escapeHtml(s || 'none')}}</span>`;
}}

function renderSummary(summary) {{
  const status = String(summary.status ?? 'unknown').toLowerCase();
  const titles = {{ stop: 'Investigation halted', duplicate: 'Duplicate located', go: 'Case cleared' }};
  const statusTitle = titles[status] ?? 'Agent result';
  const fields = Object.entries(summary).filter(([k]) => k !== 'status')
    .map(([k,v]) => `<div class="output-field"><span class="output-field-label">${{escapeHtml(labelize(k))}}</span>${{renderValue(v)}}</div>`)
    .join('');
  return `<div class="output-summary"><div class="output-verdict status-${{escapeHtml(status)}}"><strong>${{escapeHtml(statusTitle)}}</strong><span>TRIAGE returned ${{escapeHtml(String(summary.status ?? 'UNKNOWN'))}}</span></div><div class="output-field-grid">${{fields}}</div></div>`;
}}

function buildDiff(original, cleaned) {{
  const cleanedSet = new Set(cleaned.split('\\n'));
  return original.split('\\n').map(l => {{
    if (!l.trim()) return `<span class="diff-blank">  ·</span>`;
    if (cleanedSet.has(l)) return `<span class="diff-kept">+ ${{escapeHtml(l.slice(0,150))}}</span>`;
    return `<span class="diff-removed">− ${{escapeHtml(l.slice(0,150))}}</span>`;
  }}).join('');
}}

function render() {{
  const raw = document.getElementById('raw-input').value;
  const doClean   = document.getElementById('chk-clean').checked;
  const showNarr  = document.getElementById('chk-narrative').checked;
  const showVerdict = document.getElementById('chk-verdict').checked;
  const showRaw   = document.getElementById('chk-raw-collapse').checked;
  const showDiff  = document.getElementById('chk-diff').checked;

  const pane = document.getElementById('render-pane');
  const info = document.getElementById('info');

  const summary = tryReadSummaryObject(raw);
  const cleaned = doClean ? cleanAgentOutput(raw) : raw;
  const hasNarrative = /^#{{2,4}} /m.test(cleaned);

  let html = '';

  if (showDiff && doClean) {{
    html += `<div style="margin-bottom:.6rem;font-size:.7rem;color:#94a3b8;border-bottom:1px solid #21262d;padding-bottom:.35rem"><span style="color:#4ade80">+ kept</span> &nbsp; <span style="color:#f87171">− removed</span></div>`;
    html += `<div style="line-height:1.7">${{buildDiff(raw, cleaned)}}</div>`;
  }} else {{
    if (showNarr && hasNarrative) {{
      const wrapper = summary ? `<div class="output-narrative">${{renderMarkdownLite(cleaned)}}</div>` : renderMarkdownLite(cleaned);
      html += wrapper;
    }}
    if (summary && showVerdict) {{
      html += renderSummary(summary);
    }}
    if (showRaw) {{
      html += `<details class="output-details"><summary>Raw transcript (${{raw.length.toLocaleString()}} chars)</summary><pre class="output-raw">${{escapeHtml(raw)}}</pre></details>`;
    }}
    if (!html.trim()) {{
      html = `<pre class="output-raw">${{escapeHtml(cleaned || raw)}}</pre>`;
    }}
  }}

  pane.innerHTML = html;

  const rawLines = raw.split('\\n').length;
  const cleanLines = cleaned.split('\\n').length;
  info.textContent = `Raw: ${{raw.length.toLocaleString()}} chars (${{rawLines}} lines) → Cleaned: ${{cleaned.length.toLocaleString()}} chars (${{cleanLines}} lines) — ${{rawLines - cleanLines}} lines removed — Summary: ${{summary ? summary.status.toUpperCase() : 'not detected'}}`;
}}

function resetInput() {{
  document.getElementById('raw-input').value = HARDCODED;
  render();
}}

function copyClean() {{
  const raw = document.getElementById('raw-input').value;
  navigator.clipboard.writeText(cleanAgentOutput(raw));
}}

document.getElementById('raw-input').addEventListener('input', render);
['chk-clean','chk-narrative','chk-verdict','chk-raw-collapse','chk-diff'].forEach(id =>
  document.getElementById(id).addEventListener('change', render)
);

resetInput();
</script>
</body>
</html>"""

with open('tools/output-sandbox.html', 'w', encoding='utf-8') as f:
    f.write(html)

print('Done! Size:', len(html), 'bytes')
