const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const template = fs.readFileSync(path.resolve(__dirname, "../../Mzying2001.MonkeySharp.Core/Js/Init.js"), "utf8");

function waitFor(predicate) {
    return new Promise((resolve, reject) => {
        const deadline = Date.now() + 2000;
        const timer = setInterval(() => {
            if (predicate()) { clearInterval(timer); resolve(); }
            else if (Date.now() > deadline) { clearInterval(timer); reject(new Error("Timed out waiting for webRequest facade.")); }
        }, 5);
    });
}

test("GM.webRequest and GM_webRequest register rules and route events", async () => {
    const calls = [];
    globalThis.__MonkeySharpBridge = {
        dispatch: async json => {
            const request = JSON.parse(json);
            if (request.type === "hello") return JSON.stringify({ type: "hello-result", protocol: 1, ok: true, limits: {}, apis: ["GM.webRequest"] });
            calls.push(request);
            let result = { $monkeySharpType: "undefined" };
            if (request.params.operation === "addRule") result = request.params.rule.id;
            if (request.params.operation === "addListener") result = request.params.listenerId;
            if (request.params.operation === "removeRule" || request.params.operation === "removeListener") result = true;
            return JSON.stringify({ type: "response", protocol: 1, requestId: request.requestId, ok: true, result });
        }
    };
    const payload = { protocol: 1, documentId: "web-document", frameId: "main", runAt: "DocumentEnd", invocations: [{
        executionId: "web-execution", scriptKey: "77777777-7777-7777-7777-777777777777",
        source: [
            "globalThis.__webPromise = (async function () {",
            "  globalThis.__webId = await GM.webRequest.addRule({id:'block', phase:'OnBeforeRequest', filter:{urlPatterns:['https://example.com/*']}, action:{kind:'Block'}});",
            "  globalThis.__webListener = GM.webRequest.addListener({}, event => globalThis.__webEvent = event);",
            "  globalThis.__legacyWeb = GM_webRequest([{id:'legacy', phase:'OnCompleted', filter:{}, action:{kind:'Allow'}}], event => globalThis.__legacyEvent = event);",
            "})();"
        ].join("\n"),
        grants: ["GM.webRequest"], declaredGrants: ["GM_webRequest"], info: {}, capability: "web-capability", deliveryToken: "web-delivery",
        compatibility: { profile: "LegacyCompatible", strict: false, legacyGlobals: true }
    }]};
    vm.runInThisContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", Buffer.from(JSON.stringify(payload)).toString("base64")));
    await waitFor(() => globalThis.__webPromise);
    await globalThis.__webPromise;
    assert.equal(globalThis.__webId, "block");
    assert.ok(calls.some(item => item.params.operation === "addListener"));
    assert.equal(typeof globalThis.__legacyWeb.remove, "function");
    assert.equal(globalThis.__MonkeySharpRuntime.receive({
        type: "notification", protocol: 1, executionId: "web-execution", deliveryToken: "web-delivery",
        event: "webrequest-event", data: { listenerId: globalThis.__webListener, phase: "OnCompleted", requestId: 3, url: "https://example.com/" }
    }), true);
    assert.equal(globalThis.__webEvent.requestId, 3);
});
