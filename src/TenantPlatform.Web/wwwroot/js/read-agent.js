// Same-origin browser requests only. Model text is always textContent; no markdown/HTML/image loading.
(() => {
    function mount() {
        for (const panel of document.querySelectorAll('[data-read-agent]')) {
            if (panel.dataset.mounted || !panel.querySelector('[data-question-form]')) continue;
            panel.dataset.mounted = 'true';
            const get = name => panel.querySelector(`[data-${name}]`);
            let conversation = null, request = null, generation = 0;
            const clear = () => { get('answer').textContent = ''; get('sources').replaceChildren(); get('notices').replaceChildren(); };
            const busy = value => {
                get('send').disabled = value || !get('consent').checked || !get('connection').value; get('cancel').disabled = !value;
                get('connection').disabled = value; get('consent').disabled = value;
            };
            async function post(action, body, signal) {
                const response = await fetch(`/agent/${action}`, { method: 'POST', credentials: 'same-origin', redirect: 'error',
                    headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': panel.dataset.csrf }, body: JSON.stringify(body), signal });
                if (response.status === 204) return {};
                let value; try { value = await response.json(); } catch { throw new Error(panel.dataset.failure); }
                if (!response.ok) { const error = new Error(value.error || panel.dataset.failure); error.accessChanged = response.status === 401 || response.status === 403; throw error; }
                return value;
            }
            async function reset(changed = false) {
                generation++; const previous = conversation; conversation = null;
                request?.abort(); request = null; clear(); get('prompt').value = ''; busy(false);
                get('status').textContent = changed ? panel.dataset.changed : '';
                if (changed) { get('send').disabled = true; get('connection').disabled = true; get('consent').checked = false; }
                if (previous) { try { await post('cancel', { conversation: previous }); } catch { /* Context may already have changed. */ } }
            }
            async function checkContext() {
                if (!panel.isConnected) { request?.abort(); return; }
                try {
                    const response = await fetch('/agent/context', { credentials: 'same-origin', cache: 'no-store', redirect: 'error' });
                    const value = response.ok ? await response.json() : null;
                    if (!value || value.context !== panel.dataset.context) await reset(true);
                } catch { await reset(true); }
            }
            get('question-form').addEventListener('submit', async event => {
                event.preventDefault();
                if (!get('connection').value || !get('consent').checked || request) return;
                clear(); busy(true); get('status').textContent = panel.dataset.running;
                request = new AbortController(); const signal = request.signal; const version = ++generation;
                try {
                    // Explicit selection changes always create a fresh server-bound conversation.
                    if (conversation && panel.dataset.selected !== get('connection').value) {
                        await post('cancel', { conversation }, signal); conversation = null;
                    }
                    if (!conversation) {
                        const started = await post('begin', { connection: get('connection').value, account: panel.dataset.account, sharingAccepted: true }, signal);
                        conversation = started.conversation; panel.dataset.selected = get('connection').value;
                    }
                    const result = await post('ask', { conversation, prompt: get('prompt').value }, signal);
                    if (generation !== version) return;
                    get('answer').textContent = result.text;
                    for (const notice of result.notices) { const li = document.createElement('li'); li.textContent = notice; get('notices').append(li); }
                    for (const source of result.sources) {
                        const li = document.createElement('li'); li.textContent = `${source.connectionName} · ${source.tool} · ${source.version}${source.summary ? " — " + source.summary : ""}`; get('sources').append(li);
                    }
                    get('status').textContent = panel.dataset.done;
                } catch (error) { if (generation === version) { if (error.accessChanged) await reset(true); else get('status').textContent = signal.aborted ? panel.dataset.cancelled : error.message; } }
                finally { if (generation === version) { request = null; busy(false); } }
            });
            get('cancel').addEventListener('click', async () => { await reset(); get('status').textContent = panel.dataset.cancelled; });
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
