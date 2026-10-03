const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const template = fs.readFileSync(
    path.resolve(__dirname, "../../Mzying2001.MonkeySharp.Core/Js/Init.js"), "utf8");

function install(payload, bridge) {
    globalThis.__MonkeySharpBridge = bridge;
    vm.runInThisContext(template.replace(
        "__MONKEYSHARP_PAYLOAD_BASE64__",
        Buffer.from(JSON.stringify(payload), "utf8").toString("base64")));
}

function waitFor(predicate) {
    return new Promise((resolve, reject) => {
        const deadline = Date.now() + 2000;
        const timer = setInterval(() => {
            if (predicate()) {
                clearInterval(timer);
                resolve();
            } else if (Date.now() > deadline) {
                clearInterval(timer);
                reject(new Error("Timed out waiting for window facade state."));
            }
        }, 5);
    });
}

test("window.onurlchange dispatches property and event-listener handlers", async () => {
    const listeners = new Map();
    globalThis.addEventListener = (name, handler) => listeners.set(name, handler);
    globalThis.removeEventListener = name => listeners.delete(name);
    globalThis.__urlEvents = [];
    install({
        protocol: 1, documentId: "window-document", frameId: "main", runAt: "DocumentEnd",
        invocations: [{
            executionId: "window-execution", scriptKey: "11111111-1111-1111-1111-111111111111",
            source: [
                "window.onurlchange = info => globalThis.__urlEvents.push(['property', info.url, info.oldURL]);",
                "window.addEventListener('urlchange', info => globalThis.__urlEvents.push(['event', info.url, info.oldURL]));",
                "globalThis.__urlReady = window.onurlchange === null ? false : true;"
            ].join("\n"),
            declaredGrants: ["window.onurlchange"], grants: ["window.onurlchange"],
            grantDeclarationState: "ExplicitList", info: {}, capability: "window-capability",
            deliveryToken: "window-delivery", compatibility: { profile: "LegacyCompatible", strict: false, legacyGlobals: true }
        }]
    }, {
        dispatch: async requestJson => {
            const request = JSON.parse(requestJson);
            if (request.type === "hello") return JSON.stringify({
                type: "hello-result", protocol: 1, ok: true, limits: {}, apis: []
            });
            return JSON.stringify({ type: "response", protocol: 1, requestId: request.requestId,
                ok: true, result: { $monkeySharpType: "undefined" } });
        }
    });
    await waitFor(() => globalThis.__urlReady);
    assert.equal(typeof listeners.get("urlchange"), "undefined");
    globalThis.__MonkeySharpRuntime.urlChanged({ url: "https://example.test/new", oldURL: "https://example.test/old" });
    assert.deepEqual(globalThis.__urlEvents, [
        ["property", "https://example.test/new", "https://example.test/old"],
        ["event", "https://example.test/new", "https://example.test/old"]
    ]);
});

test("window.close and window.focus are grant-gated bridge methods", async () => {
    const methods = [];
    globalThis.__windowPromise = null;
    install({
        protocol: 1, documentId: "window-api-document", frameId: "main", runAt: "DocumentEnd",
        invocations: [{
            executionId: "window-api-execution", scriptKey: "22222222-2222-2222-2222-222222222222",
            source: "globalThis.__windowPromise = Promise.all([window.close(), window.focus()]);",
            declaredGrants: ["window.close", "window.focus"], grants: ["window.close", "window.focus"],
            grantDeclarationState: "ExplicitList", info: {}, capability: "window-api-capability",
            deliveryToken: "window-api-delivery", compatibility: { profile: "ModernStrict", strict: true, legacyGlobals: false }
        }]
    }, {
        dispatch: async requestJson => {
            const request = JSON.parse(requestJson);
            if (request.type === "hello") return JSON.stringify({
                type: "hello-result", protocol: 1, ok: true, limits: {},
                apis: ["window.close", "window.focus"]
            });
            methods.push(request.method);
            return JSON.stringify({ type: "response", protocol: 1, requestId: request.requestId,
                ok: true, result: true });
        }
    });
    await waitFor(() => globalThis.__windowPromise);
    assert.deepEqual(await globalThis.__windowPromise, [true, true]);
    assert.deepEqual(methods.sort(), ["window.close", "window.focus"]);
});
