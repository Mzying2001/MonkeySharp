const { waitFor } = require('./test-wait');
const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const completePlan = require('./test-plan');
const template = require('./bootstrap-template');

async function harness() {
    let window;
    const errors = [];
    const context = vm.createContext({ atob, TextDecoder, crypto, AbortController,
        console: { error: (...args) => errors.push(args) }, capture: value => { window = value; },
        __MonkeySharpBridge: { dispatch: async () => ({ protocol: 2, type: 'hello-result', ok: true, apis: [] }) } });
    const payload = completePlan({ protocol: 2, documentId: 'url-document', frameId: 'main', invocations: [{
        executionId: 'url-execution', scriptKey: 'script', capability: 'proof', deliveryToken: 'delivery',
        grants: ['window.onurlchange'], declaredGrants: ['window.onurlchange'], info: {}, source: 'capture(window);'
    }] });
    vm.runInContext(template.replace('__MONKEYSHARP_PAYLOAD_BASE64__', Buffer.from(JSON.stringify(payload)).toString('base64')), context);
    await waitFor(() => window, 'URL facade bootstrap');
    assert.ok(window);
    const runtime = context.__MonkeySharpRuntime;
    let previous = '/start';
    return { window, errors, runtime, change(url) { runtime.urlChanged({ url, oldURL: previous }); previous = url; } };
}

test('URL once, capture identity, listener objects and removal share the page registry', async () => {
    const h = await harness();
    const calls = [];
    function listener(info) { assert.equal(this, h.window); calls.push(info.url); }
    const options = { once: true };
    h.window.addEventListener('urlchange', listener, options);
    options.once = false;
    h.window.addEventListener('urlchange', listener, { once: false }); // duplicate ignored
    h.window.addEventListener('urlchange', listener, true); // distinct capture identity
    let objects = 0;
    const object = { handleEvent() { assert.equal(this, object); objects++; } };
    h.window.addEventListener('urlchange', object);
    h.change('/one'); h.change('/two');
    assert.deepEqual(calls, ['/one', '/one', '/two']);
    h.window.removeEventListener('urlchange', listener); // only non-capture
    h.window.removeEventListener('urlchange', listener, true);
    h.window.removeEventListener('urlchange', object);
    h.change('/three');
    assert.equal(objects, 2);
    assert.equal(calls.length, 3);
});

test('URL signals, exceptions, dispatch mutations, duplicate URLs and document cleanup are isolated', async () => {
    const h = await harness();
    const calls = [];
    const preAborted = new AbortController(); preAborted.abort();
    h.window.addEventListener('urlchange', () => calls.push('pre-aborted'), { signal: preAborted.signal });
    const during = new AbortController();
    const later = () => calls.push('added-later');
    const removed = () => calls.push('removed');
    h.window.onurlchange = function (info) {
        assert.equal(this, h.window); assert.equal(info.oldURL, '/start');
        calls.push('property'); h.window.onurlchange = null; throw new Error('property failed');
    };
    h.window.addEventListener('urlchange', () => {
        calls.push('first'); during.abort();
        h.window.removeEventListener('urlchange', removed);
        h.window.addEventListener('urlchange', later);
        throw new Error('listener failed');
    }, { once: true });
    h.window.addEventListener('urlchange', () => calls.push('aborted-during'), { signal: during.signal });
    h.window.addEventListener('urlchange', removed);
    h.window.addEventListener('urlchange', info => calls.push(info.url + ':' + info.oldURL));
    h.change('/one');
    assert.deepEqual(calls, ['property', 'first', '/one:/start']);
    h.runtime.urlChanged({ url: '/one', oldURL: '/start' });
    assert.equal(calls.length, 3);
    h.change('/two');
    assert.deepEqual(calls.slice(3), ['/two:/one', 'added-later']);
    assert.equal(h.errors.length, 2);
    h.runtime.endDocument('url-document');
    h.change('/three');
    assert.equal(calls.length, 5);
    assert.equal(h.window.onurlchange, null);
});
