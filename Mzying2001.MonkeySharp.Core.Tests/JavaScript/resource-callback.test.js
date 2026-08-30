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
                reject(new Error("Timed out waiting for callback facade state."));
            }
        }, 5);
    });
}

test("legacy resource and XHR facades use snapshots and abort handles", async () => {
    globalThis.__MonkeySharpBridge = {
        dispatch: async requestJson => {
            const request = JSON.parse(requestJson);
            if (request.type === "hello") {
                return JSON.stringify({
                    type: "hello-result", protocol: 1, ok: true,
                    limits: {}, apis: ["GM.getResourceText", "GM.getResourceURL", "GM.xmlHttpRequest"],
                    compatibility: {
                        profile: "LegacyCompatible", strict: false, legacyGlobals: true,
                        resources: {
                            complete: true,
                            values: { template: { text: "fixture text", url: "data:text/plain;base64,ZA==" } }
                        }
                    }
                });
            }
            if (request.method === "GM.xmlHttpRequest") {
                globalThis.__MonkeySharpRuntime.receive({
                    type: "notification", protocol: 1,
                    executionId: "callback-execution", deliveryToken: "callback-delivery",
                    event: "xhr-progress",
                    data: { xhrId: request.params.xhrId, loaded: 4, total: 8 }
                });
            }
            return JSON.stringify({
                type: "response", protocol: 1, requestId: request.requestId, ok: true,
                result: request.method === "GM.xmlHttpRequest"
                    ? { status: 200, responseText: "done" }
                    : { $monkeySharpType: "undefined" }
            });
        }
    };

    const payload = {
        protocol: 1, documentId: "callback-document", frameId: "main", runAt: "DocumentEnd",
        invocations: [{
            executionId: "callback-execution",
            scriptKey: "55555555-5555-5555-5555-555555555555",
            source: [
                "globalThis.__resourceText = GM_getResourceText('template');",
                "globalThis.__resourceUrl = GM_getResourceURL('template');",
                "globalThis.__xhrHandle = GM_xmlhttpRequest({",
                "  url: 'https://api.example.com/data',",
                "  onprogress: value => globalThis.__callbackProgress = value,",
                "  onload: value => globalThis.__callbackLoaded = value",
                "});",
                "globalThis.__handleType = typeof __xhrHandle.abort;"
            ].join("\n"),
            declaredGrants: ["GM_getResourceText", "GM_getResourceURL", "GM_xmlhttpRequest"],
            grants: ["GM.getResourceText", "GM.getResourceURL", "GM.xmlHttpRequest"],
            info: {}, capability: "callback-capability", deliveryToken: "callback-delivery",
            compatibility: { profile: "LegacyCompatible", strict: false, legacyGlobals: true }
        }]
    };
    const encoded = Buffer.from(JSON.stringify(payload), "utf8").toString("base64");
    vm.runInThisContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", encoded));

    await waitFor(() => globalThis.__callbackLoaded);
    assert.equal(globalThis.__resourceText, "fixture text");
    assert.equal(globalThis.__resourceUrl, "data:text/plain;base64,ZA==");
    assert.equal(globalThis.__handleType, "function");
    assert.deepEqual(globalThis.__callbackProgress, { loaded: 4, total: 8 });
    assert.equal(globalThis.__callbackLoaded.status, 200);
});

test("legacy menu and tab facades preserve synchronous registration and callbacks", async () => {
    let commandId;
    globalThis.__MonkeySharpBridge = {
        dispatch: async requestJson => {
            const request = JSON.parse(requestJson);
            if (request.type === "hello") {
                return JSON.stringify({
                    type: "hello-result", protocol: 1, ok: true, limits: {},
                    apis: ["GM.registerMenuCommand", "GM.getTab"]
                });
            }
            if (request.method === "GM.registerMenuCommand") commandId = request.params.commandId;
            return JSON.stringify({
                type: "response", protocol: 1, requestId: request.requestId, ok: true,
                result: request.method === "GM.getTab" ? { saved: true } : commandId
            });
        }
    };
    const payload = {
        protocol: 1, documentId: "menu-document", frameId: "main", runAt: "DocumentEnd",
        invocations: [{
            executionId: "menu-execution", scriptKey: "66666666-6666-6666-6666-666666666666",
            source: [
                "globalThis.__menuId = GM_registerMenuCommand('Open', () => globalThis.__menuCalled = true);",
                "GM_getTab(value => globalThis.__tabState = value);"
            ].join("\n"),
            declaredGrants: ["GM_registerMenuCommand", "GM_getTab"],
            grants: ["GM.registerMenuCommand", "GM.getTab"], info: {},
            capability: "menu-capability", deliveryToken: "menu-delivery",
            compatibility: { profile: "LegacyCompatible", strict: false, legacyGlobals: true }
        }]
    };
    const encoded = Buffer.from(JSON.stringify(payload), "utf8").toString("base64");
    vm.runInThisContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", encoded));
    await waitFor(() => globalThis.__tabState);
    assert.equal(typeof globalThis.__menuId, "number");
    assert.deepEqual(globalThis.__tabState, { saved: true });
    assert.equal(globalThis.__MonkeySharpRuntime.receive({
        type: "notification", protocol: 1, executionId: "menu-execution",
        deliveryToken: "menu-delivery", event: "menu-command", data: { commandId }
    }), true);
    assert.equal(globalThis.__menuCalled, true);
});
