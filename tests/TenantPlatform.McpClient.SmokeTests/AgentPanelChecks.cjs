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
