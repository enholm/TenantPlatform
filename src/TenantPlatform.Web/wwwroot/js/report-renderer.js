// Shared deterministic 9A/9B presentation. No model, HTML evaluation or network.
(() => {
    function text(tag, value, parent) { const node = document.createElement(tag); node.textContent = value; parent.append(node); return node; }
    const areas = {
        Agreements: (value, labels) => ({ value, period: value.selection.filter.endDateFrom == null && value.selection.filter.endDateTo == null ? labels.allDates : `${value.selection.filter.endDateFrom} – ${value.selection.filter.endDateTo}`, statusPrefix: 'status', extraCells: {} }),
        ServiceRequests: (value, labels) => ({ value: { ...value, aggregate: value.requests.aggregate, table: value.requests.table }, period: labels.requestAll, statusPrefix: 'requestStatus', labels: { ...labels, total: labels.requestTotal || labels.total }, extraCells: {
            CreatedAt: ['requestCreatedAt', item => item.createdAt], SubmittedAt: ['requestSubmittedAt', item => item.submittedAt || '—'],
            CompletedAt: ['requestCompletedAt', item => item.completedAt || '—'], Unit: ['requestUnit', item => item.unit?.name || labels.missing]
        } })
    };
    function render(value, components, output, labels, onPage) {
                components = value.components || components;
                const area = areas[value.area || "Agreements"](value, labels); value = area.value; labels = area.labels || labels;
                const columns = value.columns || ['Title', 'Building', 'EndDate'];
                const cells = {
                    Title: ['title', item => item.title || '—'],
                    Type: ['type', item => labels['type' + item.type] || item.type],
                    Status: ['status', item => labels[area.statusPrefix + item.status] || item.status],
                    Building: ['building', item => item.building?.name || labels.missing],
                    StartDate: ['start', item => item.startDate || '—'],
                    EndDate: ['end', item => item.endDate || '—'],
                    NoticeDeadline: ['notice', item => item.noticeDeadline || '—'],
                    RenewalDate: ['renewal', item => item.renewalDate || '—'], ...area.extraCells
                };
                const grouping = value.groupBy === 'Status' ? labels.status : labels.building;
                const period = area.period;
                text('p', `${labels.date}: ${period} · ${value.retrievedUtc}`, output);
                text('p', labels.snapshot, output);
                for (const component of components) {
                    const section = document.createElement('section'); section.className = 'card card-body my-3'; output.append(section);
                    if (component === 'Metric') { text('h2', labels.total, section); text('p', String(value.aggregate.totalCount), section); }
                    if (component === 'BuildingChart') {
                        text('h2', grouping, section);
                        const chart = document.createElement('div'); chart.className = 'report-bar-chart';
                        chart.setAttribute('role', 'list'); chart.setAttribute('aria-label', `${grouping} / ${labels.count}`); section.append(chart);
                        // All rows share the same zero-based count scale. Bar length is not a percentage of the total.
                        const maximum = Math.max(1, ...value.aggregate.groups.map(group => group.count));
                        for (const group of value.aggregate.groups) {
                            const name = value.groupBy === 'Status' ? (labels[area.statusPrefix + group.key] || group.name || group.key) : (group.name || labels.missing);
                            const row = document.createElement('div'); row.className = 'report-bar-row'; row.setAttribute('role', 'listitem'); chart.append(row);
                            text('span', name, row).className = 'report-bar-label';
                            const track = document.createElement('div'); track.className = 'report-bar-track'; track.setAttribute('aria-hidden', 'true'); row.append(track);
                            const bar = document.createElement('div'); bar.className = 'report-bar-fill';
                            bar.style.width = `${Math.max(0, Math.min(100, group.count / maximum * 100))}%`; track.append(bar);
                            text('span', String(group.count), row).className = 'report-bar-value';
                        }
                        if (!value.aggregate.groups.length) text('p', labels.empty, section);
                    }
                    if (component === 'AgreementTable') {
                        text('h2', labels.title, section);
                        const table = document.createElement('table'); table.className = 'table'; section.append(table);
                        text('caption', `${labels.page}: ${value.table.page} · ${value.table.returnedCount} / ${value.table.totalCount}`, table);
                        const header = document.createElement('tr'); for (const column of columns) text('th', labels[cells[column][0]], header).scope = 'col'; table.append(header);
                        for (const item of value.table.items) { const row = document.createElement('tr'); for (const column of columns) text('td', cells[column][1](item), row); table.append(row); }
                        text('p', `${labels.complete}: ${value.table.complete ? labels.yes : labels.no} · ${labels.more}: ${value.table.hasMore ? labels.yes : labels.no} · ${labels.limit}: ${value.table.limitReached ? labels.yes : labels.no}`, section);
                        if (!value.table.items.length) text('p', labels.empty, section);
                        for (const [label, next, disabled] of [['←', value.table.page - 1, value.table.page <= 1], ['→', value.table.nextPage, !value.table.nextPage]]) {
                            const button = text('button', label, section); button.className = 'btn btn-outline-secondary'; button.disabled = disabled;
                            button.setAttribute('aria-label', labels.page + ' ' + (next || '')); button.addEventListener('click', () => onPage(next));
                        }
                        if (value.table.limitReached) text('p', labels.error, section);
                    }
                }
            }
    window.TenantPlatformReports = { render };
})();
