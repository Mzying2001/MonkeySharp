const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const template = fs.readFileSync(
    path.resolve(__dirname, "../../Mzying2001.MonkeySharp.Core/Js/Init.js"), "utf8");

test("XHR classifies HTTP, timeout, network, abort, and stream cancellation outcomes", async () => {
    const sessions = new Map();
    let nextSession = 0;
    const notify = (event, data) => globalThis.__MonkeySharpRuntime.receive({
        type: "notification", protocol: 1, executionId: "errors-execution",
        deliveryToken: "errors-delivery", event, data
    });
    const success = (request, result) => JSON.stringify({
        type: "response", protocol: 1, requestId: request.requestId, ok: true, result
    });
    const failure = (request, code, message) => JSON.stringify({
        type: "response", protocol: 1, requestId: request.requestId, ok: false,
        error: { code, message }
    });
    globalThis.__MonkeySharpBridge = {
        dispatch: async requestJson => {
            const request = JSON.parse(requestJson);
            if (request.type === "hello") return JSON.stringify({
                type: "hello-result", protocol: 1, ok: true, limits: {}, apis: ["GM.xmlHttpRequest"]
            });
            if (request.type === "cancel") return success(request, true);
            const params = request.params;
            if (params.operation === "create") {
                const sessionId = "error-" + (++nextSession);
                sessions.set(sessionId, {
                    xhrId: params.xhrId,
                    kind: new URL(params.url).pathname.slice(1)
                });
                notify("xhr-state", {
                    xhrId: params.xhrId, readyState: 1, status: 0, statusText: "",
                    finalUrl: null, responseHeaders: ""
                });
                return success(request, { sessionId });
            }
            const session = sessions.get(params.sessionId);
            if (params.operation === "execute") {
                if (session.kind === "timeout") return failure(request, "MSP009_TIMEOUT", "timed out");
                if (session.kind === "network") return failure(request, "MSP999_INTERNAL", "network failed");
                if (session.kind === "abort") return new Promise(() => { });
                const status = session.kind === "status" ? 404 : 200;
                notify("xhr-state", {
                    xhrId: session.xhrId, readyState: 2, status, statusText: status === 404 ? "Not Found" : "OK",
                    finalUrl: "https://api.example.com/" + session.kind, responseHeaders: "Content-Type: text/plain\r\n"
                });
                if (session.kind === "stream-cancel") return success(request, {
                    status: 200, statusText: "OK", finalUrl: "https://api.example.com/stream-cancel",
                    responseHeaders: "Content-Type: text/plain\r\n", mimeType: "text/plain",
                    charset: "utf-8", streaming: true
                });
                notify("xhr-state", {
                    xhrId: session.xhrId, readyState: 4, status, statusText: "Not Found",
                    finalUrl: "https://api.example.com/status", responseHeaders: "Content-Type: text/plain\r\n"
                });
                return success(request, {
                    status, statusText: "Not Found", finalUrl: "https://api.example.com/status",
                    responseHeaders: "Content-Type: text/plain\r\n", mimeType: "text/plain",
                    charset: "utf-8", bodyLength: 7, streaming: false
                });
            }
            if (params.operation === "readBody") {
                return success(request, { chunk: Buffer.from("missing").toString("base64"), done: true });
            }
            if (params.operation === "abort" && session && session.kind === "stream-cancel") {
                notify("xhr-error", {
                    xhrId: session.xhrId, code: "MSP010_CANCELED", message: "aborted"
                });
            }
            return success(request, true);
        }
    };
    const payload = {
        protocol: 1, documentId: "errors-document", frameId: "main", runAt: "DocumentEnd",
        invocations: [{
            executionId: "errors-execution", scriptKey: "99999999-9999-9999-9999-999999999999",
            source: [
                "globalThis.__errorsPromise = (async () => {",
                "  const events = []; globalThis.__errorEvents = events;",
                "  globalThis.__status = await GM.xmlHttpRequest({ url: 'https://api.example.com/status',",
                "    onload: value => events.push('status-load:' + value.status), onerror: () => events.push('status-error') });",
                "  try { await GM.xmlHttpRequest({ url: 'https://api.example.com/timeout',",
                "    ontimeout: () => events.push('timeout'), onloadend: () => events.push('timeout-end') }); } catch (_) {}",
                "  try { await GM.xmlHttpRequest({ url: 'https://api.example.com/network',",
                "    onerror: () => events.push('error'), onloadend: () => events.push('error-end') }); } catch (_) {}",
                "  const aborted = GM.xmlHttpRequest({ url: 'https://api.example.com/abort',",
                "    onabort: () => events.push('abort'), onloadend: () => events.push('abort-end') });",
                "  aborted.abort(); try { await aborted; } catch (_) {}",
                "  const streamed = await GM.xmlHttpRequest({ url: 'https://api.example.com/stream-cancel', responseType: 'stream',",
                "    onabort: () => events.push('stream-abort'), onloadend: () => events.push('stream-end') });",
                "  await streamed.response.cancel(); await new Promise(resolve => setTimeout(resolve, 0));",
                "})();"
            ].join("\n"),
            declaredGrants: ["GM.xmlHttpRequest"], grants: ["GM.xmlHttpRequest"], info: {},
            capability: "errors-capability", deliveryToken: "errors-delivery"
        }]
    };
    vm.runInThisContext(template.replace(
        "__MONKEYSHARP_PAYLOAD_BASE64__", Buffer.from(JSON.stringify(payload)).toString("base64")));
    while (!globalThis.__errorsPromise) await new Promise(resolve => setTimeout(resolve, 5));
    await globalThis.__errorsPromise;

    assert.equal(globalThis.__status.status, 404);
    assert.equal(globalThis.__status.responseText, "missing");
    assert.deepEqual(globalThis.__errorEvents, [
        "status-load:404", "timeout", "timeout-end", "error", "error-end",
        "abort", "abort-end", "stream-abort", "stream-end"
    ]);
});
