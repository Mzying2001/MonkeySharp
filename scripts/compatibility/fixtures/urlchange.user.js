// ==UserScript==
// @name URL listener contract
// @match http://127.0.0.1/*
// @grant window.onurlchange
// @run-at document-end
// ==/UserScript==
(async function () {
    const r = { schemaVersion: 1, caseId: 'urlchange', immediateReturn: {}, promise: {}, events: [], effects: {} };
    r.immediateReturn.capability = typeof window.onurlchange;
    let count = 0;
    function log(name, event) {
        try {
            r.events.push({ name, url: event.url, oldURL: event.oldURL, type: event.type, constructor: event.constructor?.name,
                isEvent: event instanceof Event, receiver: this === window, keys: Object.keys(event) });
        } catch (error) { r.events.push({ name, error: String(error) }); }
    }
    window.onurlchange = function (event) { log.call(this, 'property', event); };
    window.addEventListener('urlchange', function (event) { log.call(this, 'listener', event); });
    const once = () => { count++; };
    window.addEventListener('urlchange', once, { once: true });
    const controller = new AbortController();
    window.addEventListener('urlchange', () => r.events.push({ name: 'aborted' }), { signal: controller.signal });
    controller.abort();
    document.documentElement.dataset.urlProbeReady = 'true';
    // The runner changes the page's history; the sandbox's own history wrapper is a different path.
    await new Promise(resolve => setTimeout(resolve, 6000));
    r.effects.onceCount = count;
    fetch('/evidence', { method: 'POST', body: JSON.stringify(r) });
})();
