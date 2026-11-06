const test = require("node:test");
const assert = require("node:assert/strict");
const vm = require("node:vm");
const template = require("./bootstrap-template");
const completePlan = require("./test-plan");

for (const failure of [null, "missing", "duplicate", "truncated", "overBudget"]) {
    test(`prepared resource chunks gate source execution: ${failure || "complete"}`, async () => {
        const json = JSON.stringify({ complete: true, values: { text: { text: "café".repeat(100), url: "data:application/octet-stream;base64,AP8B" } } });
        const parts = Array.from({ length: Math.ceil(json.length / 61) }, (_, index) => json.slice(index * 61, (index + 1) * 61));
        const errors = [];
        const context = vm.createContext({ atob, TextDecoder, crypto,
            console: { error: (...args) => errors.push(args), warn: () => {} },
            __MonkeySharpBridge: { dispatch: async input => {
                const request = JSON.parse(input);
                if (request.type === "hello") return { protocol: 2, type: "hello-result", ok: true,
                    apis: ["GM.getResourceText", "GM.getResourceUrl"], compatibility: {
                        resources: failure === "overBudget" ? undefined : { complete: false, chunkCount: parts.length, jsonLength: json.length }
                    } };
                const index = request.params.index;
                return { protocol: 2, type: "response", ok: failure !== "missing", requestId: request.requestId,
                    error: { code: "MSP003_SESSION_EXPIRED", message: "Document released." },
                    result: { index: failure === "duplicate" ? 0 : index,
                        chunk: failure === "truncated" && index === parts.length - 1 ? "" : parts[index] } };
            } }
        });
        const plan = completePlan({ protocol: 2, documentId: "doc", frameId: "main", invocations: [{
            executionId: "execution", scriptKey: "script", capability: "proof", deliveryToken: "delivery",
            grants: ["GM.getResourceText", "GM.getResourceUrl"], declaredGrants: ["GM_getResourceText", "GM_getResourceURL"], info: {},
            source: "globalThis.result = [GM_getResourceText('text'), GM_getResourceURL('text')];"
        }] });
        vm.runInContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", Buffer.from(JSON.stringify(plan)).toString("base64")), context);
        for (let tries = 0; tries < 100 && !context.result && !errors.length; tries++) await new Promise(resolve => setTimeout(resolve, 5));
        if (failure) { assert.equal(context.result, undefined); assert.equal(errors.length, 1); }
        else { assert.deepEqual(Array.from(context.result), ["café".repeat(100), "data:application/octet-stream;base64,AP8B"]); assert.equal(errors.length, 0); }
    });
}
