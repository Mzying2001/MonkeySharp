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

test("modern info is implicit while log, style, and element use granted capabilities", async () => {
    const appended = [];
    const document = {
        head: { appendChild: value => appended.push(value) },
        body: { appendChild: value => appended.push(value) },
        documentElement: { appendChild: value => appended.push(value) },
        createElement: tagName => ({
            tagName,
            attributes: {},
            style: { cssText: "" },
            value: "",
            checked: false,
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
                "  globalThis.__infoFrozen = GM_info === GM.info && Object.isFrozen(GM.info) && Object.isFrozen(GM.info.compatibility) && Object.isFrozen(GM.info.compatibility.fields);",
                "  globalThis.__infoCompatibility = [GM.info.compatibility.contractVersion, GM.info.compatibility.handler, GM.info.compatibility.executionWorld];",
                "  await GM.log({ event: 'log' });",
                "  globalThis.__style = await GM.addStyle('body { color: red; }');",
                "  globalThis.__element = await GM.addElement('input', { id: 'marker', value: 'ok', checked: true, style: 'color: red', onclick: function () {}, 'data-test': 'yes', textContent: 'body' });",
                "})();"
            ].join("\n"),
            grants: ["GM.log", "GM.addStyle", "GM.addElement"],
            info: { name: "contract", compatibility: { contractVersion: 1, handler: "MonkeySharp", executionWorld: "page", isolatedWorld: false, fields: {} } },
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
                    apis: ["GM.log", "GM.addStyle", "GM.addElement"]
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
    assert.deepEqual(globalThis.__infoCompatibility, [1, "MonkeySharp", "page"]);
    assert.deepEqual(globalThis.__logged, { event: "log" });
    assert.equal(globalThis.__style.tagName, "style");
    assert.equal(globalThis.__element.attributes.id, "marker");
    assert.equal(globalThis.__element.value, "ok");
    assert.equal(globalThis.__element.checked, true);
    assert.equal(globalThis.__element.style.cssText, "color: red");
    assert.equal(typeof globalThis.__element.onclick, "function");
    assert.equal(globalThis.__element.attributes["data-test"], "yes");
    assert.equal(globalThis.__element.textContent, "body");
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
                "  info: [typeof GM_info, GM_info === GM.info],",
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
        info: ["object", true],
        deniedStyle: "undefined",
        cookie: "object",
        webRequest: "undefined"
    });
});

test("modern and legacy addElement share DOM property semantics", async () => {
    const created = [];
    globalThis.document = {
        body: { appendChild: value => created.push(value) },
        documentElement: { appendChild: value => created.push(value) },
        createElement: tagName => ({
            tagName, attributes: {}, style: { cssText: "" }, value: "", checked: false,
            setAttribute(name, value) { this.attributes[name] = value; }, appendChild() {}
        })
    };
    globalThis.__legacyElementPromise = null;
    install({
        protocol: 1, documentId: "facade-element-legacy", frameId: "main", runAt: "DocumentEnd",
        invocations: [{
            executionId: "facade-element-legacy-execution",
            scriptKey: "99999999-9999-9999-9999-999999999999",
            source: "globalThis.__legacyElementPromise = Promise.resolve(GM_addElement('button', { value: 'legacy', style: 'display:none', onclick: function () {}, 'aria-label': 'run' }));",
            declaredGrants: ["GM_addElement"], grants: ["GM.addElement"], info: {},
            capability: "facade-element-capability", deliveryToken: "facade-element-delivery",
            compatibility: { profile: "LegacyCompatible", strict: false, legacyGlobals: true }
        }]
    }, {
        dispatch: async requestJson => {
            const request = JSON.parse(requestJson);
            if (request.type === "hello") return JSON.stringify({ type: "hello-result", protocol: 1, ok: true, limits: {}, apis: ["GM.addElement"] });
            return JSON.stringify({ type: "response", protocol: 1, requestId: request.requestId, ok: true, result: { $monkeySharpType: "undefined" } });
        }
    });
    await waitFor(() => globalThis.__legacyElementPromise);
    const element = await globalThis.__legacyElementPromise;
    assert.equal(element.value, "legacy");
    assert.equal(element.style.cssText, "display:none");
    assert.equal(typeof element.onclick, "function");
    assert.equal(element.attributes["aria-label"], "run");
});

