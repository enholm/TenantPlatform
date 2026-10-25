const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
class Element {
    constructor() { this.children = []; this.dataset = {}; this.style = {}; this.listeners = {}; this.isConnected = true; this.textContent = ''; }
    append(node) { this.children.push(node); }
    replaceChildren() { this.children = []; }
    addEventListener(event, fn) { this.listeners[event] = fn; }
    setAttribute() {}
    set innerHTML(_) { throw new Error('Unsafe HTML rendering'); }
    text() { return this.textContent + this.children.map(n => n.text()).join(' '); }
}
const tick = () => new Promise(resolve => setImmediate(resolve));
const response = value => ({ ok: true, json: async () => value });
test('report adapter clears stale results, preserves safe chart/table output and ignores late account responses', async () => {
    const panel = new Element(); panel.dataset = { id: 'page', revision: 'revision', context: 'current', components: '["Metric","BuildingChart","AgreementTable"]', csrf: 'csrf',
        total: 'TOTAL', date: 'PERIOD', allDates: 'ANY END DATE', snapshot: 'NO SNAPSHOT', title: 'TITLE', building: 'BUILDING', end: 'END', count: 'COUNT', page: 'PAGE', missing: 'MISSING', error: 'FAILURE', cancelled: 'CANCELLED', empty: 'EMPTY' };
    const nodes = Object.fromEntries(['editor', 'result', 'status', 'cancel', 'preview', 'refresh', 'delete'].map(n => [n, new Element()]));
    panel.querySelector = selector => nodes[selector.match(/\[data-([^\]]+)\]/)[1]];
    panel.querySelectorAll = () => [];
    let pending, fail = false, stamp = 'current'; const calls = [], events = {};
    const value = { retrievedUtc: '2026-10-19T12:00:00Z', selection: { filter: { endDateFrom: '2026-10-19', endDateTo: '2027-01-19' } },
        aggregate: { totalCount: 27, groups: [{ name: '<script>unsafe()</script>', count: 27 }] }, table: { page: 1, totalCount: 27, returnedCount: 25, items: [{ title: '<img src=evil>', building: null, endDate: null }], nextPage: 2 } };
    const fetch = async (url, options) => {
        assert.match(url, /^\/my-pages\//); calls.push({ url, body: options?.body && JSON.parse(options.body) });
        if (url.endsWith('/context')) return response({ context: stamp });
        if (pending) return pending.promise;
        if (fail) return { ok: false, json: async () => ({ error: 'SOURCE FAILED' }) };
        return response(value);
    };
    vm.runInNewContext(fs.readFileSync('src/TenantPlatform.Web/wwwroot/js/report-renderer.js', 'utf8') + '\n' + fs.readFileSync('src/TenantPlatform.Web/wwwroot/js/personal-pages.js', 'utf8'), {
        document: { querySelectorAll: () => [panel], createElement: () => new Element(), addEventListener() {} },
        window: { addEventListener: (event, fn) => events[event] = fn }, fetch, AbortController, setInterval() {}, clearInterval() {}, confirm: () => true, location: { assign() {} }
    });
    await tick(); await tick();
    assert.match(nodes.result.text(), /TOTAL 27/); assert.match(nodes.result.text(), /NO SNAPSHOT/);
    assert.match(nodes.result.text(), /<script>unsafe\(\)<\/script>/); assert.match(nodes.result.text(), /<img src=evil>/);
    assert(calls.some(c => c.url.endsWith('/run') && c.body.page === 1));
    fail = true; await nodes.refresh.listeners.click();
    assert.equal(nodes.result.children.length, 0); assert.equal(nodes.status.textContent, 'SOURCE FAILED');
    fail = false; let resolve; pending = { promise: new Promise(r => resolve = r) };
    const late = nodes.refresh.listeners.click(); await tick(); nodes.cancel.listeners.click(); resolve(response(value)); await late;
    assert.equal(nodes.result.children.length, 0); assert.equal(nodes.status.textContent, 'CANCELLED');
    pending = null; await nodes.refresh.listeners.click(); assert(nodes.result.children.length > 0);
    stamp = 'another-account'; await events.focus(); await tick(); assert.equal(nodes.result.children.length, 0);
    assert(calls.every(c => !c.url.includes('agent') && !c.url.includes('openai')));
});
test('manual editor emits the bounded declaration and saves without owner or account input', async () => {
    const panel = new Element(); panel.dataset = { id: '', revision: 'revision', context: 'current', csrf: 'csrf', components: '[]', error: 'FAIL' };
    const nodes = Object.fromEntries(['editor', 'result', 'status', 'cancel', 'preview', 'refresh', 'delete'].map(n => [n, new Element()]));
    const fields = Object.fromEntries(['title', 'connection', 'period', 'months', 'from', 'to', 'search', 'status', 'type', 'archive', 'building', 'unit', 'organization', 'geography', 'missing', 'Metric', 'BuildingChart', 'AgreementTable'].map(n => [n, { value: '', checked: false }]));
    Object.assign(fields.title, { value: 'My report' }); Object.assign(fields.connection, { value: 'source', selectedOptions: [{ dataset: { query: 'query-version', aggregate: 'aggregate-version', profile: 'profile-version' } }] });
    fields.period.value = 'NextMonths'; fields.months.value = '3'; fields.archive.value = 'Exclude'; fields.Metric.value = '0'; fields.BuildingChart.value = '1'; fields.AgreementTable.value = '2';
    for (const name of ['Metric', 'BuildingChart', 'AgreementTable']) fields['show' + name] = { checked: true };
    fields.groupBy = { value: 'Status' };
    for (const [index, name] of ['Title', 'Type', 'Status', 'Building', 'StartDate', 'EndDate', 'NoticeDeadline', 'RenewalDate'].entries()) {
        fields['showColumn' + name] = { checked: ['Title', 'Status', 'EndDate'].includes(name) };
        fields['column' + name] = { value: String(index) };
    }
    nodes.editor.elements = { namedItem: name => fields[name] }; panel.querySelector = selector => nodes[selector.match(/\[data-([^\]]+)\]/)[1]]; panel.querySelectorAll = () => [];
    let sent, target;
    vm.runInNewContext(fs.readFileSync('src/TenantPlatform.Web/wwwroot/js/report-renderer.js', 'utf8') + '\n' + fs.readFileSync('src/TenantPlatform.Web/wwwroot/js/personal-pages.js', 'utf8'), {
        document: { querySelectorAll: () => [panel], createElement: () => new Element(), addEventListener() {} }, window: { addEventListener() {} },
        fetch: async (url, options) => { if (url.endsWith('/context')) return response({ context: 'current' }); sent = JSON.parse(options.body); return response({ pages: [{ id: 'new-page' }] }); },
        AbortController, setInterval() {}, clearInterval() {}, confirm: () => true, location: { assign: path => target = path }
    });
    nodes.editor.listeners.submit({ preventDefault() {} }); await tick(); await tick();
    assert.equal(sent.definition.version, 2); assert.equal(sent.definition.groupBy, 'Status');
    assert.deepEqual(sent.definition.columns, ['Title', 'Status', 'EndDate']);
    assert.equal(sent.definition.period.kind, 'NextMonths'); assert.equal(sent.definition.period.months, 3);
    assert.equal(sent.definition.period.from, null); assert.equal(sent.definition.profileVersion, 'profile-version');
    assert.deepEqual(sent.definition.components, ['Metric', 'BuildingChart', 'AgreementTable']);
    assert(!('accountId' in sent.definition)); assert(!('ownerUserId' in sent.definition)); assert.equal(target, '/my-pages/new-page');
    fields.period.value = 'All';
    fields.showMetric.checked = false; fields.showAgreementTable.checked = false;
    for (const key of Object.keys(fields).filter(k => k.startsWith('showColumn'))) fields[key].checked = false;
    nodes.editor.listeners.submit({ preventDefault() {} }); await tick(); await tick();
    assert.equal(sent.definition.period.kind, 'All'); assert.equal(sent.definition.period.months, null); assert.equal(sent.definition.period.from, null); assert.equal(sent.definition.period.to, null);
    assert.deepEqual(sent.definition.components, ['BuildingChart']); assert.deepEqual(sent.definition.columns, []);
    fields.showBuildingChart.checked = false; sent = null;
    nodes.editor.listeners.submit({ preventDefault() {} }); await tick(); await tick();
    assert.equal(sent, null); assert.equal(nodes.status.textContent, 'FAIL');
});

