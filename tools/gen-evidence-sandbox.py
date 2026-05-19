"""Generate evidence-sandbox.html with real evidence data embedded for client-side iteration."""
import json, os, html

tools_dir = os.path.dirname(os.path.abspath(__file__))
data_path = os.path.join(tools_dir, 'evidence-data.json')
out_path  = os.path.join(tools_dir, 'evidence-sandbox.html')

with open(data_path) as f:
    rows = json.load(f)

# Apply same filter logic as FindingGroups in C# model
MEANINGFUL_KINDS = {'stage-evidence', 'policy-rationale', 'required-action', 'repository-profile'}
findings = [
    r for r in rows
    if r['Kind'] in MEANINGFUL_KINDS
    and not r['Name'].startswith('tool-output:')
]

# Group by stage
from collections import defaultdict
STAGE_ORDER = ['triage', 'plan', 'implement', 'review', 'docs', 'deliver']
stage_groups = defaultdict(list)
for f in findings:
    stage_groups[f['StageName']].append(f)

def stage_sort(name):
    try: return STAGE_ORDER.index(name.lower())
    except ValueError: return 99

stages_sorted = sorted(stage_groups.items(), key=lambda x: stage_sort(x[0]))

# Serialize the filtered findings as JSON for embedding in the sandbox
js_data = json.dumps(findings, indent=2, default=str)

