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
    const nodes = Object.fromEntries(['question-form', 'connection', 'area', 'consent', 'prompt', 'send', 'cancel', 'new', 'status', 'turns'].map(k => [k, new Element()]));
    panel.querySelector = query => nodes[query.match(/\[data-([^\]]+)\]/)[1]];
    nodes.connection.value = 'selected'; nodes.consent.checked = true; panel.dataset.areas = JSON.stringify({ selected: ['Agreements', 'ServiceRequests'] }); panel.dataset.editArea = 'Agreements';
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
        window: { addEventListener() {}, confirm() { return true; } }, fetch, AbortController, setInterval() {}, clearInterval() {}, setTimeout(fn) { expiry = fn; return 1; }, clearTimeout() { expiry = null; } };
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
    const nodes = Object.fromEntries(['question-form', 'connection', 'area', 'consent', 'prompt', 'send', 'cancel', 'new', 'status', 'turns', 'report-draft'].map(k => [k, new Element()]));
    panel.querySelector = query => nodes[query.match(/\[data-([^\]]+)\]/)[1]]; nodes.connection.value = 'selected'; nodes.consent.checked = true; panel.dataset.areas = JSON.stringify({ selected: ['Agreements', 'ServiceRequests'] }); panel.dataset.editArea = 'Agreements';
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
    const context = { document: { querySelectorAll: () => [panel], createElement: () => new Element(), addEventListener() {} }, window: { addEventListener() {}, confirm() { return true; } }, fetch, AbortController, setInterval() {}, clearInterval() {}, setTimeout() {}, clearTimeout() {} };
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

