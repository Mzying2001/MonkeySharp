const { waitFor, nextTurn } = require('./test-wait');
const test = require("node:test");
const assert = require("node:assert/strict");
const vm = require("node:vm");
const template = require("./bootstrap-template");

async function install(onRequest) {
    const requests = [];
    let api;
    const errors = [];
    const context = vm.createContext({ atob, btoa, TextDecoder, crypto, Blob, URL, fetch, AbortController,
        console: { error: (...args) => errors.push(args) }, capture: value => { api = value; } });
    const notify = (event, data) => context.__MonkeySharpRuntime.receive({ type: "notification", protocol: 2,
        executionId: "download", deliveryToken: "delivery", event, data });
    context.__MonkeySharpBridge = { dispatch: async json => {
        const request = JSON.parse(json);
        if (request.type === "hello") return { type: "hello-result", protocol: 2, ok: true, apis: ["GM.download"] };
        requests.push(request);
        const result = await onRequest?.(request, notify);
        return { type: "response", protocol: 2, requestId: request.requestId, ok: true,
            result: result === undefined ? { accepted: true } : result };
    } };
    const payload = { protocol: 2, documentId: "document", frameId: "main", invocations: [{
        executionId: "download", scriptKey: "script", capability: "proof", deliveryToken: "delivery",
        declaredGrants: ["GM.download", "GM_download"], grants: ["GM.download"], info: {},
        source: "capture({ modern: GM.download, legacy: GM_download });"
    }] };
    vm.runInContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", Buffer.from(JSON.stringify(payload)).toString("base64")), context);
    await waitFor(() => api);
    return { ...api, requests, notify, errors };
}
const starts = harness => harness.requests.filter(request => request.params.operation === "start");

test('download errors distinguish permission, expired execution and unsupported controls', async () => {
    for (const [code, reason] of [['MSP005_PERMISSION_DENIED', 'not_permitted'], ['MSP003_SESSION_EXPIRED', 'not_succeeded'], ['MSP006_NOT_SUPPORTED', 'not_supported']]) {
        const h = await install(() => { throw Object.assign(new Error('failure detail'), { code }); });
        const events = [];
        const promise = h.modern({ url: 'https://example.com/file', onerror(error) { events.push(error.error); } });
        await assert.rejects(promise, error => error.error === reason && error.details === 'failure detail' && error.code === code);
        events.push('rejected');
        assert.deepEqual(events, [reason, 'rejected']);
    }
});

test("modern download returns its abortable completion Promise before starting", async () => {
    const h = await install();
    const events = [];
    const details = { url: "https://example.com/file", name: "file.bin", anonymous: true,
        headers: { "X-Test": "yes" }, onprogress() { events.push("progress"); },
        onload(value) { assert.equal(this, details); events.push("load"); assert.equal(value.name, "file.bin"); } };
    const promise = h.modern(details);
    assert.equal(typeof promise.then, "function");
    assert.equal(typeof promise.abort, "function");
    assert.equal(promise.id, undefined);
    let settled = false;
    promise.then(() => { settled = true; events.push("settled"); });
    await waitFor(() => starts(h).length === 1);
    await Promise.resolve();
    assert.equal(settled, false);
    const request = starts(h)[0].params;
    assert.equal(request.anonymous, true);
    assert.deepEqual(request.headers, { "X-Test": "yes" });
    h.notify("download-progress", { clientId: request.clientId, loaded: 3, total: 3 });
    h.notify("download-complete", { clientId: request.clientId, result: { name: "file.bin", loaded: 3 } });
    assert.deepEqual(await promise, { name: "file.bin", loaded: 3 });
    assert.deepEqual(events, ["progress", "load", "settled"]);
    assert.equal(h.notify("download-error", { clientId: request.clientId }), false);
    assert.equal(promise.abort(), undefined);
    assert.equal(h.requests.filter(request => request.params.operation === "abort").length, 0);
});

test("abort before the start microtask prevents any host request", async () => {
    const h = await install();
    let errors = 0;
    const pending = h.modern({ url: "https://example.com/file", onerror: () => errors++ });
    const rejected = assert.rejects(pending, error => error.error === "not_succeeded");
    pending.abort(); pending.abort();
    await rejected;
    assert.equal(starts(h).length, 0);
    assert.equal(errors, 1);
});

