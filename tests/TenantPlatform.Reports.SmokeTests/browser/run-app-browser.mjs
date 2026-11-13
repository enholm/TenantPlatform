import { spawn } from "node:child_process";
import { mkdtemp, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";

const origin = process.env.REPORT_APP_ORIGIN;
const email = process.env.REPORT_BROWSER_EMAIL;
const password = process.env.REPORT_BROWSER_PASSWORD;
const existingPage = process.env.REPORT_BROWSER_PAGE_ID;
const width = Number(process.env.REPORT_BROWSER_WIDTH ?? 1440);
const height = Number(process.env.REPORT_BROWSER_HEIGHT ?? 1000);
if (!origin || !email || !password || !existingPage)
    throw new Error("REPORT_APP_ORIGIN, REPORT_BROWSER_EMAIL, REPORT_BROWSER_PASSWORD and REPORT_BROWSER_PAGE_ID are required.");

const chrome = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
const artifacts = await mkdtemp(join(tmpdir(), "tenantplatform-report-app-browser-"));
const records = [];
const child = spawn(chrome, [
    "--headless=new", "--no-first-run", "--no-default-browser-check", "--disable-background-networking",
    "--remote-debugging-pipe", `--window-size=${width},${height}`, `--user-data-dir=${join(artifacts, "profile")}`,
    "about:blank"
], { stdio: ["ignore", "pipe", "pipe", "pipe", "pipe"] });

let nextId = 1;
let buffer = "";
const pending = new Map();
child.stdio[4].setEncoding("utf8");
child.stdio[4].on("data", chunk => {
    buffer += chunk;
    for (;;) {
        const end = buffer.indexOf("\0");
        if (end < 0) break;
        const raw = buffer.slice(0, end); buffer = buffer.slice(end + 1);
        if (!raw) continue;
        const message = JSON.parse(raw);
        if (message.id && pending.has(message.id)) {
            const { resolve, reject } = pending.get(message.id); pending.delete(message.id);
            message.error ? reject(new Error(JSON.stringify(message.error))) : resolve(message.result);
        }
    }
});

function command(method, params = {}, sessionId) {
    return new Promise((resolve, reject) => {
        const id = nextId++;
        pending.set(id, { resolve, reject });
        child.stdio[3].write(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }) + "\0");
    });
}
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function evaluate(expression) {
    const result = await command("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true }, session);
    if (result.exceptionDetails) throw new Error(result.exceptionDetails.text);
    return result.result.value;
}
async function waitFor(expression, message, timeout = 15000) {
    const end = Date.now() + timeout;
    while (Date.now() < end) {
        try { if (await evaluate(expression)) { pass(message); return; } } catch { }
        await delay(100);
    }
    const body = await evaluate("document.body?.innerText.slice(0, 1200)");
    const chart = await evaluate("document.querySelector('.report-chart')?.outerHTML.slice(0, 1200)");
    const globals = await evaluate("({echarts:typeof echarts,blazor:typeof Blazor,error:document.querySelector('#blazor-error-ui')?.innerText})");
    const scripts = await evaluate("[...document.scripts].map(x => x.src)");
    const resources = await evaluate("performance.getEntriesByType('resource').filter(x=>x.name.includes('echarts')||x.name.includes('blazor.web')).map(x=>({name:x.name,duration:x.duration,size:x.transferSize}))");
    throw new Error(`Timed out: ${message}; url=${await evaluate("location.href")}; globals=${JSON.stringify(globals)}; scripts=${JSON.stringify(scripts)}; resources=${JSON.stringify(resources)}; chart=${chart}; body=${body}`);
}
async function navigate(path) {
    await command("Page.navigate", { url: new URL(path, origin).href }, session);
    await waitFor("document.readyState === 'complete'", `loaded ${path}`);
}
function pass(message) { records.push({ passed: true, message }); console.log(`PASS: ${message}`); }

let session;
try {
    const { targetId } = await command("Target.createTarget", { url: "about:blank" });
    ({ sessionId: session } = await command("Target.attachToTarget", { targetId, flatten: true }));
    await command("Page.enable", {}, session); await command("Runtime.enable", {}, session);
    await navigate("/login");
    await waitFor("!!document.querySelector('#email') && !!document.querySelector('#password')", "login form rendered");
    await evaluate(`(() => {
        const set = (selector, value) => { const input = document.querySelector(selector); input.value = value; input.dispatchEvent(new Event('input', {bubbles:true})); };
        set('#email', ${JSON.stringify(email)}); set('#password', ${JSON.stringify(password)});
        document.querySelector('form').requestSubmit(); return true;
    })()`);
    await waitFor("location.pathname !== '/login'", "synthetic account authenticated through the real login endpoint");

    await navigate("/reports");
    await waitFor("document.querySelector('h1')?.textContent.trim().length > 0 && !document.body.innerText.includes('Laster')", "reports overview rendered through InteractiveServer");
    pass("stable /reports route is directly accessible after login");

    await navigate(`/reports/${existingPage}`);
    await waitFor("[...document.querySelectorAll('button')].some(x => /rediger|edit/i.test(x.textContent))", "report view controls rendered");
    await evaluate(`[...document.querySelectorAll('button')].find(x => /rediger|edit/i.test(x.textContent))?.click()`);
    await waitFor("!!document.querySelector('[id^=workspace-name-]')", "InteractiveServer handled a report edit click");
    await evaluate(`document.querySelector('.sticky-actions .btn-outline-secondary')?.click()`);
    await waitFor("!document.querySelector('[id^=workspace-name-]')", "InteractiveServer handled cancel without persistence");
    await waitFor("!!document.querySelector('.report-chart canvas') || !!document.querySelector('canvas')", "saved report rendered a real ECharts canvas", 8000);
    const fallback = await evaluate("!!document.querySelector('table')");
    if (!fallback) throw new Error("Accessible table fallback was not rendered with the chart.");
    pass("saved PostgreSQL report renders its accessible table alternative");
    const directUrl = await evaluate("location.pathname");
    await command("Page.reload", {}, session);
    await waitFor("!!document.querySelector('h1') && !document.body.innerText.includes('Laster')", "direct report URL survives refresh", 30000);
    if (await evaluate("location.pathname") !== directUrl) throw new Error("Direct URL changed after refresh.");
    pass("bookmarkable report GUID remains stable across refresh");

    await navigate("/reports");
    await evaluate(`[...document.querySelectorAll('button')].find(x => /opprett|create/i.test(x.textContent))?.click()`);
    await waitFor("!!document.querySelector('#report-page-name')", "create-page dialog opened");
    const name = `Browser verification ${Date.now()}`;
    await evaluate(`(() => { const x=document.querySelector('#report-page-name'); x.value=${JSON.stringify(name)}; x.dispatchEvent(new Event('change',{bubbles:true})); return true; })()`);
    await evaluate(`document.querySelector('.modal-footer .btn-primary')?.click()`);
    await waitFor("location.pathname.startsWith('/reports/') && !!document.querySelector('h1')", "report page created through Blazor", 30000);
    const createdUrl = await evaluate("location.pathname");
    pass("new report page received a stable GUID URL");

    await evaluate(`[...document.querySelectorAll('button')].find(x => /rediger|edit/i.test(x.textContent))?.click()`);
    await waitFor("!!document.querySelector('[id^=workspace-name-]')", "edit mode opened from view mode");
    await evaluate(`(() => { const x=document.querySelector('[id^=workspace-name-]'); x.value='Unsaved browser name'; x.dispatchEvent(new Event('input',{bubbles:true})); return true; })()`);
    await evaluate(`document.querySelector('.sticky-actions .btn-outline-secondary')?.click()`);
    await waitFor("!document.querySelector('[id^=workspace-name-]')", "cancel returned to view mode");
    if ((await evaluate("document.querySelector('h1')?.textContent.trim()")) !== name) throw new Error("Cancel did not restore the saved page name.");
    pass("Cancel discards an unsaved workspace rename");

    await evaluate(`[...document.querySelectorAll('button')].find(x => /rediger|edit/i.test(x.textContent))?.click()`);
    await waitFor("!!document.querySelector('[id^=workspace-name-]')", "edit mode reopened");
    const renamed = `${name} renamed`;
    await evaluate(`(() => { const x=document.querySelector('[id^=workspace-name-]'); x.value=${JSON.stringify(renamed)}; x.dispatchEvent(new Event('input',{bubbles:true})); return true; })()`);
    await waitFor("document.querySelector('.sticky-actions .btn-primary')?.disabled === false", "workspace save enabled after rename");
    await evaluate(`document.querySelector('.sticky-actions .btn-primary')?.click()`);
    await waitFor("!document.querySelector('[id^=workspace-name-]') && document.querySelector('h1')?.textContent.includes('renamed')", "workspace rename saved", 30000);
    if (await evaluate("location.pathname") !== createdUrl) throw new Error("Rename changed the stable page URL.");
    pass("saved rename preserves the report GUID URL");

    const title = await evaluate("document.querySelector('h1')?.textContent.trim()");
    const viewport = await evaluate("({width:innerWidth,height:innerHeight,language:document.documentElement.lang,title:document.title})");
    await writeFile(join(artifacts, "result.json"), JSON.stringify({ status: "passed", records, createdUrl, title, viewport }, null, 2));
    console.log(`Actual app browser checks passed (${records.length} checks).`);
    console.log(`Artifacts: ${artifacts}`);
} finally {
    child.kill("SIGTERM");
}
