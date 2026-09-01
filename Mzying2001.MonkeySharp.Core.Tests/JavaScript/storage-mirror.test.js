const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const template = fs.readFileSync(
    path.resolve(__dirname, "../../Mzying2001.MonkeySharp.Core/Js/Init.js"), "utf8");

function waitFor(predicate) {
    return new Promise((resolve, reject) => {
        const deadline = Date.now() + 2000;
        const timer = setInterval(() => {
            if (predicate()) {
                clearInterval(timer);
                resolve();
            } else if (Date.now() > deadline) {
                clearInterval(timer);
                reject(new Error("Timed out waiting for storage mirror state."));
            }
        }, 5);
    });
}

test("legacy storage APIs read and mutate the execution-local mirror synchronously", async () => {
    const methods = [];
    globalThis.__MonkeySharpBridge = {
        dispatch: async requestJson => {
            const request = JSON.parse(requestJson);
            if (request.type === "hello") {
                return JSON.stringify({
                    type: "hello-result", protocol: 1, ok: true,
                    limits: {},
                    apis: ["GM.getValue", "GM.setValue", "GM.deleteValue", "GM.listValues",
                        "GM.addValueChangeListener", "GM.removeValueChangeListener"],
                    compatibility: {
                        profile: "LegacyCompatible",
                        strict: false,
                        legacyGlobals: true,
                        storage: { complete: true, values: { count: 2, nullable: null } }
                    }
                });
            }
            methods.push(request.method);
            return JSON.stringify({
                type: "response", protocol: 1, requestId: request.requestId, ok: true,
                result: { $monkeySharpType: "undefined" }
            });
        }
    };

    const payload = {
        protocol: 1,
        documentId: "storage-document",
        frameId: "main",
        runAt: "DocumentEnd",
        invocations: [{
            executionId: "storage-execution",
            scriptKey: "44444444-4444-4444-4444-444444444444",
            source: [
                "globalThis.__storageBefore = GM_getValue('count', 0);",
                "globalThis.__storageNull = GM_getValue('nullable', 'fallback');",
                "globalThis.__storageListener = GM_addValueChangeListener('count', (...args) => globalThis.__storageChange = args);",
                "GM_setValue('count', globalThis.__storageBefore + 1);",
                "globalThis.__storageAfter = GM_getValue('count', 0);",
                "globalThis.__storageSyncHandlerCalls = 0;",
                "globalThis.__readStorageRemote = () => GM_getValue('remote');",
                "globalThis.__storageKeys = GM_listValues();"
            ].join("\n"),
            declaredGrants: ["GM_getValue", "GM_setValue", "GM_listValues", "GM_addValueChangeListener"],
            grants: ["GM.getValue", "GM.setValue", "GM.listValues", "GM.addValueChangeListener"],
            info: {},
            capability: "storage-capability",
            deliveryToken: "storage-delivery",
            compatibility: { profile: "LegacyCompatible", strict: false, legacyGlobals: true }
        }]
    };

    const encoded = Buffer.from(JSON.stringify(payload), "utf8").toString("base64");
    vm.runInThisContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", encoded));
    await waitFor(() => globalThis.__storageKeys);

    assert.equal(globalThis.__storageBefore, 2);
    assert.equal(globalThis.__storageAfter, 3);
    assert.equal(globalThis.__storageNull, null);
    assert.deepEqual(globalThis.__storageKeys, ["count", "nullable"]);
    await waitFor(() => methods.includes("GM.setValue"));

    assert.equal(globalThis.__MonkeySharpRuntime.receive({
        type: "notification", protocol: 1, executionId: "storage-execution",
        deliveryToken: "storage-delivery", event: "storage-sync",
        data: { key: "remote", oldValue: { $monkeySharpType: "undefined" }, newValue: 4, sequence: 2 }
    }), true);
    assert.equal(globalThis.__readStorageRemote(), 4);
    assert.equal(globalThis.__storageSyncHandlerCalls, 0);

    assert.equal(globalThis.__MonkeySharpRuntime.receive({
        type: "notification", protocol: 1, executionId: "storage-execution",
        deliveryToken: "storage-delivery", event: "storage-sync",
        data: { key: "remote", oldValue: 4, newValue: 5, sequence: 1 }
    }), true);
    assert.equal(globalThis.__readStorageRemote(), 4);

    assert.equal(globalThis.__MonkeySharpRuntime.receive({
        type: "notification", protocol: 1, executionId: "storage-execution",
        deliveryToken: "storage-delivery", event: "value-change",
        data: { listenerId: globalThis.__storageListener, key: "count", oldValue: 2, newValue: 3, remote: true }
    }), true);
    assert.deepEqual(globalThis.__storageChange, ["count", 2, 3, true]);
});

