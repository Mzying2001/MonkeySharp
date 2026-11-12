// ==UserScript==
// @name Storage timing reference
// @namespace MonkeySharp compatibility
// @match http://127.0.0.1/*
// @run-at document-end
// @grant GM_getValue
// @grant GM_setValue
// @grant GM_getValues
// @grant GM_addValueChangeListener
// ==/UserScript==
(async function () {
    const result = { schemaVersion: 1, caseId: "storage-timing", events: [], promise: {}, immediateReturn: {}, effects: {},
        info: { handler: GM_info.scriptHandler, version: GM_info.version, userAgent: navigator.userAgent } };
    const log = name => result.events.push({ name });
    await GM.deleteValues(["key", "missing"]);
    const id = GM_addValueChangeListener("key", (key, oldValue, newValue, remote) => {
        result.events.push({ name: "listener", oldValue, newValue, remote, current: GM_getValue(key) });
    });
    result.immediateReturn.listenerId = typeof id;
    log("before-legacy"); GM_setValue("key", 1); log("after-legacy");
    Promise.resolve().then(() => log("microtask-legacy"));
    const promise = GM.setValue("key", 2); log("after-modern");
    promise.then(() => log("modern-settled"));
    result.immediateReturn.modern = typeof promise.then;
    result.effects.immediateValue = GM_getValue("key");
    result.effects.bulkPrototype = Object.getPrototypeOf(GM_getValues()) === Object.prototype ? "Object" : "null";
    await promise;
    await GM.setValue("key", 2);
    await GM.deleteValue("missing");
    const fallback = {};
    result.effects.defaultReference = GM_getValue("missing", fallback) === fallback;
    result.effects.bulkDefaultReference = GM_getValues({ missing: fallback }).missing === fallback;
    await GM.removeValueChangeListener(id);
    await GM.setValue("key", 3);
    await new Promise(resolve => setTimeout(resolve, 100));
    fetch("/evidence", { method: "POST", body: JSON.stringify(result) });
})();
