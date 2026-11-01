const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const template = fs.readFileSync(
    path.resolve(__dirname, "../../Mzying2001.MonkeySharp.Core/Js/Init.js"), "utf8");
const resourceUrl = "data:text/plain;base64,aGVsbG8=";

async function install({ profile = "LegacyCompatible", snapshot = false,
    grants = ["GM.getResourceURL"], apis = ["GM.getResourceURL"] } = {}) {
    const requests = [];
    const compatibility = {
        profile, strict: profile === "ModernStrict", legacyGlobals: profile === "LegacyCompatible"
    };
    const context = vm.createContext({
        console, atob, TextDecoder, crypto,
        __MonkeySharpBridge: {
            dispatch: async requestJson => {
                const request = JSON.parse(requestJson);
                if (request.type === "hello") {
                    return JSON.stringify({
                        type: "hello-result", protocol: 1, ok: true, limits: {}, apis,
                        compatibility: {
                            ...compatibility,
                            resources: snapshot ? {
                                complete: true, values: { template: { text: "hello", url: resourceUrl } }
                            } : undefined
                        }
                    });
                }
                requests.push(request);
                assert.equal(request.method, "GM.getResourceURL");
                const declared = request.params.name === "template";
                return JSON.stringify({
                    type: "response", protocol: 1, requestId: request.requestId, ok: declared,
                    result: declared ? resourceUrl : undefined,
                    error: declared ? undefined : {
                        code: "MSP007_INVALID_PARAMS", message: "The resource was not declared."
                    }
                });
            }
        }
    });
    const payload = {
        protocol: 1, documentId: "resource-document", frameId: "main", runAt: "DocumentEnd",
        invocations: [{
            executionId: "resource-execution", scriptKey: "55555555-5555-5555-5555-555555555555",
            source: "globalThis.resourceApi = { gm: GM, legacy: GM_getResourceURL };",
            declaredGrants: grants, grants, info: {}, compatibility,
            capability: "resource-capability", deliveryToken: "resource-delivery"
        }]
    };
    const encoded = Buffer.from(JSON.stringify(payload), "utf8").toString("base64");
    vm.runInContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", encoded), context);
    const deadline = Date.now() + 2000;
    while (!context.resourceApi) {
        if (Date.now() > deadline) throw new Error("Timed out waiting for resource APIs.");
        await new Promise(resolve => setTimeout(resolve, 5));
    }
    return { ...context.resourceApi, requests };
}

for (const profile of ["LegacyCompatible", "ModernStrict"]) {
    for (const snapshot of [false, true]) {
        test(`${profile} resource URL spellings share ${snapshot ? "snapshot" : "bridge"} behavior`, async () => {
            const { gm, legacy, requests } = await install({ profile, snapshot });
            assert.equal(typeof gm.getResourceUrl, "function");
            assert.equal(gm.getResourceUrl, gm.getResourceURL);
            assert.equal(Object.isFrozen(gm), true);

            const promises = [gm.getResourceUrl("template"), gm.getResourceURL("template")];
            promises.forEach(promise => assert.equal(typeof promise.then, "function"));
            assert.deepEqual(await Promise.all(promises), [resourceUrl, resourceUrl]);
            assert.equal(requests.length, snapshot ? 0 : 2);
            requests.forEach(request => {
                assert.equal(request.method, "GM.getResourceURL");
                assert.deepEqual(request.params, { name: "template" });
            });

            if (snapshot && profile === "LegacyCompatible") {
                assert.equal(legacy("template"), resourceUrl);
                assert.throws(() => legacy("missing"), /resource snapshot is unavailable/);
            } else {
                assert.equal(typeof legacy, "undefined");
            }

            for (const method of [gm.getResourceUrl, gm.getResourceURL]) {
                for (const name of ["", null, undefined, 1]) {
                    await assert.rejects(method(name), { name: "TypeError", message: "name must be a non-empty string." });
                }
            }
            assert.equal(requests.length, snapshot ? 0 : 2);
            for (const method of [gm.getResourceUrl, gm.getResourceURL]) {
                await assert.rejects(method("missing"), { code: "MSP007_INVALID_PARAMS" });
            }
        });
    }
}

for (const { name, grants, apis } of [
    { name: "no resource grant", grants: ["GM.log"], apis: ["GM.log", "GM.getResourceURL"] },
    { name: "only a resource text grant", grants: ["GM.getResourceText"], apis: ["GM.getResourceText", "GM.getResourceURL"] },
    { name: "no resource provider", grants: ["GM.getResourceURL"], apis: [] }
]) {
    test(`both resource URL spellings stay absent with ${name}`, async () => {
        const { gm, legacy, requests } = await install({ grants, apis, snapshot: true });
        assert.equal(typeof gm.getResourceUrl, "undefined");
        assert.equal(typeof gm.getResourceURL, "undefined");
        assert.equal(typeof legacy, "undefined");
        assert.equal(requests.length, 0);
    });
}
