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
    const received = [];
    const requestHandler = (request, response) => {
        if (request.url === "/evidence" && request.method === "POST") {
            const chunks = [];
            request.on("data", chunk => chunks.push(chunk));
            request.on("end", () => {
                const value = JSON.parse(Buffer.concat(chunks).toString("utf8"));
                received.push(value);
                resolveEvidence(value);
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
    };
    const server = http.createServer(requestHandler);
    await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
    const matching = path.basename(fixture) === "matching-conditions.user.js";
    const matchingScenarios = matching ? JSON.parse(fs.readFileSync(fixture.replace(/\.user\.js$/, ".json"), "utf8")) : [];
    const noAutomaticMatch = ["matching-default.user.js", "matching-all-urls.user.js"].includes(path.basename(fixture));
    const ipv6 = matching ? http.createServer(requestHandler) : null;
    let context;
    try {
        if (ipv6) await new Promise((resolve, reject) => { ipv6.once("error", reject); ipv6.listen(0, "::1", resolve); });
        context = await chromium.launchPersistentContext(profile, {
            executablePath: path.resolve(browserPath), headless: true,
            args: [`--disable-extensions-except=${path.resolve(extensionPath)}`, `--load-extension=${path.resolve(extensionPath)}`, "--no-first-run",
                ...(matching ? ["--no-proxy-server", "--host-resolver-rules=MAP xn--fsqu00a.xn--0zwm56d 127.0.0.1"] : [])]
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
        const expand = value => value.replaceAll("__PORT__", String(server.address().port)).replaceAll("__IPV6_PORT__", String(ipv6 && ipv6.address().port));
        const fixtureSource = expand(fs.readFileSync(fixture, "utf8"));
        await page.locator("input[type=file]").setInputFiles({ name: path.basename(fixture), mimeType: "text/javascript", buffer: Buffer.from(fixtureSource) });
        const ask = await askPromise;
        const installed = ask.waitForEvent("close", { timeout: 5000 }).catch(() => undefined);
        await ask.locator('input[type=button][value="安装"], input[type=button][value="Install"]').click();
        await installed;
        const target = await context.newPage();
        const session = await context.newCDPSession(target);
        const browserVersion = await session.send("Browser.getVersion");
        const tabsBefore = await worker.evaluate(() => chrome.tabs.query({}).then(tabs => tabs.length));
        let deadline;
        let result;
        if (matching) {
            const cases = [];
            for (const scenario of matchingScenarios) {
                await target.goto("about:blank");
                const url = expand(scenario.url);
                await target.goto(url);
                await target.waitForTimeout(400);
                cases.push({ id: scenario.id, url, matched: await target.evaluate(() => document.documentElement.dataset.matchProbe === "ran") });
            }
            result = { schemaVersion: 1, caseId: "matching-conditions", events: received.map(value => ({ name: "source", url: value.effects.url })),
                promise: {}, immediateReturn: { probe: "completed" }, effects: { cases } };
        } else if (noAutomaticMatch) {
            await target.goto(`http://127.0.0.1:${server.address().port}/`);
            await target.waitForTimeout(1000);
            result = { schemaVersion: 1, caseId: path.basename(fixture).replace(".user.js", ""), events: received,
                promise: {}, immediateReturn: { probe: "completed" }, effects: { automaticExecutions: received.length } };
        } else {
            await target.goto(`http://127.0.0.1:${server.address().port}/`);
            result = await Promise.race([evidence, new Promise((_, reject) => { deadline = setTimeout(() => reject(new Error("No userscript evidence within 20 seconds.")), 20000); })]).finally(() => clearTimeout(deadline));
        }
        result.effects.tabsBefore = tabsBefore;
        result.effects.tabsAfter = await worker.evaluate(() => chrome.tabs.query({}).then(tabs => tabs.length));
        const checks = matching ? {
            allCasesRecorded: result.effects.cases.length === matchingScenarios.length,
            expectedMatches: result.effects.cases.every(value => value.matched === matchingScenarios.find(scenario => scenario.id === value.id).expected)
        } : noAutomaticMatch ? { noAutomaticExecution: result.effects.automaticExecutions === 0 } : result.caseId === "storage-timing" ? {
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
        } : result.caseId === "page-helpers" ? {
            domParents: ["span", "style", "script", "link", "meta"].every(tag => result.effects[tag + "Parent"] === "HEAD"),
            styleEffect: result.effects.styleEffect === "9",
            modernNode: result.effects.modernElement === "modern" && result.immediateReturn.modernElement === "function",
            closeReturns: result.immediateReturn.legacyClose === "undefined" && result.immediateReturn.modernClose === "undefined",
            closedAfterRetry: result.effects.closed === true,
            logReturns: result.immediateReturn.legacyLog === "undefined" && result.immediateReturn.modernLog === "function" && result.promise.logValue === "undefined"
        } : result.caseId === "audio" ? {
            noError: !result.effects.error,
            muteReadback: result.effects.muted?.isMuted === true && result.effects.unmuted?.isMuted === false,
            promiseReturns: result.immediateReturn.set === "function" && result.immediateReturn.add === "function" && result.immediateReturn.remove === "function",
            callbackReturns: result.immediateReturn.legacySet === "undefined" && result.immediateReturn.legacyGet === "undefined"
        } : result.caseId === "binary-inputs" ? {
            blobText: result.effects.blobText === "blob text 你好",
            fileText: result.effects.fileText === "file text",
            urlBytes: JSON.stringify(result.effects.urlBytes) === "[0,255,1]",
            callbackReceiver: result.effects.callbackThis === true,
            abortHandle: result.immediateReturn.abort === "function"
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
        if (ipv6) await new Promise(resolve => ipv6.close(resolve));
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
