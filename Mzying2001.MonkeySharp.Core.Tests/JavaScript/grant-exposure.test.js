const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const template = require("./bootstrap-template");
const catalog = require("../../Mzying2001.MonkeySharp.Core/Js/ApiCatalog.json");
const directory = path.resolve(__dirname, "../../scripts/compatibility");

for (const name of ["missing", "none", "legacy", "modern", "mixed", "unknown", "info-only"]) {
    test(`original exposure fixture matches the pinned runtime (${name})`, async () => {
        const source = fs.readFileSync(path.join(directory, `fixtures/grants-${name}.user.js`), "utf8");
        const expected = JSON.parse(fs.readFileSync(path.join(directory, `evidence/tampermonkey-5.5.1-grants-${name}.json`), "utf8"));
        const declaredGrants = [...source.matchAll(/^\/\/ @grant (.+)$/gm)].map(match => match[1]);
        const capabilities = new Set(declaredGrants.flatMap(name => {
            const descriptor = catalog.find(api => api.capability === name || api.legacy === name);
            return descriptor ? catalog.filter(api => api.family === descriptor.family).map(api => api.capability) : [];
        }));
        let completed;
        const result = new Promise(resolve => { completed = resolve; });
        const context = vm.createContext({
            atob, TextDecoder, crypto, console, navigator: { userAgent: "node-contract-fixture" },
            fetch: (_url, request) => { completed(JSON.parse(request.body)); return Promise.resolve(); },
            __MonkeySharpBridge: { dispatch: async input => JSON.parse(input).type !== "hello" ? ({ protocol: 2, type: "response", ok: true, requestId: JSON.parse(input).requestId, result: { complete: true, sequence: 0, changes: [] } }) : ({ type: "hello-result", protocol: 2, ok: true,
                apis: [...capabilities], compatibility: { storage: { complete: true, sequence: 0, entries: [] } } }) }
        });
        const payload = { protocol: 2, documentId: name, frameId: "main", invocations: [{
            executionId: name, scriptKey: name, source, declaredGrants, grants: [...capabilities],
            grantDeclarationState: name === "none" ? "ExplicitNone" : name === "missing" ? "Missing" : "ExplicitList",
            capability: name, deliveryToken: name, info: { scriptHandler: "MonkeySharp", version: "3.0.0", script: { grant: declaredGrants } }
        }] };
        vm.runInContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", Buffer.from(JSON.stringify(payload)).toString("base64")), context);
        const actual = await result;
        assert.deepEqual(actual.immediateReturn, expected.immediateReturn);
        assert.deepEqual(actual.events, expected.events);
        assert.deepEqual(actual.effects.grants, expected.effects.grants);
    });
}

test("calling style preserves source strictness and the window top-level receiver", async () => {
    for (const strict of [false, true]) {
        let observed;
        const context = vm.createContext({ atob, TextDecoder, crypto, console, capture: value => { observed = value; } });
        const source = (strict ? '"use strict";\n' : "") + "capture([this === window, (function () { return this === undefined; })()]);";
        const payload = { protocol: 2, documentId: "semantics", invocations: [{ executionId: "semantics", scriptKey: "semantics",
            declaredGrants: [], grants: [], grantDeclarationState: "Missing", info: {}, source }] };
        vm.runInContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", Buffer.from(JSON.stringify(payload)).toString("base64")), context);
        await new Promise(resolve => setImmediate(resolve));
        assert.deepEqual(Array.from(observed), [true, strict]);
    }
});
