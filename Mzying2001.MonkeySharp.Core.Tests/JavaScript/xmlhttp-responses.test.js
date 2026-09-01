const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const template = fs.readFileSync(
    path.resolve(__dirname, "../../Mzying2001.MonkeySharp.Core/Js/Init.js"), "utf8");

test("XHR exposes response types, ordered lifecycle events, context, and streams", async t => {
    const callbackErrors = [];
    const originalConsoleError = console.error;
    console.error = (...values) => callbackErrors.push(values);
    t.after(() => { console.error = originalConsoleError; });
    const sessions = new Map();
    const responseTypes = [];
    let nextSession = 0;
    const notify = (event, data) => globalThis.__MonkeySharpRuntime.receive({
        type: "notification", protocol: 1, executionId: "responses-execution",
        deliveryToken: "responses-delivery", event, data
    });
    globalThis.__MonkeySharpBridge = {
        dispatch: async requestJson => {
            const request = JSON.parse(requestJson);
            if (request.type === "hello") return JSON.stringify({
                type: "hello-result", protocol: 1, ok: true, limits: {}, apis: ["GM.xmlHttpRequest"]
            });
            const params = request.params;
            let result = true;
            if (params.operation === "create") {
                const sessionId = "response-" + (++nextSession);
                const kind = new URL(params.url).pathname.slice(1);
                sessions.set(sessionId, { xhrId: params.xhrId, kind });
                responseTypes.push(params.responseType);
                notify("xhr-state", {
                    xhrId: params.xhrId, readyState: 1, status: 0, statusText: "",
                    finalUrl: null, responseHeaders: ""
                });
                result = { sessionId };
            } else if (params.operation === "appendBody") {
                const session = sessions.get(params.sessionId);
                notify("xhr-upload-progress", { xhrId: session.xhrId, loaded: 4, total: 4 });
                result = { length: 4 };
            } else if (params.operation === "execute") {
                const session = sessions.get(params.sessionId);
                const headers = "Content-Type: application/json; charset=utf-8\r\n" +
                    "Set-Cookie: one=1\r\nSet-Cookie: two=2\r\n";
                notify("xhr-state", {
                    xhrId: session.xhrId, readyState: 2, status: 200, statusText: "OK",
                    finalUrl: "https://api.example.com/" + session.kind, responseHeaders: headers
                });
                if (session.kind === "stream") {
                    notify("xhr-state", {
                        xhrId: session.xhrId, readyState: 3, status: 200, statusText: "OK",
                        finalUrl: "https://api.example.com/stream", responseHeaders: headers
                    });
                    notify("xhr-chunk", { xhrId: session.xhrId, chunk: Buffer.from("stream ").toString("base64") });
                    notify("xhr-chunk", { xhrId: session.xhrId, chunk: Buffer.from("body").toString("base64") });
                    notify("xhr-state", {
                        xhrId: session.xhrId, readyState: 4, status: 200, statusText: "OK",
                        finalUrl: "https://api.example.com/stream", responseHeaders: headers
                    });
                    notify("xhr-complete", { xhrId: session.xhrId });
                    result = {
                        status: 200, statusText: "OK", finalUrl: "https://api.example.com/stream",
                        responseHeaders: headers, mimeType: "text/plain", charset: "utf-8", streaming: true
                    };
                } else {
                    notify("xhr-state", {
                        xhrId: session.xhrId, readyState: 3, status: 200, statusText: "OK",
                        finalUrl: "https://api.example.com/" + session.kind, responseHeaders: headers
                    });
                    notify("xhr-progress", { xhrId: session.xhrId, loaded: 4, total: 4 });
                    notify("xhr-state", {
                        xhrId: session.xhrId, readyState: 4, status: 200, statusText: "OK",
                        finalUrl: "https://api.example.com/" + session.kind, responseHeaders: headers
                    });
                    const bodies = { text: "text", json: '{"answer":42}', arraybuffer: "bytes", blob: "blob" };
                    result = {
                        status: 200, statusText: "OK", finalUrl: "https://api.example.com/" + session.kind,
                        responseHeaders: headers, mimeType: session.kind === "blob" ? "text/custom" : "application/json",
                        charset: "utf-8", bodyLength: Buffer.byteLength(bodies[session.kind]), streaming: false
                    };
                }
            } else if (params.operation === "readBody") {
                const session = sessions.get(params.sessionId);
                const bodies = { text: "text", json: '{"answer":42}', arraybuffer: "bytes", blob: "blob" };
                result = { chunk: Buffer.from(bodies[session.kind]).toString("base64"), done: true };
            }
            return JSON.stringify({
                type: "response", protocol: 1, requestId: request.requestId, ok: true, result
            });
        }
    };
    const payload = {
        protocol: 1, documentId: "responses-document", frameId: "main", runAt: "DocumentEnd",
        invocations: [{
            executionId: "responses-execution", scriptKey: "88888888-8888-8888-8888-888888888888",
            source: [
                "globalThis.__responsesPromise = (async () => {",
                "  const context = { marker: 'same' }; globalThis.__context = context;",
                "  const events = []; globalThis.__events = events;",
                "  globalThis.__text = await GM.xmlHttpRequest({ url: 'https://api.example.com/text',",
                "    data: 'data', context,",
                "    onreadystatechange: function (value) { events.push('state:' + value.readyState); globalThis.__callbackThis = this; },",
                "    onloadstart: () => events.push('loadstart'), onuploadprogress: () => events.push('upload'),",
                "    onprogress: () => { events.push('progress'); throw new Error('ignored callback failure'); },",
                "    onload: () => events.push('load'), onloadend: () => events.push('loadend') });",
                "  globalThis.__json = await GM.xmlHttpRequest({ url: 'https://api.example.com/json', responseType: 'json' });",
                "  globalThis.__array = await GM.xmlHttpRequest({ url: 'https://api.example.com/arraybuffer', responseType: 'arraybuffer' });",
                "  globalThis.__blob = await GM.xmlHttpRequest({ url: 'https://api.example.com/blob', responseType: 'blob' });",
                "  globalThis.__stream = await GM.xmlHttpRequest({ url: 'https://api.example.com/stream', responseType: 'stream' });",
                "  const reader = __stream.response.getReader(); const chunks = [];",
                "  while (true) { const item = await reader.read(); if (item.done) break; chunks.push(item.value); }",
                "  const length = chunks.reduce((sum, item) => sum + item.length, 0); const bytes = new Uint8Array(length);",
                "  let offset = 0; chunks.forEach(item => { bytes.set(item, offset); offset += item.length; });",
                "  globalThis.__streamText = new TextDecoder().decode(bytes);",
                "})();"
            ].join("\n"),
            declaredGrants: ["GM.xmlHttpRequest"], grants: ["GM.xmlHttpRequest"], info: {},
            capability: "responses-capability", deliveryToken: "responses-delivery"
        }]
    };
    vm.runInThisContext(template.replace(
        "__MONKEYSHARP_PAYLOAD_BASE64__", Buffer.from(JSON.stringify(payload)).toString("base64")));
    while (!globalThis.__responsesPromise) await new Promise(resolve => setTimeout(resolve, 5));
    await globalThis.__responsesPromise;

    assert.equal(globalThis.__text.response, "text");
    assert.equal(globalThis.__text.responseText, "text");
    assert.equal(globalThis.__text.context, globalThis.__context);
    assert.equal(globalThis.__callbackThis, globalThis.__text);
    assert.match(globalThis.__text.responseHeaders, /Set-Cookie: one=1\r\nSet-Cookie: two=2/);
    assert.deepEqual(globalThis.__json.response, { answer: 42 });
    assert.equal(Buffer.from(new Uint8Array(globalThis.__array.response)).toString(), "bytes");
    assert.equal(await globalThis.__blob.response.text(), "blob");
    assert.equal(globalThis.__blob.response.type, "text/custom");
    assert.equal(globalThis.__streamText, "stream body");
    assert.deepEqual(responseTypes, ["text", "json", "arraybuffer", "blob", "stream"]);
    assert.deepEqual(globalThis.__events, [
        "state:1", "loadstart", "upload", "state:2", "state:3", "progress", "state:4", "load", "loadend"
    ]);
    assert.equal(callbackErrors.length, 1);
});
