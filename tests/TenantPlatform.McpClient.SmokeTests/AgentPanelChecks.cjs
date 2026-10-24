// Deterministic DOM/transport harness for the actual browser adapter; no browser/model/network.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
class Element {
    constructor() { this.children = []; this.dataset = {}; this.style = {}; this.listeners = {}; this.value = ''; this.checked = false; this.isConnected = true; this.textContent = ''; }
    append(node) { this.children.push(node); }
    replaceChildren() { this.children = []; }
    addEventListener(name, fn) { this.listeners[name] = fn; }
    setAttribute() {}
    set innerHTML(_) { throw new Error('Untrusted HTML rendering'); }
    text() { return this.textContent + this.children.map(n => n.text()).join(' '); }
}
const response = value => ({ status: 200, ok: true, json: async () => value });
const completed = (text, count, revalidated = false) => ({ text, retainedTurns: count, currentTurnRetained: true,
    contextInvalidated: false, notices: ['bounded evidence'], sources: [{ revalidated, connectionName: '<img src=https://evil.test>', tool: 'read', version: 'v', summary: 'page 1' }] });
const deferred = () => { let resolve; const promise = new Promise(r => resolve = r); return { promise, resolve }; };
const tick = () => new Promise(r => setImmediate(r));
test('panel preserves completed pairs, cancels message, deletes context and ignores late results', async () => {
    const panel = new Element();
    panel.dataset = { account: 'account', context: 'context', csrf: 'csrf', questionLabel: 'Question', answerLabel: 'Answer',
        revalidated: 'REVALIDATED', newSource: 'NEW', pendingHistory: 'pending', expired: 'expired', cancelled: 'cancelled', done: 'done', failure: 'failure', changed: 'changed' };
    const nodes = Object.fromEntries(['question-form', 'connection', 'consent', 'prompt', 'send', 'cancel', 'new', 'status', 'turns'].map(k => [k, new Element()]));
    panel.querySelector = query => nodes[query.match(/\[data-([^\]]+)\]/)[1]];
    nodes.connection.value = 'selected'; nodes.consent.checked = true;
    const calls = []; let pendingAsk, pendingBegin, answers = 0, expiry;
    const fetch = async (url, options) => {
        calls.push({ url, body: options?.body && JSON.parse(options.body) });
        assert.match(url, /^\/agent\//); // Text cannot trigger image loading, links or arbitrary network requests.
        if (url === '/agent/context') return response({ context: 'context' });
        if (url === '/agent/begin') return pendingBegin ? pendingBegin.promise : response({ conversation: 'conversation', expiresInSeconds: 1800 });
        if (url === '/agent/ask') return pendingAsk ? pendingAsk.promise : response(completed('<script>untrusted()</script>', ++answers, answers > 1));
        return { status: 204, ok: true };
    };
    const context = { document: { querySelectorAll: () => [panel], createElement: () => new Element(), addEventListener() {} },
        window: { addEventListener() {} }, fetch, AbortController, setInterval() {}, clearInterval() {}, setTimeout(fn) { expiry = fn; return 1; }, clearTimeout() { expiry = null; } };
    vm.runInNewContext(fs.readFileSync('src/TenantPlatform.Web/wwwroot/js/read-agent.js', 'utf8'), context);
    const submit = async question => { nodes.prompt.value = question; return nodes['question-form'].listeners.submit({ preventDefault() {} }); };
    await submit('first question'); await submit('follow-up');
    assert.equal(nodes.turns.children.length, 2);
    assert.match(nodes.turns.text(), /first question/); assert.match(nodes.turns.text(), /follow-up/);
    assert.match(nodes.turns.text(), /<script>untrusted\(\)<\/script>/); assert.match(nodes.turns.text(), /REVALIDATED/);
    pendingAsk = deferred(); const cancelled = submit('cancelled question'); await tick();
    await nodes.cancel.listeners.click(); pendingAsk.resolve(response(completed('LATE ANSWER', 3))); await cancelled;
    assert.equal(nodes.turns.children.length, 2); assert.doesNotMatch(nodes.turns.text(), /LATE ANSWER|cancelled question/);
    assert(calls.some(c => c.url === '/agent/cancel')); assert(!calls.some(c => c.url === '/agent/delete'));
    await nodes.new.listeners.click(); assert.equal(nodes.turns.children.length, 0);
    assert(calls.some(c => c.url === '/agent/delete'));
    pendingAsk = null; pendingBegin = deferred(); const staleBegin = submit('stale start'); await tick();
    await nodes.new.listeners.click(); pendingBegin.resolve(response({ conversation: 'late-conversation' })); await staleBegin; await tick();
    assert.equal(nodes.turns.children.length, 0);
    assert(calls.some(c => c.url === '/agent/delete' && c.body.conversation === 'late-conversation'));
    pendingBegin = null; await submit('will expire'); assert.equal(nodes.turns.children.length, 1);
    const expire = expiry; expire(); await tick();
    assert.equal(nodes.turns.children.length, 0); assert.equal(nodes.status.textContent, 'expired');
});

