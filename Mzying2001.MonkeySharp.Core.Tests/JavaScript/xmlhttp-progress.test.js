const { waitFor } = require('./test-wait');
const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const template = require('./bootstrap-template');

async function install(read, options = {}) {
    let api, runtime;
    const requests = [];
    const context = vm.createContext({ atob, btoa, crypto, Blob, URL, TextDecoder, TextEncoder,
        AbortController, ReadableStream, setTimeout, clearTimeout, console, capture: value => { api = value; } });
    context.__MonkeySharpBridge = { dispatch: async json => {
        const r = JSON.parse(json), p = r.params;
        if (r.type === 'hello') return { type: 'hello-result', protocol: 2, ok: true, apis: ['GM.xmlHttpRequest'] };
        requests.push(p);
        let result = true;
        if (p.operation === 'create') result = { sessionId: 'session' };
        else if (p.operation === 'execute') result = { status: 200, statusText: 'OK', finalUrl: 'https://example.com/',
            responseHeaders: '', charset: options.charset || 'utf-8', streaming: true, bodyLength: null };
        else if (p.operation === 'readBody') result = await read(p);
        return { type: 'response', protocol: 2, requestId: r.requestId, ok: true, result };
    } };
    const payload = { protocol: 2, documentId: 'doc', frameId: 'main', invocations: [{ executionId: 'progress', scriptKey: 'script',
        capability: 'proof', deliveryToken: 'delivery', declaredGrants: ['GM.xmlHttpRequest'], grants: ['GM.xmlHttpRequest'],
        info: {}, source: 'capture(GM.xmlHttpRequest);' }] };
    vm.runInContext(template.replace('__MONKEYSHARP_PAYLOAD_BASE64__', Buffer.from(JSON.stringify(payload)).toString('base64')), context);
    await waitFor(() => api, 'XHR progress bootstrap');
    runtime = context.__MonkeySharpRuntime;
    return { api, requests, notify(event, data) { runtime.receive({ type: 'notification', protocol: 2,
        executionId: 'progress', deliveryToken: 'delivery', event, data: { xhrId: requests[0].xhrId, ...data } }); } };
}

test('text progress precedes completion and uses one decoder across split UTF-8 bytes', async () => {
    let finish, prefix;
    const waiting = new Promise(resolve => { finish = resolve; });
    const observed = new Promise(resolve => { prefix = resolve; });
    const bytes = Buffer.from('a你好');
    let active = 0, maximum = 0;
    const h = await install(async p => {
        maximum = Math.max(maximum, ++active);
        if (p.offset) await waiting;
        active--;
        const end = p.offset ? bytes.length : 3; // first UTF-8 character is incomplete
        return { offset: p.offset, chunk: bytes.subarray(p.offset, end).toString('base64'), done: end === bytes.length, total: null };
    });
    const events = [];
    const operation = h.api({ url: 'https://example.com/', onprogress(value) {
        assert.equal(value.readyState, 3);
        assert.equal(value.response, value.responseText);
        assert.equal(value.lengthComputable, false);
        events.push(value.responseText); prefix();
    }, onload() { events.push('load'); } });
    await observed;
    assert.deepEqual(events, ['a']);
    finish();
    assert.equal((await operation).responseText, 'a你好');
    assert.deepEqual(events, ['a', 'a你好', 'load']);
    assert.equal(maximum, 1);
    assert.equal(h.requests.filter(p => p.operation === 'readBody').length, 2);
});

test('empty responses, charset replacement and json keep final response semantics', async () => {
    for (const [bytes, charset, type, expected] of [
        [Buffer.alloc(0), 'utf-8', 'text', ''],
        [Buffer.from([99, 97, 102, 233]), 'windows-1252', 'text', 'café'],
        [Buffer.from([0xff]), 'utf-8', 'text', '\ufffd'],
        [Buffer.from('{"ok":true}'), 'utf-8', 'json', '{"ok":true}']
    ]) {
        const progress = [];
        const h = await install(async p => ({ offset: p.offset, chunk: bytes.toString('base64'), done: true }), { charset });
        const result = await h.api({ url: 'https://example.com/', responseType: type,
            onprogress(value) { progress.push(value.response); } });
        assert.equal(result.responseText, expected);
        if (type === 'json') { assert.equal(result.response.ok, true); assert.equal(progress[0], null); }
    }
});

test('incremental errors settle once and never report load after native failure', async () => {
    let rejectRead, started;
    const ready = new Promise(resolve => { started = resolve; });
    const h = await install(() => new Promise((_, reject) => { rejectRead = reject; started(); }));
    const events = [];
    const operation = h.api({ url: 'https://example.com/', onerror() { events.push('error'); },
        onloadend() { events.push('end'); }, onload() { events.push('load'); } });
    await ready;
    h.notify('xhr-error', { code: 'MSP999_INTERNAL', message: 'network failed' });
    rejectRead(new Error('session released'));
    await assert.rejects(operation);
    assert.deepEqual(events, ['error', 'end']);
});
