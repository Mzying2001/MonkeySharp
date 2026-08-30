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
            if (predicate()) { clearInterval(timer); resolve(); }
            else if (Date.now() > deadline) { clearInterval(timer); reject(new Error("Timed out waiting for cookie facade.")); }
        }, 5);
    });
}

test("modern GM.cookie and legacy GM_cookie share CRUD and listener bridge", async () => {
    const cookies = [];
    globalThis.__MonkeySharpBridge = {
        dispatch: async requestJson => {
            const request = JSON.parse(requestJson);
            if (request.type === "hello") return JSON.stringify({
                type: "hello-result", protocol: 1, ok: true, limits: {}, apis: ["GM.cookie"]
            });
            const details = request.params.details || {};
            if (request.method !== "GM.cookie") return JSON.stringify({
                type: "response", protocol: 1, requestId: request.requestId, ok: true,
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
            return JSON.stringify({ type: "response", protocol: 1, requestId: request.requestId, ok: true, result });
        }
    };
    const payload = {
        protocol: 1, documentId: "cookie-document", frameId: "main", runAt: "DocumentEnd", invocations: [{
            executionId: "cookie-execution", scriptKey: "66666666-6666-6666-6666-666666666666",
            source: [
                "globalThis.__cookiePromise = (async function () {",
                "  await GM.cookie.set({url:'https://example.com/', name:'sid', value:'abc'});",
                "  globalThis.__modernCookies = await GM.cookie.list({url:'https://example.com/'});",
                "  globalThis.__cookieListener = GM.cookie.addListener({url:'https://example.com/'}, value => globalThis.__cookieEvent = value);",
                "  GM_cookie.list({url:'https://example.com/'}, value => globalThis.__legacyCookies = value);",
                "})();"
            ].join("\n"),
            grants: ["GM.cookie"], declaredGrants: ["GM_cookie"], info: {}, capability: "cookie-capability",
            deliveryToken: "cookie-delivery", compatibility: { profile: "LegacyCompatible", strict: false, legacyGlobals: true }
        }]
    };
    const encoded = Buffer.from(JSON.stringify(payload), "utf8").toString("base64");
    vm.runInThisContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", encoded));
    await waitFor(() => globalThis.__modernCookies && globalThis.__legacyCookies);
    assert.equal(globalThis.__modernCookies[0].value, "abc");
    assert.equal(globalThis.__legacyCookies[0].name, "sid");
    assert.equal(globalThis.__MonkeySharpRuntime.receive({
        type: "notification", protocol: 1, executionId: "cookie-execution", deliveryToken: "cookie-delivery",
        event: "cookie-change", data: { listenerId: globalThis.__cookieListener,
            cookie: { name: "sid", value: "next" }, cause: "explicit", removed: false, sequence: 1 }
    }), true);
    assert.equal(globalThis.__cookieEvent.value, "next");
});
