const completePlan = require("./test-plan");
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const template = require("./bootstrap-template");

function waitFor(predicate) {
    return new Promise((resolve, reject) => {
        const deadline = Date.now() + 2000;
        const timer = setInterval(() => {
            if (predicate()) { clearInterval(timer); resolve(); }
            else if (Date.now() > deadline) { clearInterval(timer); reject(new Error("Timed out waiting for cookie facade.")); }
        }, 5);
    });
}

test("modern GM.cookie and legacy GM_cookie share CRUD without listener extensions", async () => {
    const cookies = [];
    globalThis.__MonkeySharpBridge = {
        dispatch: async requestJson => {
            const request = JSON.parse(requestJson);
            if (request.type === "hello") return JSON.stringify({
                type: "hello-result", protocol: 2, ok: true, limits: {}, apis: ["GM.cookie"]
            });
            const details = request.params.details || {};
            if (request.method !== "GM.cookie") return JSON.stringify({
                type: "response", protocol: 2, requestId: request.requestId, ok: true,
                result: { $monkeySharpType: "undefined" }
            });
            let result = { $monkeySharpType: "undefined" };
            if (request.params.operation === "set") {
                const cookie = { name: details.name, value: details.value, domain: "example.com", path: details.path || "/" };
                cookies.push(cookie); result = cookie;
            } else if (request.params.operation === "list") result = cookies;
            else if (request.params.operation === "delete") { cookies.length = 0; result = true; }
            else if (request.params.operation === "addListener") result = request.params.listenerId;
            else if (request.params.operation === "removeListener") result = true;
            return JSON.stringify({ type: "response", protocol: 2, requestId: request.requestId, ok: true, result });
        }
    };
    const payload = {
        protocol: 2, documentId: "cookie-document", frameId: "main", runAt: "DocumentEnd", invocations: [{
            executionId: "cookie-execution", scriptKey: "66666666-6666-6666-6666-666666666666",
            source: [
                "globalThis.__cookieNamespace = GM_cookie;",
                "globalThis.__cookiePromise = (async function () {",
                "  globalThis.__cookieSetResult = await GM.cookie.set({url:'https://example.com/', name:'sid', value:'abc'});",
                "  globalThis.__modernCookies = await GM.cookie.list({url:'https://example.com/'});",
                "  globalThis.__cookieListenerRemoved = typeof GM.cookie.addListener === 'undefined' && typeof GM_cookie.removeListener === 'undefined';",
                "  GM_cookie.list({url:'https://example.com/'}, (value, error) => { globalThis.__legacyCookies = value; globalThis.__legacyCookieError = error; });",
                "})();"
            ].join("\n"),
            grants: ["GM.cookie"], declaredGrants: ["GM_cookie"], info: {}, capability: "cookie-capability",
            deliveryToken: "cookie-delivery",
        }]
    };
    const encoded = Buffer.from(JSON.stringify(completePlan(payload)), "utf8").toString("base64");
    vm.runInThisContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", encoded));
    await waitFor(() => globalThis.__modernCookies && globalThis.__legacyCookies);
    assert.equal(globalThis.__modernCookies[0].value, "abc");
    assert.equal(globalThis.__legacyCookies[0].name, "sid");
    assert.equal(globalThis.__legacyCookieError, null);
    assert.equal(globalThis.__cookieListenerRemoved, true);
    assert.equal(globalThis.__cookieSetResult, undefined);
    let callbackCount = 0;
    globalThis.__cookieCallback = () => { callbackCount++; throw new Error("callback failed"); };
    // Use a second invocation through the captured facade to verify callback failures are isolated.
    assert.equal(typeof globalThis.__cookieNamespace, "function");
    globalThis.__cookieNamespace.set({name:'callback',value:'yes'}, globalThis.__cookieCallback);
    await new Promise(resolve => setTimeout(resolve, 20));
    assert.equal(callbackCount, 1);
});