test('renderer honors optional components, Status labels and ordered allowlisted columns without hidden rows', () => {
    const window = {}, output = new Element();
    vm.runInNewContext(fs.readFileSync('src/TenantPlatform.Web/wwwroot/js/report-renderer.js', 'utf8'), {
        document: { createElement: () => new Element() }, window
    });
    const labels = { allDates: 'ANY END DATE', status: 'STATUS', statusActive: 'ACTIVE LABEL', title: 'TITLE', building: 'BUILDING', start: 'START', end: 'END', count: 'COUNT', total: 'TOTAL' };
    const value = { retrievedUtc: 'now', selection: { filter: {} }, groupBy: 'Status', columns: ['Status', 'Title'],
        aggregate: { totalCount: 27, groups: [{ key: 'Active', name: 'Active', count: 27 }] }, table: null, components: ['BuildingChart', 'Metric'] };
    // A stale SSR component list must not override the server's current execution plan.
    window.TenantPlatformReports.render(value, ['Metric', 'AgreementTable', 'BuildingChart'], output, labels, () => {});
    assert.match(output.text(), /ANY END DATE/); assert(!output.text().includes('null'));
    assert.equal(output.children.filter(x => x.className?.includes('card')).length, 2);
    assert.match(output.text(), /ACTIVE LABEL/); assert.match(output.text(), /TOTAL 27/);
    output.replaceChildren();
    value.components = ['AgreementTable']; value.aggregate = null; value.table = { page: 1, totalCount: 27, returnedCount: 1, items: [{ status: 'Active', title: '<script>unsafe</script>', building: { name: 'HIDDEN BUILDING' } }] };
    window.TenantPlatformReports.render(value, ['AgreementTable'], output, labels, () => {});
    const section = output.children.find(x => x.className?.includes('card'));
    const table = section.children.find(x => x.className === 'table');
    assert.deepEqual(table.children[1].children.map(x => x.textContent), ['STATUS', 'TITLE']);
    assert.deepEqual(table.children[2].children.map(x => x.textContent), ['ACTIVE LABEL', '<script>unsafe</script>']);
    assert(!output.text().includes('HIDDEN BUILDING')); assert(!output.text().includes('TOTAL'));
    output.replaceChildren();
    value.columns = ['RenewalDate', 'NoticeDeadline', 'StartDate', 'Type', 'EndDate', 'Building', 'Title', 'Status'];
    Object.assign(labels, { renewal: 'RENEWAL', notice: 'NOTICE', type: 'TYPE', typeSoftwareLicense: 'LICENSE LABEL' });
    Object.assign(value.table.items[0], { renewalDate: '2027-01-01', noticeDeadline: '2026-11-01', startDate: '2025-01-01', type: 'SoftwareLicense', endDate: '2026-12-31' });
    window.TenantPlatformReports.render(value, ['AgreementTable'], output, labels, () => {});
    const allTable = output.children.find(x => x.className?.includes('card')).children.find(x => x.className === 'table');
    assert.deepEqual(allTable.children[1].children.map(x => x.textContent), ['RENEWAL', 'NOTICE', 'START', 'TYPE', 'END', 'BUILDING', 'TITLE', 'STATUS']);
    assert.deepEqual(allTable.children[2].children.map(x => x.textContent), ['2027-01-01', '2026-11-01', '2025-01-01', 'LICENSE LABEL', '2026-12-31', 'HIDDEN BUILDING', '<script>unsafe</script>', 'ACTIVE LABEL']);
});

