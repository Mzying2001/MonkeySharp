const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const template = fs.readFileSync(
    path.resolve(__dirname, "../../Mzying2001.MonkeySharp.Core/Js/Init.js"), "utf8");

test("XHR serializes Tampermonkey request inputs and exposes abort", async () => {
    const requests = [];
    let session = 0;
    globalThis.__MonkeySharpBridge = {
        dispatch: async requestJson => {
            const request = JSON.parse(requestJson);
            requests.push(request);
            if (request.type === "hello") return JSON.stringify({
                type: "hello-result", protocol: 1, ok: true, limits: {}, apis: ["GM.xmlHttpRequest"]
            });
            let result = true;
            if (request.params.operation === "create") result = { sessionId: "xhr-" + (++session) };
            else if (request.params.operation === "execute") result = { status: 200, bodyLength: 0 };
            return JSON.stringify({
                type: "response", protocol: 1, requestId: request.requestId, ok: true, result
            });
        }
    };
    const payload = {
        protocol: 1, documentId: "inputs-document", frameId: "main", runAt: "DocumentEnd",
        invocations: [{
            executionId: "inputs-execution", scriptKey: "77777777-7777-7777-7777-777777777777",
            source: [
                "globalThis.__inputsPromise = (async () => {",
                "  const request = GM.xmlHttpRequest({",
                "    url: new URL('https://api.example.com/data'), method: 'PROPFIND',",
                "    data: { answer: 42 }, cookie: 'a=b', user: 'alice', password: 'secret',",
                "    anonymous: true, overrideMimeType: 'application/json',",
                "    redirect: 'manual', nocache: true, revalidate: true, fetch: true, timeout: 10",
                "  });",
                "  globalThis.__abortType = typeof request.abort;",
                "  await request;",
                "  const form = new FormData(); form.append('text', 'value');",
                "  form.append('file', new File(['data'], 'data.txt', { type: 'text/plain' }));",
                "  await GM.xmlHttpRequest({ url: 'https://api.example.com/form', data: form });",
                "  for (const details of [{ url: new Blob(['url']) },",
                "    { url: 'https://api.example.com/', proxy: {} },",
                "    { url: 'https://api.example.com/', cookiePartition: {} }]) {",
                "    try { GM.xmlHttpRequest(details); } catch (error) {",
                "      (globalThis.__unsupported || (globalThis.__unsupported = [])).push(error.code);",
                "    }",
                "  }",
                "})();"
            ].join("\n"),
            declaredGrants: ["GM.xmlHttpRequest"], grants: ["GM.xmlHttpRequest"], info: {},
            capability: "inputs-capability", deliveryToken: "inputs-delivery"
        }]
    };
    vm.runInThisContext(template.replace(
        "__MONKEYSHARP_PAYLOAD_BASE64__", Buffer.from(JSON.stringify(payload)).toString("base64")));
    while (!globalThis.__inputsPromise) await new Promise(resolve => setTimeout(resolve, 5));
    await globalThis.__inputsPromise;

    assert.equal(globalThis.__abortType, "function");
    const creates = requests.filter(value => value.params && value.params.operation === "create");
    assert.equal(creates[0].params.url, "https://api.example.com/data");
    assert.equal(creates[0].params.method, "PROPFIND");
    assert.equal(creates[0].params.cookie, "a=b");
    assert.equal(creates[0].params.anonymous, true);
    assert.equal(creates[0].params.redirect, "manual");
    assert.equal(creates[0].params.nocache, true);
    assert.equal(creates[0].params.revalidate, true);
    assert.equal(creates[0].params.fetch, true);
    assert.equal(creates[0].params.timeout, undefined);
    assert.equal(creates[0].params.headers["Content-Type"], "application/json;charset=UTF-8");
    const appends = requests.filter(value => value.params && value.params.operation === "appendBody");
    assert.equal(Buffer.from(appends[0].params.chunk, "base64").toString(), '{"answer":42}');
    assert.match(creates[1].params.headers["Content-Type"], /^multipart\/form-data; boundary=/);
    const multipart = Buffer.from(appends[1].params.chunk, "base64").toString();
    assert.match(multipart, /name="text"/);
    assert.match(multipart, /filename="data.txt"/);

    assert.deepEqual(globalThis.__unsupported, [
        "MSP006_NOT_SUPPORTED", "MSP006_NOT_SUPPORTED", "MSP006_NOT_SUPPORTED"
    ]);
});
