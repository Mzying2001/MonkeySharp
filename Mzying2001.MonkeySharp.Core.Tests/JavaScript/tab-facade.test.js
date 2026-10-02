const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const template = fs.readFileSync(
    path.resolve(__dirname, "../../Mzying2001.MonkeySharp.Core/Js/Init.js"), "utf8");

test("openInTab handles are local, default inactive, and close before id is queued", async () => {
    const payload = {
        protocol: 1,
        documentId: "tab-document",
        frameId: "main",
        runAt: "DocumentEnd",
        invocations: [{
            executionId: "tab-execution",
            scriptKey: "55555555-5555-5555-5555-555555555555",
            source: [
                "globalThis.__tabPromise = (async function () {",
                "  globalThis.__modern = GM.openInTab('https://example.com/modern');",
                "  globalThis.__modern.onclose = () => globalThis.__modernClosed = (globalThis.__modernClosed || 0) + 1;",
                "  globalThis.__modernClose = globalThis.__modern.close();",
                "  globalThis.__legacy = GM_openInTab('https://example.com/legacy', true);",
                "  globalThis.__legacy.onclose = () => globalThis.__legacyClosed = (globalThis.__legacyClosed || 0) + 1;",
                "})();"
            ].join("\n"),
            grants: ["GM.openInTab"],
            info: {},
            compatibility: { legacyGlobals: true },
            capability: "tab-capability",
            deliveryToken: "tab-delivery"
        }]
    };
    const starts = [];
    globalThis.__MonkeySharpBridge = {
        dispatch: async requestJson => {
            const request = JSON.parse(requestJson);
            if (request.type === "hello") {
                return JSON.stringify({ type: "hello-result", protocol: 1, ok: true, limits: {}, apis: ["GM.openInTab"] });
            }
            if (request.method === "GM.openInTab" && !request.params.close) {
                starts.push(request.params);
                const id = starts.length;
                await new Promise(resolve => setTimeout(resolve, 5));
                return JSON.stringify({ type: "response", protocol: 1, requestId: request.requestId, ok: true,
                    result: { id: "tab-" + id } });
            }
            if (request.method === "GM.openInTab" && request.params.close) {
                globalThis.__MonkeySharpRuntime.receive({
                    type: "notification", protocol: 1, executionId: "tab-execution", deliveryToken: "tab-delivery",
                    event: "tab-closed", data: { tabId: request.params.tabId }
                });
                globalThis.__MonkeySharpRuntime.receive({
                    type: "notification", protocol: 1, executionId: "tab-execution", deliveryToken: "tab-delivery",
                    event: "tab-closed", data: { tabId: request.params.tabId }
                });
                return JSON.stringify({ type: "response", protocol: 1, requestId: request.requestId, ok: true, result: true });
            }
            return JSON.stringify({ type: "response", protocol: 1, requestId: request.requestId, ok: true,
                result: { $monkeySharpType: "undefined" } });
        }
    };

    const encoded = Buffer.from(JSON.stringify(payload), "utf8").toString("base64");
    vm.runInThisContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", encoded));
    await globalThis.__tabPromise;
    await new Promise(resolve => setTimeout(resolve, 30));

    assert.equal(starts.length, 2);
    assert.equal(starts[0].active, false);
    assert.equal(starts[1].active, false);
    assert.equal(globalThis.__modern.id, "tab-1");
    assert.equal(globalThis.__modern.closed, true);
    assert.equal(globalThis.__modernClosed, 1);
    assert.equal(globalThis.__legacy.id, "tab-2");
    assert.equal(globalThis.__legacy.closed, false);
    assert.equal(typeof globalThis.__modernClose.then, "function");
});