html_out = f"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>Evidence Panel Sandbox — Cyberpilot</title>
<style>
* {{ box-sizing: border-box; margin: 0; padding: 0; }}
body {{ font-family: system-ui, sans-serif; background: #0a0f1a; color: #d1d5db; min-height: 100vh; }}

/* ── Layout ── */
.sandbox-layout {{ display: grid; grid-template-columns: 260px 1fr; min-height: 100vh; }}
.sidebar {{ background: #0d1117; border-right: 1px solid #21262d; padding: 1rem; display: flex; flex-direction: column; gap: .75rem; position: sticky; top: 0; height: 100vh; overflow-y: auto; }}
.main {{ padding: 1.5rem; overflow-y: auto; }}

/* ── Sidebar controls ── */
.sidebar h2 {{ font-size: .72rem; font-weight: 900; letter-spacing: .14em; color: #22d3ee; text-transform: uppercase; margin-bottom: .25rem; }}
.sidebar p {{ font-size: .7rem; color: #64748b; line-height: 1.5; }}
.control-group {{ display: flex; flex-direction: column; gap: .3rem; margin-top: .5rem; }}
.control-group label {{ display: flex; align-items: center; gap: .4rem; font-size: .72rem; color: #94a3b8; cursor: pointer; }}
.control-group input[type=checkbox] {{ accent-color: #22d3ee; }}
.control-group input[type=range] {{ accent-color: #22d3ee; width: 100%; }}
.control-group select {{ width: 100%; background: #161b22; border: 1px solid #30363d; color: #e2e8f0; padding: .25rem .4rem; border-radius: 4px; font-size: .72rem; }}
.stat-row {{ font-size: .68rem; color: #64748b; display: flex; justify-content: space-between; border-top: 1px solid #21262d; padding-top: .5rem; margin-top: .25rem; }}

/* ── Findings panel (prototype) ── */
.findings-panel {{
    border: 1px solid rgba(34, 211, 238, .2);
    border-radius: 10px;
    background:
        linear-gradient(135deg, rgba(12, 18, 33, .95), rgba(6, 10, 18, .85)),
        linear-gradient(90deg, rgba(34, 211, 238, .06), transparent 60%, rgba(52, 211, 153, .04));
    overflow: hidden;
}}
.findings-panel-header {{
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: .85rem 1rem;
    border-bottom: 1px solid rgba(34, 211, 238, .16);
    cursor: pointer;
    user-select: none;
    transition: background .18s;
}}
.findings-panel-header:hover {{ background: rgba(34, 211, 238, .06); }}
.findings-panel-header .eyebrow {{
    font-size: .62rem;
    font-weight: 900;
    letter-spacing: .14em;
    text-transform: uppercase;
    color: #22d3ee;
    margin-bottom: .15rem;
}}
.findings-panel-header h3 {{ margin: 0; font-size: 1rem; font-weight: 900; color: #f0f9ff; letter-spacing: .02em; }}
.panel-meta {{ display: flex; align-items: center; gap: .5rem; }}
.evidence-count {{
    font-size: .68rem; font-weight: 900; color: #22d3ee;
    background: rgba(34,211,238,.12); border: 1px solid rgba(34,211,238,.25);
    border-radius: 999px; padding: .1rem .45rem; letter-spacing: .04em;
}}
.chevron {{ color: #64748b; font-size: .9rem; transition: transform .2s; }}
.chevron.open {{ transform: rotate(180deg); }}

.findings-body {{ padding: 1rem; display: flex; flex-direction: column; gap: 1.25rem; }}
.findings-stage-group {{ display: flex; flex-direction: column; gap: .55rem; }}
.findings-stage-label {{
    margin: 0 0 .35rem; font-size: .68rem; font-weight: 900; letter-spacing: .14em;
    text-transform: uppercase; color: #22d3ee;
    padding-bottom: .3rem; border-bottom: 1px solid rgba(34, 211, 238, .15);
}}
.findings-list {{
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(min(100%, 22rem), 1fr));
    gap: .55rem;
}}
.finding-item {{
    padding: .65rem .75rem;
    border-radius: 8px;
    background: rgba(255, 255, 255, .04);
    border: 1px solid rgba(148, 163, 184, .14);
    border-left: 3px solid rgba(148, 163, 184, .3);
    display: flex; flex-direction: column; gap: .3rem;
}}
.finding-item.kind-stage-evidence {{ border-left-color: #22d3ee; }}
.finding-item.kind-policy-rationale {{ border-left-color: #a78bfa; }}
.finding-item.kind-required-action {{ border-left-color: #fbbf24; }}
.finding-item.kind-repository-profile {{ border-left-color: #34d399; }}
.finding-item-header {{ display: flex; align-items: center; gap: .5rem; flex-wrap: wrap; }}
.finding-kind {{
    font-size: .6rem; font-weight: 900; letter-spacing: .1em;
    text-transform: uppercase; color: #22d3ee; white-space: nowrap;
}}
.finding-name {{ font-size: .72rem; font-weight: 700; color: #94a3b8; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 20ch; }}
.finding-summary {{ margin: 0; font-size: .78rem; color: #cbd5e1; line-height: 1.55; }}
.finding-link {{ font-size: .7rem; color: #22d3ee; text-decoration: none; margin-top: .15rem; }}
.finding-link:hover {{ text-decoration: underline; }}

/* ── Raw data inspector ── */
.data-inspector {{
    margin-top: 1.5rem;
    border: 1px solid #21262d;
    border-radius: 8px;
    overflow: hidden;
}}
.inspector-header {{
    background: #161b22;
    padding: .5rem .75rem;
    font-size: .7rem;
    font-weight: 700;
    letter-spacing: .08em;
    color: #64748b;
    text-transform: uppercase;
    cursor: pointer;
    display: flex;
    justify-content: space-between;
}}
.inspector-body {{
    max-height: 300px; overflow-y: auto;
    background: #020617;
    font-family: 'Cascadia Code', Consolas, monospace;
    font-size: .68rem; line-height: 1.6; color: #6b7280;
    padding: .6rem .75rem;
    white-space: pre-wrap; word-break: break-all;
}}
</style>
</head>
<body>

<div class="sandbox-layout">
  <!-- Sidebar controls -->
  <aside class="sidebar">
    <div>
      <h2>🔬 Evidence Sandbox</h2>
      <p>Prototype the findings panel using real run data. Tweak the controls and watch it re-render live.</p>
    </div>
    <div class="control-group">
      <strong style="font-size:.68rem;color:#64748b;letter-spacing:.1em;text-transform:uppercase;">Filter kinds</strong>
      <label><input type="checkbox" class="kind-filter" value="stage-evidence" checked> Stage evidence</label>
      <label><input type="checkbox" class="kind-filter" value="policy-rationale" checked> Policy rationale</label>
      <label><input type="checkbox" class="kind-filter" value="required-action" checked> Required action</label>
      <label><input type="checkbox" class="kind-filter" value="repository-profile" checked> Repo profile</label>
    </div>
    <div class="control-group">
      <strong style="font-size:.68rem;color:#64748b;letter-spacing:.1em;text-transform:uppercase;">Display</strong>
      <label><input type="checkbox" id="chk-expanded" checked> Expanded by default</label>
      <label><input type="checkbox" id="chk-group-stages" checked> Group by stage</label>
      <label><input type="checkbox" id="chk-show-kind" checked> Show kind badge</label>
      <label><input type="checkbox" id="chk-show-name" checked> Show name</label>
      <label><input type="checkbox" id="chk-show-summary" checked> Show summary</label>
      <label><input type="checkbox" id="chk-grid" checked> Grid layout (vs list)</label>
    </div>
    <div class="control-group">
      <strong style="font-size:.68rem;color:#64748b;letter-spacing:.1em;text-transform:uppercase;">Summary truncation</strong>
      <input type="range" id="summary-len" min="60" max="500" value="300" step="20">
      <span id="summary-len-val" style="font-size:.68rem;color:#94a3b8;">300 chars</span>
    </div>
    <div class="control-group">
      <strong style="font-size:.68rem;color:#64748b;letter-spacing:.1em;text-transform:uppercase;">Sort findings by</strong>
      <select id="sort-by">
        <option value="stage">Stage order</option>
        <option value="kind">Kind</option>
        <option value="name">Name</option>
        <option value="created">Created at</option>
      </select>
    </div>
    <div class="stat-row" id="stat-row">
      <span id="stat-count">— findings</span>
      <span id="stat-stages">— stages</span>
    </div>
    <details style="margin-top:auto;">
      <summary style="font-size:.68rem;color:#64748b;cursor:pointer;">Raw JSON sample</summary>
      <pre id="raw-sample" style="font-size:.6rem;color:#475569;white-space:pre-wrap;word-break:break-all;margin-top:.5rem;max-height:200px;overflow-y:auto;"></pre>
    </details>
  </aside>

  <!-- Main render area -->
  <main class="main">
    <div id="panel-container"></div>
  </main>
</div>

<script>
const ALL_DATA = {js_data};

const STAGE_ORDER = ['triage', 'plan', 'implement', 'review', 'docs', 'deliver'];

function stageIndex(name) {{
  const i = STAGE_ORDER.indexOf((name || '').toLowerCase());
  return i === -1 ? 99 : i;
}}

function humanKind(kind) {{
  return {{ 'stage-evidence': 'Evidence', 'policy-rationale': 'Policy', 'required-action': 'Action', 'repository-profile': 'Repo' }}[kind] || kind;
}}

function humanStage(name) {{
  if (!name) return 'Unknown';
  return name.charAt(0).toUpperCase() + name.slice(1);
}}

function esc(s) {{
  return String(s || '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}}

function truncate(s, max) {{
  s = String(s || '');
  return s.length > max ? s.slice(0, max) + '…' : s;
}}

function getOptions() {{
  const kinds = new Set([...document.querySelectorAll('.kind-filter:checked')].map(el => el.value));
  return {{
    kinds,
    expanded: document.getElementById('chk-expanded').checked,
    groupByStage: document.getElementById('chk-group-stages').checked,
    showKind: document.getElementById('chk-show-kind').checked,
    showName: document.getElementById('chk-show-name').checked,
    showSummary: document.getElementById('chk-show-summary').checked,
    grid: document.getElementById('chk-grid').checked,
    summaryLen: parseInt(document.getElementById('summary-len').value, 10),
    sortBy: document.getElementById('sort-by').value,
  }};
}}

function getFindings(opts) {{
  let items = ALL_DATA.filter(r => opts.kinds.has(r.Kind));
  if (opts.sortBy === 'stage')   items = [...items].sort((a,b) => stageIndex(a.StageName) - stageIndex(b.StageName) || a.Kind.localeCompare(b.Kind));
  if (opts.sortBy === 'kind')    items = [...items].sort((a,b) => a.Kind.localeCompare(b.Kind));
  if (opts.sortBy === 'name')    items = [...items].sort((a,b) => a.Name.localeCompare(b.Name));
  if (opts.sortBy === 'created') items = [...items].sort((a,b) => a.CreatedAt.localeCompare(b.CreatedAt));
  return items;
}}

function renderFindingCard(item, opts) {{
  const kindCls = 'kind-' + item.Kind;
  const kindBadge = opts.showKind ? `<span class="finding-kind">${{humanKind(item.Kind)}}</span>` : '';
  const nameBadge = opts.showName ? `<span class="finding-name" title="${{esc(item.Name)}}">${{esc(item.Name)}}</span>` : '';
  const header = (kindBadge || nameBadge) ? `<div class="finding-item-header">${{kindBadge}}${{nameBadge}}</div>` : '';
  const summary = opts.showSummary && item.Summary
    ? `<p class="finding-summary">${{esc(truncate(item.Summary, opts.summaryLen))}}</p>` : '';
  const link = item.Uri ? `<a class="finding-link" href="${{esc(item.Uri)}}" target="_blank">View ↗</a>` : '';
  return `<article class="finding-item ${{kindCls}}">${{header}}${{summary}}${{link}}</article>`;
}}

function render() {{
  const opts = getOptions();
  const items = getFindings(opts);
  const container = document.getElementById('panel-container');

  document.getElementById('stat-count').textContent = items.length + ' findings';
  document.getElementById('stat-stages').textContent = new Set(items.map(i=>i.StageName)).size + ' stages';
  document.getElementById('summary-len-val').textContent = opts.summaryLen + ' chars';

  if (items.length === 0) {{
    container.innerHTML = '<p style="color:#64748b;font-size:.8rem;padding:1rem;">No findings match current filters.</p>';
    return;
  }}

  const bodyId = 'findings-body';
  const showBody = opts.expanded;
  let bodyHtml = '';

  if (opts.groupByStage) {{
    const groups = {{}};
    const groupOrder = [];
    for (const item of items) {{
      if (!groups[item.StageName]) {{ groups[item.StageName] = []; groupOrder.push(item.StageName); }}
      groups[item.StageName].push(item);
    }}
    const sortedGroups = groupOrder
      .filter((v, i, a) => a.indexOf(v) === i)
      .sort((a,b) => stageIndex(a) - stageIndex(b));

    bodyHtml = sortedGroups.map(stage => {{
      const stageItems = groups[stage];
      const cards = stageItems.map(it => renderFindingCard(it, opts)).join('');
      const listStyle = opts.grid ? 'findings-list' : 'findings-list' + ' style="grid-template-columns:1fr"';
      return `
        <div class="findings-stage-group">
          <h4 class="findings-stage-label">${{humanStage(stage)}}</h4>
          <div class="${{opts.grid ? 'findings-list' : 'findings-list'}}" style="${{opts.grid ? '' : 'grid-template-columns:1fr'}}">${{cards}}</div>
        </div>`;
    }}).join('');
  }} else {{
    const cards = items.map(it => renderFindingCard(it, opts)).join('');
    bodyHtml = `<div class="findings-list" style="${{opts.grid ? '' : 'grid-template-columns:1fr'}}">${{cards}}</div>`;
  }}

  const chevronCls = showBody ? 'chevron open' : 'chevron';
  container.innerHTML = `
    <div class="findings-panel">
      <div class="findings-panel-header" onclick="togglePanel()">
        <div>
          <p class="eyebrow">Agent intelligence</p>
          <h3>Evidence &amp; Findings</h3>
        </div>
        <div class="panel-meta">
          <span class="evidence-count">${{items.length}}</span>
          <span class="${{chevronCls}}" id="panel-chevron">&#8964;</span>
        </div>
      </div>
      <div class="findings-body" id="${{bodyId}}" style="${{showBody ? '' : 'display:none'}}">${{bodyHtml}}</div>
    </div>`;

  // Show a sample in the sidebar raw viewer
  document.getElementById('raw-sample').textContent = JSON.stringify(items.slice(0, 2), null, 2);
}}

function togglePanel() {{
  const body = document.getElementById('findings-body');
  const chevron = document.getElementById('panel-chevron');
  if (!body) return;
  const hidden = body.style.display === 'none';
  body.style.display = hidden ? '' : 'none';
  if (chevron) chevron.classList.toggle('open', hidden);
}}

// Wire controls
document.querySelectorAll('input, select').forEach(el => el.addEventListener('change', render));

render();
</script>
</body>
</html>
"""

with open(out_path, 'w', encoding='utf-8') as f:
    f.write(html_out)
print(f"Wrote {out_path}")
