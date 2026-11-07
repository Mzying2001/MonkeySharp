const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const template = require('./bootstrap-template');

async function install(dispatch = () => undefined) {
    let api;
    const requests = [];
    const context = vm.createContext({ crypto, atob, TextDecoder, Event, console: { error() {} }, capture: value => { api = value; } });
    const notify = (event, data) => context.__MonkeySharpRuntime.receive({ type: 'notification', protocol: 2,
        executionId: 'ui', deliveryToken: 'delivery', event, data });
    const grants = ['notification', 'registerMenuCommand', 'unregisterMenuCommand', 'setClipboard'];
    context.__MonkeySharpBridge = { dispatch: async json => {
        const request = JSON.parse(json);
        if (request.type === 'hello') return { type: 'hello-result', protocol: 2, ok: true, apis: grants.map(name => 'GM.' + name) };
        requests.push(request);
        const result = await dispatch(request, notify);
        return { type: 'response', protocol: 2, requestId: request.requestId, ok: true,
            result: result === undefined ? { $monkeySharpType: 'undefined' } : result };
    } };
    const payload = { protocol: 2, documentId: 'document', frameId: 'main', invocations: [{ executionId: 'ui', scriptKey: 'script',
        capability: 'proof', deliveryToken: 'delivery', grants: grants.map(name => 'GM.' + name),
        declaredGrants: grants.flatMap(name => ['GM.' + name, 'GM_' + name]), info: {},
        source: 'capture({ GM, notification: GM_notification, menu: GM_registerMenuCommand, remove: GM_unregisterMenuCommand, clipboard: GM_setClipboard });' }] };
    vm.runInContext(template.replace('__MONKEYSHARP_PAYLOAD_BASE64__', Buffer.from(JSON.stringify(payload)).toString('base64')), context);
    await tick();
    return { ...api, requests, notify };
}
const tick = () => new Promise(resolve => setTimeout(resolve, 10));

test('notification waits for terminal UI and acknowledges preventDefault despite callback errors', async () => {
    const h = await install();
    const events = [];
    const pending = h.GM.notification({ text: 'body', tag: 'tag', url: 'https://example.com/',
        onclick(event) { events.push('click'); event.preventDefault(); throw new Error('callback'); },
        ondone() { events.push('done'); throw new Error('callback'); } });
    let settled = false;
    pending.then(() => { settled = true; events.push('resolved'); });
    await tick();
    assert.equal(settled, false);
    const id = h.requests[0].params.clientId;
    h.notify('notification-click', { clientId: id });
    assert.equal(h.notify('notification-click', { clientId: id }), false);
    await tick();
    assert.equal(h.requests.find(request => request.params.operation === 'click-result').params.preventDefault, true);
    assert.equal(settled, false);
    h.notify('notification-done', { clientId: id, clicked: true });
    assert.equal(await pending, true);
    assert.equal(h.notify('notification-done', { clientId: id }), false);
    assert.deepEqual(events, ['click', 'done', 'resolved']);
});

test('early notification close before start acknowledgment settles false once', async () => {
    const h = await install((request, notify) => {
        if (request.method === 'GM.notification') notify('notification-done', { clientId: request.params.clientId, clicked: false });
    });
    let done = 0;
    assert.equal(await h.GM.notification({ text: 'early' }, () => done++), false);
    assert.equal(done, 1);
    assert.equal(h.notification('positional', 'title', 'image', () => {}), undefined);
    assert.equal(h.requests.at(-1).params.title, 'title');
});

test('tag replacements preserve call order across delayed authorization acknowledgements', async () => {
    let acknowledge;
    let oldId;
    const h = await install((request, notify) => {
        if (request.method !== 'GM.notification') return;
        if (request.params.title === 'old') {
            oldId = request.params.clientId;
            return new Promise(resolve => { acknowledge = resolve; });
        }
        notify('notification-done', { clientId: oldId, clicked: false });
        notify('notification-done', { clientId: request.params.clientId, clicked: true });
    });
    const old = h.GM.notification({ text: 'old', title: 'old', tag: 'same' });
    const replacement = h.GM.notification({ text: 'new', title: 'new', tag: 'same' });
    assert.deepEqual(h.requests.map(request => request.params.title), ['old']);
    acknowledge();
    assert.equal(await old, false);
    assert.equal(await replacement, true);
    assert.deepEqual(h.requests.map(request => request.params.title), ['old', 'new']);
});

test('menu updates are serialized, keep IDs and remove returns undefined', async () => {
    const h = await install();
    let first = 0, updated = 0;
    const id = h.menu('first', () => first++, { id: 'stable', title: 'hint', autoClose: false, accessKey: 'x' });
    assert.equal(id, 'stable');
    assert.equal(await h.GM.registerMenuCommand('updated', () => updated++, { id, title: 'new' }), id);
    h.notify('menu-command', { commandId: id });
    assert.equal(first, 0); assert.equal(updated, 1);
    assert.equal(h.requests[0].params.autoClose, false);
    assert.equal(h.requests[1].params.title, 'new');
    assert.equal(h.remove(id), undefined);
    assert.equal(h.notify('menu-command', { commandId: id }), false);
    assert.equal(await h.GM.unregisterMenuCommand(id), undefined);
    assert.deepEqual(h.requests.map(request => request.method), ['GM.registerMenuCommand', 'GM.registerMenuCommand', 'GM.unregisterMenuCommand', 'GM.unregisterMenuCommand']);
});

test('clipboard normalizes text HTML info and completes callbacks after host write', async () => {
    let finish;
    const h = await install(request => request.method === 'GM.setClipboard' ? new Promise(resolve => { finish = resolve; }) : undefined);
    let calls = 0;
    const pending = h.GM.setClipboard('<b>hello</b>', { type: 'html', mimetype: 'text/html' }, () => { calls++; throw new Error('callback'); });
    assert.equal(calls, 0);
    assert.equal(h.requests[0].params.type, 'text/html');
    finish();
    assert.equal(await pending, undefined);
    assert.equal(calls, 1);
    assert.equal(h.clipboard('plain', 'text', () => calls++), undefined);
    assert.equal(h.requests.at(-1).params.type, 'text/plain');
    finish(); await tick();
    assert.equal(calls, 2);
    await assert.rejects(h.GM.setClipboard('invalid', { mimetype: 'image/png' }));
    assert.equal(h.requests.filter(request => request.method === 'GM.setClipboard').length, 2);
});
