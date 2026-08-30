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
                reject(new Error("Timed out waiting for legacy bootstrap state."));
            }
        }, 5);
    });
}

test("legacy profile exposes lexical globals and window-like top-level this", async () => {
    globalThis.__MonkeySharpBridge = {
        dispatch: async requestJson => {
            const request = JSON.parse(requestJson);
            if (request.type === "hello") {
                return JSON.stringify({
                    type: "hello-result", protocol: 1, ok: true, limits: {}, apis: ["GM.log"]
                });
            }
            return JSON.stringify({
                type: "response", protocol: 1, requestId: request.requestId, ok: true,
                result: { $monkeySharpType: "undefined" }
            });
        }
    };

    const payload = {
        protocol: 1,
        documentId: "legacy-document",
        frameId: "main",
        runAt: "DocumentEnd",
        invocations: [{
            executionId: "legacy-execution",
            scriptKey: "33333333-3333-3333-3333-333333333333",
            source: [
                "globalThis.__legacyThis = this === window;",
                "legacyImplicitGlobal = 7;",
                "globalThis.__legacyImplicit = globalThis.legacyImplicitGlobal;",
                "globalThis.__legacyType = typeof GM_log;"
            ].join("\n"),
            declaredGrants: ["GM_log"],
            grants: ["GM.log"],
            info: { grants: ["GM_log"] },
            capability: "legacy-capability",
            deliveryToken: "legacy-delivery",
            compatibility: {
                profile: "LegacyCompatible",
                strict: false,
                legacyGlobals: true,
                synchronousStorageMirror: true,
                synchronousResourceSnapshot: true
            }
        }]
    };

    const encoded = Buffer.from(JSON.stringify(payload), "utf8").toString("base64");
    vm.runInThisContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", encoded));
    await waitFor(() => globalThis.__legacyType);

    assert.equal(globalThis.__legacyThis, true);
    assert.equal(globalThis.__legacyImplicit, 7);
    assert.equal(globalThis.__legacyType, "function");
});
