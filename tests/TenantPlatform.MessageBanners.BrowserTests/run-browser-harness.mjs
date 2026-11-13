import { createServer } from "node:http";
import { spawn } from "node:child_process";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, extname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const directory = dirname(fileURLToPath(import.meta.url));
const root = resolve(directory, "../..");
const chrome = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
const profiles = await mkdtemp(join(tmpdir(), "tenantplatform-message-banners-"));
let receiveCompletion = null;
const routes = new Map([
    ["/", join(directory, "index.html")],
    ["/browser-harness.js", join(directory, "browser-harness.js")],
    ["/bootstrap.css", join(root, "src/TenantPlatform.Web/wwwroot/bootstrap/bootstrap.min.css")],
    ["/app.css", join(root, "src/TenantPlatform.Web/wwwroot/app.css")],
    ["/js/message-banners.js", join(root, "src/TenantPlatform.Web/wwwroot/js/message-banners.js")]
]);
const mime = new Map([[".html", "text/html; charset=utf-8"], [".js", "text/javascript; charset=utf-8"], [".css", "text/css; charset=utf-8"]]);

const server = createServer(async (request, response) => {
    try {
        const path = new URL(request.url, "http://127.0.0.1").pathname;
        if (path === "/complete" && request.method === "POST") {
            let body = "";
            for await (const chunk of request) body += chunk;
            receiveCompletion?.(JSON.parse(body));
            response.writeHead(204).end();
            return;
        }
        const file = routes.get(path);
        if (!file) { response.writeHead(404).end("Not found"); return; }
        response.writeHead(200, { "content-type": mime.get(extname(file)) ?? "application/octet-stream", "cache-control": "no-store" });
        response.end(await readFile(file));
    } catch (error) {
        response.writeHead(500).end(String(error));
    }
});

await new Promise((resolvePromise, reject) => {
    server.once("error", reject);
    server.listen(0, "127.0.0.1", resolvePromise);
});
const origin = `http://127.0.0.1:${server.address().port}`;

function execute(command, args) {
    return new Promise((resolvePromise, reject) => {
        const child = spawn(command, args, { cwd: root });
        let stdout = "", stderr = "";
        child.stdout.on("data", chunk => stdout += chunk);
        child.stderr.on("data", chunk => stderr += chunk);
        child.once("error", reject);
        child.once("close", code => code === 0 ? resolvePromise({ stdout, stderr }) : reject(new Error(`${command} exited ${code}\n${stdout}\n${stderr}`)));
    });
}

try {
    const version = (await execute(chrome, ["--version"])).stdout.trim();
    for (const run of [{ name: "desktop", width: 1280, height: 800 }, { name: "mobile", width: 390, height: 844 }]) {
        let stderr = "";
        const browser = spawn(chrome, [
            "--headless=new", "--no-first-run", "--no-default-browser-check", "--disable-background-networking",
            "--remote-debugging-port=0", `--window-size=${run.width},${run.height}`,
            `--user-data-dir=${join(profiles, run.name)}`,
            `${origin}/?viewport=${run.name}`
        ], { cwd: root });
        browser.stderr.on("data", chunk => stderr += chunk);
        try {
            const result = await new Promise((resolvePromise, reject) => {
                const timeout = setTimeout(() => reject(new Error(`${run.name} timed out\n${stderr}`)), 15000);
                receiveCompletion = value => {
                    if (value.viewport !== run.name) return;
                    clearTimeout(timeout);
                    resolvePromise(value);
                };
                browser.once("error", reject);
                browser.once("exit", code => {
                    if (receiveCompletion) reject(new Error(`${run.name} Chrome exited ${code}\n${stderr}`));
                });
            });
            receiveCompletion = null;
            if (result.status !== "passed")
                throw new Error(result.records.filter(x => !x.passed).map(x => `FAIL: ${x.message}`).join("\n"));
            console.log(`${run.name} ${result.width}x${result.height}: PASS (${result.checks} checks)`);
        } finally {
            receiveCompletion = null;
            browser.kill("SIGTERM");
        }
    }
    console.log(`Browser: ${version}`);
} finally {
    await new Promise(resolvePromise => server.close(resolvePromise));
    await rm(profiles, { recursive: true, force: true });
}
