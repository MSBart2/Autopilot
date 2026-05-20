(() => {
    const bootstrapEl = document.getElementById('pipeline-details-bootstrap');
    if (!bootstrapEl) return;

    let bootstrap = {};
    try {
        bootstrap = JSON.parse(bootstrapEl.textContent ?? '{}');
    } catch {
        return;
    }

    const runId = String(bootstrap.runId ?? '');
    if (!runId) return;
    const runIsActive = Boolean(bootstrap.runIsActive);
    const runIsRemote = Boolean(bootstrap.runIsRemote);
    const maxStageRetries = Number(bootstrap.maxStageRetries ?? 3);
    const runStartUtc = typeof bootstrap.runStartUtc === 'string' ? bootstrap.runStartUtc : null;
    const existingLogs = Array.isArray(bootstrap.logs) ? bootstrap.logs : [];
    const existingDispatches = Array.isArray(bootstrap.dispatches) ? bootstrap.dispatches : [];
    const serverCanReworkFromReview = Boolean(bootstrap.canReworkFromReview);

    // ── Agent portrait images ────────────────────────────────────────────
    const PORTRAIT = {
        triage:    '/images/agents/triage.png',
        plan:      '/images/agents/plan.png',
        implement: '/images/agents/implement.png',
        review:    '/images/agents/review.png',
        docs:      '/images/agents/docs.png',
        deliver:   '/images/agents/deliver.png',
        pipeline:  '/images/agents/deliver.png'  // reuse deliver for pipeline/cyberpilot
    };

    // ── Agent definitions ──────────────────────────────────────────────
    const AGENTS = {
        'triage':        { mark:'CASE', img: PORTRAIT.triage,    display:'TRIAGE',        role:'Tech-noir detective',     tagline:'Evidence, duplicates, and case viability.',              cls:'DETECTIVE',      color:'#7c3aed', rgb:'124,58,237'  },
        'plan':          { mark:'#',    img: PORTRAIT.plan,      display:'PLAN',          role:'Heist mastermind',         tagline:'Blueprints, sequences, and the perfect score.',          cls:'MASTERMIND',     color:'#2563eb', rgb:'37,99,235'   },
        'implement':     { mark:'>',    img: PORTRAIT.implement, display:'IMPLEMENT',     role:'Speed-demon coder',        tagline:'Code hits the branch before you blink.',                 cls:'ENGINEER',       color:'#db2777', rgb:'219,39,119'  },
        'review':        { mark:'!',    img: PORTRAIT.review,    display:'REVIEW',        role:'80s music critic',         tagline:'Two thumbs up — or a scathing teardown.',                cls:'CRITIC',         color:'#ea580c', rgb:'234,88,12'   },
        'docs':          { mark:'i',    img: PORTRAIT.docs,      display:'DOCS',          role:'Documentation bestie \u2728', tagline:'Making docs clear, complete, and developer-friendly.',  cls:'BESTIE',         color:'#0d9488', rgb:'13,148,136'  },
        'deliver':       { mark:'^',    img: PORTRAIT.deliver,   display:'DELIVER',       role:'NASA landing director',    tagline:'Touchdown confirmed. Payload on the surface of main.',   cls:'FLIGHT DIRECTOR', color:'#16a34a', rgb:'22,163,74'   },
        'pipeline':      { mark:'AP',   img: PORTRAIT.pipeline,  display:'CYBERPILOT',     role:'Mission control',          tagline:'Run-level coordination and status updates.',             cls:'CONTROL',        color:'#0f766e', rgb:'15,118,110'   },
    };

    const COMPLETION_TEXT = {
        triage:    'Case cleared',
        plan:      'Blueprint delivered',
        implement: 'Code shipped',
        review:    'Review sealed',
        docs:      'Docs published',
        deliver:   'Mission landed',
    };

    const STATE_NOTES = {
        done: stage => (COMPLETION_TEXT[stage] ?? 'Completed') + ' — cleared for the next stage.',
        duplicate: 'Duplicate located — work stopped because this issue is already covered.',
        stopped: 'Stopped intentionally. This agent needs clearer input before continuing.',
        failed: 'Failed during execution. Review the output for the blocking error.',
        skipped: 'Skipped by pipeline rules or prior stage outcome.',
    };

    const feed      = document.getElementById('agent-feed');
    let currentStage = null;
    let cursorEl     = null;

    // ── Round tracking for re-entry ────────────────────────────────────
    const activeCardIds = {};   // stage → current card DOM id
    const roundCounts   = {};   // stage → highest round seen
    const runIsTerminal = !runIsActive;
    const isRemoteRun = runIsRemote;
    const antiForgeryToken = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';

    function getActiveCard(stage) {
        const id = activeCardIds[stage] || `card-${stage}`;
        return document.getElementById(id);
    }

    // ── Build a card DOM node ──────────────────────────────────────────
    const STAGE_ORDER = ['triage','plan','implement','review','docs','deliver'];
    const STAGE_DOCUMENTS = {
        triage: 'Triage',
        plan: 'Plan',
        implement: 'Implement',
        review: 'Review',
        docs: 'Docs',
        deliver: 'Deliver'
    };

    function buildCard(stage, round, retryCount, retryReason) {
        round = round || 1;
        const a = AGENTS[stage];
        if (!a) return null;

        const stageIndex = STAGE_ORDER.indexOf(stage);
        const isRight = stageIndex % 2 === 1;

        const cardId = round > 1 ? `card-${stage}-${round}` : `card-${stage}`;
        const attemptSuffix = (retryCount != null && retryCount > 0) ? ` (Attempt ${retryCount + 1})` : (round > 1 ? ` (Round ${round})` : '');
        const displayName = `${a.display}${attemptSuffix}`;

        const card = document.createElement('div');
        card.className   = `agent-card state-active ${isRight ? 'card-right' : 'card-left'}`;
        card.id          = cardId;
        card.dataset.stage = stage;
        card.dataset.round = round;
        card.style.setProperty('--glow-color', a.color);
        card.style.setProperty('--glow',    `rgba(${a.rgb},.28)`);
        card.style.setProperty('--glow-sm', `rgba(${a.rgb},.12)`);
        card.style.setProperty('--agent-color', a.color);
        card.style.setProperty('--agent-rgb', a.rgb);

        card.innerHTML = `
          <div class="agent-portrait"
               style="background:linear-gradient(155deg,#0f172a 0%,${a.color} 100%)">
            <div class="portrait-frame">
              <div class="portrait-face" aria-hidden="true">
                <img src="${a.img}" alt="${displayName}" />
              </div>
            </div>
            <div class="portrait-nameplate">
              <span class="portrait-cls">${a.cls}</span>
              <span class="portrait-name">${displayName}</span>
            </div>
          </div>
          <div class="agent-content">
            <div class="agent-content-header">
              <span class="agent-display-name">${displayName}</span>
              <span class="agent-role-label">${a.role}</span>
              <span class="badge text-bg-primary agent-status-badge d-flex align-items-center gap-1">
                <span class="spinner-border" style="width:.6rem;height:.6rem;border-width:.15em" role="status"></span>
                Running
              </span>
              <span class="agent-dur"></span>
            </div>
            <div class="agent-tagline">"${a.tagline}"</div>
                        ${retryReason ? `<div class="agent-retry-reason"><span class="agent-retry-reason-label">Retry reason</span><span>${escapeHtml(retryReason)}</span></div>` : ''}
            <div class="agent-state-note" aria-live="polite"></div>
                        <div class="agent-output"></div>
          </div>`;

        return card;
    }

    function escapeHtml(value) {
        return String(value ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#39;');
    }

    function inlineMarkdown(text) {
        let safe = escapeHtml(text);
        safe = safe.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');
        safe = safe.replace(/`([^`]+)`/g, '<code class="prose-code">$1</code>');
        return safe;
    }

    // Strip JSON blobs, pre-section thinking prose, and short tool-call interjections
    // so only the agent's actual narrative sections are rendered.
    function cleanAgentOutput(text) {
        // Remove fenced json / bare json code blocks
        let out = text.replace(/```(?:json)?\s*\n[\s\S]*?\n```/gi, '');

        // Ensure ## / ### section headers always start on their own line
        out = out.replace(/([^\n])(#{2,4} )/g, '$1\n\n$2');

        // Plan artifact: numbered task entries like "1. **File path:**" or "## The Jobs"
        // often arrive concatenated — split before each numbered list item
        out = out.replace(/([^\n])(\d+\. \*\*)/g, '$1\n\n$2');

        // Split before standalone "- " bullet lines when concatenated mid-text
        out = out.replace(/([^\n])(- \*\*)/g, '$1\n\n$2');

        // Split before bold section markers common in plan artifacts
        out = out.replace(/([^\n])(\*\*(?:Dependencies|Action|Details|File path|Assigned agent|File|Task):\*\*)/g, '$1\n$2');

        // Split common tool-call interjections onto their own lines when concatenated mid-text.
        out = out.replace(/(?<=[^\n])(Good\.|Great\.|Perfect\.|Right\.|Excellent\.|Noted\.|Let me |Now let me )/g, '\n$1');

        // Drop everything before the first ## header (tool-call noise before the narrative)
        const firstHeader = out.search(/^#{2,4} /m);
        if (firstHeader > 0) out = out.slice(firstHeader);

        // Filter short tool-call interjection lines within sections
        out = out.split('\n').filter(line => {
            const t = line.trim();
            if (!t) return true;                      // keep blank lines for spacing
            if (t.startsWith('#')) return true;       // section headers
            if (t.startsWith('|')) return true;       // table rows
            if (/^[-*]\s/.test(t)) return true;       // bullet items
            if (/^\d+\.\s/.test(t)) return true;      // numbered list items
            if (t.startsWith('`')) return true;       // code lines
            if (t.length < 120 && /^(Good\.?|Great\.?|Perfect\.?|Right\.?|Excellent\.?|Noted\.?|Let me|Now let me|Moving on|I'll now|I can see|I need to|Looking at|Starting with|Checking|Next,|First,?\s+I)/i.test(t)) return false;
            return true;
        }).join('\n');

        return out.trim();
    }

    function labelize(key) {
        return key.replaceAll('_', ' ').replace(/\b\w/g, char => char.toUpperCase());
    }

    function chip(text, cls) { return `<span class="chip ${cls}">${escapeHtml(text)}</span>`; }
    function difficultyChip(v) {
        const cls = v === 'hard' ? 'chip-hard' : v === 'medium' ? 'chip-medium' : 'chip-neutral';
        return chip(v, cls);
    }
    function priorityChip(v) {
        const cls = v === 'critical' ? 'chip-critical' : v === 'high' ? 'chip-high' : 'chip-neutral';
        return chip(v, cls);
    }

    function tryReadSummaryObject(text) {
        // Strategy 1: Look for fenced JSON code blocks (```json ... ```)
        const fenced = /```(?:json)?\s*\n(\{[\s\S]*?\})\s*\n```/i.exec(text);
        if (fenced) {
            try {
                const obj = JSON.parse(fenced[1]);
                if (obj && typeof obj === 'object' && obj.status) return obj;
            } catch {}
        }

        // Strategy 2: Look for a standalone JSON paragraph (blank-line delimited)
        const blocks = text.split(/\n\s*\n/);
        for (let i = blocks.length - 1; i >= 0; i--) {
            const block = blocks[i].trim();
            if (block.startsWith('{') && block.endsWith('}')) {
                try {
                    const obj = JSON.parse(block);
                    if (obj && typeof obj === 'object' && obj.status) return obj;
                } catch {}
            }
        }

        // Strategy 3: Original approach — first { to last }
        const start = text.indexOf('{');
        const end = text.lastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try {
            const obj = JSON.parse(text.slice(start, end + 1));
            if (obj && typeof obj === 'object' && obj.status) return obj;
        } catch {}

        return null;
    }

    function objectLabel(item) {
        // Extract a human-readable label from a plain object
        const LABEL_KEYS = ['name', 'title', 'file', 'path', 'key', 'id', 'label', 'type', 'kind'];
        for (const k of LABEL_KEYS) {
            if (item[k] && typeof item[k] === 'string') return item[k];
        }
        // Fall back to the first short string value
        for (const v of Object.values(item)) {
            if (typeof v === 'string' && v.length <= 80) return v;
        }
        return null;
    }

    function renderKvObject(obj) {
        const rows = Object.entries(obj).map(([k, v]) => {
            const label = escapeHtml(labelize(k));
            let cell;
            if (Array.isArray(v)) {
                cell = v.length === 0
                    ? '<span class="text-muted">none</span>'
                    : v.map(it => {
                        if (it && typeof it === 'object') {
                            const lbl = objectLabel(it);
                            return lbl
                                ? `<span class="output-chip" title="${escapeHtml(JSON.stringify(it))}">${escapeHtml(lbl)}</span>`
                                : `<span class="output-chip">${escapeHtml(JSON.stringify(it))}</span>`;
                        }
                        return `<span class="output-chip">${escapeHtml(String(it))}</span>`;
                    }).join('');
                cell = `<div class="output-chip-row">${cell}</div>`;
            } else if (v && typeof v === 'object') {
                cell = `<pre class="output-code">${escapeHtml(JSON.stringify(v, null, 2))}</pre>`;
            } else {
                const s = String(v ?? '');
                cell = s.length > 140
                    ? `<span class="output-value-long" title="${escapeHtml(s)}">${escapeHtml(s.slice(0, 140))}&hellip;</span>`
                    : `<span>${escapeHtml(s || 'none')}</span>`;
            }
            return `<div class="output-field"><span class="output-field-label">${label}</span>${cell}</div>`;
        }).join('');
        return `<div class="output-field-grid">${rows}</div>`;
    }

    function renderValue(value) {
        if (Array.isArray(value)) {
            if (value.length === 0) return '<span class="text-muted">none</span>';
            // Array of plain objects → extract a label per item
            if (value.some(it => it && typeof it === 'object')) {
                const chips = value.map(item => {
                    if (!item || typeof item !== 'object') return `<span class="output-chip">${escapeHtml(String(item))}</span>`;
                    const lbl = objectLabel(item);
                    return lbl
                        ? `<span class="output-chip" title="${escapeHtml(JSON.stringify(item, null, 2))}">${escapeHtml(lbl)}</span>`
                        : `<span class="output-chip">${escapeHtml(JSON.stringify(item))}</span>`;
                }).join('');
                return `<div class="output-chip-row">${chips}</div>`;
            }
            // Array of primitives
            return `<div class="output-chip-row">${value.map(item => `<span class="output-chip">${escapeHtml(String(item))}</span>`).join('')}</div>`;
        }

        if (value && typeof value === 'object') {
            // Plain object → render as key-value rows inside the field
            return renderKvObject(value);
        }

        const s = String(value ?? '');
        if (s.length > 140) {
            return `<span class="output-value-long" title="${escapeHtml(s)}">${escapeHtml(s.slice(0, 140))}&hellip;</span>`;
        }
        return `<span>${escapeHtml(s || 'none')}</span>`;
    }


    function renderSummary(stage, summary) {
        const status = String(summary.status ?? 'unknown').toLowerCase();
        const completionLabel = COMPLETION_TEXT[stage] ?? 'Agent result';
        const titles = { stop: '🚫 Investigation Halted', duplicate: '🔁 Duplicate Located', go: `✅ ${completionLabel}` };
        const statusTitle = titles[status] ?? 'Agent Result';
        const statusChipCls = status === 'go' ? 'chip-go' : status === 'stop' ? 'chip-stop' : 'chip-neutral';

        const verdictRow = `<div class="output-verdict status-${escapeHtml(status)}">
          <strong>${escapeHtml(statusTitle)}</strong>
          ${chip(String(summary.status ?? 'UNKNOWN').toUpperCase(), statusChipCls)}
        </div>`;

        const meta = [];
        if (summary.type)                   meta.push(chip(summary.type, 'chip-neutral'));
        if (summary.difficulty)             meta.push(difficultyChip(summary.difficulty));
        if (summary.priority)               meta.push(priorityChip(summary.priority));
        if (summary.recommended_model_tier) meta.push(chip('model: ' + summary.recommended_model_tier, 'chip-neutral'));
        const metaRow = meta.length
            ? `<div class="summary-chips-row"><span class="summary-section-label">classification</span>${meta.join('')}</div>`
            : '';

        const agents = Array.isArray(summary.agents) ? summary.agents : [];
        const agentsRow = agents.length
            ? `<div class="summary-chips-row"><span class="summary-section-label">agents</span>${agents.map(a => chip(a, 'chip-agent')).join('')}</div>`
            : '';

        const scope = Array.isArray(summary.scope) ? summary.scope : [];
        const scopeRow = scope.length
            ? `<div class="summary-chips-row"><span class="summary-section-label">scope</span>${scope.map(s => chip(s, 'chip-scope')).join('')}</div>`
            : '';

        const related = [...(summary.related_issues ?? []), ...(summary.related_prs ?? [])];
        const relatedRow = related.length
            ? `<div class="summary-chips-row"><span class="summary-section-label">related</span>${related.map(r => chip('#' + r, 'chip-neutral')).join('')}</div>`
            : '';

        return `<div class="output-summary">${verdictRow}${metaRow}${agentsRow}${scopeRow}${relatedRow}</div>`;
    }

    function renderMarkdownLite(text) {
        const lines = text.split(/\r?\n/);
        let inList = false;
        const html = [];

        for (let index = 0; index < lines.length; index++) {
            const line = lines[index];
            const trimmed = line.trim();
            if (!trimmed) {
                if (inList) { html.push('</ul>'); inList = false; }
                continue;
            }

            const heading = /^(#{2,4})\s+(.+)$/.exec(trimmed);
            if (heading) {
                if (inList) { html.push('</ul>'); inList = false; }
                html.push(`<h3>${escapeHtml(heading[2])}</h3>`);
                continue;
            }

            if (trimmed.startsWith('|') && trimmed.endsWith('|')) {
                const tableLines = [];
                while (index < lines.length) {
                    const tableLine = lines[index].trim();
                    if (!tableLine.startsWith('|') || !tableLine.endsWith('|')) break;
                    tableLines.push(tableLine);
                    index++;
                }
                index--;

                if (inList) { html.push('</ul>'); inList = false; }
                const rows = tableLines
                    .filter(tableLine => !/^\|\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?$/.test(tableLine))
                    .map(tableLine => tableLine.slice(1, -1).split('|').map(cell => cell.trim()));
                if (rows.length > 0) {
                    const [header, ...body] = rows;
                    html.push('<table class="output-table"><thead><tr>');
                    html.push(header.map(cell => `<th>${inlineMarkdown(cell)}</th>`).join(''));
                    html.push('</tr></thead><tbody>');
                    html.push(body.map(row => `<tr>${row.map(cell => `<td>${inlineMarkdown(cell)}</td>`).join('')}</tr>`).join(''));
                    html.push('</tbody></table>');
                }
                continue;
            }

            const bullet = /^[-*]\s+(.+)$/.exec(trimmed);
            if (bullet) {
                if (!inList) { html.push('<ul>'); inList = true; }
                html.push(`<li>${inlineMarkdown(bullet[1])}</li>`);
                continue;
            }

            if (inList) { html.push('</ul>'); inList = false; }
            html.push(`<p>${inlineMarkdown(trimmed)}</p>`);
        }

        if (inList) html.push('</ul>');
        return `<div class="output-markdown">${html.join('')}</div>`;
    }

    function renderSmartProse(text) {
        const paragraphs = text.split(/\n{2,}/).map(p => p.trim()).filter(Boolean);
        if (paragraphs.length < 2) return null;

        const THINKING = /^(Now I|I'll|Let me|I'm going to|I need to|I will|First,?\s+I|Next,?\s+I|I'm now|I should|Looking at|Checking|Starting|Moving on|I can see|I notice)/i;

        // Group consecutive paragraphs by type (thinking vs content)
        const groups = [];
        let currentType = null;
        let currentGroup = [];

        for (const para of paragraphs) {
            const type = THINKING.test(para) ? 'thinking' : 'content';
            if (type !== currentType) {
                if (currentGroup.length > 0) groups.push({ type: currentType, items: currentGroup });
                currentType = type;
                currentGroup = [para];
            } else {
                currentGroup.push(para);
            }
        }
        if (currentGroup.length > 0) groups.push({ type: currentType, items: currentGroup });

        // If everything is "thinking" or everything is "content", don't collapse anything
        const hasThinking = groups.some(g => g.type === 'thinking');
        const hasContent = groups.some(g => g.type === 'content');
        if (!hasThinking && paragraphs.length < 4) return null;

        const html = [];
        const contentGroups = groups.filter(g => g.type === 'content');
        const lastContentGroup = contentGroups[contentGroups.length - 1];

        for (const group of groups) {
            if (group.type === 'thinking') {
                html.push(`<details class="prose-thinking"><summary>\u{1F4AD} Agent reasoning (${group.items.length})</summary>`);
                html.push(group.items.map(p => `<p class="prose-thought">${inlineMarkdown(p)}</p>`).join(''));
                html.push('</details>');
            } else {
                const isLastGroup = group === lastContentGroup;
                if (group.items.length > 6) {
                    // Show first 2, collapse middle, show last 2
                    html.push(group.items.slice(0, 2).map(p => `<p class="prose-para">${inlineMarkdown(p)}</p>`).join(''));
                    const middle = group.items.slice(2, -2);
                    html.push(`<details class="prose-collapsed"><summary>${middle.length} more sections\u2026</summary>`);
                    html.push(middle.map(p => `<p class="prose-para">${inlineMarkdown(p)}</p>`).join(''));
                    html.push('</details>');
                    html.push(group.items.slice(-2).map((p, i, arr) => {
                        const cls = (isLastGroup && i === arr.length - 1) ? 'prose-para prose-conclusion' : 'prose-para';
                        return `<p class="${cls}">${inlineMarkdown(p)}</p>`;
                    }).join(''));
                } else {
                    group.items.forEach((p, i) => {
                        const cls = (isLastGroup && i === group.items.length - 1) ? 'prose-para prose-conclusion' : 'prose-para';
                        html.push(`<p class="${cls}">${inlineMarkdown(p)}</p>`);
                    });
                }
            }
        }

        return `<div class="output-prose">${html.join('')}</div>`;
    }

    function renderOutput(out) {
        const raw = out.dataset.rawOutput ?? '';
        const summary = tryReadSummaryObject(raw);
        if (!raw.trim()) {
            out.innerHTML = '<span class="output-empty">Waiting for output...</span>';
        } else if (summary) {
            const cleaned = cleanAgentOutput(raw);
            const hasNarrative = /^#{2,4} /m.test(cleaned);
            let html = '';
            if (hasNarrative) {
                const narrativeId = `narrative-${out.dataset.stage ?? 'stage'}-${Date.now()}`;
                html += `<span class="narrative-toggle" onclick="const b=document.getElementById('${narrativeId}');const open=b.style.display==='block';b.style.display=open?'none':'block';this.textContent=open?'▸ Show full narrative':'▾ Hide narrative'">▸ Show full narrative</span>`;
                html += `<div id="${narrativeId}" class="narrative-collapsed"><div class="output-narrative">${renderMarkdownLite(cleaned)}</div></div>`;
            }
            html += renderSummary(out.dataset.stage ?? '', summary);
            html += `<details class="output-details"><summary>Raw transcript</summary><pre class="output-raw">${escapeHtml(raw)}</pre></details>`;
            out.innerHTML = html;
        } else {
            const cleaned = cleanAgentOutput(raw);
            if (/^\s*#{2,4}\s+/m.test(cleaned) || /^\s*[-*]\s+/m.test(cleaned)) {
                out.innerHTML = renderMarkdownLite(cleaned || raw);
            } else {
                const prose = renderSmartProse(cleaned || raw);
                if (prose) {
                    out.innerHTML = prose;
                } else {
                    out.innerHTML = `<pre class="output-raw">${escapeHtml(raw)}</pre>`;
                }
            }
        }

        if (cursorEl?.parentNode === out) out.appendChild(cursorEl);
        out.scrollTop = out.scrollHeight;
    }

    function setOutput(stage, text) {
        const card = getActiveCard(stage);
        const out = card?.querySelector('.agent-output');
        if (!out) return;
        out.dataset.stage = stage;
        out.dataset.rawOutput = text ?? '';
        renderOutput(out);
    }

    // ── Ensure a card exists; create + append if not ───────────────────
    function ensureCard(stage, retryReason) {
        // If there's already an active card for this stage, return it
        const activeId = activeCardIds[stage];
        if (activeId) {
            const existing = document.getElementById(activeId);
            if (existing && existing.classList.contains('state-active')) return existing;
        }

        // Check if any card exists for this stage (completed = re-entry)
        const anyExisting = activeId
            ? document.getElementById(activeId)
            : document.getElementById(`card-${stage}`);

        if (anyExisting) {
            // Stage was seen before — create new round card
            roundCounts[stage] = (roundCounts[stage] || 1) + 1;
            const round = roundCounts[stage];
            const card = buildCard(stage, round, null, retryReason);
            if (!card) return null;
            closeDispatchGroup();
            feed.appendChild(card);
            activeCardIds[stage] = card.id;
            setTimeout(() => card.scrollIntoView({ behavior: 'smooth', block: 'start' }), 50);
            return card;
        }

        // First time — create round 1
        roundCounts[stage] = 1;
        const card = buildCard(stage, 1, null, retryReason);
        if (!card) return null;
        closeDispatchGroup();
        feed.appendChild(card);
        activeCardIds[stage] = card.id;
        setTimeout(() => card.scrollIntoView({ behavior: 'smooth', block: 'start' }), 50);
        return card;
    }

    // ── Append text to a card's output box ────────────────────────────
    function appendOutput(stage, text) {
        const card = getActiveCard(stage) ?? ensureCard(stage);
        const out = card?.querySelector('.agent-output');
        if (!out) return;
        if (cursorEl?.parentNode === out) out.removeChild(cursorEl);
        card.dataset.lastOutputAt = String(Date.now());
        out.dataset.stage = stage;
        out.dataset.rawOutput = `${out.dataset.rawOutput ?? ''}${text}`;
        if (!cursorEl) {
            cursorEl = document.createElement('span');
            cursorEl.className = 'terminal-cursor';
        }
        renderOutput(out);
        out.appendChild(cursorEl);
        out.scrollTop = out.scrollHeight;
        currentStage = stage;
        updateRunningNote(stage);
    }

    function removeCursor() {
        cursorEl?.parentNode?.removeChild(cursorEl);
    }

    // ── Map a stage status string → card state + badge HTML ───────────
    function resolveState(statusLower, durSec) {
        const dur = durSec > 0 ? ` · ${durSec}s` : '';
        switch (statusLower) {
            case 'go':
                return { cls: 'state-done',    badge: `<span class="badge text-bg-success agent-status-badge">✓ Done${dur}</span>` };
            case 'stop':
                return { cls: 'state-stopped', badge: `<span class="badge text-bg-warning  agent-status-badge text-dark">⊘ Stopped${dur}</span>` };
            case 'duplicate':
                return { cls: 'state-done',    badge: `<span class="badge text-bg-info     agent-status-badge">⊘ Duplicate${dur}</span>` };
            case 'invalid':
            case 'failed':
                return { cls: 'state-failed',  badge: `<span class="badge text-bg-danger   agent-status-badge">✗ Failed${dur}</span>` };
            case 'skipped':
                return { cls: 'state-skipped', badge: `<span class="badge text-bg-secondary agent-status-badge">⟩ Skipped</span>` };
            default:
                return { cls: 'state-done',    badge: `<span class="badge text-bg-success  agent-status-badge">✓ Done${dur}</span>` };
        }
    }

    // ── Finalize a card (stage completed or recovered) ─────────────────
    function finalizeCard(stage, statusLower, durSec, tokenData) {
        const card = getActiveCard(stage);
        if (!card) return;
        const { cls, badge } = resolveState(statusLower, durSec);
        card.classList.remove('state-active');
        card.classList.add(cls);
        const badgeEl = card.querySelector('.agent-status-badge');
        if (badgeEl) badgeEl.outerHTML = badge;
        const durEl = card.querySelector('.agent-dur');
        if (durEl) durEl.textContent = '';
        const noteEl = card.querySelector('.agent-state-note');
        if (noteEl) {
            if (statusLower === 'duplicate') noteEl.textContent = STATE_NOTES.duplicate;
            else if (cls === 'state-stopped') noteEl.textContent = STATE_NOTES.stopped;
            else if (cls === 'state-failed') noteEl.textContent = STATE_NOTES.failed;
            else if (cls === 'state-skipped') noteEl.textContent = STATE_NOTES.skipped;
            else noteEl.textContent = typeof STATE_NOTES.done === 'function' ? STATE_NOTES.done(stage) : STATE_NOTES.done;
        }
        if (['go', 'duplicate', 'stop'].includes(statusLower)) {
            addStageDocumentLink(card, stage);
        }
        if (tokenData && hasMetricData(tokenData)) {
            const tagline = card.querySelector('.agent-tagline');
            if (tagline) {
                card.querySelector('.agent-token-badge')?.remove();
                const tokenBadge = document.createElement('div');
                tokenBadge.className = 'agent-token-badge';
                const lines = [];
                if ((tokenData.inputTokens ?? 0) > 0 || (tokenData.outputTokens ?? 0) > 0) {
                    lines.push(`${Number(tokenData.inputTokens ?? 0).toLocaleString()} in / ${Number(tokenData.outputTokens ?? 0).toLocaleString()} out`);
                }
                if (tokenData.estimatedCostUsd > 0) {
                    lines.push(`~$${Number(tokenData.estimatedCostUsd).toFixed(4)} estimated`);
                }
                if ((tokenData.turnCount ?? 0) > 0 || (tokenData.toolCallCount ?? 0) > 0) {
                    const failedTools = Number(tokenData.failedToolCallCount ?? 0);
                    const failedSuffix = failedTools > 0 ? `, ${failedTools.toLocaleString()} failed` : '';
                    lines.push(`${Number(tokenData.turnCount ?? 0).toLocaleString()} turns / ${Number(tokenData.toolCallCount ?? 0).toLocaleString()} tools${failedSuffix}`);
                }
                if (tokenData.durationMs > 0) {
                    lines.push(`${formatDurationMs(tokenData.durationMs)} model time`);
                }
                if ((tokenData.sessionErrorCount ?? 0) > 0) {
                    lines.push(`${Number(tokenData.sessionErrorCount).toLocaleString()} session errors`);
                }
                if (tokenData.model) {
                    lines.push(String(tokenData.model));
                }
                tokenBadge.innerHTML = lines.map(line => `<div class="agent-cost-line">${escapeHtml(line)}</div>`).join('');
                tagline.insertAdjacentElement('afterend', tokenBadge);
            }
        }
        // Inject retry button for failed/stopped stages on terminal non-remote runs
        if (!isRemoteRun && runIsTerminal
            && (cls === 'state-failed' || cls === 'state-stopped')
            && !card.querySelector('.stage-retry-btn')) {
            const stageRetryCount = roundCounts[stage] || 1;
            if (stageRetryCount < maxStageRetries) {
                const content = card.querySelector('.agent-content');
                if (content) {
                    const form = document.createElement('form');
                    form.action = `/Pipelines/${runId}/RetryStage`;
                    form.method = 'post';
                    form.className = 'd-inline';
                    form.innerHTML = `
                        <input type="hidden" name="__RequestVerificationToken" value="${escapeHtml(antiForgeryToken)}" />
                        <input type="hidden" name="StageName" value="${escapeHtml(stage)}" />
                        <button type="submit" class="btn btn-sm btn-outline-warning stage-retry-btn">↩ Retry ${escapeHtml(stage)}</button>`;
                    content.appendChild(form);
                }
            }
        }
        removeCursor();
    }

    function addStageDocumentLink(card, stage) {
        const documentName = STAGE_DOCUMENTS[stage];
        if (!documentName || card.querySelector('.plan-output-actions')) return;
        const content = card.querySelector('.agent-content');
        const output = card.querySelector('.agent-output');
        if (!content || !output) return;
        const actions = document.createElement('div');
        actions.className = 'plan-output-actions';
        actions.innerHTML = `<a href="/Pipelines/${runId}/${documentName}" class="btn btn-xs btn-outline-info">View ${escapeHtml(documentName)}</a>`;
        content.insertBefore(actions, output);
    }

    function hasMetricData(tokenData) {
        return (tokenData.inputTokens ?? 0) > 0
            || (tokenData.outputTokens ?? 0) > 0
            || (tokenData.estimatedCostUsd ?? 0) > 0
            || (tokenData.turnCount ?? 0) > 0
            || (tokenData.toolCallCount ?? 0) > 0
            || (tokenData.failedToolCallCount ?? 0) > 0
            || (tokenData.sessionErrorCount ?? 0) > 0
            || (tokenData.durationMs ?? 0) > 0
            || Boolean(tokenData.model);
    }

    function formatDurationMs(value) {
        const totalSeconds = Math.round(Number(value) / 1000);
        const minutes = Math.floor(totalSeconds / 60);
        const seconds = totalSeconds % 60;
        return `${minutes}:${String(seconds).padStart(2, '0')}`;
    }

    function formatElapsedSeconds(totalSeconds) {
        const seconds = Math.max(0, Math.round(Number(totalSeconds) || 0));
        const minutes = Math.floor(seconds / 60);
        const remaining = seconds % 60;
        return minutes > 0 ? `${minutes}:${String(remaining).padStart(2, '0')}` : `${remaining}s`;
    }

    // ── Per-stage live timer ───────────────────────────────────────────
    let timerHandle = null, timerStart = null, timerStage = null;

    function startTimer(stage, startedAtUtc) {
        stopTimer();
        const parsedStart = startedAtUtc ? Date.parse(startedAtUtc) : NaN;
        timerStage = stage;
        timerStart = Number.isNaN(parsedStart) ? Date.now() : parsedStart;
        const card = getActiveCard(stage);
        if (card) card.dataset.startedAt = String(timerStart);
        timerHandle = setInterval(() => {
            const card = getActiveCard(timerStage);
            const dur = card?.querySelector('.agent-dur');
            if (dur) dur.textContent = formatElapsedSeconds((Date.now() - timerStart) / 1000);
            updateRunningNote(timerStage);
        }, 1000);
        updateRunningNote(stage);
    }

    function updateRunningNote(stage) {
        const card = getActiveCard(stage);
        if (!card?.classList.contains('state-active')) return;
        const note = card.querySelector('.agent-state-note');
        if (!note) return;
        const out = card.querySelector('.agent-output');
        const hasOutput = Boolean(out?.dataset.rawOutput?.trim());
        const now = Date.now();
        const startedAt = Number(card.dataset.startedAt || timerStart || now);
        const lastOutputAt = Number(card.dataset.lastOutputAt || 0);
        const elapsed = formatElapsedSeconds((now - startedAt) / 1000);

        if (!hasOutput) {
            const quietSeconds = Math.round((now - startedAt) / 1000);
            note.textContent = quietSeconds >= 60
                ? `Still running · ${elapsed} elapsed · waiting for first output`
                : `Waiting for first output · ${elapsed} elapsed`;
            return;
        }

        const sinceLast = lastOutputAt > 0 ? formatElapsedSeconds((now - lastOutputAt) / 1000) : 'just now';
        note.textContent = `Output captured · last update ${sinceLast} ago · ${elapsed} elapsed`;
    }

    function stopTimer() {
        clearInterval(timerHandle);
        timerHandle = timerStage = timerStart = null;
    }

    // ── Overall run state helpers ──────────────────────────────────────
    const STATUS_CLASSES = { Completed:'text-bg-success', Failed:'text-bg-danger', Running:'text-bg-primary', Pausing:'text-bg-info awaiting-pulse', Paused:'text-bg-warning text-dark', Stopped:'text-bg-warning text-dark', Cancelled:'text-bg-warning' };
    const badgeEl   = document.getElementById('run-status-badge');
    const statusEl  = document.getElementById('run-status');
    const cancelEl  = document.getElementById('cancel-section');
    const spinnerEl = document.getElementById('run-spinner');
    const elapsedEl = document.getElementById('run-elapsed');
    const HALT_BANNER_STATUSES = new Set(['Queued', 'Running', 'Pausing', 'Paused', 'Failed', 'Stopped', 'Cancelled']);
    let   elapsedTimer = null;
    let   currentRunStatus = String(bootstrap.currentRunStatus ?? 'Unknown');

    function applyRunStatus(status) {
        currentRunStatus = status;
        if (badgeEl) badgeEl.className = 'badge fs-6 ' + (STATUS_CLASSES[status] ?? 'text-bg-secondary');
        if (statusEl) statusEl.textContent = status === 'Pausing' ? '⏸ Pausing…' : status;
        if (spinnerEl && status !== 'Running' && status !== 'Pausing') spinnerEl.remove();
        if (!HALT_BANNER_STATUSES.has(status)) removeHaltBanner();
        // Hide pause button when pausing
        const pauseForm = document.getElementById('pause-form');
        if (status === 'Pausing' && pauseForm) {
            pauseForm.style.display = 'none';
        }
    }

    function finalizeRun(status) {
        applyRunStatus(status);
        cancelEl?.remove();
        stopTimer();
        removeCursor();
        clearInterval(elapsedTimer);
        // Allow retry button injection on newly terminal runs
        if (['Failed', 'Stopped', 'Cancelled', 'Paused'].includes(status) && !isRemoteRun) {
            document.querySelectorAll('.agent-card.state-failed, .agent-card.state-stopped').forEach(card => {
                const stage = card.dataset.stage;
                if (!stage || card.querySelector('.stage-retry-btn')) return;
                const stageRetryCount = roundCounts[stage] || 1;
                if (stageRetryCount < maxStageRetries) {
                    const content = card.querySelector('.agent-content');
                    if (content) {
                        const form = document.createElement('form');
                        form.action = `/Pipelines/${runId}/RetryStage`;
                        form.method = 'post';
                        form.className = 'd-inline';
                        form.innerHTML = `
                            <input type="hidden" name="__RequestVerificationToken" value="${escapeHtml(antiForgeryToken)}" />
                            <input type="hidden" name="StageName" value="${escapeHtml(stage)}" />
                            <button type="submit" class="btn btn-sm btn-outline-warning stage-retry-btn">↩ Retry ${escapeHtml(stage)}</button>`;
                        content.appendChild(form);
                    }
                }
            });
        }
    }

    if (runIsActive && runStartUtc) {
        const runStart = new Date(runStartUtc);
        elapsedTimer = setInterval(() => {
            if (!elapsedEl || !Number.isFinite(runStart.getTime())) return;
            const s = Math.max(0, Math.floor((Date.now() - runStart) / 1000));
            const m = Math.floor(s / 60);
            elapsedEl.textContent = m > 0 ? `${m}:${(s % 60).toString().padStart(2,'0')}` : `0:${(s % 60).toString().padStart(2,'0')}`;
        }, 1000);
    }

    // ── Cyberpilot dispatch (inline in agent feed) ─────────────────────
    let currentDispatchNode = null;
    let currentDispatchPhase = null;

    function dispatchTypeClass(type) {
        return String(type ?? 'dispatch')
            .replace(/([a-z0-9])([A-Z])/g, '$1-$2')
            .replace(/[^a-z0-9]+/gi, '-')
            .replace(/^-+|-+$/g, '')
            .toLowerCase() || 'dispatch';
    }

    function dispatchPhase(type, message) {
        const normalizedType = dispatchTypeClass(type);
        const text = String(message ?? '').toLowerCase();

        if (text.includes('resume') || text.includes('resuming')) return 'resume';
        if (normalizedType === 'approval' || normalizedType === 'gate' || normalizedType === 'halt') return 'approval';
        if (normalizedType === 'preflight' || normalizedType === 'repo-profile' || normalizedType === 'model-fallback') return 'setup';
        if (normalizedType === 'routing' || normalizedType === 'branch' || normalizedType === 'review-loop') return 'transition';
        if (normalizedType === 'skip' || normalizedType === 'issue-close') return 'completion';

        return 'coordination';
    }

    function dispatchPhaseLabel(phase) {
        const labels = {
            approval: 'Approval gate',
            completion: 'Completion',
            coordination: 'Run coordination',
            resume: 'Resume handoff',
            setup: 'Run setup',
            transition: 'Stage handoff'
        };

        return labels[phase] ?? labels.coordination;
    }

    function appendSpineNode(type, message) {
        const phase = dispatchPhase(type, message);
        if (currentDispatchPhase && currentDispatchPhase !== phase) {
            closeDispatchGroup();
        }

        if (!currentDispatchNode) {
            currentDispatchNode = document.createElement('div');
            currentDispatchNode.className = 'spine-dispatch';
            currentDispatchNode.innerHTML = `
            <div class="spine-dispatch-inner">
                <div class="spine-dispatch-header">
                    <img src="/images/agents/cyberpilot.png" class="spine-dispatch-avatar" alt="AP" onerror="this.style.display='none'">
                    <div>
                        <div class="spine-dispatch-title">Cyberpilot</div>
                        <div class="spine-dispatch-subtitle">${escapeHtml(dispatchPhaseLabel(phase))}</div>
                    </div>
                </div>
                <div class="spine-dispatch-rows"></div>
            </div>`;
            feed.appendChild(currentDispatchNode);
            currentDispatchPhase = phase;
        }

        const row = document.createElement('div');
        row.className = 'spine-dispatch-row';
        row.innerHTML = `<span class="spine-dot dot-${dispatchTypeClass(type)}"></span><span class="spine-dispatch-text">${escapeHtml(message)}</span>`;
        currentDispatchNode.querySelector('.spine-dispatch-rows')?.appendChild(row);
    }

    function closeDispatchGroup() {
        currentDispatchNode = null;
        currentDispatchPhase = null;
    }

    // ── Pre-populate from server-rendered data (merged timeline) ──────
    const timeline = [];
    existingLogs.forEach(l => timeline.push({ kind: 'log', ...l }));
    existingDispatches.forEach(d => timeline.push({ kind: 'dispatch', ...d }));
    timeline.sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp));

    for (const entry of timeline) {
        if (entry.kind === 'dispatch') {
            appendSpineNode(entry.type, entry.message);
            checkForHaltBanner(entry.type, entry.message);
            continue;
        }
        const log = entry;
        closeDispatchGroup();
        roundCounts[log.stage] = (roundCounts[log.stage] || 0) + 1;
        const round = roundCounts[log.stage];

        const card = buildCard(log.stage, round, log.retryCount, log.retryReason);
        if (!card) continue;

        activeCardIds[log.stage] = card.id;

        const out = card.querySelector('.agent-output');
        if (out) {
            out.dataset.stage = log.stage;
            out.dataset.rawOutput = log.output ?? '';
            renderOutput(out);
        }

        const s = (log.status ?? '').toLowerCase();
        feed.appendChild(card);

        if (s === 'running' && runIsActive) {
            currentStage = log.stage;
            startTimer(log.stage, log.timestamp);
            if (out) out.scrollTop = out.scrollHeight;
        } else if (s === 'running') {
            finalizeCard(log.stage, 'failed', log.duration);
        } else {
            finalizeCard(log.stage, s, log.duration, {
                inputTokens: log.inputTokens,
                outputTokens: log.outputTokens,
                estimatedCostUsd: log.estimatedCostUsd,
                model: log.model,
                turnCount: log.turnCount,
                toolCallCount: log.toolCallCount,
                failedToolCallCount: log.failedToolCallCount,
                sessionErrorCount: log.sessionErrorCount,
                durationMs: log.durationMs
            });
        }
    }

    // ── Halt / retry-exhausted banner ─────────────────────────────────
    function inferCorrectiveActions(message) {
        const text = String(message ?? '').toLowerCase();
        if (text.includes('approve-all')) {
            return [
                'Enable approval for trusted dashboard runs in Cyberpilot configuration.',
                'Continue or restart the run after approval is enabled.'
            ];
        }
        if (text.includes('model unavailable')) {
            return [
                'Select a model that is available to the current Copilot account.',
                'Start a new run with the available model.'
            ];
        }
        if (text.includes('review') || text.includes('changes_requested') || text.includes('max retries')) {
            return [
                'Open the linked PR and the latest Review card output to identify the blocking findings.',
                'Address the requested changes on the existing branch.',
                'Use Rework from Review to send those findings back to implementation, then Cyberpilot will return to review.'
            ];
        }
        if (text.includes('json') || text.includes('invalid')) {
            return [
                'Check the agent transcript for the final response shape.',
                'Rerun the stage after the agent can return the required fenced JSON result block.'
            ];
        }
        return [
            'Read the stopped stage output for the blocking condition.',
            'Correct the issue, branch, PR, or stage handoff artifact called out by that output.',
            'Use Continue to retry from the stopped stage, or Reset to rerun from a clean issue state.'
        ];
    }

    function checkForHaltBanner(type, message) {
        if (!HALT_BANNER_STATUSES.has(currentRunStatus)) return;
        if (type === 'review_loop' && message.toLowerCase().includes('halting')) {
            showHaltBanner('⚠️ Retry Limit Reached',
                'Review requested changes on both attempts. Pipeline halted for human intervention — review the PR, address findings, and re-run.',
                inferCorrectiveActions(message));
        } else if (type === 'halt') {
            showHaltBanner('🛑 Pipeline Halted', message, inferCorrectiveActions(message));
        }
    }

    function removeHaltBanner() {
        document.getElementById('halt-banner')?.remove();
    }

    function showHaltBanner(title, message, actions) {
        if (document.getElementById('halt-banner')) return;
        const banner = document.createElement('div');
        banner.id = 'halt-banner';
        banner.className = 'halt-banner';
        const actionItems = (actions ?? [])
            .map(action => `<li>${escapeHtml(action)}</li>`)
            .join('');
        banner.innerHTML = `
            <div class="halt-banner-content">
                <div class="halt-banner-title">${escapeHtml(title)}</div>
                <p class="halt-banner-message">${escapeHtml(message)}</p>
                ${actionItems ? `<ul class="halt-banner-actions">${actionItems}</ul>` : ''}
            </div>`;
        feed.parentNode.insertBefore(banner, feed);
    }

    // ── Station strip live updates ────────────────────────────────────
    const STATION_STATE_MAP = { go: 'is-complete', stop: 'is-stopped', duplicate: 'is-complete', failed: 'is-failed', invalid: 'is-failed', running: 'is-running', skipped: 'is-skipped' };
    function updateStation(stage, stateClass, statusText) {
        const node = document.querySelector(`.station-node[data-stage="${stage}"]`);
        if (!node) return;
        node.classList.remove('is-queued', 'is-running', 'is-complete', 'is-stopped', 'is-failed', 'is-skipped');
        node.classList.add(stateClass);
        const small = node.querySelector('small');
        if (small) small.textContent = statusText;
    }

    // ── Post-run button injection ─────────────────────────────────────
    const isRemote = runIsRemote;
    let lastCompletedStage = null;
    let lastCompletedStatus = null;
    function injectPostRunButtons() {
        const bar = document.getElementById('run-bar-actions');
        if (!bar || document.getElementById('post-run-buttons')) return;
        if (bar.querySelector('form[action$="/Continue"], form[action$="/ResetMission"]')) return;
        const currentStatus = statusEl?.textContent?.trim() ?? '';
        if (!['Paused', 'Failed', 'Stopped', 'Cancelled'].includes(currentStatus)) return;
        const isPaused = currentStatus === 'Paused';
        const shouldShowRework = serverCanReworkFromReview
            || (lastCompletedStage === 'review' && ['stop', 'failed', 'invalid'].includes(lastCompletedStatus));
        const wrapper = document.createElement('span');
        wrapper.id = 'post-run-buttons';
        wrapper.className = 'd-inline-flex gap-1';
        const antiForgeryInput = `<input type="hidden" name="__RequestVerificationToken" value="${escapeHtml(antiForgeryToken)}" />`;
        wrapper.innerHTML = `
            ${shouldShowRework ? `<form action="/Pipelines/${runId}/ReworkFromReview" method="post" class="d-inline">
                ${antiForgeryInput}
                <button type="submit" class="btn btn-sm btn-success" title="Send review findings back to implementation on the existing PR branch">Rework from Review</button>
            </form>` : ''}
            <form action="/Pipelines/${runId}/Continue" method="post" class="d-inline">
                ${antiForgeryInput}
                <button type="submit" class="btn btn-sm btn-primary">${isPaused ? '▶ Resume' : shouldShowRework ? 'Retry Review' : 'Continue'}</button>
            </form>
            ${isRemote ? '' : `<form action="/Pipelines/${runId}/ResetMission" method="post" class="d-inline">
                ${antiForgeryInput}
                <button type="submit" class="btn btn-sm btn-outline-danger">Reset</button>
            </form>`}`;
        bar.appendChild(wrapper);
    }

    // ── Initial error state (for failures that occur before SignalR connects) ──────────────────
    if (bootstrap.currentRunStatus === 'Failed' && bootstrap.error) {
        // The run failed before the client established SignalR connection.
        // Apply the failure state immediately from the persisted error.
        applyRunStatus('Failed');
        if (badgeEl) badgeEl.className = 'badge fs-6 ' + (STATUS_CLASSES['Failed'] ?? 'text-bg-secondary');
        if (statusEl) statusEl.textContent = 'Failed';
        if (spinnerEl) spinnerEl.remove();
        cancelEl?.remove();
        stopTimer();
        removeCursor();
        clearInterval(elapsedTimer);
        injectPostRunButtons();
        // Display halt banner with the error
        checkForHaltBanner('run-failed', bootstrap.error);
    }

    // ── SignalR live updates ───────────────────────────────────────────
    if (window.signalR) {
        const conn = new signalR.HubConnectionBuilder()
            .withUrl('/pipelineHub')
            .withAutomaticReconnect()
            .build();

        conn.on('stageStarted', e => {
            // Ensure run status reflects Running (catches missed runStarted event)
            if (statusEl && (statusEl.textContent === 'Queued' || statusEl.textContent === '')) {
                applyRunStatus('Running');
            }
            // Finalize ALL active cards (safety net for missed stageCompleted)
            document.querySelectorAll('.agent-card.state-active').forEach(stale => {
                const staleStage = stale.dataset.stage;
                if (staleStage) {
                    const { cls, badge } = resolveState('go', 0);
                    stale.classList.remove('state-active');
                    stale.classList.add(cls);
                    const badgeEl = stale.querySelector('.agent-status-badge');
                    if (badgeEl) badgeEl.outerHTML = badge;
                    const noteEl = stale.querySelector('.agent-state-note');
                    if (noteEl) noteEl.textContent = typeof STATE_NOTES.done === 'function' ? STATE_NOTES.done(staleStage) : STATE_NOTES.done;
                    updateStation(staleStage, 'is-complete', 'go');
                }
            });

            currentStage = e.stage;
            closeDispatchGroup();
            const card = ensureCard(e.stage, e.retryReason);
            if (card) {
                // ensureCard returns a fresh card in state-active for re-entries
                // For first-time cards, it's also already state-active from buildCard
                card.scrollIntoView({ behavior: 'smooth', block: 'start' });
            }
            startTimer(e.stage, e.startedAt);
            updateStation(e.stage, 'is-running', 'running');
        });

        conn.on('streamDelta', e => {
            const stage = e.stage || currentStage;
            if (stage) appendOutput(stage, e.content);
        });

        conn.on('message', e => {
            const stage = e.stage || currentStage;
            if (stage) appendOutput(stage, `[${e.level}] ${e.message}\n`);
        });

        conn.on('stageCompleted', e => {
            const durSec = timerStart ? Math.round((Date.now() - timerStart) / 1000) : 0;
            const statusLower = (e.status ?? '').toLowerCase();
            lastCompletedStage = e.stage;
            lastCompletedStatus = statusLower;
            finalizeCard(e.stage, statusLower, durSec, {
                inputTokens: e.inputTokens,
                outputTokens: e.outputTokens,
                estimatedCostUsd: e.estimatedCostUsd,
                model: e.model,
                turnCount: e.turnCount,
                toolCallCount: e.toolCallCount,
                failedToolCallCount: e.failedToolCallCount,
                sessionErrorCount: e.sessionErrorCount,
                durationMs: e.durationMs
            });
            updateStation(e.stage, STATION_STATE_MAP[statusLower] ?? 'is-complete', statusLower);
            stopTimer();
            currentStage = null;
            closeDispatchGroup();
        });

        conn.on('runStarted',   ()  => applyRunStatus('Running'));
        conn.on('runCompleted', e   => {
            finalizeRun(e.status);
            injectPostRunButtons();
            // Inject delivery panel if run completed with deliver skipped
            if (e.status === 'Completed' && e.skipDeliver && !document.getElementById('delivery-panel')) {
                const tpl = document.getElementById('delivery-panel-template');
                if (tpl) {
                    const clone = tpl.content.cloneNode(true);
                    const feedEl = document.getElementById('agent-feed');
                    if (feedEl) feedEl.parentNode.insertBefore(clone, feedEl);
                }
            }
        });
        conn.on('runPaused', e => {
            finalizeRun('Paused');
            // Remove pause button if still present
            const pauseBtn = document.getElementById('pause-btn');
            if (pauseBtn) pauseBtn.closest('form')?.remove();
            injectPostRunButtons();
        });
        conn.on('runFailed',    e   => {
            if (currentStage) {
                lastCompletedStage = currentStage;
                lastCompletedStatus = 'failed';
                appendOutput(currentStage, `\n! ${e.error}\n`);
                finalizeCard(currentStage, 'failed', timerStart ? Math.round((Date.now() - timerStart) / 1000) : 0);
                updateStation(currentStage, 'is-failed', 'failed');
            } else if (e.error) {
                // No current stage (failure before any stage started), but we have an error message.
                // Display it in a visible banner or alert area.
                checkForHaltBanner('run-failed', e.error);
            }
            finalizeRun('Failed');
            injectPostRunButtons();
        });

        conn.on('prDiscovered', e => {
            const bar = document.getElementById('run-bar-actions');
            if (bar && e.prUrl && !document.getElementById('pr-link-live')) {
                const link = document.createElement('a');
                link.id = 'pr-link-live';
                link.href = e.prUrl;
                link.target = '_blank';
                link.rel = 'noopener';
                link.className = 'btn btn-sm btn-primary';
                link.textContent = 'View PR';
                bar.appendChild(link);
            }
        });

        conn.on('branchReady', e => {
            const el = document.getElementById('branch-value');
            if (el && e.branchName) {
                const code = document.createElement('code');
                code.className = 'small';
                code.id = 'branch-value';
                code.textContent = e.branchName;
                el.replaceWith(code);
            }
        });

        conn.on('cyberpilotDispatch', e => {
            appendSpineNode(e.type, e.message);
            checkForHaltBanner(e.type, e.message);
        });

        conn.start()
            .then(() => conn.invoke('JoinRun', runId))
            .catch(() => {});
    }

    // ── Button loading feedback ───────────────────────────────────────
    document.querySelectorAll('#run-bar-actions form').forEach(form => {
        form.addEventListener('submit', () => {
            const btn = form.querySelector('button[type="submit"]');
            if (!btn || btn.disabled) return;
            btn.disabled = true;
            const original = btn.innerHTML;
            btn.dataset.originalText = original;
            btn.innerHTML = `<span class="spinner-border spinner-border-sm me-1" role="status"></span> Working...`;
            setTimeout(() => { btn.disabled = false; btn.innerHTML = original; }, 12000);
        });
    });
})();
