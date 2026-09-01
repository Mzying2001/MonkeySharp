const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const template = fs.readFileSync(
    path.resolve(__dirname, "../../Mzying2001.MonkeySharp.Core/Js/Init.js"), "utf8");

function waitFor(predicate) {
    return new Promise((resolve, reject) => {
        const timeout = Date.now() + 2000;
        const timer = setInterval(() => {
            if (predicate()) {
                clearInterval(timer);
                resolve();
            } else if (Date.now() > timeout) {
                clearInterval(timer);
                reject(new Error("Timed out waiting for advanced bootstrap state."));
            }
        }, 5);
    });
}

test("advanced APIs use promises, progress notifications, and capability discovery", async () => {
    let xhrId;
    const payload = {
        protocol: 1,
        documentId: "advanced-document",
        frameId: "main",
        runAt: "DocumentEnd",
        invocations: [{
            executionId: "advanced-execution",
            scriptKey: "22222222-2222-2222-2222-222222222222",
            source: [
                "globalThis.__advancedPromise = (async function () {",
                "  globalThis.__resource = await GM.getResourceText('template');",
                "  globalThis.__xhr = await GM.xmlHttpRequest({",
                "    url: 'https://api.example.com/data',",
                "    signal: globalThis.__trackedSignal,",
                "    onprogress: value => globalThis.__progress = value,",
                "    onload: value => globalThis.__loaded = value",
                "  });",
                "  globalThis.__menuId = await GM.registerMenuCommand('Open', () => globalThis.__menuCalled = true);",
                "  globalThis.__downloadType = typeof GM.download;",
                "})();"
            ].join("\n"),
            grants: [
                "GM.getResourceText", "GM.xmlHttpRequest", "GM.registerMenuCommand", "GM.download"
            ],
            info: {},
            capability: "advanced-capability",
            deliveryToken: "advanced-delivery"
        }]
    };

    globalThis.__MonkeySharpBridge = {
        dispatch: async requestJson => {
            const request = JSON.parse(requestJson);
            if (request.type === "hello") {
                return JSON.stringify({
                    type: "hello-result",
                    protocol: 1,
                    ok: true,
                    limits: {},
                    apis: ["GM.getResourceText", "GM.xmlHttpRequest", "GM.registerMenuCommand"]
                });
            }
            let result = { $monkeySharpType: "undefined" };
            if (request.method === "GM.getResourceText") result = "resource text";
            if (request.method === "GM.xmlHttpRequest") {
                if (request.params.operation === "create") {
                    xhrId = request.params.xhrId;
                    result = { sessionId: "advanced-xhr" };
                } else if (request.params.operation === "execute") {
                    globalThis.__MonkeySharpRuntime.receive({
                        type: "notification",
                        protocol: 1,
                        executionId: "advanced-execution",
                        deliveryToken: "advanced-delivery",
                        event: "xhr-progress",
                        data: { xhrId, loaded: 5, total: 10 }
                    });
                    result = { status: 200, bodyLength: 4 };
                } else if (request.params.operation === "readBody") {
                    result = { chunk: Buffer.from("done").toString("base64"), done: true };
                } else result = true;
            }
            if (request.method === "GM.registerMenuCommand") result = request.params.commandId;
            return JSON.stringify({
                type: "response",
                protocol: 1,
                requestId: request.requestId,
                ok: true,
                result
            });
        }
    };
    globalThis.__signalAdds = 0;
    globalThis.__signalRemoves = 0;
    globalThis.__trackedSignal = {
        aborted: false,
        addEventListener: () => { globalThis.__signalAdds += 1; },
        removeEventListener: () => { globalThis.__signalRemoves += 1; }
    };

    const encoded = Buffer.from(JSON.stringify(payload), "utf8").toString("base64");
    vm.runInThisContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", encoded));
    await waitFor(() => globalThis.__advancedPromise);
    await globalThis.__advancedPromise;

    assert.equal(globalThis.__resource, "resource text");
    assert.equal(globalThis.__progress.loaded, 5);
    assert.equal(globalThis.__progress.total, 10);
    assert.equal(globalThis.__progress.lengthComputable, true);
    assert.equal(globalThis.__loaded.status, 200);
    assert.equal(globalThis.__loaded.responseText, "done");
    assert.equal(globalThis.__xhr.status, 200);
    assert.equal(globalThis.__signalAdds, 1);
    assert.equal(globalThis.__signalRemoves, 1);
    assert.equal(globalThis.__downloadType, "undefined");

    assert.equal(globalThis.__MonkeySharpRuntime.receive({
        type: "notification",
        protocol: 1,
        executionId: "advanced-execution",
        deliveryToken: "advanced-delivery",
        event: "menu-command",
        data: { commandId: globalThis.__menuId }
    }), true);
    assert.equal(globalThis.__menuCalled, true);
});