test('chat draft uses shared real preview, explicit revision-bound save and clears stale or late data', async () => {
    const panel = new Element(); panel.dataset = { account: 'account', context: 'context', csrf: 'csrf', draftTitle: 'DRAFT', draftSave: 'SAVE', draftOpen: 'OPEN', draftOld: 'UNCHANGED', draftSnapshot: 'SNAPSHOT', total: 'TOTAL', date: 'PERIOD' };
    const nodes = Object.fromEntries(['question-form', 'connection', 'consent', 'prompt', 'send', 'cancel', 'new', 'status', 'turns', 'report-draft'].map(k => [k, new Element()]));
    panel.querySelector = query => nodes[query.match(/\[data-([^\]]+)\]/)[1]]; nodes.connection.value = 'selected'; nodes.consent.checked = true;
    const preview = { retrievedUtc: 'NOW', selection: { filter: { endDateFrom: '2026-10-19', endDateTo: '2027-01-19' } }, aggregate: { totalCount: 27, groups: [{ name: '<script>not-code</script>', count: 27 }] }, table: { page: 1, items: [], totalCount: 27, returnedCount: 0, hasMore: true, nextPage: 2 } };
    const report = () => ({ id: 'draft-id', revision: 'revision-2', title: '<img src=unsafe>', sourceName: 'SOURCE', components: ['Metric', 'BuildingChart', 'AgreementTable'], summary: { period: 'Next 3 months', status: 'Active', type: 'All', archive: 'Exclude', building: 'All', search: '' }, preview });
    const calls = []; let failAsk = false, pending, stamp = 'context';
    const fetch = async (url, options) => {
        calls.push({ url, body: options?.body && JSON.parse(options.body) });
        if (url.endsWith('/context')) return response({ context: stamp });
        if (url.endsWith('/begin')) return response({ conversation: 'conversation', expiresInSeconds: 1800 });
        if (url.endsWith('/ask')) { if (pending) return pending.promise; if (failAsk) return { status: 400, ok: false, json: async () => ({ error: 'INVALID CHANGE' }) }; return response({ ...completed('', 1), report: report() }); }
        if (url.endsWith('/report-save')) return response({ pageId: 'stable-page', url: 'https://untrusted.invalid/ignored' });
        if (url.endsWith('/report-preview')) return response({ report: report() });
        return { status: 204, ok: true };
    };
    const context = { document: { querySelectorAll: () => [panel], createElement: () => new Element(), addEventListener() {} }, window: { addEventListener() {} }, fetch, AbortController, setInterval() {}, clearInterval() {}, setTimeout() {}, clearTimeout() {} };
    vm.runInNewContext(fs.readFileSync('src/TenantPlatform.Web/wwwroot/js/report-renderer.js', 'utf8') + '\n' + fs.readFileSync('src/TenantPlatform.Web/wwwroot/js/read-agent.js', 'utf8'), context);
    const submit = async () => { nodes.prompt.value = 'Lag en side'; await nodes['question-form'].listeners.submit({ preventDefault() {} }); };
    await submit(); const root = nodes['report-draft']; assert.match(root.text(), /TOTAL 27/); assert.match(root.text(), /<script>not-code<\/script>/);
    assert(!calls.some(c => c.url.endsWith('/report-save')));
    const findSave = () => root.children.find(c => c.textContent === 'SAVE');
    assert.equal(findSave().disabled, false);
    await findSave().listeners.click();
    const write = calls.find(c => c.url.endsWith('/report-save')); assert.deepEqual(write.body, { conversation: 'conversation', draft: 'draft-id', revision: 'revision-2', page: 1 });
    assert(!('definition' in write.body)); assert.equal(root.children.find(c => c.textContent === 'OPEN').href, '/my-pages/stable-page');
    await nodes.new.listeners.click(); await submit(); failAsk = true; await submit();
    assert.match(root.text(), /UNCHANGED/); assert.doesNotMatch(root.text(), /TOTAL 27/); assert.equal(findSave().disabled, true);
    failAsk = false; pending = deferred(); const late = submit(); await tick(); await nodes.new.listeners.click(); pending.resolve(response({ ...completed('', 1), report: report() })); await late;
    assert.equal(root.children.length, 0);
    pending = null; await submit(); stamp = 'another-account';
    await findSave().listeners.click(); assert.equal(root.children.length, 0);
    assert.equal(calls.filter(c => c.url.endsWith('/report-save')).length, 1);
});
