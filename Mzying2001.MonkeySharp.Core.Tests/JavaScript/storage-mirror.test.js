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
        deliveryToken: "storage-delivery", event: "value-change",
        data: { listenerId: globalThis.__storageListener, key: "count", oldValue: 2, newValue: 3, remote: true }
    }), true);
    assert.deepEqual(globalThis.__storageChange, ["count", 2, 3, true]);
});
