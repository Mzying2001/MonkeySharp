const { waitFor } = require('./test-wait');
const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const template = require('./bootstrap-template');

async function install(hooks = {}) {
    let api;
    const requests = [], chunks = [];
    const context = vm.createContext({ atob, btoa, crypto, Blob, URL, TextDecoder, TextEncoder, Response,
        AbortController, ReadableStream, setTimeout, clearTimeout, console, capture: value => { api = value; } });
    context.__MonkeySharpBridge = { dispatch: async json => {
        const r = JSON.parse(json), p = r.params;
        if (r.type === 'hello') return { type: 'hello-result', protocol: 2, ok: true,
            limits: { binaryTransferBytes: hooks.budget || 256 * 1024 * 1024 }, apis: ['GM.xmlHttpRequest'] };
        requests.push(p);
        let result = true;
        if (p.operation === 'create') {
            if (hooks.denied) return { type: 'response', protocol: 2, requestId: r.requestId, ok: false,
                error: { code: 'MSP005_PERMISSION_DENIED', message: 'denied' } };
            result = { sessionId: 'session' };
        } else if (p.operation === 'appendBody') {
            const bytes = Buffer.from(p.chunk, 'base64');
            assert.ok(bytes.length <= 65536);
            chunks.push(bytes);
            if (hooks.append) await hooks.append();
            result = { length: chunks.reduce((sum, chunk) => sum + chunk.length, 0) };
        } else if (p.operation === 'execute') result = { status: 200, bodyLength: 0 };
        return { type: 'response', protocol: 2, requestId: r.requestId, ok: true, result };
    } };
    const payload = { protocol: 2, documentId: 'doc', frameId: 'main', invocations: [{ executionId: 'body', scriptKey: 'script',
        capability: 'proof', deliveryToken: 'delivery', declaredGrants: ['GM.xmlHttpRequest'], grants: ['GM.xmlHttpRequest'],
        info: {}, source: 'capture(GM.xmlHttpRequest);' }] };
    vm.runInContext(template.replace('__MONKEYSHARP_PAYLOAD_BASE64__', Buffer.from(JSON.stringify(payload)).toString('base64')), context);
    await waitFor(() => api, 'XHR stream bootstrap');
    return { api, requests, chunks, end: () => context.__MonkeySharpRuntime.endDocument('doc') };
}

test('unknown-length stream stages bounded chunks and waits for each append acknowledgement', async () => {
    let acknowledge, appended, pulls = 0;
    const ready = new Promise(resolve => { appended = resolve; });
    const gate = new Promise(resolve => { acknowledge = resolve; });
    const h = await install({ append: () => { appended(); return gate; } });
    const bytes = Uint8Array.from({ length: 150003 }, (_, index) => index % 251);
    const body = new ReadableStream({ pull(controller) { pulls++; if (pulls === 1) controller.enqueue(bytes); else controller.close(); } }, { highWaterMark: 0 });
    const operation = h.api({ url: 'https://example.com/', method: 'POST', data: body });
    await ready;
    assert.equal(pulls, 1);
    assert.equal(h.chunks.length, 1);
    acknowledge(); await operation;
    assert.deepEqual(Buffer.concat(h.chunks), Buffer.from(bytes));
    assert.equal(body.locked, false);
    assert.deepEqual(h.requests.map(p => p.operation), ['create', 'appendBody', 'appendBody', 'appendBody', 'execute', 'release']);
});

test('denial, locked and disturbed input never acquire a reader or execute a request', async () => {
    let reads = 0;
    const h = await install({ denied: true });
    const body = new ReadableStream({ pull() { reads++; } }, { highWaterMark: 0 });
    await assert.rejects(h.api({ url: 'https://example.com/', data: body }));
    assert.equal(reads, 0); assert.equal(body.locked, false);
    assert.deepEqual(h.requests.map(p => p.operation), ['create']);
    const locked = body.getReader();
    await assert.rejects(h.api({ url: 'https://example.com/', data: body }), { name: 'TypeError' });
    locked.releaseLock();
    const disturbed = new ReadableStream({ start(c) { c.enqueue(new Uint8Array([1])); c.close(); } });
    const reader = disturbed.getReader(); await reader.read(); reader.releaseLock();
    await assert.rejects(h.api({ url: 'https://example.com/', data: disturbed }), { name: 'TypeError' });
    assert.equal(h.requests.length, 1);
});

test('abort and document end unblock a pending producer and release staging', async () => {
    for (const navigate of [false, true]) {
        let pulling, canceled = 0;
        const ready = new Promise(resolve => { pulling = resolve; });
        const h = await install();
        const body = new ReadableStream({ pull() { pulling(); }, cancel() { canceled++; } }, { highWaterMark: 0 });
        const operation = h.api({ url: 'https://example.com/', data: body });
        await ready;
        if (navigate) h.end(); else operation.abort();
        await assert.rejects(operation, { name: 'AbortError' });
        assert.equal(canceled, 1); assert.equal(body.locked, false);
        assert.equal(h.requests.some(p => p.operation === 'execute'), false);
    }
});

test('zero bytes, bad chunk types, producer errors and unknown-length overflow settle cleanly', async () => {
    const empty = await install();
    await empty.api({ url: 'https://example.com/', data: new ReadableStream({ start(c) { c.close(); } }) });
    assert.equal(empty.chunks.length, 0);
    for (const kind of ['type', 'producer', 'budget']) {
        let canceled = 0;
        const h = await install({ budget: 10 });
        const body = new ReadableStream({ pull(c) {
            if (kind === 'producer') throw new Error('producer failed');
            c.enqueue(kind === 'type' ? 'bad' : new Uint8Array(11));
        }, cancel() { canceled++; } }, { highWaterMark: 0 });
        await assert.rejects(h.api({ url: 'https://example.com/', data: body }));
        assert.equal(body.locked, false);
        assert.equal(h.requests.some(p => p.operation === 'execute'), false);
        assert.equal(h.requests.at(-1).operation, 'release');
        assert.equal(canceled, kind === 'producer' ? 0 : 1);
    }
});

test('immediate abort consumes nothing and many small chunks preserve order', async () => {
    let reads = 0;
    const h = await install();
    const untouched = new ReadableStream({ pull() { reads++; } }, { highWaterMark: 0 });
    const aborted = h.api({ url: 'https://example.com/', data: untouched }); aborted.abort();
    await assert.rejects(aborted); assert.equal(reads, 0); assert.equal(untouched.locked, false);
    let index = 0;
    await h.api({ url: 'https://example.com/', data: new ReadableStream({ pull(controller) {
        if (index < 1000) controller.enqueue(new Uint8Array([index++ % 251])); else controller.close();
    } }, { highWaterMark: 0 }) });
    assert.deepEqual(Buffer.concat(h.chunks), Buffer.from(Array.from({ length: 1000 }, (_, i) => i % 251)));
});
