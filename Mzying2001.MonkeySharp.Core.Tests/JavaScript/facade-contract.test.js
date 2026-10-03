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
                reject(new Error("Timed out waiting for facade contract state."));
            }
        }, 5);
    });
}

function install(payload, bridge) {
    globalThis.__MonkeySharpBridge = bridge;
    const encoded = Buffer.from(JSON.stringify(payload), "utf8").toString("base64");
    vm.runInThisContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", encoded));
}

test("modern info, log, style, and element facades use granted capabilities", async () => {
    const appended = [];
    const document = {
        head: { appendChild: value => appended.push(value) },
        body: { appendChild: value => appended.push(value) },
        documentElement: { appendChild: value => appended.push(value) },
        createElement: tagName => ({
            tagName,
            attributes: {},
            setAttribute(name, value) { this.attributes[name] = value; },
            appendChild() {}
        })
    };
    globalThis.document = document;
    globalThis.__contractPromise = null;
    globalThis.__logged = null;
    install({
        protocol: 1,
        documentId: "facade-modern-document",
        frameId: "main",
        runAt: "DocumentEnd",
        invocations: [{
            executionId: "facade-modern-execution",
            scriptKey: "77777777-7777-7777-7777-777777777777",
            source: [
                "globalThis.__contractPromise = (async function () {",
                "  globalThis.__infoFrozen = Object.isFrozen(GM.info);",
                "  await GM.log({ event: 'log' });",
                "  globalThis.__style = await GM.addStyle('body { color: red; }');",
                "  globalThis.__element = await GM.addElement('div', { id: 'marker', textContent: 'ok' });",
                "})();"
            ].join("\n"),
            grants: ["GM.info", "GM.log", "GM.addStyle", "GM.addElement"],
            info: { name: "contract" },
            capability: "facade-modern-capability",
            deliveryToken: "facade-modern-delivery",
            compatibility: { profile: "ModernStrict", strict: true, legacyGlobals: false }
        }]
    }, {
        dispatch: async requestJson => {
            const request = JSON.parse(requestJson);
            if (request.type === "hello") {
                return JSON.stringify({
                    type: "hello-result", protocol: 1, ok: true, limits: {},
                    apis: ["GM.info", "GM.log", "GM.addStyle", "GM.addElement"]
                });
            }
            if (request.method === "GM.log") globalThis.__logged = request.params.value;
            return JSON.stringify({
                type: "response", protocol: 1, requestId: request.requestId,
                ok: true, result: { $monkeySharpType: "undefined" }
            });
        }
    });

    await waitFor(() => globalThis.__contractPromise);
    await globalThis.__contractPromise;
    assert.equal(globalThis.__infoFrozen, true);
    assert.deepEqual(globalThis.__logged, { event: "log" });
    assert.equal(globalThis.__style.tagName, "style");
    assert.equal(globalThis.__element.attributes.id, "marker");
    assert.equal(globalThis.__element.textContent, "ok");
    assert.equal(appended.length, 2);
});

test("legacy facade shadows ungranted globals and keeps new APIs absent without providers", async () => {
    globalThis.GM_addStyle = () => "page value";
    globalThis.__contractLegacy = null;
    install({
        protocol: 1,
        documentId: "facade-legacy-document",
        frameId: "main",
        runAt: "DocumentEnd",
        invocations: [{
            executionId: "facade-legacy-execution",
            scriptKey: "88888888-8888-8888-8888-888888888888",
            source: [
                "globalThis.__contractLegacy = {",
                "  log: typeof GM_log,",
                "  info: typeof GM_info,",
                "  deniedStyle: typeof GM_addStyle,",
                "  cookie: typeof GM,",
                "  webRequest: typeof GM_webRequest",
                "};"
            ].join("\n"),
            declaredGrants: ["GM_log"],
            grants: ["GM.log"],
            info: { grants: ["GM_log"] },
            capability: "facade-legacy-capability",
            deliveryToken: "facade-legacy-delivery",
            compatibility: { profile: "LegacyCompatible", strict: false, legacyGlobals: true }
        }]
    }, {
        dispatch: async requestJson => {
            const request = JSON.parse(requestJson);
            if (request.type === "hello") {
                return JSON.stringify({
                    type: "hello-result", protocol: 1, ok: true, limits: {}, apis: ["GM.log"]
                });
            }
            return JSON.stringify({
                type: "response", protocol: 1, requestId: request.requestId,
                ok: true, result: { $monkeySharpType: "undefined" }
            });
        }
    });

    await waitFor(() => globalThis.__contractLegacy);
    assert.deepEqual(globalThis.__contractLegacy, {
        log: "function",
        info: "undefined",
        deniedStyle: "undefined",
        cookie: "object",
        webRequest: "undefined"
    });
});

test("missing grants expose no GM object while explicit none keeps GM.info", async () => {
    globalThis.__grantState = null;
    install({
        protocol: 1,
        documentId: "grant-state-document",
        frameId: "main",
        runAt: "DocumentEnd",
        invocations: [{
            executionId: "grant-state-missing",
            scriptKey: "99999999-9999-9999-9999-999999999999",
            source: "globalThis.__grantState = [typeof GM, typeof GM_info];",
            declaredGrants: [], grants: [], grantDeclarationState: "Missing",
            info: { name: "missing" }, capability: "grant-state-missing-capability",
            deliveryToken: "grant-state-missing-delivery",
            compatibility: { profile: "LegacyCompatible", strict: false, legacyGlobals: true }
        }, {
            executionId: "grant-state-none",
            scriptKey: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            source: "globalThis.__grantStateExplicit = [typeof GM, GM.info.name, typeof GM_log];",
            declaredGrants: ["none"], grants: ["none"], grantDeclarationState: "ExplicitNone",
            info: { name: "explicit" }, capability: "grant-state-none-capability",
            deliveryToken: "grant-state-none-delivery",
            compatibility: { profile: "LegacyCompatible", strict: false, legacyGlobals: true }
        }]
    }, null);

    await waitFor(() => globalThis.__grantState && globalThis.__grantStateExplicit);
    assert.deepEqual(globalThis.__grantState, ["undefined", "undefined"]);
    assert.deepEqual(globalThis.__grantStateExplicit, ["object", "explicit", "undefined"]);
});