test('chart renders visible proportional bars with a common scale, safe labels and no agreement table', () => {
    const window = {}, output = new Element();
    vm.runInNewContext(fs.readFileSync('src/TenantPlatform.Web/wwwroot/js/report-renderer.js', 'utf8'), {
        document: { createElement: () => new Element() }, window
    });
    const value = { selection: { filter: {} }, retrievedUtc: 'now', components: ['BuildingChart'], groupBy: 'Status',
        aggregate: { totalCount: 15, groups: [{ key: 'Active', count: 10 }, { key: 'Draft', count: 5 }, { key: 'Expired', count: 0 }] } };
    window.TenantPlatformReports.render(value, value.components, output, { allDates: 'All dates', status: 'Status', statusActive: 'Active', statusDraft: 'Draft', statusExpired: 'Expired', count: 'Count' }, () => {});
    const section = output.children.find(x => x.className?.includes('card'));
    const chart = section.children.find(x => x.className === 'report-bar-chart');
    assert(chart); assert.equal(chart.children.length, 3);
    assert.deepEqual(chart.children.map(x => x.children[1].children[0].style.width), ['100%', '50%', '0%']);
    assert.deepEqual(chart.children.map(x => x.children[0].textContent), ['Active', 'Draft', 'Expired']);
    assert.deepEqual(chart.children.map(x => x.children[2].textContent), ['10', '5', '0']);
    assert(!section.children.some(x => x.className === 'table'));
    output.replaceChildren(); value.groupBy = 'Building'; value.aggregate.groups = [{ key: 'building', name: '<img src=evil>', count: 10 }];
    window.TenantPlatformReports.render(value, value.components, output, { building: 'Building' }, () => {});
    assert.match(output.text(), /<img src=evil>/);
});

