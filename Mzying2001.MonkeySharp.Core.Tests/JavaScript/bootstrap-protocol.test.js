const completePlan = require("./test-plan");
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const templatePath = path.resolve(__dirname, "../../Mzying2001.MonkeySharp.Core/Js/Init.js");
const template = require("./bootstrap-template");

function waitFor(predicate) {
    return new Promise((resolve, reject) => {
        const started = Date.now();
        const timer = setInterval(() => {
            if (predicate()) {
                clearInterval(timer);
                resolve();
            } else if (Date.now() - started > 2000) {
                clearInterval(timer);
                reject(new Error("Timed out waiting for bootstrap state."));
            }
        }, 5);
    });
}

function executePayload(payload) {
    const encoded = Buffer.from(JSON.stringify(completePlan(payload)), "utf8").toString("base64");
    vm.runInThisContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", encoded));
}

test("bootstrap exposes only granted Promise APIs and authenticates notifications", async () => {
    const values = new Map();
    const journal = [];
    const methods = [];
    const capability = "capability-token";
    globalThis.__MonkeySharpBridge = {
        dispatch: async requestJson => {
            const request = JSON.parse(requestJson);
            assert.equal(request.protocol, 2);
            assert.equal(request.documentId, "document");
            assert.equal(request.scriptKey, "11111111-1111-1111-1111-111111111111");
            assert.equal(request.capability, capability);
            if (request.type === "hello") {
                return JSON.stringify({
                    type: "hello-result",
                    protocol: 2,
                    ok: true,
                    limits: {},
                    apis: ["GM.getValue", "GM.setValue", "GM.addValueChangeListener"],
                    compatibility: { storage: { complete: true, sequence: 0, entries: [] } }
                });
            }
            if (request.method === "runtime.getStorageChanges") return JSON.stringify({ protocol: 2, type: "response", ok: true, requestId: request.requestId, result: { complete: true, sequence: journal.length, changes: journal.filter(change => change.sequence > request.params.afterSequence) } });
            methods.push(request.method);
            let result = { $monkeySharpType: "undefined" };
            if (request.method === "GM.setValue") {
                values.set(request.params.key, request.params.value);
                journal.push({ sequence: journal.length + 1, key: request.params.key, newValue: request.params.value, newValueExists: true,
                    oldValueExists: false, mutationId: request.requestId, originExecutionId: "execution" });
                result = { sequence: journal.length };
            }
            if (request.method === "GM.getValue") result = values.has(request.params.key)
                ? { exists: true, value: values.get(request.params.key) }
                : { exists: false, value: null };
            if (request.method === "GM.addValueChangeListener") result = true;
            return JSON.stringify({
                type: "response",
                protocol: 2,
                requestId: request.requestId,
                ok: true,
                result
            });
        }
    };

    executePayload({
        protocol: 2,
        documentId: "document",
        frameId: "main",
        runAt: "DocumentEnd",
        invocations: [{
            executionId: "execution",
            scriptKey: "11111111-1111-1111-1111-111111111111",
            source: [
                "globalThis.__bootstrapPromise = (async function () {",
                "  await GM.setValue('theme', { dark: true });",
                "  globalThis.__stored = await GM.getValue('theme');",
                "  globalThis.__legacyType = typeof GM_getValue;",
                "  globalThis.__listenerId = await GM.addValueChangeListener('theme', (...args) => globalThis.__change = args);",
                "})();"
            ].join("\n"),
            grants: ["GM.getValue", "GM.setValue", "GM.addValueChangeListener"],
            info: { name: "fixture" },
            capability,
            deliveryToken: "delivery-token"
        }]
    });

    await waitFor(() => globalThis.__bootstrapPromise);
    await globalThis.__bootstrapPromise;
    await waitFor(() => methods.includes("GM.addValueChangeListener"));
    assert.deepEqual(globalThis.__stored, { dark: true });
    assert.equal(globalThis.__legacyType, "function");
    assert.deepEqual(methods.slice(0, 2), ["GM.setValue", "GM.addValueChangeListener"]);

    assert.equal(globalThis.__MonkeySharpRuntime.receive({
        type: "notification",
        protocol: 2,
        executionId: "execution",
        deliveryToken: "forged",
        event: "value-change",
        data: { listenerId: globalThis.__listenerId }
    }), false);
    assert.equal(globalThis.__MonkeySharpRuntime.receive({
        type: "notification",
        protocol: 2,
        executionId: "execution",
        deliveryToken: "delivery-token",
        event: "value-change",
        data: {
            listenerId: globalThis.__listenerId,
            key: "theme",
            oldValue: null, oldValueExists: false,
            newValue: "sdark", newValueExists: true,
            remote: true
        }
    }), true);
    assert.deepEqual(globalThis.__change, ["theme", undefined, "dark", true]);

    executePayload({
        protocol: 2,
        documentId: "document",
        frameId: "main",
        runAt: "DocumentEnd",
        invocations: [{
            executionId: "grant-none",
            scriptKey: "11111111-1111-1111-1111-111111111111",
            source: "globalThis.__grantNoneType = typeof GM; globalThis.__grantNoneInfoType = typeof GM.info; globalThis.__grantNoneDone = true;",
            grants: ["none"],
            grantDeclarationState: "ExplicitNone",
            info: {},
            capability: "unused",
            deliveryToken: "unused"
        }]
    });
    await waitFor(() => globalThis.__grantNoneDone);
    assert.equal(globalThis.__grantNoneType, "object");
    assert.equal(globalThis.__grantNoneInfoType, "object");
});
