(() => {
    'use strict';
    function mount() {
        document.querySelectorAll('[data-personal-pages]').forEach(panel => {
            if (panel.dataset.mounted) return;
            panel.dataset.mounted = 'true';
            const form = panel.querySelector('[data-editor]'), output = panel.querySelector('[data-result]'), status = panel.querySelector('[data-status]');
            const field = name => form.elements.namedItem(name);
            let request, generation = 0, currentPage = 1;
            function clear() { generation++; request?.abort(); request = null; output.replaceChildren(); panel.querySelector('[data-cancel]').disabled = true; }
            function text(tag, value, parent) { const node = document.createElement(tag); node.textContent = value; parent.append(node); return node; }
            async function context() {
                const response = await fetch('/my-pages/context', { credentials: 'same-origin', cache: 'no-store', redirect: 'error' });
                const value = response.ok ? await response.json() : null;
                if (!value || value.context !== panel.dataset.context) { clear(); status.textContent = panel.dataset.error; return false; }
                return true;
            }
            async function post(action, body, signal) {
                const response = await fetch('/my-pages/action/' + action, { method: 'POST', credentials: 'same-origin', cache: 'no-store', redirect: 'error', signal,
                    headers: { 'Content-Type': 'application/json', RequestVerificationToken: panel.dataset.csrf }, body: JSON.stringify(body) });
                const value = await response.json(); if (!response.ok) throw new Error(value.error || panel.dataset.error); return value;
            }
            function definition() {
                const option = field('connection').selectedOptions[0];
                const positions = ['Metric', 'BuildingChart', 'AgreementTable'].map(c => [Number(field(c).value), c]);
                if (new Set(positions.map(p => p[0])).size !== 3) throw new Error(panel.dataset.error);
                const fixed = field('period').value === 'Fixed';
                return { version: 1, title: field('title').value, connectionId: field('connection').value, profileVersion: option?.dataset.profile || '', queryVersion: option?.dataset.query || '', aggregateVersion: option?.dataset.aggregate || '',
                    period: { kind: fixed ? 'Fixed' : 'NextMonths', months: fixed ? null : Number(field('months').value), from: fixed ? field('from').value : null, to: fixed ? field('to').value : null },
                    filter: { search: field('search').value || null, status: field('status').value || null, type: field('type').value || null, archive: field('archive').value,
                        unitId: field('unit').value || null, organizationElementId: field('organization').value || null, geographicAreaId: field('geography').value || null, buildingId: field('building').value || null, missingBuilding: field('missing').checked }, components: positions.sort((a, b) => a[0] - b[0]).map(p => p[1]), layout: 'Stacked' };
            }
            function render(value, components, preview) {
                const filters = value.selection.filter;
                text('p', `${panel.dataset.date}: ${filters.endDateFrom} – ${filters.endDateTo} · ${value.retrievedUtc}`, output);
                text('p', panel.dataset.snapshot, output);
                for (const component of components) {
                    const section = document.createElement('section'); section.className = 'card card-body my-3'; output.append(section);
                    if (component === 'Metric') { text('h2', panel.dataset.total, section); text('p', String(value.aggregate.totalCount), section); }
                    if (component === 'BuildingChart') {
                        text('h2', panel.dataset.building, section);
                        const table = document.createElement('table'); table.className = 'table'; section.append(table);
                        text('caption', `${panel.dataset.building} / ${panel.dataset.count}`, table);
                        const header = document.createElement('tr'); text('th', panel.dataset.building, header).scope = 'col'; text('th', panel.dataset.count, header).scope = 'col'; table.append(header);
                        for (const group of value.aggregate.groups) {
                            const row = document.createElement('tr'); text('th', group.name || panel.dataset.missing, row).scope = 'row'; const cell = text('td', String(group.count), row);
                            const bar = document.createElement('meter'); bar.className = 'w-100'; bar.min = 0; bar.max = Math.max(1, value.aggregate.totalCount); bar.value = group.count; bar.setAttribute('aria-label', (group.name || panel.dataset.missing) + ': ' + group.count); cell.append(bar); table.append(row);
                        }
                        if (!value.aggregate.groups.length) text('p', panel.dataset.empty, section);
                    }
                    if (component === 'AgreementTable') {
                        text('h2', panel.dataset.title, section);
                        const table = document.createElement('table'); table.className = 'table'; section.append(table);
                        text('caption', `${panel.dataset.page}: ${value.table.page} · ${value.table.returnedCount} / ${value.table.totalCount}`, table);
                        const header = document.createElement('tr'); for (const key of ['title', 'building', 'end']) text('th', panel.dataset[key], header).scope = 'col'; table.append(header);
                        for (const item of value.table.items) { const row = document.createElement('tr'); text('td', item.title, row); text('td', item.building?.name || panel.dataset.missing, row); text('td', item.endDate || '—', row); table.append(row); }
                        text('p', `${panel.dataset.complete}: ${value.table.complete ? panel.dataset.yes : panel.dataset.no} · ${panel.dataset.more}: ${value.table.hasMore ? panel.dataset.yes : panel.dataset.no} · ${panel.dataset.limit}: ${value.table.limitReached ? panel.dataset.yes : panel.dataset.no}`, section);
                        if (!value.table.items.length) text('p', panel.dataset.empty, section);
                        for (const [label, next, disabled] of [['←', value.table.page - 1, value.table.page <= 1], ['→', value.table.nextPage, !value.table.nextPage]]) {
                            const button = text('button', label, section); button.className = 'btn btn-outline-secondary'; button.disabled = disabled;
                            button.setAttribute('aria-label', panel.dataset.page + ' ' + (next || '')); button.addEventListener('click', () => run(next, preview));
                        }
                        if (value.table.limitReached) text('p', panel.dataset.error, section);
                    }
                }
            }
            async function run(page, preview) {
                clear(); const version = generation; const controller = request = new AbortController(); panel.querySelector('[data-cancel]').disabled = false; status.textContent = panel.dataset.loading;
                try {
                    if (!await context() || version !== generation) return;
                    const draft = preview ? definition() : null;
                    const value = await post(preview ? 'preview' : 'run', { id: preview ? null : panel.dataset.id, revision: panel.dataset.revision, definition: draft, page }, controller.signal);
                    if (version !== generation || !await context() || version !== generation) return;
                    currentPage = value.table.page;
                    render(value, preview ? draft.components : JSON.parse(panel.dataset.components), preview); status.textContent = '';
                } catch (error) { if (version === generation) { output.replaceChildren(); status.textContent = controller.signal.aborted ? panel.dataset.cancelled : error.message; } }
                finally { if (version === generation) { request = null; panel.querySelector('[data-cancel]').disabled = true; } }
            }
            async function mutate(action, body) {
                clear(); const version = generation; request = new AbortController();
                try {
                    if (!await context()) return;
                    const result = await post(action, body, request.signal);
                    if (version !== generation || !await context()) return;
                    const target = action === 'save' ? (body.id || result.pages[result.pages.length - 1].id) : null;
                    location.assign(target ? '/my-pages/' + target : '/my-pages');
                } catch (error) { if (version === generation) status.textContent = error.message; }
                finally { if (version === generation) request = null; }
            }
            form.addEventListener('submit', event => { event.preventDefault(); try { mutate('save', { id: panel.dataset.id || null, revision: panel.dataset.revision, definition: definition() }); } catch (error) { status.textContent = error.message; } });
            panel.querySelector('[data-preview]').addEventListener('click', () => run(1, true));
            panel.querySelector('[data-refresh]')?.addEventListener('click', () => run(1, false));
            panel.querySelector('[data-delete]')?.addEventListener('click', () => { if (confirm(panel.dataset.deleteConfirm)) mutate('delete', { id: panel.dataset.id, revision: panel.dataset.revision, definition: null }); });
            panel.querySelectorAll('[data-move]').forEach(button => button.addEventListener('click', () => mutate('move', { id: button.dataset.target, revision: panel.dataset.revision, definition: null, move: Number(button.dataset.move) })));
            panel.querySelector('[data-cancel]').addEventListener('click', () => { clear(); status.textContent = panel.dataset.cancelled; });
            window.addEventListener('pagehide', clear); window.addEventListener('focus', () => context().catch(clear));
            const timer = setInterval(() => { if (!panel.isConnected) { clearInterval(timer); clear(); } else context().catch(clear); }, 2000);
            if (panel.dataset.id) run(currentPage, false);
        });
    }
    document.addEventListener('DOMContentLoaded', mount); if (window.Blazor) Blazor.addEventListener('enhancedload', mount); mount();
})();
