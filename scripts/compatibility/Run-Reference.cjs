// Requires Playwright and a locally installed, unmodified Tampermonkey 5.5.1.
// Only the fresh test profile is changed. No extension data is copied from a user profile.
const fs = require("node:fs");
const path = require("node:path");
const http = require("node:http");
const crypto = require("node:crypto");
const { execFileSync } = require("node:child_process");
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || "playwright");

async function main() {
    const [browserPath, extensionPath, output = "artifacts/compatibility/reference-returns.json", fixturePath = "docs/compatibility/fixtures/returns.user.js"] = process.argv.slice(2);
    if (!browserPath || !extensionPath) throw new Error("Usage: node Run-Reference.cjs <Edge executable> <Tampermonkey directory> [output.json]");
    const manifest = JSON.parse(fs.readFileSync(path.join(extensionPath, "manifest.json"), "utf8"));
    if (manifest.version !== "5.5.1" || manifest.manifest_version !== 3) throw new Error("Reference requires Tampermonkey 5.5.1/MV3; record a new environment before changing this pin.");
    const extensionId = "iikmkjmpaadaobahmlepeloendndfphd";
    const root = path.resolve(__dirname, "../..");
    const fixture = path.resolve(root, fixturePath);
    const fixtureHash = crypto.createHash("sha256").update(fs.readFileSync(fixture)).digest("hex");
    const artifacts = path.join(root, "artifacts/compatibility");
    fs.mkdirSync(artifacts, { recursive: true });
    const profile = fs.mkdtempSync(path.join(artifacts, "reference-profile-"));
    let resolveEvidence;
    const evidence = new Promise(resolve => { resolveEvidence = resolve; });
    const server = http.createServer((request, response) => {
        if (request.url === "/evidence" && request.method === "POST") {
            const chunks = [];
            request.on("data", chunk => chunks.push(chunk));
            request.on("end", () => {
                resolveEvidence(JSON.parse(Buffer.concat(chunks).toString("utf8")));
                response.end("ok");
            });
        } else if (/^\/fixture-content\/[a-z0-9.-]+$/.test(request.url)) {
            const contentPath = path.join(root, "docs/compatibility/fixtures/content", path.basename(request.url));
            if (!fs.existsSync(contentPath)) { response.statusCode = 404; response.end(); return; }
            response.setHeader("Content-Type", request.url.endsWith(".js") ? "text/javascript; charset=windows-1252" : "application/octet-stream");
            response.end(fs.readFileSync(contentPath));
        } else {
            response.setHeader("Content-Type", "text/html; charset=utf-8");
            response.end("<!doctype html><html><head><title>Return contract fixture</title></head><body>Return contract fixture</body></html>");
        }
    });
    await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
    let context;
    try {
        context = await chromium.launchPersistentContext(profile, {
            executablePath: path.resolve(browserPath), headless: true,
            args: [`--disable-extensions-except=${path.resolve(extensionPath)}`, `--load-extension=${path.resolve(extensionPath)}`, "--no-first-run"]
        });
        const worker = context.serviceWorkers()[0] || await context.waitForEvent("serviceworker");
        if (await worker.evaluate(() => chrome.runtime.getManifest().version) !== manifest.version) throw new Error("Wrong running extension version.");
        const page = await context.newPage();
        await page.goto(`edge://extensions/?id=${extensionId}`);
        await page.evaluate(id => new Promise((resolve, reject) => chrome.developerPrivate.updateExtensionConfiguration(
            { extensionId: id, userScriptsAccess: true }, () => chrome.runtime.lastError ? reject(new Error(chrome.runtime.lastError.message)) : resolve())), extensionId);
        await page.goto(`chrome-extension://${extensionId}/options.html`);
        await page.locator("input[type=file]").waitFor({ state: "attached" });
        // Import through Tampermonkey's own installation UI, not storage injection.
        const askPromise = context.waitForEvent("page", page => page.url().includes("/ask.html"));
        const fixtureSource = fs.readFileSync(fixture, "utf8").replaceAll("__PORT__", String(server.address().port));
        await page.locator("input[type=file]").setInputFiles({ name: path.basename(fixture), mimeType: "text/javascript", buffer: Buffer.from(fixtureSource) });
        const ask = await askPromise;
        const installed = ask.waitForEvent("close", { timeout: 5000 }).catch(() => undefined);
        await ask.locator('input[type=button][value="安装"], input[type=button][value="Install"]').click();
        await installed;
        const target = await context.newPage();
        const session = await context.newCDPSession(target);
        const browserVersion = await session.send("Browser.getVersion");
        const tabsBefore = await worker.evaluate(() => chrome.tabs.query({}).then(tabs => tabs.length));
        await target.goto(`http://127.0.0.1:${server.address().port}/`);
        let deadline;
        const result = await Promise.race([evidence, new Promise((_, reject) => { deadline = setTimeout(() => reject(new Error("No userscript evidence within 20 seconds.")), 20000); })]).finally(() => clearTimeout(deadline));
        result.effects.tabsBefore = tabsBefore;
        result.effects.tabsAfter = await worker.evaluate(() => chrome.tabs.query({}).then(tabs => tabs.length));
        const checks = result.caseId === "storage-timing" ? {
            eventOrder: result.events.map(event => event.name).join(",") === "before-legacy,listener,after-legacy,listener,after-modern,microtask-legacy,modern-settled",
            localListeners: result.events.filter(event => event.name === "listener").every(event => event.remote === false && event.current === event.newValue),
            immediateMirror: result.effects.immediateValue === 2,
            nullPrototype: result.effects.bulkPrototype === "null",
            defaultReferences: result.effects.defaultReference === true && result.effects.bulkDefaultReference === true
        } : result.caseId === "prepared-content" ? {
            sourceExecuted: result.effects.shared === 3,
            strictInherited: result.effects.strict === true,
            nonUtf8: result.effects.accented === "café",
            binaryResource: result.effects.binary === "AP8B",
            allAlgorithms: result.effects.hashes === "sha1,sha384,sha512"
        } : result.caseId === "returns" ? {
            noInvocationErrors: !result.events.some(event => event.name.endsWith(":throw") || event.name.endsWith(":rejected")),
            domNodes: result.effects.nodes.every(Boolean),
            stylesApplied: JSON.stringify(result.effects.styles) === JSON.stringify(["17", "23"]),
            promisesSettled: Object.values(result.promise).every(value => value.state === "fulfilled"),
            openedTabsClosed: result.effects.tabsBefore === result.effects.tabsAfter
        } : { exposureRecorded: Object.keys(result.immediateReturn).length > 0 };
        const record = {
            ...result, fixtureHash,
            contractSource: ["https://www.tampermonkey.net/documentation.php?locale=en", "Tampermonkey 5.5.1/MV3 runtime"],
            environment: {
                manager: "Tampermonkey", managerVersion: manifest.version,
                chromiumVersion: browserVersion.product, userAgent: browserVersion.userAgent,
                manifestVersion: manifest.manifest_version,
                settings: { headless: true, userScriptsAccess: true, freshProfile: true },
                commit: execFileSync("git", ["rev-parse", "HEAD"], { cwd: root, encoding: "utf8" }).trim(),
                platform: process.arch, capabilities: { referenceReturnProbe: true }, world: "manager-selected"
            },
            checks, status: Object.values(checks).every(Boolean) ? "passed" : "failed"
        };
        fs.mkdirSync(path.dirname(path.resolve(output)), { recursive: true });
        fs.writeFileSync(output, JSON.stringify(record, null, 2) + "\n");
        console.log(JSON.stringify({ output, status: record.status, immediateReturn: record.immediateReturn, promise: record.promise, effects: record.effects }));
        if (record.status !== "passed") process.exitCode = 1;
    } finally {
        if (context) await context.close();
        await new Promise(resolve => server.close(resolve));
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
