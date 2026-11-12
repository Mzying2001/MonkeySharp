// ==UserScript==
// @name incremental XHR text
// @match http://127.0.0.1/*
// @grant GM.xmlHttpRequest
// @connect 127.0.0.1
// @run-at document-end
// ==/UserScript==
(async function () {
    const r = { schemaVersion: 1, caseId: 'progress', immediateReturn: {}, promise: {}, events: [], effects: {} };
    try {
        const operation = GM.xmlHttpRequest({ url: location.origin + '/progress', onprogress(value) {
            r.events.push({ name: 'progress', text: value.responseText, response: value.response, readyState: value.readyState, loaded: value.loaded });
        } });
        r.immediateReturn.promise = typeof operation.then;
        r.effects.text = (await operation).responseText;
        r.promise.text = { state: 'fulfilled' };
        r.effects.fetchProgress = 0;
        await GM.xmlHttpRequest({ url: location.origin + '/progress', fetch: true,
            onprogress() { r.effects.fetchProgress++; } });
    } catch (error) { r.effects.error = String(error); }
    await fetch('/evidence', { method: 'POST', body: JSON.stringify(r) });
})();
