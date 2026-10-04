const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const template = fs.readFileSync(path.resolve(__dirname, "../../Mzying2001.MonkeySharp.Core/Js/Init.js"), "utf8");

test("GM.webRequest uses Tampermonkey selectors, actions, and listener arguments", async () => {
    const calls = [];
    globalThis.__MonkeySharpBridge = {
        dispatch: async json => {
            const request = JSON.parse(json);
            if (request.type === "hello") return JSON.stringify({ type: "hello-result", protocol: 1, ok: true, limits: {}, apis: ["GM.webRequest"] });
            calls.push(request);
            return JSON.stringify({ type: "response", protocol: 1, requestId: request.requestId, ok: true,
                result: request.params.operation === "register" ? { id: "registration-1", listenerId: request.params.listenerId } : true });
        }
    };
    const payload = { protocol: 1, documentId: "web-document", frameId: "main", runAt: "DocumentEnd", invocations: [{
        executionId: "web-execution", scriptKey: "77777777-7777-7777-7777-777777777777",
        source: [
            "globalThis.__webPromise = (async function () {",
            "  globalThis.__modern = GM.webRequest([{selector:'https://example.com/*', action:'cancel'}, {selector:{match:'https://example.com/*', exclude:'https://example.com/ignore'}, action:{from:'/old',to:'/new'}}], (info, message, details) => globalThis.__webArgs = [info, message, details]);",
            "  globalThis.__legacy = GM_webRequest({selector:'https://legacy.example/*', action:'cancel'}, () => globalThis.__legacyCalled = true);",
            "})();"
        ].join("\n"),
        grants: ["GM.webRequest"], declaredGrants: ["GM_webRequest"], info: {}, capability: "web-capability", deliveryToken: "web-delivery",
        compatibility: { profile: "LegacyCompatible", strict: false, legacyGlobals: true }
    }]};
    vm.runInThisContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", Buffer.from(JSON.stringify(payload)).toString("base64")));
    await globalThis.__webPromise;
    await new Promise(resolve => setTimeout(resolve, 10));
    assert.equal(calls.filter(item => item.params.operation === "register").length, 2);
    const registration = calls.find(item => item.params.operation === "register");
    assert.equal(registration.params.rules[0].action, "cancel");
    assert.deepEqual(registration.params.rules[1].action, { from: "/old", to: "/new" });
    assert.equal(typeof globalThis.__modern.remove, "function");
    assert.equal(typeof globalThis.__legacy.remove, "function");
    assert.equal(globalThis.__MonkeySharpRuntime.receive({
        type: "notification", protocol: 1, executionId: "web-execution", deliveryToken: "web-delivery",
        event: "webrequest-result", data: {
            listenerId: registration.params.listenerId,
            info: { requestId: 3, url: "https://example.com/old", method: "GET", type: "script" },
            message: "redirect", details: { redirectUrl: "https://example.com/new" }
        }
    }), true);
    assert.equal(globalThis.__webArgs[0].requestId, 3);
    assert.equal(globalThis.__webArgs[1], "redirect");
    assert.equal(globalThis.__webArgs[2].redirectUrl, "https://example.com/new");
    assert.equal(globalThis.__modern.id, "registration-1");
    globalThis.__modern.remove();
});
