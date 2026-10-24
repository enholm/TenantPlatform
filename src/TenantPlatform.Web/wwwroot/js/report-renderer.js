// Shared deterministic 9A/9B presentation. No model, HTML evaluation or network.
(() => {
    function text(tag, value, parent) { const node = document.createElement(tag); node.textContent = value; parent.append(node); return node; }
    function render(value, components, output, labels, onPage) {
                const filters = value.selection.filter;
                text('p', `${labels.date}: ${filters.endDateFrom} – ${filters.endDateTo} · ${value.retrievedUtc}`, output);
                text('p', labels.snapshot, output);
                for (const component of components) {
                    const section = document.createElement('section'); section.className = 'card card-body my-3'; output.append(section);
                    if (component === 'Metric') { text('h2', labels.total, section); text('p', String(value.aggregate.totalCount), section); }
                    if (component === 'BuildingChart') {
                        text('h2', labels.building, section);
                        const table = document.createElement('table'); table.className = 'table'; section.append(table);
                        text('caption', `${labels.building} / ${labels.count}`, table);
                        const header = document.createElement('tr'); text('th', labels.building, header).scope = 'col'; text('th', labels.count, header).scope = 'col'; table.append(header);
                        for (const group of value.aggregate.groups) {
                            const row = document.createElement('tr'); text('th', group.name || labels.missing, row).scope = 'row'; const cell = text('td', String(group.count), row);
                            const bar = document.createElement('meter'); bar.className = 'w-100'; bar.min = 0; bar.max = Math.max(1, value.aggregate.totalCount); bar.value = group.count; bar.setAttribute('aria-label', (group.name || labels.missing) + ': ' + group.count); cell.append(bar); table.append(row);
                        }
                        if (!value.aggregate.groups.length) text('p', labels.empty, section);
                    }
                    if (component === 'AgreementTable') {
                        text('h2', labels.title, section);
                        const table = document.createElement('table'); table.className = 'table'; section.append(table);
                        text('caption', `${labels.page}: ${value.table.page} · ${value.table.returnedCount} / ${value.table.totalCount}`, table);
                        const header = document.createElement('tr'); for (const key of ['title', 'building', 'end']) text('th', labels[key], header).scope = 'col'; table.append(header);
                        for (const item of value.table.items) { const row = document.createElement('tr'); text('td', item.title, row); text('td', item.building?.name || labels.missing, row); text('td', item.endDate || '—', row); table.append(row); }
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