test('saved-page editing uses bound actions, preserves conflicts and requires explicit reload confirmation', async () => {
    const panel = new Element(); panel.dataset = { account: 'account', context: 'context', csrf: 'csrf', editPage: 'original-page', editConnection: 'own-source',
        editing: 'EDITING', editSave: 'UPDATE', editCopy: 'COPY', editDiscard: 'DISCARD', editUpdated: 'UPDATED ORIGINAL', editCopied: 'SAVED COPY', editReload: 'RELOAD', editReloadConfirm: 'Discard draft and reload?',
        editChanges: 'CHANGES', editNoChanges: 'UNCHANGED', draftTitle: 'DRAFT', draftSave: 'SAVE', draftOpen: 'OPEN', draftOld: 'STALE', date: 'REFRESH', total: 'TOTAL' };
    const nodes = Object.fromEntries(['question-form', 'connection', 'area', 'consent', 'prompt', 'send', 'cancel', 'new', 'status', 'turns', 'report-draft', 'edit-start', 'edit-intro'].map(k => [k, new Element()]));
    panel.querySelector = query => nodes[query.match(/\[data-([^\]]+)\]/)[1]];
    nodes.connection.value = 'own-source'; nodes.consent.checked = true; panel.dataset.areas = JSON.stringify({ 'own-source': ['Agreements', 'ServiceRequests'] }); panel.dataset.editArea = 'Agreements';
    const report = (changed = false) => ({ id: 'draft', revision: changed ? 'r2' : 'r1', editingPageId: 'original-page', originalTitle: '<script>old</script>',
        title: changed ? 'New title' : '<script>old</script>', sourceName: 'SOURCE', components: ['Metric'],
        changes: changed ? [{ field: 'PERIOD', before: '3 months', after: '6 months' }] : [], summary: { period: 'period' },
        preview: { selection: { filter: { endDateFrom: '2026-10-24', endDateTo: '2027-04-24' } }, retrievedUtc: 'NOW', aggregate: { totalCount: 42, groups: [] }, table: { items: [] } } });
    const calls = []; let stamp = 'context', confirm = false, failReload = false, pendingReload, failUpdate = true, sequence = 0;
    const fetch = async (url, options) => {
        const body = options?.body && JSON.parse(options.body); calls.push({ url, body });
        if (url.endsWith('/context')) return response({ context: stamp });
        if (url.endsWith('/report-edit')) return response({ conversation: 'edit-' + ++sequence, expiresInSeconds: 1800, report: report() });
        if (url.endsWith('/ask')) return response({ ...completed('', 1), report: report(true) });
        if (url.endsWith('/report-update')) return failUpdate ? { status: 409, ok: false, json: async () => ({ error: 'CONFLICT' }) } : response({ pageId: 'original-page' });
        if (url.endsWith('/report-copy')) return response({ pageId: 'new-page', url: 'https://untrusted.invalid' });
        if (url.endsWith('/report-reload')) {
            if (pendingReload) return pendingReload.promise;
            if (failReload) return { status: 400, ok: false, json: async () => ({ error: 'RELOAD FAILED' }) };
            return response({ conversation: 'reloaded', expiresInSeconds: 1800, report: report() });
        }
        return { status: 204, ok: true };
    };
    const context = { document: { querySelectorAll: () => [panel], createElement: () => new Element(), addEventListener() {} },
        window: { addEventListener() {}, confirm(message) { assert.equal(message, 'Discard draft and reload?'); return confirm; } },
        fetch, AbortController, setInterval() {}, clearInterval() {}, setTimeout() {}, clearTimeout() {} };
    vm.runInNewContext(fs.readFileSync('src/TenantPlatform.Web/wwwroot/js/report-renderer.js', 'utf8') + '\n' + fs.readFileSync('src/TenantPlatform.Web/wwwroot/js/read-agent.js', 'utf8'), context);
    const root = nodes['report-draft']; const button = label => root.children.find(n => n.textContent === label);
    await nodes['edit-start'].listeners.click();
    assert.equal(calls.find(c => c.url.endsWith('/report-edit')).body.pageId, 'original-page');
    assert.match(root.text(), /EDITING: <script>old<\/script>/); assert.match(root.text(), /UNCHANGED/); assert.equal(nodes.connection.disabled, true);
    nodes.prompt.value = 'Vis seks måneder'; await nodes['question-form'].listeners.submit({ preventDefault() {} });
    assert.match(root.text(), /PERIOD: 3 months → 6 months/); assert(!calls.some(c => c.url.endsWith('/report-update')));
    await button('UPDATE').listeners.click(); assert.match(nodes.status.textContent, /CONFLICT/);
    assert.match(root.text(), /New title/); assert.doesNotMatch(root.text(), /TOTAL 42/); assert.equal(button('UPDATE').disabled, true);
    assert.deepEqual(calls.find(c => c.url.endsWith('/report-update')).body, { conversation: 'edit-1', draft: 'draft', revision: 'r2', page: 1 });
    await button('RELOAD').listeners.click(); assert(!calls.some(c => c.url.endsWith('/report-reload')));
    confirm = true; failReload = true; await button('RELOAD').listeners.click(); assert.match(root.text(), /New title/); assert.match(nodes.status.textContent, /RELOAD FAILED/);
    failReload = false; await button('RELOAD').listeners.click(); assert.match(root.text(), /UNCHANGED/); assert.doesNotMatch(root.text(), /New title/);
    await button('COPY').listeners.click();
    assert.equal(calls.find(c => c.url.endsWith('/report-copy')).body.conversation, 'reloaded');
    assert.equal(root.children.find(c => c.textContent === 'OPEN').href, '/my-pages/new-page');
    assert.equal(nodes.status.textContent, 'SAVED COPY');
    await nodes['edit-start'].listeners.click(); failUpdate = false; await button('UPDATE').listeners.click();
    assert.equal(nodes.status.textContent, 'UPDATED ORIGINAL'); assert.equal(root.children.find(c => c.textContent === 'OPEN').href, '/my-pages/original-page');
    await nodes['edit-start'].listeners.click(); await button('DISCARD').listeners.click(); assert.equal(root.children.length, 0);
    assert(calls.some(c => c.url.endsWith('/report-discard')));
    await nodes['edit-start'].listeners.click(); pendingReload = deferred(); const late = button('RELOAD').listeners.click(); await tick();
    await nodes.new.listeners.click(); pendingReload.resolve(response({ conversation: 'late-context', expiresInSeconds: 1800, report: report(true) })); await late; await tick();
    assert.equal(root.children.length, 0); assert(calls.some(c => c.url.endsWith('/delete') && c.body.conversation === 'late-context'));
    await nodes['edit-start'].listeners.click(); stamp = 'different-user'; failUpdate = false; const writes = calls.filter(c => c.url.endsWith('/report-update')).length;
    await button('UPDATE').listeners.click(); assert.equal(root.children.length, 0); assert.equal(nodes['edit-intro'].children.length, 0);
    assert.equal(calls.filter(c => c.url.endsWith('/report-update')).length, writes);
});

