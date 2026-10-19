const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
class Element {
    constructor() { this.children = []; this.dataset = {}; this.listeners = {}; this.isConnected = true; this.textContent = ''; }
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
        total: 'TOTAL', date: 'PERIOD', snapshot: 'NO SNAPSHOT', title: 'TITLE', building: 'BUILDING', end: 'END', count: 'COUNT', page: 'PAGE', missing: 'MISSING', error: 'FAILURE', cancelled: 'CANCELLED', empty: 'EMPTY' };
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
    vm.runInNewContext(fs.readFileSync('src/TenantPlatform.Web/wwwroot/js/personal-pages.js', 'utf8'), {
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
    nodes.editor.elements = { namedItem: name => fields[name] }; panel.querySelector = selector => nodes[selector.match(/\[data-([^\]]+)\]/)[1]]; panel.querySelectorAll = () => [];
    let sent, target;
    vm.runInNewContext(fs.readFileSync('src/TenantPlatform.Web/wwwroot/js/personal-pages.js', 'utf8'), {
        document: { querySelectorAll: () => [panel], createElement: () => new Element(), addEventListener() {} }, window: { addEventListener() {} },
        fetch: async (url, options) => { if (url.endsWith('/context')) return response({ context: 'current' }); sent = JSON.parse(options.body); return response({ pages: [{ id: 'new-page' }] }); },
        AbortController, setInterval() {}, clearInterval() {}, confirm: () => true, location: { assign: path => target = path }
    });
    nodes.editor.listeners.submit({ preventDefault() {} }); await tick(); await tick();
    assert.equal(sent.definition.period.kind, 'NextMonths'); assert.equal(sent.definition.period.months, 3);
    assert.equal(sent.definition.period.from, null); assert.equal(sent.definition.profileVersion, 'profile-version');
    assert.deepEqual(sent.definition.components, ['Metric', 'BuildingChart', 'AgreementTable']);
    assert(!('accountId' in sent.definition)); assert(!('ownerUserId' in sent.definition)); assert.equal(target, '/my-pages/new-page');
});
