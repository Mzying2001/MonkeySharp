const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function harness(maxPending = 64) {
    const timers = new Map();
    const sent = [];
    let timerId = 0;
    const proof = { generation: 'test', documentId: 'document', binding: 'nativeBinding', maxPending, timeout: 30000 };
    const directory = path.resolve(__dirname, '../../Mzying2001.MonkeySharp.CefSharp/Js');
    const source = fs.readFileSync(path.join(directory, 'Isolated.js'), 'utf8')
        .replace('__MONKEYSHARP_ISOLATED_BASE64__', Buffer.from(JSON.stringify(proof)).toString('base64'))
        .replace('/*__MONKEYSHARP_LIFECYCLE_EVENTS__*/', fs.readFileSync(path.join(directory, 'LifecycleEvents.js'), 'utf8'));
    const context = vm.createContext({
        atob, TextDecoder, Uint8Array,
        document: { body: null, readyState: 'loading', addEventListener() {}, removeEventListener() {} },
        addEventListener() {}, removeEventListener() {},
        MutationObserver: class { observe() {} disconnect() {} },
        nativeBinding: value => sent.push(JSON.parse(value)),
        setTimeout(callback, delay) { const id = ++timerId; timers.set(id, { callback, delay }); return id; },
        clearTimeout: id => timers.delete(id)
    });
    vm.runInContext(source, context);
    const transport = context.__MonkeySharpIsolatedTransport;
    transport.ready();
    return { transport, sent, timers };
}

for (const request of [
    { type: 'hello' },
    { method: 'GM.notification', params: {} },
    { method: 'GM.xmlHttpRequest', params: { operation: 'execute' } },
    { method: 'GM.xmlHttpRequest', params: { operation: 'readBody' } }
]) test(`host owns deadlines for ${request.type || request.params.operation || request.method}`, async () => {
    const h = harness();
    const pending = h.transport.dispatch(JSON.stringify(request));
    const rejection = pending.catch(error => error);
    // Simulate the entire ordinary deadline elapsing while consent or body data waits.
    for (const timer of h.timers.values()) if (timer.delay <= 30000) timer.callback();
    assert.equal(h.timers.size, 0, 'transport must not start a competing pre-authorization deadline');
    h.transport.complete(h.sent[0].id, 'host response');
    assert.equal(await pending, 'host response');
    await rejection;
});

test('pending limit, host cancellation and document cleanup release isolated waits', async () => {
    const h = harness(2);
    const first = h.transport.dispatch('{}');
    const second = h.transport.dispatch('{}');
    const ended = assert.rejects(second, /document ended/);
    await assert.rejects(h.transport.dispatch('{}'), /Too many/);
    h.transport.complete(h.sent[0].id, '{"ok":false,"error":{"code":"MSP004_CANCELED"}}');
    assert.equal(JSON.parse(await first).ok, false);
    const next = h.transport.dispatch('{}');
    const nextEnded = assert.rejects(next, /document ended/);
    h.transport.dispose();
    await Promise.all([ended, nextEnded]);
    await assert.rejects(h.transport.dispatch('{}'), /document ended/);
    assert.equal(h.timers.size, 0);
});
