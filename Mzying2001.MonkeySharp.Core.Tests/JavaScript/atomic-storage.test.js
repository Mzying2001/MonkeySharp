const { waitFor } = require('./test-wait');
const test = require("node:test");
const assert = require("node:assert/strict");
const vm = require("node:vm");
const template = require("./bootstrap-template");
const completePlan = require("./test-plan");

for (const mode of ["complete", "missing", "duplicate", "truncated", "gap", "incomplete"]) {
    test(`atomic snapshot and replay gate source: ${mode}`, async () => {
        const snapshot = JSON.stringify({ complete: true, sequence: 1, entries: [["key", "sold"]] });
        const parts = Array.from({ length: Math.ceil(snapshot.length / 19) }, (_, index) => snapshot.slice(index * 19, (index + 1) * 19));
        const change = { sequence: 2, key: "key", oldValueExists: true, oldValue: "sold", newValueExists: true, newValue: "snew", originExecutionId: "remote" };
        const errors = [];
        let live = false;
        let freshSnapshot = false;
        let context;
        const deliver = data => context.__MonkeySharpRuntime.receive({ protocol: 2, type: "notification", executionId: "execution", deliveryToken: "delivery", event: "storage-sync", data });
        context = vm.createContext({ atob, TextDecoder, crypto, console: { error: (...args) => errors.push(args), warn: () => {} },
            __MonkeySharpBridge: { dispatch: async input => {
                const request = JSON.parse(input);
                const response = result => ({ protocol: 2, type: "response", requestId: request.requestId, ok: true, result });
                if (request.type === "hello") return { protocol: 2, type: "hello-result", ok: true,
                    apis: ["GM.getValue"], compatibility: { storage: mode === "incomplete" ? { complete: false } : {
                        complete: false, sequence: 1, snapshotId: "snapshot", chunkCount: parts.length, jsonLength: snapshot.length } } };
                if (request.method === "runtime.getStorageChunk") {
                    // A committed notification arrives while the pinned snapshot is still being transferred.
                    if (request.params.index === 0) deliver(change);
                    if (mode === "missing") return { ...response(null), ok: false, error: { code: "MSP003_SESSION_EXPIRED", message: "Navigation" } };
                    return response({ snapshotId: "snapshot", index: mode === "duplicate" ? 0 : request.params.index,
                        chunk: mode === "truncated" && request.params.index === parts.length - 1 ? "" : parts[request.params.index] });
                }
                if (request.method === "runtime.getStorageChanges") {
                    if (live && !freshSnapshot) return response({ complete: false, sequence: 4 });
                    return response(live ? { complete: true, sequence: 4, changes: [] } : {
                        complete: true, sequence: mode === "gap" ? 3 : 2, changes: [{ ...change, sequence: mode === "gap" ? 3 : 2 }, change] });
                }
                if (request.method === "runtime.getStorageSnapshot") { freshSnapshot = true; return response({ complete: true, sequence: 4, entries: [["key", "sfour"]] }); }
                throw new Error(`Unexpected request ${request.method}`);
            } }
        });
        const plan = completePlan({ protocol: 2, documentId: "doc", frameId: "main", invocations: [{
            executionId: "execution", scriptKey: "script", capability: "proof", deliveryToken: "delivery",
            grants: ["GM.getValue"], declaredGrants: ["GM_getValue"], info: {},
            source: "globalThis.firstValue = GM_getValue('key'); globalThis.readValue = () => GM_getValue('key');"
        }] });
        vm.runInContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", Buffer.from(JSON.stringify(plan)).toString("base64")), context);
        await waitFor(() => context.readValue || errors.length, 'storage bootstrap result');
        if (mode !== "complete") { assert.equal(context.readValue, undefined); assert.equal(errors.length, 1); return; }
        assert.equal(context.firstValue, "new");
        live = true;
        deliver({ ...change, sequence: 4, newValue: "sfour" });
        await waitFor(() => context.readValue() === 'four', 'storage replay');
        assert.equal(context.readValue(), "four");
        assert.equal(freshSnapshot, true);
        assert.equal(errors.length, 0);
    });
}
