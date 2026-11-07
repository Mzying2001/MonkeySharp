const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const template = require('./bootstrap-template');

async function install(dispatch, document) {
    let api;
    const logs = [], requests = [];
    const grants = ['GM.log', 'GM.addStyle', 'GM.addElement', 'GM.openInTab', 'GM.getTab', 'GM.saveTab', 'GM.getTabs'];
    const context = vm.createContext({ atob, TextDecoder, crypto, URL, document, location: { href: 'https://example.com/path/page' },
        console: { log: (...args) => logs.push(args), error() {}, warn() {} }, capture: value => { api = value; } });
    const notify = data => context.__MonkeySharpRuntime.receive({ type: 'notification', protocol: 2,
        executionId: 'page', deliveryToken: 'delivery', event: 'tab-closed', data });
    context.__MonkeySharpBridge = { dispatch: async json => {
        const r = JSON.parse(json);
        if (r.type === 'hello') return { type: 'hello-result', protocol: 2, ok: true, apis: grants };
        requests.push(r);
        return { type: 'response', protocol: 2, requestId: r.requestId, ok: true,
            result: await dispatch?.(r, notify) ?? { $monkeySharpType: 'undefined' } };
    } };
    const payload = { protocol: 2, documentId: 'document', frameId: 'main', invocations: [{ executionId: 'page', scriptKey: 'script',
        capability: 'proof', deliveryToken: 'delivery', grants, declaredGrants: grants.concat(['GM_log', 'GM_addElement', 'GM_addStyle', 'GM_openInTab', 'GM_saveTab']),
        info: {}, source: 'capture({GM, log: GM_log, element: GM_addElement, style: GM_addStyle, tab: GM_openInTab, save: GM_saveTab});' }] };
    vm.runInContext(template.replace('__MONKEYSHARP_PAYLOAD_BASE64__', Buffer.from(JSON.stringify(payload)).toString('base64')), context);
    while (!api) await tick();
    return { ...api, logs, requests, notify };
}
const tick = () => new Promise(resolve => setTimeout(resolve, 5));

test('logs preserve original console arguments and cycles while host failure cannot reject the local log', async () => {
    const h = await install(() => { throw new Error('sink failed'); });
    const cyclic = {}; cyclic.self = cyclic;
    Object.defineProperty(cyclic, 'getter', { enumerable: true, get() { throw new Error('must not inspect'); } });
    assert.equal(await h.GM.log(cyclic, 'second', 3), undefined);
    assert.equal(h.log(cyclic, 'legacy'), undefined);
    await tick();
    assert.equal(h.logs.length, 2);
    assert.equal(h.logs[0][0], cyclic);
    assert.deepEqual(h.logs[0].slice(1), ['second', 3]);
    assert.equal(h.requests[0].params.value[0].self, '[Circular]');
    assert.equal(h.requests[0].params.value[0].getter, '[Accessor]');
});

test('DOM helpers use head, explicit parent and documentElement fallback with node/Promise returns', async () => {
    const container = () => ({ appendChild(node) { node.parentNode = this; return node; } });
    const doc = { head: container(), body: container(), documentElement: container(),
        createElement: tagName => ({ tagName, setAttribute(name, value) { this[name] = value; } }) };
    const h = await install(undefined, doc);
    const legacy = h.element('span', { textContent: 'legacy' });
    assert.equal(legacy.parentNode, doc.head);
    const modern = h.GM.addElement(doc.body, 'span', { textContent: 'modern' });
    assert.equal(typeof modern.then, 'function');
    assert.equal((await modern).parentNode, doc.body);
    assert.equal(h.style('body {}').parentNode, doc.head);
    doc.head = null; doc.body = null;
    assert.equal(h.element('span', {}).parentNode, doc.documentElement);
    assert.equal((await h.GM.addStyle('html {}')).parentNode, doc.documentElement);
    assert.equal(h.requests.length, 0);
});

test('tabs normalize both overloads and relative URLs, queue early close, and correlate pre-ack closure once', async () => {
    let acknowledge;
    const h = await install((r, notify) => {
        if (r.params.close) { notify({ tabId: r.params.tabId }); notify({ tabId: r.params.tabId }); return true; }
        if (r.params.url.endsWith('/early')) return new Promise(resolve => { acknowledge = () => resolve({ id: 'early' }); });
        if (r.params.url.endsWith('/closed')) { notify({ tabId: 'closed', clientId: r.params.clientId }); return { id: 'closed', closed: true }; }
        return { id: 'tab' };
    });
    const early = h.tab('../early', false);
    let count = 0; early.onclose = function () { assert.equal(this, early); count++; };
    assert.equal(early.close(), undefined); early.close();
    acknowledge(); await tick();
    assert.equal(early.closed, true); assert.equal(count, 1);
    assert.deepEqual(Object.keys(early).sort(), ['close', 'closed', 'onclose']);
    assert.equal(h.requests[0].params.url, 'https://example.com/early');
    assert.equal(h.requests[0].params.active, true);
    await h.GM.openInTab('/modern', true);
    assert.equal(h.requests.at(-1).params.active, false);
    await h.GM.openInTab('/index', { insert: 0, setParent: true, loadInBackground: false });
    assert.equal(h.requests.at(-1).params.insert, 0);
    assert.equal(h.requests.at(-1).params.active, true);
    assert.equal(h.requests.at(-1).params.setParent, true);
    assert.equal((await h.GM.openInTab('/closed')).closed, true);
    const before = h.requests.length;
    assert.throws(() => h.GM.openInTab('/bad', { incognito: true }), { code: 'MSP006_NOT_SUPPORTED' });
    assert.throws(() => h.GM.openInTab('/bad', { insert: -1 }));
    assert.equal(h.requests.length, before);
});

test('tab-state queue preserves save/read order and snapshots the value before delayed host commit', async () => {
    let finish, stored = {};
    const h = await install(async r => {
        if (r.method === 'GM.saveTab') { await new Promise(resolve => { finish = resolve; }); stored = r.params.value; }
        if (r.method === 'GM.getTab') return stored;
    });
    const value = { marker: 1 }; let callback = 0;
    assert.equal(h.save(value, () => callback++), undefined);
    value.marker = 2;
    const read = h.GM.getTab();
    await tick(); assert.deepEqual(h.requests.map(r => r.method), ['GM.saveTab']);
    finish(); assert.equal((await read).marker, 1);
    assert.equal(callback, 1);
    assert.throws(() => h.GM.saveTab([]));
});
