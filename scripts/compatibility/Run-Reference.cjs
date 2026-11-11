// Requires Playwright and a locally installed, unmodified Tampermonkey 5.5.1.
// Only the fresh test profile is changed. No extension data is copied from a user profile.
const fs = require("node:fs");
const path = require("node:path");
const http = require("node:http");
const https = require("node:https");
const crypto = require("node:crypto");
const { execFileSync } = require("node:child_process");
const { verifyEnvironment } = require('./ReferenceEnvironment.cjs');

async function main() {
    const [browserPath, extensionPath, output = `artifacts/compatibility/reference-${Date.now()}.json`, fixturePath = "scripts/compatibility/fixtures/returns.user.js", environmentPath = path.join(__dirname, 'reference-stable.json')] = process.argv.slice(2);
    if (!browserPath || !extensionPath) throw new Error("Usage: node Run-Reference.cjs <Edge executable> <Tampermonkey directory> [output.json] [fixture.user.js]");
    if (fs.existsSync(output)) throw new Error('Refusing to overwrite an existing reference record. Choose a new output path.');
    const pin = JSON.parse(fs.readFileSync(environmentPath, 'utf8'));
    const { manifest, contentSha256 } = verifyEnvironment(extensionPath, pin);
    const extensionId = pin.extensionId;
    const { chromium } = require(process.env.PLAYWRIGHT_MODULE || "playwright");
    const root = path.resolve(__dirname, "../..");
    const fixture = path.resolve(root, fixturePath);
    const fixtureHash = crypto.createHash("sha256").update(fs.readFileSync(fixture)).digest("hex");
    const artifacts = path.join(root, "artifacts/compatibility");
    fs.mkdirSync(artifacts, { recursive: true });
    const profile = fs.mkdtempSync(path.join(artifacts, "reference-profile-"));
    const extensionSnapshot = path.join(profile, 'reference-extension');
    fs.cpSync(extensionPath, extensionSnapshot, { recursive: true });
    let resolveEvidence;
    const evidence = new Promise(resolve => { resolveEvidence = resolve; });
    const received = [];
    let privateEvidence;
    let privateReloadEvidence;
    const requestHandler = (request, response) => {
        if ((request.url === "/evidence" || request.url === "/private-evidence" || request.url === "/reload-evidence") && request.method === "POST") {
            const chunks = [];
            request.on("data", chunk => chunks.push(chunk));
            request.on("end", () => {
                const value = JSON.parse(Buffer.concat(chunks).toString("utf8"));
                if (request.url === "/reload-evidence") privateReloadEvidence = value;
                else if (request.url === "/private-evidence") privateEvidence = value;
                else { received.push(value); resolveEvidence(value); }
                response.end("ok");
            });
        } else if (request.url.startsWith('/p1-body')) {
            const chunks = [];
            request.on('data', chunk => chunks.push(chunk));
            request.on('end', () => {
                if (request.url.startsWith('/p1-body-redirect')) { response.statusCode = 307; response.setHeader('Location', '/p1-body'); }
                const bytes = Buffer.concat(chunks);
                response.setHeader('Content-Type', 'application/json');
                response.end(JSON.stringify({ length: bytes.length, sha256: crypto.createHash('sha256').update(bytes).digest('hex'), method: request.method }));
            });
        } else if (request.url.startsWith('/p1-progress')) {
            response.setHeader('Content-Type', 'text/plain; charset=utf-8');
            response.write(Buffer.from([97, 228, 189]));
            setTimeout(() => response.end(Buffer.from([160, 229, 165, 189])), 700);
        } else if (request.url.startsWith('/cookie-echo') || request.url.startsWith('/cookie-redirect')) {
            if (request.url.startsWith('/cookie-redirect')) { response.statusCode = 302; response.setHeader('Location', '/cookie-echo'); }
            response.setHeader('Content-Type', 'text/plain');
            response.end(request.headers.cookie || '');
        } else if (request.url === '/partition-echo' || request.url === '/partition-receive') {
            if (request.url === '/partition-receive') response.setHeader('Set-Cookie','partition-received=done; Path=/; Secure; SameSite=None; Partitioned');
            response.setHeader('Content-Type','text/plain');
            response.end(request.headers.cookie || '');
        } else if (/^\/fixture-content\/[a-z0-9.-]+$/.test(request.url)) {
            const contentPath = path.join(root, "scripts/compatibility/fixtures/content", path.basename(request.url));
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
    const partitionProbe = path.basename(fixture) === 'partition-cookies.user.js';
    const streamBodyProbe = path.basename(fixture) === 'p1-stream-body.user.js';
    const tlsProbe = partitionProbe || streamBodyProbe;
    let tls;
    const matching = path.basename(fixture) === "matching-conditions.user.js";
    const matchingScenarios = matching ? JSON.parse(fs.readFileSync(fixture.replace(/\.user\.js$/, ".json"), "utf8")) : [];
    const noAutomaticMatch = ["matching-default.user.js", "matching-all-urls.user.js"].includes(path.basename(fixture));
    const ipv6 = matching ? http.createServer(requestHandler) : null;
    let context;
    try {
        if (tlsProbe) {
            const transport = streamBodyProbe ? require('node:http2') : https;
            tls = transport.createSecureServer ? transport.createSecureServer({pfx:fs.readFileSync(path.join(root,'Mzying2001.MonkeySharp.CefSharp.SmokeHost/Fixtures/loopback.pfx')),passphrase:'fixture',allowHTTP1:true},requestHandler) :
                transport.createServer({pfx:fs.readFileSync(path.join(root,'Mzying2001.MonkeySharp.CefSharp.SmokeHost/Fixtures/loopback.pfx')),passphrase:'fixture'},requestHandler);
            await new Promise(resolve => tls.listen(0,'127.0.0.1',resolve));
        }
        if (ipv6) await new Promise((resolve, reject) => { ipv6.once("error", reject); ipv6.listen(0, "::1", resolve); });
        context = await chromium.launchPersistentContext(profile, {
            executablePath: path.resolve(browserPath), headless: pin.settings?.headless !== false,
            args: [`--disable-extensions-except=${extensionSnapshot}`, `--load-extension=${extensionSnapshot}`, "--no-first-run",
                ...(matching ? ["--no-proxy-server", "--host-resolver-rules=MAP xn--fsqu00a.xn--0zwm56d 127.0.0.1"] : []),
                ...(tlsProbe ? ['--ignore-certificate-errors'] : [])]
        });
        let worker = context.serviceWorkers()[0] || await context.waitForEvent("serviceworker");
        if (await worker.evaluate(() => chrome.runtime.getManifest().version) !== manifest.version ||
            await worker.evaluate(() => chrome.runtime.id) !== extensionId) throw new Error("Wrong running extension version or identity.");
        const page = await context.newPage();
        await page.goto(`edge://extensions/?id=${extensionId}`);
        const privateProbe = path.basename(fixture) === 'private-tabs.user.js';
        const restarted = privateProbe ? context.waitForEvent('serviceworker', {timeout:5000}).catch(() => null) : null;
        await page.evaluate(({id, privateProbe}) => new Promise((resolve, reject) => chrome.developerPrivate.updateExtensionConfiguration(
            { extensionId: id, userScriptsAccess: true, ...(privateProbe ? {incognitoAccess:true} : {}) }, () => chrome.runtime.lastError ? reject(new Error(chrome.runtime.lastError.message)) : resolve())),
            {id:extensionId, privateProbe});
        if (restarted) worker = await restarted || worker;
        await page.goto(`chrome-extension://${extensionId}/options.html`);
        await page.locator("input[type=file]").waitFor({ state: "attached" });
        // Import through Tampermonkey's own installation UI, not storage injection.
        const askPromise = context.waitForEvent("page", page => page.url().includes("/ask.html"));
        const expand = value => value.replaceAll("__PORT__", String(server.address().port)).replaceAll("__IPV6_PORT__", String(ipv6 && ipv6.address().port)).replaceAll('__TLS_PORT__',String(tls && tls.address().port));
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
        const privateSnapshot = privateProbe ? new Promise(resolve => setTimeout(async () => {
            try { resolve(await worker.evaluate(async () => ({
                allowed: await new Promise(resolve => chrome.extension.isAllowedIncognitoAccess(resolve))
            }))); } catch (error) { resolve({error:String(error)}); }
        }, 3000)) : null;
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
            if (path.basename(fixture) === 'p0-urlchange.user.js') {
                await target.waitForFunction(() => document.documentElement.dataset.urlProbeReady === 'true');
                await target.waitForTimeout(500);
                for (const operation of ['push', 'replace', 'hash', 'back']) {
                    await target.evaluate(operation => {
                        if (operation === 'push') history.pushState({}, '', '/one');
                        if (operation === 'replace') history.replaceState({}, '', '/two');
                        if (operation === 'hash') location.hash = 'hash';
                        if (operation === 'back') history.back();
                    }, operation);
                    await target.waitForTimeout(1100);
                }
            }
            result = await Promise.race([evidence, new Promise((_, reject) => { deadline = setTimeout(() => reject(new Error("No userscript evidence within 20 seconds.")), 20000); })]).finally(() => clearTimeout(deadline));
        }
        result.effects.tabsBefore = tabsBefore;
        if (result.caseId === "private-tabs") {
            result.effects.private = privateEvidence;
            result.effects.privateSnapshot = await privateSnapshot;
            const newWorker = context.waitForEvent('serviceworker');
            await worker.evaluate(() => { setTimeout(() => chrome.runtime.reload(), 0); });
            worker = await newWorker;
            await target.goto(`http://127.0.0.1:${server.address().port}/private-check`);
            const reloadDeadline = Date.now() + 10000;
            while (!privateReloadEvidence && Date.now() < reloadDeadline) {
                await target.waitForTimeout(500);
                if (!privateReloadEvidence) await target.reload();
            }
            result.effects.afterManagerReload = privateReloadEvidence;
        }
        result.effects.tabsAfter = await worker.evaluate(() => chrome.tabs.query({}).then(tabs => tabs.length));
        const checks = result.caseId === 'p1-stream-body' ? {
            noError: !result.effects.error,
            exactBytes: result.effects.upload?.sha256 === result.effects.expectedHash && result.effects.upload?.length === 150003,
            previewRedirectRefusesDisturbedReplay: result.effects.redirectError?.status === 408,
            previewReaderRemainsLocked: result.effects.locked === true,
            lockedRejected: result.promise.locked?.state === 'rejected',
            disturbedAndProducerPendingWithinProbe: result.promise.disturbed?.state === 'pending' && result.promise.producer?.state === 'pending',
            emptyBody: result.effects.empty?.length === 0
        } : result.caseId === 'p1-progress' ? {
            noError: !result.effects.error,
            partialText: result.events.some(event => event.name === 'progress' && event.text === 'a'),
            finalText: result.effects.text === 'a你好',
            decoderBoundary: result.events.filter(event => event.name === 'progress').every(event => !event.text?.includes('\ufffd')),
            fetchProgressObserved: result.effects.fetchProgress > 0
        } : result.caseId === 'p0-dom' ? {
            invalidCreation: ['invalidTag', 'invalidAttribute', 'invalidParent', 'emptyTag'].every(name =>
                result.immediateReturn[name] === 'null' && result.promise[name]?.state === 'fulfilled' && result.promise[name]?.value === 'null'),
            invalidType: result.immediateReturn.invalidType === 'throws:TypeError' && result.promise.invalidType?.state === 'rejected',
            continued: result.effects.continued === true
        } : result.caseId === 'p0-urlchange' ? {
            changesObserved: result.events.some(event => event.name === 'listener'),
            once: result.effects.onceCount === 1,
            signalIgnoredByPinnedReference: result.events.some(event => event.name === 'aborted'),
            plainUrlPayload: result.events.filter(event => event.name === 'listener').every(event => event.isEvent === false && event.keys?.join(',') === 'url')
        } : result.caseId === 'p0-cookies' ? {
            jarObserved: result.effects.jar?.includes('same=jar'),
            headerWins: result.effects.both?.includes('same=header') && !result.effects.both?.includes('same=details'),
            values: result.effects.values?.includes('equals=a=b=c') && result.effects.values?.includes('empty='),
            anonymous: result.effects.anonymous === 'same=header'
        } : result.caseId === 'p0-notification' ? {
            replacementTimeout: result.promise.replacement?.state === 'fulfilled' && result.promise.replacement.value === false
        } : matching ? {
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
        } : result.caseId === 'partition-cookies' ? {
            noError:!result.effects.error,
            partitionSelection:result.effects.defaults?.[0]?.value === 'plain' && result.effects.a?.[0]?.value === 'a' && result.effects.b?.[0]?.value === 'b',
            requestSelection:result.effects.requestA?.includes('partition-proof=a') && !result.effects.requestA?.includes('partition-proof=b'),
            responsePartition:result.effects.receivedA?.[0]?.value === 'done',
            preciseDelete:!result.effects.afterDeleteA?.some(cookie => cookie.value === 'a') && result.effects.afterDeleteA?.some(cookie => cookie.value === 'b') && result.effects.afterDeleteA?.some(cookie => cookie.value === 'plain')
        } : result.caseId === "private-tabs" ? {
            noError: !result.effects.error,
            contexts: result.effects.normalIncognito === false && result.effects.private?.isIncognito === true,
            copiedBaseline: result.effects.private?.normal === "shared-from-normal",
            isolatedWrites: result.effects.fromPrivate === null && result.effects.modernFromPrivate === null && result.effects.afterManagerReload?.fromPrivate === null
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
            contractSource: ["https://www.tampermonkey.net/documentation.php?locale=en", `Tampermonkey ${pin.version}/${pin.channel}/MV3 runtime`],
            environment: {
                manager: "Tampermonkey", managerVersion: manifest.version,
                chromiumVersion: browserVersion.product, userAgent: browserVersion.userAgent,
                manifestVersion: manifest.manifest_version, channel: pin.channel, extensionId, contentSha256,
                minimumChromeVersion: manifest.minimum_chrome_version,
                settings: { ...pin.settings, headless: pin.settings?.headless !== false, userScriptsAccess: true, freshProfile: true,
                    ignoreLoopbackCertificateErrors: tlsProbe, legacyPfxProvider: process.execArgv.includes('--openssl-legacy-provider') },
                commit: execFileSync("git", ["rev-parse", "HEAD"], { cwd: root, encoding: "utf8" }).trim(),
                sourcePatchSha256: crypto.createHash('sha256').update(execFileSync('git', ['diff', '--binary', 'HEAD'], { cwd: root })).digest('hex'),
                runnerSha256: crypto.createHash('sha256').update(fs.readFileSync(__filename)).digest('hex'),
                platform: process.arch, capabilities: { referenceReturnProbe: true }, world: "manager-selected"
            },
            checks, status: result.status === 'blocked' ? 'blocked' : Object.values(checks).every(Boolean) ? "passed" : "failed"
        };
        fs.mkdirSync(path.dirname(path.resolve(output)), { recursive: true });
        fs.writeFileSync(output, JSON.stringify(record, null, 2) + "\n");
        console.log(JSON.stringify({ output, status: record.status, immediateReturn: record.immediateReturn, promise: record.promise, effects: record.effects }));
        if (record.status !== "passed") process.exitCode = 1;
    } finally {
        if (context) await context.close();
        await new Promise(resolve => server.close(resolve));
        if (ipv6) await new Promise(resolve => ipv6.close(resolve));
        if (tls?.listening) await new Promise(resolve => tls.close(resolve));
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
