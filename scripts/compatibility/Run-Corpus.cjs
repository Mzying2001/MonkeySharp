const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const crypto = require('node:crypto');
const zlib = require('node:zlib');
const { execFile, execFileSync } = require('node:child_process');
const { verifyEnvironment } = require('./ReferenceEnvironment.cjs');
const root = path.resolve(__dirname, '../..');
const hash = data => crypto.createHash('sha256').update(data).digest('hex');
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==', 'base64');
function argumentsOf(values) {
    const result = {};
    for (let i = 0; i < values.length; i += 2) {
        if (!values[i].startsWith('--') || !values[i + 1]) throw new Error('Expected --name value arguments.');
        result[values[i].slice(2)] = values[i + 1];
    }
    return result;
}
function validateCorpus(manifest) {
    for (const script of Object.values(manifest.scripts)) {
        const file = path.resolve(root, script.sourcePath);
        if (!file.startsWith(root + path.sep) || hash(fs.readFileSync(file)) !== script.sha256)
            throw new Error('Corpus source bytes do not match provenance: ' + script.sourcePath);
        if (!script.sourceUrl.includes(script.commit) || !script.license || !script.version)
            throw new Error('Corpus requires immutable provenance, version and license.');
        const license = path.resolve(root, script.licensePath);
        if (!license.startsWith(root + path.sep) || hash(fs.readFileSync(license)) !== script.licenseSha256)
            throw new Error('Corpus license bytes do not match provenance.');
    }
}
const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
function zipEntries(bytes) {
    const entries = [];
    for (let offset = 0; offset + 46 <= bytes.length; offset++) {
        if (bytes.readUInt32LE(offset) !== 0x02014b50) continue;
        const method = bytes.readUInt16LE(offset + 10), size = bytes.readUInt32LE(offset + 20), nameSize = bytes.readUInt16LE(offset + 28);
        const name = bytes.subarray(offset + 46, offset + 46 + nameSize).toString('utf8');
        const local = bytes.readUInt32LE(offset + 42);
        const start = local + 30 + bytes.readUInt16LE(local + 26) + bytes.readUInt16LE(local + 28);
        const compressed = bytes.subarray(start, start + size);
        const data = method === 0 ? compressed : method === 8 ? zlib.inflateRawSync(compressed) : null;
        if (!data) throw new Error('Unsupported corpus ZIP compression.');
        entries.push({ name, bytes: data.length, sha256: hash(data) });
        offset += 45 + nameSize + bytes.readUInt16LE(offset + 30) + bytes.readUInt16LE(offset + 32);
    }
    return entries;
}
async function until(condition, timeout = 12000) {
    const deadline = Date.now() + timeout;
    let error;
    do { try { if (await condition()) return; } catch (failure) { error = failure; } await delay(50); } while (Date.now() < deadline);
    throw new Error('Corpus action did not meet its functional assertion.', { cause: error });
}
async function reference(input, options, directory) {
    const pin = JSON.parse(fs.readFileSync(options.environment || path.join(__dirname, 'reference-stable.json')));
    const verified = verifyEnvironment(options.extension, pin);
    const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
    const profile = path.join(directory, 'profile');
    const extension = path.join(directory, 'extension');
    fs.cpSync(options.extension, extension, { recursive: true });
    const context = await chromium.launchPersistentContext(profile, { executablePath: options.browser, headless: options.headed !== 'true', locale: 'en-US', acceptDownloads: true,
        args: [`--disable-extensions-except=${extension}`, `--load-extension=${extension}`, '--no-first-run'] });
    const observations = { browserDownloads: [] }, checks = {}, pages = [], downloads = [], events = [];
    const downloadSession = await context.browser().newBrowserCDPSession();
    await downloadSession.send('Browser.setDownloadBehavior', { behavior: 'allow', downloadPath: input.downloads, eventsEnabled: true });
    downloadSession.on('Browser.downloadWillBegin', event => {
        observations.browserDownloads.push({ guid: event.guid, url: event.url, name: event.suggestedFilename, state: 'inProgress' });
        if (input.cancelDownloads) downloads.push(downloadSession.send('Browser.cancelDownload', { guid: event.guid }));
    });
    downloadSession.on('Browser.downloadProgress', event => {
        const item = observations.browserDownloads.find(item => item.guid === event.guid);
        if (item) { item.state = event.state; item.receivedBytes = event.receivedBytes; }
    });
    const browserProcesses = await downloadSession.send('SystemInfo.getProcessInfo');
    const browserPid = browserProcesses.processInfo.find(process => process.type === 'browser')?.id;
    let active = 0, failure, failedAction, worker;
    try {
        worker = context.serviceWorkers()[0] || await context.waitForEvent('serviceworker');
        if (await worker.evaluate(() => chrome.runtime.id) !== pin.extensionId) throw new Error('Wrong running extension identity.');
        const page = await context.newPage();
        await page.goto(`edge://extensions/?id=${pin.extensionId}`);
        await page.evaluate(id => new Promise((resolve, reject) => chrome.developerPrivate.updateExtensionConfiguration({ extensionId: id, userScriptsAccess: true },
            () => chrome.runtime.lastError ? reject(new Error(chrome.runtime.lastError.message)) : resolve())), pin.extensionId);
        await page.goto(`chrome-extension://${pin.extensionId}/options.html`);
        await page.locator('input[type=file]').waitFor({ state: 'attached' });
        await page.goto(`chrome-extension://${pin.extensionId}/options.html#nav=settings`);
        await page.waitForTimeout(700);
        fs.writeFileSync(path.join(directory, 'options-ui.txt'), await page.locator('body').innerText());
        const selects = await page.locator('select').evaluateAll(elements => elements.map(element => ({ id: element.id, name: element.name, value: element.value, options: element.innerText })).filter(element => /download|下载/i.test(JSON.stringify(element))));
        fs.writeFileSync(path.join(directory, 'download-settings.json'), JSON.stringify(selects, null, 2));
        const handleDownloadPrompt = async prompt => {
            if (!prompt.url().includes('/ask.html')) return;
            try {
                await prompt.waitForLoadState(); await prompt.waitForTimeout(300);
                const body = await prompt.locator('body').innerText();
                const buttons = await prompt.locator('input[type=button],button').evaluateAll(elements => elements.map(element => ({ value: element.value, text: element.textContent })));
                fs.writeFileSync(path.join(directory, 'download-prompt.json'), JSON.stringify({ body, buttons }, null, 2));
                if (/浏览器 API 下载|Browser API download/i.test(body)) observations.downloadPermissionPrompt = 'requires manual browser permission confirmation';
            } catch (_) { /* The ordinary installation UI can close before this observer. */ }
        };
        context.on('page', handleDownloadPrompt);
        await page.locator('select').filter({ has: page.locator('option[value="100"]') }).filter({ has: page.locator('option[value="0"]') }).selectOption('100');
        await page.locator('select').filter({ has: page.locator('option[value="chrome"]') }).selectOption('chrome');
        for (const save of await page.locator('input[type=button][value="保存"],input[type=button][value="Save"]').all())
            if (await save.isVisible() && await save.isEnabled()) await save.click();
        const askPromise = context.waitForEvent('page', page => page.url().includes('/ask.html'));
        await page.locator('input[type=file]').setInputFiles({ name: 'corpus.user.js', mimeType: 'text/javascript', buffer: fs.readFileSync(input.source) });
        const ask = await askPromise;
        const closed = ask.waitForEvent('close', { timeout: 5000 }).catch(() => undefined);
        await ask.locator('input[type=button][value="安装"], input[type=button][value="Install"]').click(); await closed;
        fs.writeFileSync(path.join(directory, 'permissions-state.json'), JSON.stringify({ downloads: await worker.evaluate(() => chrome.permissions.contains({ permissions: ['downloads'] })) }));
        async function popup() {
            const tabId = await worker.evaluate(url => chrome.tabs.query({}).then(tabs => tabs.find(tab => tab.url === url)?.id), pages[active].url());
            if (!tabId) throw new Error('Cannot identify the source tab for Tampermonkey action UI.');
            const popup = await context.newPage();
            await worker.evaluate(id => chrome.tabs.update(id, { active: true }), tabId);
            await popup.goto(`chrome-extension://${pin.extensionId}/action.html#${tabId}`);
            await popup.waitForTimeout(500);
            fs.writeFileSync(path.join(directory, 'action-ui.txt'), await popup.locator('body').innerText());
            return popup;
        }
        for (const action of input.actions) {
            failedAction = action;
            if (action.kind === 'open') {
                const page = await context.newPage(); pages.push(page); active = pages.length - 1;
                await context.addCookies(['127.0.0.1', 'localhost'].map(domain => ({ name: 'corpus-auth', value: 'fixture-only', domain, path: '/' })));
                await page.goto(action.url);
                observations.engineFeatures = await page.evaluate(() => ({ replaceAll: typeof String.prototype.replaceAll, compressionStream: typeof CompressionStream, saveFilePicker: typeof window.showSaveFilePicker }));
            } else if (action.kind === 'switch') active = action.index;
            else if (action.kind === 'navigate') await pages[active].goto(action.url);
            else if (action.kind === 'wait') await delay(action.milliseconds);
            else if (action.kind === 'assert') { await until(() => pages[active].evaluate(expression => Boolean(eval(expression)), action.expression)); checks[action.name] = true; }
            else if (action.kind === 'evaluate') {
                const value = await pages[active].evaluate(expression => eval(expression), action.expression);
                if (action.name) observations[action.name] = value;
            } else if (action.kind === 'menu' || action.kind === 'assertMenu') {
                const panel = await popup();
                const locator = panel.getByText(new RegExp(action.caption.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '$', 'i'));
                if (action.kind === 'menu') await locator.first().click();
                else { if ((await locator.count() > 0) !== action.present) throw new Error('Unexpected menu state: ' + action.caption); checks[action.name] = true; }
                if (!panel.isClosed()) await panel.close();
            } else if (action.kind === 'files') {
                await until(() => Promise.resolve(fs.readdirSync(input.downloads).filter(file => !file.endsWith('.crdownload')).length >= action.minimum &&
                    observations.browserDownloads.every(item => item.state !== 'inProgress')), Number(options['permission-wait-ms'] || 12000));
                checks[action.name] = true;
                if (action.maximum != null && fs.readdirSync(input.downloads).length > action.maximum) throw new Error('Canceled corpus download left a file.');
            } else throw new Error('Unknown corpus action: ' + action.kind);
            events.push({ kind: action.kind, name: action.name }); failedAction = null;
        }
        await Promise.all(downloads);
    } catch (error) {
        failure = String(error.stack || error);
        fs.writeFileSync(path.join(directory, 'open-pages.json'), JSON.stringify(context.pages().map(page => page.url()), null, 2));
        for (let i = 0; i < pages.length; i++) fs.writeFileSync(path.join(directory, `page-${i}-failure.txt`), await pages[i].locator('body').innerText().catch(() => 'unavailable'));
    }
    const browserVersion = context.browser()?.version();
    if (worker) observations.downloadsPermission = await Promise.race([
        worker.evaluate(() => chrome.permissions.contains({ permissions: ['downloads'] })).catch(() => null), delay(2000).then(() => null)
    ]);
    let closeDeadline;
    try {
        await Promise.race([context.close(), new Promise((_, reject) => { closeDeadline = setTimeout(() => reject(new Error('Reference browser did not close within eight seconds.')), 8000); })]);
    } catch (error) {
        observations.browserCloseError = String(error);
        if (browserPid) execFileSync('taskkill', ['/PID', String(browserPid), '/T', '/F'], { stdio: 'ignore' });
        failure = failure || String(error);
    } finally { clearTimeout(closeDeadline); }
    return { status: failure ? 'failed' : 'passed', error: failure, checks, observations, events, failedAction,
        environment: { manager: 'Tampermonkey', managerVersion: pin.version, chromiumVersion: browserVersion, manifestVersion: pin.manifestVersion,
            extensionId: pin.extensionId, contentSha256: verified.contentSha256, channel: pin.channel,
            settings: { freshProfile: true, headless: options.headed !== 'true', userScriptsAccess: true, locale: 'en-US', downloadMode: 'browser (selected in options UI)',
                browserDownloadBehavior: 'allow into isolated output directory; actual permission recorded separately', privateStorage: 'normal profile only' } } };
}
async function main() {
    const options = argumentsOf(process.argv.slice(2));
    if (!['monkeysharp', 'tampermonkey'].includes(options.manager)) throw new Error('--manager must be monkeysharp or tampermonkey.');
    if (options.manager === 'monkeysharp' && !options.host || options.manager === 'tampermonkey' && (!options.browser || !options.extension)) throw new Error('Supply --host, or --browser and --extension.');
    const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, 'corpus.json'))); validateCorpus(manifest);
    const inputSnapshot = {
        commit: execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).trim(),
        sourcePatchSha256: hash(execFileSync('git', ['-c', 'core.safecrlf=false', 'diff', '--binary', 'HEAD'], { cwd: root, stdio: ['ignore', 'pipe', 'pipe'] })),
        inputsSha256: Object.fromEntries(['Run-Corpus.cjs', 'corpus/scenarios.cjs', 'corpus.json', 'PackageConsumer/CorpusHost.cs', 'ReferenceEnvironment.cjs', 'reference-stable.json'].map(file => [file, hash(fs.readFileSync(path.join(__dirname, file)))]))
    };
    const directory = path.resolve(options.output || path.join(root, 'artifacts/corpus', options.manager + '-' + Date.now()));
    if (fs.existsSync(directory)) throw new Error('Refusing to overwrite an existing corpus run.');
    fs.mkdirSync(directory, { recursive: true });
    const requests = [];
    let base;
    let otherBase;
    const handler = (request, response) => {
        const url = new URL(request.url, base);
        requests.push({ method: request.method, path: url.pathname, host: request.headers.host, cookie: request.headers.cookie || '', status: 200 });
        const entry = requests.at(-1);
        if (url.pathname.startsWith('/protected/')) {
            if (url.pathname.endsWith('redirect.png')) { response.writeHead(302, { Location: otherBase + '/protected/image.png' }); entry.status = 302; response.end(); return; }
            if (!entry.cookie.includes('corpus-auth=fixture-only')) { response.statusCode = entry.status = 403; response.end('missing fixture login'); return; }
            response.setHeader('Content-Type', 'image/png'); response.setHeader('Content-Length', png.length); response.end(request.method === 'HEAD' ? undefined : png); return;
        }
        response.setHeader('Content-Type', 'text/html; charset=utf-8');
        if (url.searchParams.has('csp')) { response.setHeader('Content-Security-Policy', "style-src 'self'"); entry.contentSecurityPolicy = "style-src 'self'"; }
        response.end(`<!doctype html><html><head><title>Corpus fixture</title><script>window.__firstInlineHook=!!history.pushState.__snifferPatched;</script></head><body><img id="asset" src="${base}/protected/redirect.png" alt="Controlled asset">${url.searchParams.has('frame') ? '<iframe src="/corpus-child"></iframe>' : ''}</body></html>`);
    };
    const server = http.createServer(handler), other = http.createServer(handler);
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve)); base = `http://127.0.0.1:${server.address().port}`;
    await new Promise(resolve => other.listen(0, '127.0.0.1', resolve)); otherBase = `http://127.0.0.1:${other.address().port}`;
    const results = [];
    try {
        const scenarios = require('./corpus/scenarios.cjs')(base).filter(scenario => (!options.case || scenario.id === options.case) && (!scenario.managers || scenario.managers.includes(options.manager)));
        if (!scenarios.length) throw new Error('Unknown corpus case.');
        for (const scenario of scenarios) {
            const source = manifest.scripts[scenario.script];
            const caseDirectory = path.join(directory, scenario.id); fs.mkdirSync(caseDirectory);
            const input = { ...scenario, source: path.resolve(root, source.sourcePath), downloads: path.join(caseDirectory, 'downloads') }; fs.mkdirSync(input.downloads);
            const start = requests.length;
            let actual;
            if (options.manager === 'tampermonkey') actual = await reference(input, options, caseDirectory);
            else {
                const inputFile = path.join(caseDirectory, 'input.json'), report = path.join(caseDirectory, 'native.json'); fs.writeFileSync(inputFile, JSON.stringify(input));
                await new Promise(resolve => execFile(path.resolve(options.host), ['--corpus', inputFile, report], { windowsHide: true, timeout: 90000, encoding: 'buffer' }, (error, stdout, stderr) => {
                    fs.writeFileSync(path.join(caseDirectory, 'stdout.log'), stdout || ''); fs.writeFileSync(path.join(caseDirectory, 'stderr.log'), stderr || '');
                    actual = fs.existsSync(report) ? JSON.parse(fs.readFileSync(report)) : { status: 'blocked', error: String(error || 'Missing native report'), checks: {}, observations: {} };
                    const phaseFile = report + '.phase.json';
                    actual.nativeProcess = { code: error?.code ?? 0, signal: error?.signal || null, killed: error?.killed || false,
                        lastPhase: fs.existsSync(phaseFile) ? JSON.parse(fs.readFileSync(phaseFile)) : null };
                    if (error && actual.status === 'passed') { actual.status = 'failed'; actual.error = 'Native process failed after producing its report: ' + String(error); }
                    resolve();
                }));
            }
            const files = fs.readdirSync(input.downloads).filter(file => fs.statSync(path.join(input.downloads, file)).isFile()).map(name => {
                const bytes = fs.readFileSync(path.join(input.downloads, name)); return { name, bytes: bytes.length, sha256: hash(bytes), ...(name.endsWith('.zip') ? { entries: zipEntries(bytes) } : {}) };
            });
            const result = { schemaVersion: 1, caseId: scenario.id, fixtureHash: source.sha256, actionHash: hash(JSON.stringify(scenario.actions)),
                contractSource: [source.sourceUrl], environment: { manager: options.manager === 'monkeysharp' ? 'MonkeySharp' : 'Tampermonkey', managerVersion: null, chromiumVersion: null, manifestVersion: null,
                    ...actual.environment, ...inputSnapshot,
                    platform: actual.environment?.platform || process.arch, world: options.manager === 'monkeysharp' ? 'raw' : 'manager-selected/default', capabilities: {}, settings: actual.environment?.settings || { trustedPageWorld: true, testCookiesOnly: true } },
                immediateReturn: {}, events: actual.events || [], promise: {},
                effects: { observations: actual.observations, checks: actual.checks, files, requests: requests.slice(start), diagnostics: actual.diagnostics, failedAction: actual.failedAction, nativeProcess: actual.nativeProcess },
                status: actual.status, ...(actual.error ? { reason: actual.error, reasonCategory: 'runtime-or-host' } : {}) };
            if (actual.environment && (options['expected-cef-version'] && actual.environment.cefSharpVersion?.split('.').slice(0, 3).join('.') !== options['expected-cef-version'] ||
                options['expected-platform'] && actual.environment.platform !== options['expected-platform'])) {
                result.status = 'failed'; result.reasonCategory = 'environment'; result.reason = 'The native engine/platform did not match the requested build.';
            }
            if (scenario.id === 'download' && actual.status === 'passed' && !files.some(file => file.sha256 === hash(png))) { result.status = 'failed'; result.reason = 'Saved file did not match the controlled PNG bytes.'; }
            if (scenario.id === 'download-cancel' && actual.status === 'passed') {
                const canceled = options.manager === 'tampermonkey' ? actual.observations.browserDownloads.some(item => item.state === 'canceled') :
                    actual.observations.downloadTransitions?.some(item => item.state === 'aborted');
                if (!canceled) { result.status = 'failed'; result.reason = 'No actual canceled download was observed; an empty output directory alone does not prove cancellation.'; }
            }
            if (scenario.id === 'authenticated-xhr' && actual.status === 'passed' && !result.effects.requests.some(request => request.method === 'HEAD' && request.path === '/protected/image.png' && request.cookie.includes('corpus-auth=fixture-only'))) { result.status = 'failed'; result.reason = 'No authenticated redirected HEAD request reached the server.'; }
            if (scenario.id === 'authenticated-xhr' && actual.status === 'passed' && !files.some(file => file.entries?.some(entry => entry.sha256 === hash(png)))) { result.status = 'failed'; result.reason = 'Authenticated XHR archive did not contain the controlled response bytes.'; }
            if (scenario.id === 'page-hook' && result.status === 'passed' && actual.observations.hookBeforeFirstInline !== true) { result.status = 'expected-engine-limitation'; result.reason = 'History hook works after startup but was not installed before first inline.'; result.reasonCategory = 'engine-timing'; }
            if (scenario.id === 'dom-decoration-csp' && result.status === 'passed' && actual.observations.styleApplied !== true) { result.status = 'expected-engine-limitation'; result.reason = 'Source created its live DOM toolbar, but page style-src blocked its inline styles.'; result.reasonCategory = 'engine-csp'; }
            if (scenario.script === 'find-scripts' && result.status === 'failed' && actual.environment?.chromiumVersion?.startsWith('84.') &&
                actual.observations?.engineFeatures?.replaceAll === 'undefined' && actual.failedAction?.name === 'settingsPanel' &&
                actual.diagnostics?.some(message => message.includes('replaceAll is not a function'))) {
                result.status = 'expected-engine-limitation'; result.reasonCategory = 'javascript-engine';
                result.reason = 'The unchanged upstream settings UI requires String.prototype.replaceAll, which this Chromium 84 engine does not provide. ' + result.reason;
            }
            if (scenario.id === 'authenticated-xhr-denied' && result.status === 'passed') {
                const mark = result.effects.requests.findIndex(request => request.path === '/phase/denial-start');
                if (mark < 0 || result.effects.requests.slice(mark + 1).some(request => request.path.startsWith('/protected/'))) { result.status = 'failed'; result.reason = 'Denied GM requests produced a network effect.'; }
            }
            if (options.manager === 'tampermonkey' && result.status === 'failed' && (actual.failedAction?.kind === 'files' || scenario.id === 'download-cancel') && actual.observations.downloadsPermission === false) {
                result.status = 'blocked'; result.reasonCategory = 'reference-environment';
                result.reason = 'The browser has not granted Tampermonkey downloads permission; no saved-file parity result is available. ' + result.reason;
            }
            results.push(result); fs.writeFileSync(path.join(directory, 'results.json'), JSON.stringify({ schemaVersion: 1, manager: options.manager, results }, null, 2) + '\n');
            console.log(JSON.stringify({ caseId: scenario.id, status: result.status, reason: result.reason, files }));
        }
    } finally { await new Promise(resolve => server.close(resolve)); await new Promise(resolve => other.close(resolve)); }
    console.log(JSON.stringify({ output: path.join(directory, 'results.json'), cases: results.length }));
    if (results.some(result => ['failed', 'blocked'].includes(result.status))) process.exitCode = 1;
}
module.exports = { validateCorpus };
if (require.main === module) main().catch(error => { console.error(error); process.exitCode = 1; });
