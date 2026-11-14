const { nextTurn } = require('./test-wait');
const completePlan = require("./test-plan");
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const template = require("./bootstrap-template");

test("GM.webRequest uses Tampermonkey selectors, actions, and listener arguments", async () => {
    const calls = [];
    globalThis.__MonkeySharpBridge = {
        dispatch: async json => {
            const request = JSON.parse(json);
            if (request.type === "hello") return JSON.stringify({ type: "hello-result", protocol: 2, ok: true, limits: {}, apis: ["GM.webRequest"] });
            calls.push(request);
            return JSON.stringify({ type: "response", protocol: 2, requestId: request.requestId, ok: true,
                result: request.params.operation === "register" ? { id: "registration-1", listenerId: request.params.listenerId } : true });
        }
    };
    const payload = { protocol: 2, documentId: "web-document", frameId: "main", runAt: "DocumentEnd", invocations: [{
        executionId: "web-execution", scriptKey: "77777777-7777-7777-7777-777777777777",
        source: [
            "globalThis.__webPromise = (async function () {",
            "  globalThis.__modern = GM.webRequest([{selector:'https://example.com/*', action:'cancel'}, {selector:{match:'https://example.com/*', exclude:'https://example.com/ignore'}, action:{redirect:{from:'/old',to:'/new'}}}], (info, message, details) => globalThis.__webArgs = [info, message, details]);",
            "  globalThis.__legacy = GM_webRequest([{selector:'https://legacy.example/*', action:'cancel'}], (info, message, details) => globalThis.__webArgs = [info, message, details]);",
            "})();"
        ].join("\n"),
        grants: ["GM.webRequest"], declaredGrants: ["GM_webRequest"], info: {}, capability: "web-capability", deliveryToken: "web-delivery",
    }]};
    vm.runInThisContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", Buffer.from(JSON.stringify(completePlan(payload))).toString("base64")));
    await globalThis.__webPromise;
    await nextTurn();
    assert.equal(calls.filter(item => item.params.operation === "register").length, 2);
    const registration = calls.filter(item => item.params.operation === "register")[1];
    assert.equal(registration.params.rules[0].action, "cancel");
    assert.deepEqual(calls[0].params.rules[1].action, { redirect: { from: "/old", to: "/new" } });
    assert.equal(globalThis.__modern, undefined);
    assert.equal(globalThis.__legacy, undefined);
    assert.equal(globalThis.__MonkeySharpRuntime.receive({
        type: "notification", protocol: 2, executionId: "web-execution", deliveryToken: "web-delivery",
        event: "webrequest-result", data: {
            listenerId: registration.params.listenerId,
            info: "redirect", message: "ok", details: { url: "https://example.com/old", redirect_url: "https://example.com/new" }
        }
    }), true);
    assert.equal(globalThis.__webArgs[0], "redirect");
    assert.equal(globalThis.__webArgs[1], "ok");
    assert.equal(globalThis.__webArgs[2].redirect_url, "https://example.com/new");

});