test("an abort rejected before registration is retried after start acknowledgement", async () => {
    let release;
    let aborts = 0;
    const h = await install(request => {
        if (request.params.operation === "start") return new Promise(resolve => { release = resolve; });
        if (request.params.operation === "abort") return { accepted: ++aborts > 1 };
    });
    const pending = h.modern("https://example.com/file", "chosen.bin");
    const rejected = assert.rejects(pending, error => error.error === "not_succeeded");
    await waitFor(() => release);
    pending.abort(); pending.abort();
    await waitFor(() => aborts === 1);
    release({ id: "host-id" });
    await waitFor(() => aborts === 2);
    assert.equal(starts(h)[0].params.name, "chosen.bin");
    h.notify("download-aborted", { clientId: starts(h)[0].params.clientId, error: "not_succeeded" });
    await rejected;
});

test("zero-byte completion before the start reply is delivered exactly once", async () => {
    const h = await install((request, notify) => {
        if (request.params.operation === "start") {
            notify("download-complete", { clientId: request.params.clientId, result: { name: "empty", loaded: 0 } });
            return { id: "host" };
        }
    });
    let completed = 0;
    const result = await h.modern({ url: "https://example.com/empty", onload: () => completed++ });
    assert.equal(result.loaded, 0);
    assert.equal(completed, 1);
});

test("callback exceptions are reported without changing completion or invoking onerror", async () => {
    const h = await install();
    let failed = 0;
    const pending = h.modern({ url: "https://example.com/file", onprogress() { throw new Error("progress"); },
        onload() { throw new Error("load"); }, onerror() { failed++; } });
    await waitFor(() => starts(h).length);
    const clientId = starts(h)[0].params.clientId;
    h.notify("download-progress", { clientId, loaded: 1 });
    h.notify("download-complete", { clientId, result: { loaded: 1 } });
    await pending;
    assert.equal(failed, 0);
    assert.equal(h.requests.filter(request => request.method === "runtime.reportError").length, 2);
});

test("legacy uses the same operation and returns only a synchronous abort handle", async () => {
    const h = await install();
    let loaded;
    const handle = h.legacy({ url: "https://example.com/file", onload: result => { loaded = result; } });
    assert.equal(typeof handle.abort, "function");
    assert.equal(handle.then, undefined);
    assert.equal(handle.id, undefined);
    await waitFor(() => starts(h).length);
    h.notify("download-complete", { clientId: starts(h)[0].params.clientId, result: { name: "legacy.bin" } });
    assert.equal(loaded.name, "legacy.bin");
    assert.equal(handle.abort(), undefined);
});

test("timeout and start failure reject through one terminal callback", async () => {
    const h = await install();
    let timeouts = 0;
    let errors = 0;
    const pending = h.modern({ url: "https://example.com/file", ontimeout: () => timeouts++, onerror: () => errors++ });
    const rejected = assert.rejects(pending, error => error.error === "timeout");
    await waitFor(() => starts(h).length);
    h.notify("download-timeout", { clientId: starts(h)[0].params.clientId, error: "timeout" });
    await rejected;
    assert.equal(timeouts, 1); assert.equal(errors, 0);
    const bad = await install(() => { throw Object.assign(new Error("disabled"), { code: "MSP006_NOT_SUPPORTED" }); });
    await assert.rejects(bad.modern("https://example.com/file"), error => error.error === "not_supported");
});

test("unsupported local headers and malformed options fail before any request", async () => {
    const h = await install();
    for (const details of [{ url: new Blob(["binary"]), headers: { Test: 'no' } }, { url: "blob:renderer-owned" },
        { url: "https://example.com/file", anonymous: "yes" }, { url: "https://example.com/file", conflictAction: "discard" }]) {
        await assert.rejects(h.modern(details));
    }
    assert.equal(starts(h).length, 0);
});

test("Blob download stages bounded chunks with backpressure and releases on transfer failure", async () => {
    let acknowledge, appends = 0;
    const h = await install(request => {
        if (request.params.operation === "appendBody" && ++appends === 1) return new Promise(resolve => { acknowledge = resolve; });
        if (request.params.operation === "startBody") throw new Error("destination failed");
    });
    const bytes = Uint8Array.from({ length: 150003 }, (_, i) => i % 251);
    const pending = h.modern({ url: new Blob([bytes]), name: "blob.bin" });
    const rejected = assert.rejects(pending, error => error.details.includes("destination failed"));
    await waitFor(() => acknowledge);
    await nextTurn();
    assert.equal(appends, 1);
    acknowledge(); await rejected;
    const parts = h.requests.filter(r => r.params.operation === "appendBody").map(r => r.params);
    assert.deepEqual(parts.map(p => p.offset), [0, 65536, 131072]);
    assert.deepEqual(Buffer.concat(parts.map(p => Buffer.from(p.chunk, "base64"))), Buffer.from(bytes));
    assert.equal(h.requests.at(-1).params.operation, "releaseBody");
});
