// ==UserScript==
// @name notification lifecycle
// @match http://127.0.0.1/*
// @grant GM_notification
// @grant GM.notification
// @run-at document-end
// ==/UserScript==
(async function () {
    const r = { schemaVersion: 1, caseId: 'notification', immediateReturn: {}, promise: {}, events: [], effects: {} };
    const first = GM.notification({ text: 'First', tag: 'notification-contract', timeout: 600,
        onclick(event) { event.preventDefault(); r.events.push({ name: 'first-click', prevented: event.defaultPrevented }); },
        ondone() { r.events.push({ name: 'first-done' }); } });
    r.immediateReturn.first = typeof first?.then;
    const replacement = GM.notification({ text: 'Replacement', tag: 'notification-contract', timeout: 600,
        ondone() { r.events.push({ name: 'replacement-done' }); } });
    for (const [name, promise] of [['first', first], ['replacement', replacement]]) {
        promise.then(value => { r.promise[name] = { state: 'fulfilled', value }; }, error => { r.promise[name] = { state: 'rejected', error: String(error) }; });
    }
    await new Promise(resolve => setTimeout(resolve, 2000));
    r.effects.clickStatus = 'pending: requires an observed native click, default action and navigation';
    fetch('/evidence', { method: 'POST', body: JSON.stringify(r) });
})();
