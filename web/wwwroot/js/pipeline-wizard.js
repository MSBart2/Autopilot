(() => {
    const form = document.querySelector('[data-wizard-form]');
    if (!form) return;

    const TOTAL_STEPS = 5;

    // -----------------------------------------------------------------------
    // Core: show/hide panels and update step map
    // -----------------------------------------------------------------------
    function showStep(n) {
        const step = Math.max(1, Math.min(TOTAL_STEPS, n));
        form.setAttribute('data-wizard-active-step', step);

        // Panels
        form.querySelectorAll('[data-wizard-step-panel]').forEach(panel => {
            const panelStep = parseInt(panel.getAttribute('data-wizard-step-panel'), 10);
            panel.classList.toggle('wizard-panel--active', panelStep === step);
        });

        // Step map indicators
        const stepMap = document.querySelector('[data-wizard-form]')?.closest('.guide-content-wrap')
            ?.previousElementSibling?.nextElementSibling
            || document.querySelector('.wizard-step-map');

        document.querySelectorAll('[data-wizard-step-indicator]').forEach(indicator => {
            const indStep = parseInt(indicator.getAttribute('data-wizard-step-indicator'), 10);
            indicator.classList.remove('wizard-step--active', 'wizard-step--complete');
            if (indStep === step) {
                indicator.classList.add('wizard-step--active');
            } else if (indStep < step) {
                indicator.classList.add('wizard-step--complete');
            }
        });

        // Nav buttons
        const prevBtn = form.querySelector('[data-wizard-prev]');
        const nextBtn = form.querySelector('[data-wizard-next]');
        const saveBtn = form.querySelector('.wizard-save-btn');
        if (prevBtn) prevBtn.style.visibility = step > 1 ? 'visible' : 'hidden';
        if (nextBtn) nextBtn.style.display = step < TOTAL_STEPS ? 'inline-block' : 'none';
        if (saveBtn) saveBtn.style.display = step === TOTAL_STEPS ? 'inline-block' : 'none';

        if (step === TOTAL_STEPS) {
            buildReviewSummary();
        }
    }

    // -----------------------------------------------------------------------
    // Validation: Constraint Validation API on active panel inputs
    // -----------------------------------------------------------------------
    function validateStep(n) {
        const panel = form.querySelector(`[data-wizard-step-panel="${n}"]`);
        if (!panel) return true;
        let valid = true;
        panel.querySelectorAll('input, select, textarea').forEach(field => {
            if (!field.checkValidity()) {
                field.reportValidity();
                valid = false;
            }
        });
        return valid;
    }

    // -----------------------------------------------------------------------
    // Review summary builder
    // -----------------------------------------------------------------------
    function buildReviewSummary() {
        const target = form.querySelector('[data-review-content]');
        if (!target) return;

        const name = form.querySelector('[name="Name"]')?.value || '(none)';
        const version = form.querySelector('[name="Version"]')?.value || '(none)';
        const policy = form.querySelector('[name="PolicyProfileName"]')?.value || '(none)';
        const transitions = form.querySelector('[name="TransitionsText"]')?.value?.trim();

        const stageCards = [...form.querySelectorAll('[data-stage-card]')];
        const stagesHtml = stageCards.length === 0
            ? '<li class="text-secondary">No stages defined</li>'
            : stageCards.map((card, i) => {
                const displayName = card.querySelector(`[name*=".DisplayName"]`)?.value || `Stage ${i + 1}`;
                const stageName = card.querySelector(`[name*=".Name"]`)?.value || '';
                return `<li>${escHtml(displayName)}${stageName ? ` <code class="small">${escHtml(stageName)}</code>` : ''}</li>`;
            }).join('');

        const transitionsHtml = transitions
            ? `<pre class="mb-0 small font-monospace">${escHtml(transitions)}</pre>`
            : '<span class="text-secondary">Auto-linked in order</span>';

        target.innerHTML = `
            <dl class="row mb-0">
                <dt class="col-sm-3">Name</dt>
                <dd class="col-sm-9">${escHtml(name)}</dd>
                <dt class="col-sm-3">Version</dt>
                <dd class="col-sm-9">${escHtml(version)}</dd>
                <dt class="col-sm-3">Policy</dt>
                <dd class="col-sm-9">${escHtml(policy)}</dd>
                <dt class="col-sm-3">Stages</dt>
                <dd class="col-sm-9"><ol class="mb-0">${stagesHtml}</ol></dd>
                <dt class="col-sm-3">Transitions</dt>
                <dd class="col-sm-9">${transitionsHtml}</dd>
            </dl>`;
    }

    function escHtml(text) {
        return String(text)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    // -----------------------------------------------------------------------
    // Gate panel sync: MutationObserver keeps Gates step in sync with Stages step
    // -----------------------------------------------------------------------
    function syncGatePanel() {
        const stageList = form.querySelector('[data-stage-list]');
        const gateList = form.querySelector('[data-gate-list]');
        if (!stageList || !gateList) return;

        const stageCards = [...stageList.querySelectorAll('[data-stage-card]')];
        const existingGateCards = [...gateList.querySelectorAll('[data-gate-card]')];

        // Remove gate cards for stages that no longer exist
        existingGateCards.slice(stageCards.length).forEach(card => card.remove());

        stageCards.forEach((stageCard, index) => {
            const displayName = stageCard.querySelector('[name*=".DisplayName"]')?.value || `Stage ${index + 1}`;
            const stageName = stageCard.querySelector('[name*=".Name"]')?.value || '';

            let gateCard = gateList.querySelector(`[data-gate-card][data-gate-index="${index}"]`);
            if (!gateCard) {
                // Create a minimal gate card for the new stage
                gateCard = document.createElement('article');
                gateCard.className = 'telemetry-panel';
                gateCard.setAttribute('data-gate-card', '');
                gateCard.setAttribute('data-gate-index', index);
                gateCard.innerHTML = `
                    <p class="eyebrow">${escHtml(displayName)}</p>
                    <h3 class="h5 mb-3">${escHtml(stageName)}</h3>
                    <div>
                        <label class="form-label" for="Stages[${index}].GatesText">Gates</label>
                        <textarea class="form-control font-monospace" id="Stages[${index}].GatesText"
                                  name="Stages[${index}].GatesText" rows="3"
                                  placeholder="model-available|BeforeStage|true&#10;review-approval|AfterStage|true"></textarea>
                        <div class="form-text text-secondary">One gate per line: name|BeforeStage or AfterStage|true or false.</div>
                    </div>`;
                gateList.appendChild(gateCard);
            } else {
                // Update title
                const eyebrow = gateCard.querySelector('.eyebrow');
                if (eyebrow) eyebrow.textContent = displayName;
                const h3 = gateCard.querySelector('h3');
                if (h3) h3.textContent = stageName;
                // Re-index GatesText field
                gateCard.querySelectorAll('[name]').forEach(el => {
                    el.name = el.name.replace(/Stages\[(?:\d+|__index__)\]/, `Stages[${index}]`);
                });
                gateCard.querySelectorAll('[id]').forEach(el => {
                    el.id = el.id.replace(/Stages\[(?:\d+|__index__)\]/, `Stages[${index}]`);
                });
                // Update label's for attribute
                gateCard.querySelectorAll('label[for]').forEach(el => {
                    el.setAttribute('for', el.getAttribute('for').replace(/Stages\[(?:\d+|__index__)\]/, `Stages[${index}]`));
                });
            }
        });

        // Remove the empty-nudge if we now have stages
        const nudge = gateList.querySelector('[data-gate-empty-nudge]');
        if (nudge && stageCards.length > 0) nudge.remove();
    }

    // -----------------------------------------------------------------------
    // Wire events
    // -----------------------------------------------------------------------
    form.querySelector('[data-wizard-prev]')?.addEventListener('click', () => {
        const current = parseInt(form.getAttribute('data-wizard-active-step'), 10) || 1;
        showStep(current - 1);
    });

    form.querySelector('[data-wizard-next]')?.addEventListener('click', () => {
        const current = parseInt(form.getAttribute('data-wizard-active-step'), 10) || 1;
        if (validateStep(current)) {
            showStep(current + 1);
        }
    });

    // MutationObserver: keep gate panel in sync when stages are added/removed
    const stageList = form.querySelector('[data-stage-list]');
    if (stageList) {
        const observer = new MutationObserver(syncGatePanel);
        observer.observe(stageList, { childList: true, subtree: false });

        // Also sync on stage field changes (display name / name updates)
        stageList.addEventListener('input', event => {
            if (event.target.matches('[name*=".DisplayName"], [name*=".Name"]')) {
                syncGatePanel();
            }
        });
    }

    // -----------------------------------------------------------------------
    // Initialize: jump to the server-specified active step
    // -----------------------------------------------------------------------
    const initialStep = parseInt(form.getAttribute('data-wizard-active-step'), 10) || 1;
    showStep(initialStep);
})();
