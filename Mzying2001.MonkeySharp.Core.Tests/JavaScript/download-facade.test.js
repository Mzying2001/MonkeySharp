const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const template = fs.readFileSync(
    path.resolve(__dirname, "../../Mzying2001.MonkeySharp.Core/Js/Init.js"), "utf8");

test("download callbacks are registered before early terminal notifications", async () => {
    const payload = {
        protocol: 1,
        documentId: "download-document",
        frameId: "main",
        runAt: "DocumentEnd",
        invocations: [{
            executionId: "download-execution",
            scriptKey: "44444444-4444-4444-4444-444444444444",
            source: [
                "globalThis.__downloadPromise = (async function () {",
                "  globalThis.__handle = await GM.download({ url: 'https://example.com/file', headers: { 'X-Test': 'yes' }, conflictAction: 'overwrite', timeout: 50 });",
                "})();",
                "globalThis.__legacy = GM_download('https://example.com/legacy', function (value) { globalThis.__legacyLoad = value; }, function (error) { globalThis.__legacyError = error; });"
            ].join("\n"),
            grants: ["GM.download"],
            info: {},
            compatibility: { legacyGlobals: true },
            capability: "download-capability",
            deliveryToken: "download-delivery"
        }]
    };
    const starts = [];
    let aborts = 0;
    globalThis.__MonkeySharpBridge = {
        dispatch: async requestJson => {
            const request = JSON.parse(requestJson);
            if (request.type === "hello") {
                return JSON.stringify({
                    type: "hello-result", protocol: 1, ok: true, limits: {}, apis: ["GM.download"]
                });
            }
            if (request.method === "GM.download" && request.params.operation === "start") {
                starts.push(request.params);
                const event = request.params.clientId === starts[0].clientId ? "download-complete" : "download-progress";
                globalThis.__MonkeySharpRuntime.receive({
                    type: "notification", protocol: 1, executionId: "download-execution",
                    deliveryToken: "download-delivery", event,
                    data: event === "download-complete"
                        ? { clientId: request.params.clientId, downloadId: "host-" + starts.length }
                        : { clientId: request.params.clientId, downloadId: "host-" + starts.length, loaded: 2, total: 4 }
                });
                if (starts.length > 1) {
                    globalThis.__MonkeySharpRuntime.receive({
                        type: "notification", protocol: 1, executionId: "download-execution",
                        deliveryToken: "download-delivery", event: "download-complete",
                        data: { clientId: request.params.clientId, downloadId: "host-" + starts.length }
                    });
                }
                return JSON.stringify({ type: "response", protocol: 1, requestId: request.requestId, ok: true,
                    result: { id: "host-" + starts.length, clientId: request.params.clientId } });
            }
            if (request.method === "GM.download" && request.params.operation === "abort") {
                aborts += 1;
                return JSON.stringify({ type: "response", protocol: 1, requestId: request.requestId, ok: true, result: true });
            }
            return JSON.stringify({ type: "response", protocol: 1, requestId: request.requestId, ok: true,
                result: { $monkeySharpType: "undefined" } });
        }
    };

    const encoded = Buffer.from(JSON.stringify(payload), "utf8").toString("base64");
    vm.runInThisContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", encoded));
    await globalThis.__downloadPromise;
    await new Promise(resolve => setTimeout(resolve, 10));

    assert.equal(globalThis.__handle.id, "host-1");
    assert.equal(starts[0].headers["X-Test"], "yes");
    assert.equal(starts[0].conflictAction, "overwrite");
    assert.equal(starts[0].timeout, 50);
    assert.equal(globalThis.__legacyLoad.id, "host-2");
    assert.equal(globalThis.__legacyError, undefined);
    globalThis.__legacy.abort();
    await new Promise(resolve => setTimeout(resolve, 10));
    assert.equal(aborts, 1);
});
