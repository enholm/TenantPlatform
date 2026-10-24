// Browser memory only. Same-origin requests; untrusted text always uses textContent.
(() => {
    function mount() {
        for (const panel of document.querySelectorAll('[data-read-agent]')) {
            if (panel.dataset.mounted || !panel.querySelector('[data-question-form]')) continue;
            panel.dataset.mounted = 'true';
            const get = name => panel.querySelector(`[data-${name}]`);
            let conversation = null, request = null, generation = 0, turns = [], expiryTimer = null, report = null, reportFresh = false;
            let editPage = panel.dataset.editPage || null;
            const saves = new Set(['report-save', 'report-update', 'report-copy']);
            function text(tag, value, parent) { const node = document.createElement(tag); node.textContent = value; parent.append(node); return node; }
            function render(verified = true, extra = null) {
                get('turns').replaceChildren();
                for (const turn of extra ? [...turns, extra] : turns) {
                    const article = document.createElement('article'); article.className = 'border rounded p-3 mt-3'; get('turns').append(article);
                    text('h2', panel.dataset.questionLabel, article).className = 'h6'; text('p', turn.question, article).style.whiteSpace = 'pre-wrap';
                    if (!verified || !turn.answer) { text('p', panel.dataset.pendingHistory, article); continue; }
                    text('h2', panel.dataset.answerLabel, article).className = 'h6'; text('p', turn.answer.text, article).style.whiteSpace = 'pre-wrap';
                    const notices = document.createElement('ul'); article.append(notices);
                    for (const notice of turn.answer.notices) text('li', notice, notices);
                    const sources = document.createElement('ol'); article.append(sources);
                    for (const source of turn.answer.sources) text('li', `${source.revalidated ? panel.dataset.revalidated : panel.dataset.newSource}: ${source.connectionName} · ${source.tool} · ${source.version}${source.summary ? ' — ' + source.summary : ''}`, sources);
                }
            }
            function renderDraft(fresh = false) {
                const root = get('report-draft'); if (!root) return;
                root.replaceChildren(); reportFresh = fresh;
                if (!report) return;
                text('h2', panel.dataset.draftTitle, root).className = 'h5';
                text('h3', report.title, root).className = 'h6';
                text('p', report.sourceName, root);
                if (report.editingPageId) {
                    text('p', `${panel.dataset.editing}: ${report.originalTitle}`, root);
                    text('h3', panel.dataset.editChanges, root).className = 'h6';
                    const changes = document.createElement('ul'); root.append(changes);
                    for (const change of report.changes || []) text('li', `${change.field}: ${change.before} → ${change.after}`, changes);
                    if (!report.changes?.length) text('p', panel.dataset.editNoChanges, root);
                }
                text('p', panel.dataset.draftSummary, root);
                text('p', [report.summary.period, report.summary.status, report.summary.type, report.summary.archive, report.summary.building, report.summary.search].join(' / '), root);
                text('p', panel.dataset.draftSnapshot, root);
                if (fresh && report.preview) {
                    const preview = document.createElement('div'); root.append(preview);
                    window.TenantPlatformReports.render(report.preview, report.components, preview, panel.dataset, page => reportAction('report-preview', page));
                } else text('p', panel.dataset.draftOld, root);
                if (report.savedPageId) { const link = text('a', panel.dataset.draftOpen, root); link.href = '/my-pages/' + report.savedPageId; }
                else {
                    const refresh = text('button', panel.dataset.date, root); refresh.className = 'btn btn-outline-secondary'; refresh.disabled = !!request; refresh.addEventListener('click', () => reportAction('report-preview', 1));
                    const save = text('button', report.editingPageId ? panel.dataset.editSave : panel.dataset.draftSave, root); save.className = 'btn btn-primary'; save.disabled = !fresh || !!request; save.addEventListener('click', () => reportFresh && reportAction(report.editingPageId ? 'report-update' : 'report-save'));
                    if (report.editingPageId) {
                        const copy = text('button', panel.dataset.editCopy, root); copy.className = 'btn btn-outline-primary'; copy.disabled = !fresh || !!request; copy.addEventListener('click', () => reportFresh && reportAction('report-copy'));
                        const discard = text('button', panel.dataset.editDiscard, root); discard.className = 'btn btn-outline-secondary'; discard.disabled = !!request; discard.addEventListener('click', () => reportAction('report-discard'));
                        const reload = text('button', panel.dataset.editReload, root); reload.className = 'btn btn-outline-secondary'; reload.disabled = !!request; reload.addEventListener('click', () => window.confirm(panel.dataset.editReloadConfirm) ? reportAction('report-reload') : undefined);
                    }
                }
            }
            async function reportAction(action, page = 1) {
                if (!report || !conversation || request || (saves.has(action) && !reportFresh)) return;
                const shown = { draft: report.id, revision: report.revision };
                request = new AbortController(); const signal = request.signal; const version = ++generation;
                busy(true); renderDraft(false); get('status').textContent = panel.dataset.running;
                try {
                    await checkContext(); if (version !== generation) return;
                    const result = await post(action, { conversation, ...shown, page }, signal);
                    if (version !== generation) { if (result.conversation) post('delete', { conversation: result.conversation }).catch(() => {}); return; }
                    await checkContext(); if (version !== generation) { if (result.conversation) post('delete', { conversation: result.conversation }).catch(() => {}); return; }
                    if (action === 'report-discard') { await reset(); get('status').textContent = panel.dataset.editDiscard; return; }
                    if (saves.has(action)) { report.savedPageId = result.pageId; report.preview = null; get('status').textContent = action === 'report-update' ? panel.dataset.editUpdated : action === 'report-copy' ? panel.dataset.editCopied : panel.dataset.draftSaved; }
                    else if (action === 'report-reload') { acceptStart(result); turns = []; render(); report = result.report; get('status').textContent = panel.dataset.draftNew; }
                    else { report = result.report; get('status').textContent = panel.dataset.draftNew; }
                    request = null; renderDraft(!saves.has(action));
                } catch (error) {
                    if (version === generation) { if (error.draftInvalidated) { report = null; renderDraft(); }
                        if (error.accessChanged) await reset(true); else { if (report) report.preview = null; renderDraft(false); get('status').textContent = error.message; } }
                } finally { if (version === generation) { request = null; busy(false); renderDraft(reportFresh); } }
            }
            const busy = value => {
                get('send').disabled = value || !get('consent').checked || !get('connection').value;
                get('cancel').disabled = !value; get('connection').disabled = value || !!editPage || !!report?.editingPageId; get('consent').disabled = value;
                const start = get('edit-start'); if (start) start.disabled = value || !get('consent').checked;
            };
            async function post(action, body, signal) {
                const response = await fetch(`/agent/${action}`, { method: 'POST', credentials: 'same-origin', redirect: 'error',
                    headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': panel.dataset.csrf }, body: JSON.stringify(body), signal });
                if (response.status === 204) return {};
                let value; try { value = await response.json(); } catch { throw new Error(panel.dataset.failure); }
                if (!response.ok) { const error = new Error(value.error || panel.dataset.failure); error.draftInvalidated = value.invalidateDraft === true; error.accessChanged = response.status === 401 || response.status === 403; throw error; }
                return value;
            }
            async function reset(changed = false) {
                generation++; if (expiryTimer) clearTimeout(expiryTimer); expiryTimer = null; const previous = conversation; conversation = null; turns = []; report = null; editPage = null; renderDraft();
                get('edit-intro')?.replaceChildren();
                request?.abort(); request = null; render(); get('prompt').value = ''; busy(false);
                get('status').textContent = changed ? panel.dataset.changed : '';
                if (changed) { get('send').disabled = true; get('connection').disabled = true; get('consent').checked = false; if (get('edit-start')) get('edit-start').disabled = true; }
                if (previous) { try { await post('delete', { conversation: previous }); } catch { /* Already removed or no longer accessible. */ } }
            }
            async function checkContext() {
                if (!panel.isConnected) { request?.abort(); return; }
                try {
                    const response = await fetch('/agent/context', { credentials: 'same-origin', cache: 'no-store', redirect: 'error' });
                    const value = response.ok ? await response.json() : null;
                    if (!value || value.context !== panel.dataset.context) await reset(true);
                } catch { await reset(true); }
            }
            function acceptStart(started) {
                conversation = started.conversation;
                if (expiryTimer) clearTimeout(expiryTimer);
                expiryTimer = setTimeout(() => { reset(); get('status').textContent = panel.dataset.expired; }, started.expiresInSeconds * 1000);
            }
            get('edit-start')?.addEventListener('click', async () => {
                if (request || !get('consent').checked) return;
                // Explicit fresh opening; no old chat history or other connection is sent to this session.
                const resetting = generation + 1; await reset(); if (generation !== resetting) return;
                editPage = panel.dataset.editPage; get('connection').value = panel.dataset.editConnection;
                request = new AbortController(); const signal = request.signal; const version = ++generation; busy(true);
                try {
                    await checkContext(); if (version !== generation) return;
                    const started = await post('report-edit', { pageId: editPage, account: panel.dataset.account, sharingAccepted: true }, signal);
                    if (version !== generation) { post('delete', { conversation: started.conversation }).catch(() => {}); return; }
                    await checkContext(); if (version !== generation) { post('delete', { conversation: started.conversation }).catch(() => {}); return; }
                    acceptStart(started); report = started.report; request = null; renderDraft(true); get('status').textContent = panel.dataset.draftNew;
                } catch (error) { if (version === generation) { report = null; renderDraft(); get('status').textContent = error.message; } }
                finally { if (version === generation) { request = null; busy(false); } }
            });
            get('question-form').addEventListener('submit', async event => {
                event.preventDefault();
                if (!get('connection').value || !get('consent').checked || request) return;
                const question = get('prompt').value;
                render(false); if (report) { report.preview = null; renderDraft(false); } busy(true); get('status').textContent = panel.dataset.running;
                request = new AbortController(); const signal = request.signal; const version = ++generation;
                try {
                    if (!conversation) {
                        const started = await post(editPage ? 'report-edit' : 'begin', { connection: get('connection').value, pageId: editPage || undefined, account: panel.dataset.account, sharingAccepted: true }, signal);
                        if (generation !== version) { post('delete', { conversation: started.conversation }).catch(() => {}); return; }
                        acceptStart(started); if (started.report) { report = started.report; renderDraft(false); }
                    }
                    const result = await post('ask', { conversation, prompt: question }, signal);
                    if (generation !== version) return;
                    await checkContext(); if (generation !== version) return;
                    if (result.contextInvalidated) turns = turns.map(t => ({ question: t.question, answer: null }));
                    // Report previews are transient UI, not revalidated historical chat answers.
                    const current = { question, answer: result.report ? { ...result, report: null, sources: [], text: panel.dataset.draftNew } : result };
                    if (result.currentTurnRetained) turns.push(current);
                    turns = result.retainedTurns ? turns.slice(-result.retainedTurns) : [];
                    render(true, result.currentTurnRetained ? null : current);
                    if (result.report) { report = result.report; request = null; renderDraft(true); }
                    get('prompt').value = ''; get('status').textContent = panel.dataset.done;
                } catch (error) {
                    if (generation === version) {
                        if (error.draftInvalidated) { report = null; renderDraft(); }
                        if (error.accessChanged) await reset(true);
                        else { render(false); if (report) { report.preview = null; renderDraft(false); } get('status').textContent = signal.aborted ? panel.dataset.cancelled : error.message; }
                    }
                } finally { if (generation === version) { request = null; busy(false); } }
            });
            get('cancel').addEventListener('click', async () => {
                generation++; request?.abort(); request = null; busy(false); render(false); if (report) { report.preview = null; renderDraft(false); }
                get('status').textContent = panel.dataset.cancelled;
                // Retain completed turns; only New/connection change/logout deletes the conversation.
                if (conversation) { try { await post('cancel', { conversation }); } catch { /* No completed data is reintroduced. */ } }
            });
            get('new').addEventListener('click', () => reset());
            get('connection').addEventListener('change', () => reset());
            get('consent').addEventListener('change', () => { if (!get('consent').checked) reset(); else busy(false); });
            busy(false);
            window.addEventListener('focus', checkContext);
            window.addEventListener('pagehide', () => request?.abort());
            const timer = setInterval(() => { if (!panel.isConnected) { clearInterval(timer); request?.abort(); } else checkContext(); }, 2000);
        }
    }
    document.addEventListener('DOMContentLoaded', mount);
    if (window.Blazor) Blazor.addEventListener('enhancedload', mount);
    mount();
})();
