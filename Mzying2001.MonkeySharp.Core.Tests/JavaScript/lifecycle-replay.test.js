const test = require("node:test");
const assert = require("node:assert/strict");
const vm = require("node:vm");
const fs = require("node:fs");
const path = require("node:path");
const completePlan = require("./test-plan");
const template = require("./bootstrap-template");
const eventsModule = fs.readFileSync(path.resolve(__dirname, "../../Mzying2001.MonkeySharp.CefSharp/Js/LifecycleEvents.js"), "utf8");

for (const stage of ["end", "idle"]) {
    test(`original ${stage} fixture matches pinned private window event replay`, async () => {
        const target = new EventTarget();
        const document = new EventTarget();
        document.readyState = "complete";
        document.body = {};
        let complete;
        const evidence = new Promise(resolve => { complete = resolve; });
        const context = vm.createContext({ console, Event, atob, TextDecoder, crypto, document, performance: { now: () => 1, getEntriesByType: () => [] },
            setTimeout, clearTimeout, addEventListener: (...args) => target.addEventListener(...args), removeEventListener: (...args) => target.removeEventListener(...args),
            fetch: (_url, request) => { complete(JSON.parse(request.body)); return Promise.resolve(); },
            __MonkeySharpBridge: { dispatch: async input => {
                const request = JSON.parse(input);
                if (request.type === "hello") return { protocol: 2, type: "hello-result", ok: true, apis: ["GM.getValue"],
                    compatibility: { storage: { complete: true, sequence: 0, entries: [] } } };
                return { protocol: 2, type: "response", ok: true, requestId: request.requestId, result: { complete: true, sequence: 0, changes: [] } };
            } }
        });
        vm.runInContext(`(function(){${eventsModule}; globalThis.__MonkeySharpLifecycleEvents=createLifecycleEvents('document');})();`, context);
        const source = fs.readFileSync(path.resolve(__dirname, `../../docs/compatibility/fixtures/lifecycle-${stage}.user.js`), "utf8");
        const payload = completePlan({ protocol: 2, documentId: "document", frameId: "main", runAt: stage === "end" ? "DocumentEnd" : "DocumentIdle", invocations: [{
            executionId: "execution", scriptKey: "script", capability: "capability", deliveryToken: "delivery", source, declaredGrants: ["GM_getValue"], grants: ["GM.getValue"], info: {}
        }] });
        vm.runInContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", Buffer.from(JSON.stringify(payload)).toString("base64")), context);
        const actual = await evidence;
        const expected = JSON.parse(fs.readFileSync(path.resolve(__dirname, `../../docs/compatibility/evidence/tampermonkey-5.5.1-lifecycle-${stage}.json`), "utf8"));
        assert.deepEqual(actual.events, expected.events);
        context.__MonkeySharpRuntime.endDocument("document");
        context.__MonkeySharpLifecycleEvents.dispose();
    });
}