test('request report adapter renders metric, real bars, nullable timestamps and paged rows without agreement date semantics', () => {
    const window = {}, output = new Element();
    vm.runInNewContext(fs.readFileSync('src/TenantPlatform.Web/wwwroot/js/report-renderer.js', 'utf8'), { document: { createElement: () => new Element() }, window });
    const value = { area: 'ServiceRequests', retrievedUtc: 'now', selection: null, components: ['Metric', 'BuildingChart', 'AgreementTable'], groupBy: 'Status',
        columns: ['Status', 'Title', 'CreatedAt', 'SubmittedAt', 'CompletedAt', 'Unit'], requests: { filter: {}, aggregate: { totalCount: 27, groups: [{ key: 'Submitted', name: 'Submitted', count: 27 }] },
            table: { page: 1, totalCount: 27, returnedCount: 1, nextPage: 2, hasMore: true, complete: false, items: [{ title: null, status: 'Submitted', createdAt: '2026-01-01T00:00:00Z', submittedAt: null, completedAt: null, unit: null }] } } };
    let page;
    window.TenantPlatformReports.render(value, [], output, { requestAll: 'ALL REQUESTS', requestStatusSubmitted: 'RECEIVED', status: 'STATUS', title: 'TITLE', requestCreatedAt: 'CREATED', requestSubmittedAt: 'SUBMITTED', requestCompletedAt: 'COMPLETED', requestUnit: 'UNIT', missing: 'MISSING', total: 'TOTAL' }, next => page = next);
    assert.match(output.text(), /ALL REQUESTS/); assert.match(output.text(), /RECEIVED/); assert.match(output.text(), /TOTAL 27/); assert(!output.text().includes('null'));
    const sections = output.children.filter(x => x.className?.includes('card'));
    assert.equal(sections[1].children.find(x => x.className === 'report-bar-chart').children[0].children[1].children[0].style.width, '100%');
    const table = sections[2].children.find(x => x.className === 'table');
    assert.deepEqual(table.children[2].children.map(x => x.textContent), ['RECEIVED', '—', '2026-01-01T00:00:00Z', '—', '—', 'MISSING']);
    sections[2].children.filter(x => x.className === 'btn btn-outline-secondary')[1].listeners.click(); assert.equal(page, 2);
});

test('request editor serializes only the typed request filter and All period, retaining backend tool bindings', async () => {
    const panel = new Element(); panel.dataset = { area: 'ServiceRequests', revision: 'r', context: 'current', error: 'FAIL', components: '[]' };
    const nodes = Object.fromEntries(['editor', 'result', 'status', 'cancel', 'preview', 'refresh', 'delete'].map(n => [n, new Element()]));
    const fields = { title: { value: 'Requests' }, connection: { value: 'source', selectedOptions: [{ dataset: { profile: 'profile-v', query: 'query-v', aggregate: 'aggregate-v' } }] },
        groupBy: { value: 'Status' }, search: { value: 'Needle' }, status: { value: 'Submitted' }, building: { value: '' }, missing: { checked: false } };
    for (const [i, name] of ['Metric', 'BuildingChart', 'AgreementTable'].entries()) { fields['show' + name] = { checked: true }; fields[name] = { value: String(i) }; }
    for (const [i, name] of ['Title', 'Status', 'Building', 'Unit', 'CreatedAt', 'SubmittedAt', 'CompletedAt'].entries()) { fields['showColumn' + name] = { checked: true }; fields['column' + name] = { value: String(i) }; }
    nodes.editor.elements = { namedItem: name => fields[name] }; panel.querySelector = selector => nodes[selector.match(/\[data-([^\]]+)\]/)[1]]; panel.querySelectorAll = () => [];
    let sent;
    vm.runInNewContext(fs.readFileSync('src/TenantPlatform.Web/wwwroot/js/personal-pages.js', 'utf8'), {
        document: { querySelectorAll: () => [panel], createElement: () => new Element(), addEventListener() {} }, window: { addEventListener() {} },
        fetch: async (url, options) => { if (url.endsWith('/context')) return response({ context: 'current' }); sent = JSON.parse(options.body); return response({ pages: [{ id: 'request-page' }] }); },
        AbortController, setInterval() {}, clearInterval() {}, location: { assign() {} }
    });
    nodes.editor.listeners.submit({ preventDefault() {} }); await tick(); await tick();
    assert.equal(sent.definition.area, 'ServiceRequests'); assert.equal(sent.definition.version, 3);
    assert.deepEqual(sent.definition.period, { kind: 'All', months: null, from: null, to: null });
    assert.deepEqual(sent.definition.filter, {}); assert.deepEqual(sent.definition.requestFilter, { search: 'Needle', status: 'Submitted', buildingId: null, missingBuilding: false });
    assert.equal(sent.definition.queryVersion, 'query-v'); assert.equal(sent.definition.aggregateVersion, 'aggregate-v'); assert.equal(sent.definition.profileVersion, 'profile-v');
    assert(!('userId' in sent.definition)); assert(!('ownerUserId' in sent.definition)); assert.equal(sent.definition.columns.length, 7);
});