test("failed compatibility mutations roll back without publishing a false local change", async () => {
    const methods = [];
    let setAttempts = 0;
    globalThis.__MonkeySharpBridge = {
        dispatch: async requestJson => {
            const request = JSON.parse(requestJson);
            if (request.type === "hello") {
                return JSON.stringify({
                    type: "hello-result", protocol: 1, ok: true,
                    limits: {},
                    apis: ["GM.getValue", "GM.setValue", "GM.addValueChangeListener"],
                    compatibility: {
                        profile: "LegacyCompatible",
                        strict: false,
                        legacyGlobals: true,
                        storage: { complete: true, values: { count: 1 } }
                    }
                });
            }
            methods.push(request.method);
            if (request.method === "GM.setValue" && ++setAttempts === 1) {
                return JSON.stringify({
                    type: "response", protocol: 1, requestId: request.requestId, ok: false,
                    error: { code: "MSP999_INTERNAL", message: "write failed" }
                });
            }
            const result = request.method === "runtime.getStorageSnapshot"
                ? { complete: true, values: { count: 1 } }
                : { $monkeySharpType: "undefined" };
            return JSON.stringify({
                type: "response", protocol: 1, requestId: request.requestId, ok: true, result: result
            });
        }
    };

    const payload = {
        protocol: 1,
        documentId: "failed-storage-document",
        frameId: "main",
        runAt: "DocumentEnd",
        invocations: [{
            executionId: "failed-storage-execution",
            scriptKey: "55555555-5555-5555-5555-555555555555",
            source: [
                "globalThis.__failedStorageChanges = [];",
                "GM_addValueChangeListener('count', (...args) => globalThis.__failedStorageChanges.push(args));",
                "GM_setValue('count', 2);",
                "globalThis.__failedStorageOptimistic = GM_getValue('count');",
                "globalThis.__readFailedStorage = () => GM_getValue('count');",
                "globalThis.__setRecoveredStorage = value => GM_setValue('count', value);"
            ].join("\n"),
            declaredGrants: ["GM_getValue", "GM_setValue", "GM_addValueChangeListener"],
            grants: ["GM.getValue", "GM.setValue", "GM.addValueChangeListener"],
            info: {},
            capability: "failed-storage-capability",
            deliveryToken: "failed-storage-delivery",
            compatibility: { profile: "LegacyCompatible", strict: false, legacyGlobals: true }
        }]
    };

    const encoded = Buffer.from(JSON.stringify(payload), "utf8").toString("base64");
    vm.runInThisContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", encoded));

    await waitFor(() => methods.includes("runtime.reportError"));
    assert.equal(globalThis.__failedStorageOptimistic, 2);
    assert.equal(globalThis.__readFailedStorage(), 1);
    assert.deepEqual(globalThis.__failedStorageChanges, []);

    globalThis.__setRecoveredStorage(3);
    await waitFor(() => setAttempts === 2 && globalThis.__failedStorageChanges.length === 1);
    assert.equal(globalThis.__readFailedStorage(), 3);
    assert.deepEqual(globalThis.__failedStorageChanges, [["count", 1, 3, false]]);
});

test("storage reconcile reapplies optimistic mutations still waiting in the queue", async () => {
    const methods = [];
    let setAttempts = 0;
    globalThis.__MonkeySharpBridge = {
        dispatch: async requestJson => {
            const request = JSON.parse(requestJson);
            if (request.type === "hello") {
                return JSON.stringify({
                    type: "hello-result", protocol: 1, ok: true,
                    limits: {},
                    apis: ["GM.getValue", "GM.setValue", "GM.addValueChangeListener"],
                    compatibility: {
                        profile: "LegacyCompatible", strict: false, legacyGlobals: true,
                        storage: { complete: true, values: { count: 1 } }
                    }
                });
            }
            methods.push(request.method);
            if (request.method === "GM.setValue" && ++setAttempts === 1) {
                return JSON.stringify({
                    type: "response", protocol: 1, requestId: request.requestId, ok: false,
                    error: { code: "MSP999_INTERNAL", message: "write failed" }
                });
            }
            const result = request.method === "runtime.getStorageSnapshot"
                ? { complete: true, values: { count: 1 } }
                : { $monkeySharpType: "undefined" };
            return JSON.stringify({
                type: "response", protocol: 1, requestId: request.requestId, ok: true, result: result
            });
        }
    };

    const payload = {
        protocol: 1,
        documentId: "queued-storage-document",
        frameId: "main",
        runAt: "DocumentEnd",
        invocations: [{
            executionId: "queued-storage-execution",
            scriptKey: "66666666-6666-6666-6666-666666666666",
            source: [
                "globalThis.__queuedStorageChanges = [];",
                "GM_addValueChangeListener('count', (...args) => globalThis.__queuedStorageChanges.push(args));",
                "GM_setValue('count', 2);",
                "GM_setValue('count', 3);",
                "globalThis.__queuedStorageOptimistic = GM_getValue('count');",
                "globalThis.__readQueuedStorage = () => GM_getValue('count');"
            ].join("\n"),
            declaredGrants: ["GM_getValue", "GM_setValue", "GM_addValueChangeListener"],
            grants: ["GM.getValue", "GM.setValue", "GM.addValueChangeListener"],
            info: {}, capability: "queued-storage-capability", deliveryToken: "queued-storage-delivery",
            compatibility: { profile: "LegacyCompatible", strict: false, legacyGlobals: true }
        }]
    };

    const encoded = Buffer.from(JSON.stringify(payload), "utf8").toString("base64");
    vm.runInThisContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", encoded));

    await waitFor(() => setAttempts === 2 && globalThis.__queuedStorageChanges.length === 1);
    assert.equal(globalThis.__queuedStorageOptimistic, 3);
    assert.equal(globalThis.__readQueuedStorage(), 3);
    assert.deepEqual(globalThis.__queuedStorageChanges, [["count", 1, 3, false]]);
    assert.equal(methods.filter(method => method === "runtime.getStorageSnapshot").length, 1);
});
