import { createServer } from "node:http";
import { spawn } from "node:child_process";
import { mkdtemp, readFile, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, extname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const browserDirectory = dirname(fileURLToPath(import.meta.url));
const root = resolve(browserDirectory, "../../..");
const chrome = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
const artifacts = await mkdtemp(join(tmpdir(), "tenantplatform-report-browser-"));

function execute(command, args, options = {}) {
    return new Promise((resolvePromise, reject) => {
        const child = spawn(command, args, { cwd: root, ...options });
        let stdout = "";
        let stderr = "";
        child.stdout?.on("data", chunk => stdout += chunk);
        child.stderr?.on("data", chunk => stderr += chunk);
        child.on("error", reject);
        child.on("close", code => {
            if (code === 0) resolvePromise({ stdout, stderr });
            else reject(new Error(`${command} exited ${code}\n${stdout}\n${stderr}`));
        });
    });
}

const fixtureRun = await execute("dotnet", ["run", "--project",
    "tests/TenantPlatform.Reports.SmokeTests/TenantPlatform.Reports.SmokeTests.csproj",
    "--no-restore", "--", "--browser-fixtures"]);
const fixtureLine = fixtureRun.stdout.split(/\r?\n/).reverse().find(line => line.startsWith("{"));
if (!fixtureLine) throw new Error(`Fixture JSON was not produced.\n${fixtureRun.stdout}\n${fixtureRun.stderr}`);
const fixture = JSON.parse(fixtureLine);
let receiveCompletion = null;

const routes = new Map([
    ["/", join(browserDirectory, "index.html")],
    ["/browser-harness.js", join(browserDirectory, "browser-harness.js")],
    ["/bootstrap.css", join(root, "src/TenantPlatform.Web/wwwroot/bootstrap/bootstrap.min.css")],
    ["/app.css", join(root, "src/TenantPlatform.Web/wwwroot/app.css")],
    ["/report-chart.css", join(root, "src/TenantPlatform.Web/Components/Pages/Reports/ReportChart.razor.css")],
    ["/report-grid.css", join(root, "src/TenantPlatform.Web/Components/Pages/Reports/ReportGrid.razor.css")],
    ["/js/report-charts.js", join(root, "src/TenantPlatform.Web/wwwroot/js/report-charts.js")],
    ["/js/report-grid.js", join(root, "src/TenantPlatform.Web/wwwroot/js/report-grid.js")],
    ["/lib/echarts/echarts.min.js", join(root, "src/TenantPlatform.Web/wwwroot/lib/echarts/echarts.min.js")]
]);
const mime = new Map([[".html", "text/html; charset=utf-8"], [".js", "text/javascript; charset=utf-8"], [".css", "text/css; charset=utf-8"]]);
const server = createServer(async (request, response) => {
    try {
        const path = new URL(request.url, "http://127.0.0.1").pathname;
        if (path === "/complete" && request.method === "POST") {
            let body = "";
            for await (const chunk of request) body += chunk;
            const result = JSON.parse(body);
            receiveCompletion?.(result);
            response.writeHead(204);
            response.end();
            return;
        }
        if (path === "/fixture.json") {
            response.writeHead(200, { "content-type": "application/json; charset=utf-8", "cache-control": "no-store" });
            response.end(JSON.stringify(fixture));
            return;
        }
        const file = routes.get(path);
        if (!file) {
            response.writeHead(404);
            response.end("Not found");
            return;
        }
        response.writeHead(200, { "content-type": mime.get(extname(file)) ?? "application/octet-stream", "cache-control": "no-store" });
        response.end(await readFile(file));
    } catch (error) {
        response.writeHead(500);
        response.end(String(error));
    }
});

await new Promise((resolvePromise, reject) => {
    server.once("error", reject);
    server.listen(0, "127.0.0.1", resolvePromise);
});
const address = server.address();
const origin = `http://127.0.0.1:${address.port}`;

try {
    const version = (await execute(chrome, ["--version"])).stdout.trim();
    const runs = [
        { name: "desktop", width: 1440, height: 1000 },
        { name: "mobile", width: 390, height: 844 }
    ];
    for (const run of runs) {
        const profile = join(artifacts, `${run.name}-profile`);
        const url = `${origin}/?viewport=${run.name}`;
        let browserStdout = "";
        let browserStderr = "";
        const browser = spawn(chrome, [
            "--headless=new", "--no-first-run", "--no-default-browser-check", "--disable-background-networking",
            "--force-device-scale-factor=1", "--force-prefers-reduced-motion=reduce", "--hide-scrollbars",
            "--run-all-compositor-stages-before-draw",
            `--user-data-dir=${profile}`, `--window-size=${run.width},${run.height}`,
            "--remote-debugging-port=0", url
        ], { cwd: root });
        browser.stdout.on("data", chunk => browserStdout += chunk);
        browser.stderr.on("data", chunk => browserStderr += chunk);
        const result = await new Promise((resolvePromise, reject) => {
            const timeout = setTimeout(() => reject(new Error(
                `${run.name} browser harness timed out.\n${browserStdout}\n${browserStderr}`)), 30000);
            receiveCompletion = value => {
                if (value.viewport !== run.name) return;
                clearTimeout(timeout);
                resolvePromise(value);
            };
            browser.once("error", error => {
                clearTimeout(timeout);
                reject(error);
            });
            browser.once("exit", code => {
                if (receiveCompletion) {
                    clearTimeout(timeout);
                    reject(new Error(`${run.name} Chrome exited ${code} before completion.\n${browserStdout}\n${browserStderr}`));
                }
            });
        });
        receiveCompletion = null;
        browser.kill("SIGTERM");
        await new Promise(resolvePromise => {
            const timeout = setTimeout(resolvePromise, 2000);
            browser.once("exit", () => { clearTimeout(timeout); resolvePromise(); });
        });
        await writeFile(join(artifacts, `${run.name}.json`), JSON.stringify(result, null, 2));
        if (result.status !== "passed") {
            const failures = result.records.filter(x => !x.passed).map(x => `FAIL: ${x.message}`).join("\n");
            throw new Error(`${run.name} browser harness did not pass.\n${failures}\nResult: ${join(artifacts, `${run.name}.json`)}`);
        }
        console.log(`${run.name} requested ${run.width}x${run.height}, actual ${result.width}x${result.height}: PASS (${result.checks} checks)`);
    }
    console.log(`Browser: ${version}`);
    console.log(`Artifacts: ${artifacts}`);
} finally {
    await new Promise(resolvePromise => server.close(resolvePromise));
}
