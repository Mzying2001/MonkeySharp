const { waitFor } = require('./test-wait');
const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const { File } = require('node:buffer');
const template = require('./bootstrap-template');

async function install() {
    let api;
    const requests = [];
    const types = [];
    const context = vm.createContext({ atob, btoa, crypto, Blob, File, URL, fetch, TextDecoder, TextEncoder,
        AbortController, ReadableStream, setTimeout, clearTimeout, console, capture: value => { api = value; },
        DOMParser: class { parseFromString(text, type) { types.push(type); return { text, type, querySelector: () => null }; } } });
    context.__MonkeySharpBridge = { dispatch: async json => {
        const r = JSON.parse(json);
        if (r.type === 'hello') return { type: 'hello-result', protocol: 2, ok: true, apis: ['GM.xmlHttpRequest'] };
        requests.push(r);
        throw new Error('Renderer binary requests must not cross the native HTTP bridge');
    } };
    const payload = { protocol: 2, documentId: 'doc', frameId: 'main', invocations: [{ executionId: 'binary', scriptKey: 'script',
        capability: 'proof', deliveryToken: 'delivery', declaredGrants: ['GM.xmlHttpRequest'], grants: ['GM.xmlHttpRequest'],
        info: {}, source: 'capture(GM.xmlHttpRequest);' }] };
    vm.runInContext(template.replace('__MONKEYSHARP_PAYLOAD_BASE64__', Buffer.from(JSON.stringify(payload)).toString('base64')), context);
    await waitFor(() => api, 'renderer binary bootstrap');
    return { api, requests, types };
}

test('renderer Blob/File/blob URL preserve bytes, split multibyte text, charset, HTML and callback receiver', async () => {
    const h = await install();
    const text = 'x'.repeat(65535) + '你好';
    const context = { original: true };
    const events = [];
    const operation = h.api({ url: new File([text], 'text.txt'), context,
        onload(value) { assert.equal(this, value); assert.equal(value.context, context); events.push('load'); },
        onloadend() { events.push('end'); } });
    assert.equal(typeof operation.abort, 'function');
    assert.equal((await operation).responseText, text);
    assert.deepEqual(events, ['load', 'end']);
    const latin = await h.api({ url: new Blob([Uint8Array.of(99, 97, 102, 233)], { type: 'text/plain;charset=windows-1252' }) });
    assert.equal(latin.responseText, 'café');
    const html = await h.api({ url: new Blob(['<title>html</title>'], { type: 'text/html' }), responseType: 'document' });
    assert.equal(html.response.type, 'text/html');
    const url = URL.createObjectURL(new Blob([Uint8Array.of(0, 255, 1)]));
    try { assert.deepEqual([...new Uint8Array((await h.api({ url, responseType: 'arraybuffer' })).response)], [0, 255, 1]); }
    finally { URL.revokeObjectURL(url); }
    assert.equal(h.requests.length, 0);
});

test('renderer stream pulls bounded chunks and immediate abort emits one terminal pair', async () => {
    const h = await install();
    const bytes = Uint8Array.from({ length: 150003 }, (_, i) => i % 251);
    const stream = (await h.api({ url: new Blob([bytes]), responseType: 'stream' })).response.getReader();
    const chunks = [];
    for (;;) { const part = await stream.read(); if (part.done) break; assert.ok(part.value.length <= 65536); chunks.push(part.value); }
    assert.deepEqual(Buffer.concat(chunks), Buffer.from(bytes));
    const events = [];
    const aborted = h.api({ url: new Blob([bytes]), onabort() { events.push('abort'); }, onloadend() { events.push('end'); } });
    aborted.abort(); aborted.abort();
    await assert.rejects(aborted, { name: 'AbortError' });
    assert.deepEqual(events, ['abort', 'end']);
    assert.throws(() => h.api({ url: new Blob([]), proxy: {} }), { code: 'MSP006_NOT_SUPPORTED' });
    assert.equal(h.requests.length, 0);
});