test('area switch warns for drafts, cancels active work and rejects late ask, begin and switched-context replies', async () => {
    const panel = new Element();
    panel.dataset = { account: 'account', context: 'identity', csrf: 'csrf',
        areas: JSON.stringify({ source: ['Agreements', 'AgreementDetails'] }), areaConfirm: 'DISCARD CONTEXT?', areaPlaceholder: 'AREA',
        areaAgreements: 'AGREEMENTS', areaAgreementDetails: 'REQUESTS', pendingHistory: 'pending', draftTitle: 'DRAFT', changed: 'CHANGED' };
    const nodes = Object.fromEntries(['question-form', 'connection', 'area', 'consent', 'prompt', 'send', 'cancel', 'new', 'status', 'turns', 'report-draft'].map(k => [k, new Element()]));
    panel.querySelector = q => nodes[q.match(/\[data-([^\]]+)\]/)[1]];
    nodes.connection.value = 'source'; nodes.consent.checked = true;
    let confirmed = false, confirms = 0, blockedAsk, blockedBegin, blockedSwitch, identity = 'identity', asks = 0;
    const calls = [];
    const draft = { id: 'draft', revision: 'r1', title: 'UNSAVED PRIVATE DRAFT', sourceName: 'source', summary: {}, components: [], preview: {} };
    const fetch = async (url, options) => {
        const body = options?.body && JSON.parse(options.body); calls.push({ url, body, signal: options?.signal });
        if (url === '/agent/context') return response({ context: identity });
        if (url === '/agent/begin') return blockedBegin ? blockedBegin.promise : response({ conversation: 'old', area: body.area, expiresInSeconds: 1800 });
        if (url === '/agent/switch-area') return blockedSwitch ? blockedSwitch.promise : response({ conversation: 'new', area: body.area, expiresInSeconds: 1800 });
        if (url === '/agent/ask') return blockedAsk ? blockedAsk.promise : response({ ...completed('ANSWER', 1), report: asks++ === 0 ? draft : null });
        return { status: 204, ok: true };
    };
    const context = { document: { querySelectorAll: () => [panel], createElement: () => new Element(), addEventListener() {} },
        window: { addEventListener() {}, confirm(message) { assert.equal(message, 'DISCARD CONTEXT?'); confirms++; return confirmed; }, TenantPlatformReports: { render() {} } },
        fetch, AbortController, setInterval() {}, clearInterval() {}, setTimeout() { return 1; }, clearTimeout() {} };
    vm.runInNewContext(fs.readFileSync('src/TenantPlatform.Web/wwwroot/js/read-agent.js', 'utf8'), context);
    const submit = () => nodes['question-form'].listeners.submit({ preventDefault() {} });
    assert.equal(nodes.area.value, ''); assert.equal(nodes.send.disabled, true);
    nodes.area.value = 'Agreements'; await nodes.area.listeners.change();
    await submit(); assert.match(nodes['report-draft'].text(), /UNSAVED PRIVATE DRAFT/);
    nodes.area.value = 'AgreementDetails'; await nodes.area.listeners.change();
    assert.equal(nodes.area.value, 'Agreements'); assert.match(nodes['report-draft'].text(), /UNSAVED PRIVATE DRAFT/);
    blockedAsk = deferred(); const active = submit(); await tick();
    const oldSignal = calls.filter(c => c.url === '/agent/ask').at(-1).signal;
    confirmed = true; blockedSwitch = deferred(); nodes.area.value = 'AgreementDetails'; const switched = nodes.area.listeners.change(); await tick();
    assert.equal(oldSignal.aborted, true); assert.equal(nodes['report-draft'].text(), ''); assert.equal(nodes.turns.text(), '');
    blockedAsk.resolve(response(completed('LATE SECRET', 9))); await active; blockedAsk = null;
    blockedSwitch.resolve(response({ conversation: 'new', area: 'AgreementDetails', expiresInSeconds: 1800 })); await switched; blockedSwitch = null;
    await submit(); assert.equal(calls.filter(c => c.url === '/agent/ask').at(-1).body.conversation, 'new');
    assert.doesNotMatch(nodes.turns.text(), /LATE SECRET|UNSAVED PRIVATE/);
    await nodes.new.listeners.click(); blockedBegin = deferred(); const beginning = submit(); await tick();
    nodes.area.value = 'Agreements'; await nodes.area.listeners.change();
    blockedBegin.resolve(response({ conversation: 'late-begin', area: 'AgreementDetails', expiresInSeconds: 1800 })); await beginning; blockedBegin = null;
    assert(calls.some(c => c.url === '/agent/delete' && c.body.conversation === 'late-begin'));
    assert(!calls.some(c => c.url === '/agent/ask' && c.body.conversation === 'late-begin'));
    await submit(); blockedSwitch = deferred(); nodes.area.value = 'AgreementDetails'; const changing = nodes.area.listeners.change(); await tick();
    identity = 'different-user-or-account'; blockedSwitch.resolve(response({ conversation: 'late-switch', area: 'AgreementDetails', expiresInSeconds: 1800 })); await changing;
    assert.equal(nodes.turns.text(), ''); assert.equal(nodes.send.disabled, true);
    assert(calls.some(c => c.url === '/agent/delete' && c.body.conversation === 'late-switch'));
    assert(confirms >= 4);
});