for (const bridgeState of ["absent", "failing"]) {
    test(`both info names are unconditional without a usable bridge (${bridgeState})`, async () => {
        const previousCefSharp = globalThis.CefSharp;
        let bindCalls = 0;
        let dispatchCalls = 0;
        globalThis.CefSharp = bridgeState === "absent" ? undefined : {
            BindObjectAsync: async () => { bindCalls++; throw new Error("binding unavailable"); }
        };
        globalThis.__infoStates = {};
        const invocations = [];
        for (const compatibility of [
            { profile: "LegacyCompatible", strict: false, legacyGlobals: true },
            { profile: "LegacyCompatible", strict: false, legacyGlobals: false },
            { profile: "ModernStrict", strict: true, legacyGlobals: false }
        ]) {
            for (const declaration of [
                { declaredGrants: [], grants: [], grantDeclarationState: "Missing" },
                { declaredGrants: ["none"], grants: ["none"], grantDeclarationState: "ExplicitNone" },
                { declaredGrants: ["GM.info"], grants: ["GM.info"], grantDeclarationState: "ExplicitList" },
                { declaredGrants: ["GM_info"], grants: ["GM.info"], grantDeclarationState: "ExplicitList" },
                { declaredGrants: ["GM.info", "GM_info"], grants: ["GM.info"], grantDeclarationState: "ExplicitList" }
            ]) {
                const id = `info-${bridgeState}-${invocations.length}`;
                invocations.push({
                    ...declaration, compatibility, executionId: id, scriptKey: id,
                    capability: id, deliveryToken: id,
                    info: { script: { name: id, grant: declaration.declaredGrants } },
                    source: `globalThis.__infoStates[${JSON.stringify(id)}] = {
                        name: GM_info.script.name, same: GM_info === GM.info,
                        frozen: Object.isFrozen(GM_info) && Object.isFrozen(GM_info.script) && Object.isFrozen(GM_info.script.grant),
                        grants: GM_info.script.grant.slice(),
                        storage: typeof GM.getValue, legacyStorage: typeof GM_getValue,
                        log: typeof GM.log, legacyLog: typeof GM_log
                    };`
                });
            }
        }
        try {
            install({
                protocol: 1, documentId: `info-${bridgeState}`, frameId: "main",
                runAt: "DocumentEnd", invocations
            }, bridgeState === "absent" ? null : {
                dispatch: async () => { dispatchCalls++; throw new Error("dispatch unavailable"); }
            });
            await waitFor(() => Object.keys(globalThis.__infoStates).length === invocations.length);
            assert.equal(bindCalls, 0);
            assert.equal(dispatchCalls, 0);
            for (const invocation of invocations) {
                assert.deepEqual(globalThis.__infoStates[invocation.executionId], {
                    name: invocation.executionId, same: true, frozen: true,
                    grants: invocation.declaredGrants,
                    storage: "undefined", legacyStorage: "undefined", log: "undefined", legacyLog: "undefined"
                });
            }
        } finally {
            globalThis.CefSharp = previousCefSharp;
        }
    });
}

test("info bypasses legacy switches while other aliases still follow the profile", async () => {
    const previousCefSharp = globalThis.CefSharp;
    let bindCalls = 0;
    let helloCalls = 0;
    globalThis.CefSharp = { BindObjectAsync: async () => { bindCalls++; } };
    globalThis.__profileInfo = {};
    const profiles = [
        { profile: "LegacyCompatible", strict: false, legacyGlobals: true },
        { profile: "LegacyCompatible", strict: false, legacyGlobals: false },
        { profile: "ModernStrict", strict: true, legacyGlobals: false }
    ];
    try {
        install({
            protocol: 1, documentId: "info-profile-switches", frameId: "main", runAt: "DocumentEnd",
            invocations: profiles.map((compatibility, index) => ({
                executionId: `info-profile-${index}`, scriptKey: `info-profile-${index}`,
                declaredGrants: ["GM_info", "GM.log"], grants: ["GM.info", "GM.log"],
                grantDeclarationState: "ExplicitList", info: {}, compatibility,
                source: `globalThis.__profileInfo[${index}] = [GM_info === GM.info, typeof GM.log, typeof GM_log, typeof GM.getValue];`
            }))
        }, {
            dispatch: async requestJson => {
                assert.equal(JSON.parse(requestJson).type, "hello");
                helloCalls++;
                return { type: "hello-result", protocol: 1, ok: true, apis: ["GM.log"] };
            }
        });
        await waitFor(() => Object.keys(globalThis.__profileInfo).length === profiles.length);
        assert.equal(bindCalls, 1);
        assert.equal(helloCalls, profiles.length);
        profiles.forEach((profile, index) => assert.deepEqual(globalThis.__profileInfo[index],
            [true, "function", profile.legacyGlobals ? "function" : "undefined", "undefined"]));
    } finally {
        globalThis.CefSharp = previousCefSharp;
    }
});

test("a failed privileged binding does not prevent a later info-only invocation", async () => {
    const previousCefSharp = globalThis.CefSharp;
    const previousError = console.error;
    const errors = [];
    let bindCalls = 0;
    globalThis.CefSharp = {
        BindObjectAsync: async () => { bindCalls++; throw new Error("binding unavailable"); }
    };
    console.error = (...args) => errors.push(args);
    globalThis.__afterBindingFailure = null;
    try {
        install({
            protocol: 1, documentId: "info-after-failed-binding", frameId: "main", runAt: "DocumentEnd",
            invocations: [{
                executionId: "failed-binding", scriptKey: "privileged", grants: ["GM.getValue"],
                info: {}, source: "throw new Error('privileged source must not execute');"
            }, {
                executionId: "info-after-failure", scriptKey: "info", grants: ["GM.info"],
                info: {}, source: "globalThis.__afterBindingFailure = GM_info === GM.info;"
            }]
        }, null);
        await waitFor(() => globalThis.__afterBindingFailure);
        assert.equal(bindCalls, 1);
        assert.equal(errors.length, 1);
        assert.match(errors[0][1].message, /binding unavailable/);
    } finally {
        globalThis.CefSharp = previousCefSharp;
        console.error = previousError;
    }
});
