// ==UserScript==
// @name download failure contract
// @match http://127.0.0.1/*
// @grant GM_download
// @grant GM.download
// @run-at document-end
// ==/UserScript==
(async function () {
    const r = { schemaVersion: 1, caseId: 'download-errors', immediateReturn: {}, promise: {}, events: [], effects: { downloadMode: GM_info.downloadMode } };
    const options = { url: location.origin + '/download.txt', name: 'download-contract.txt' };
    const legacy = GM_download({ ...options, onerror(error) { r.events.push({ name: 'legacy-error', error }); } });
    r.immediateReturn.legacyAbort = typeof legacy?.abort;
    const modern = GM.download({ ...options, onerror(error) { r.events.push({ name: 'modern-error', error }); } });
    r.immediateReturn.modernThen = typeof modern?.then;
    r.immediateReturn.modernAbort = typeof modern?.abort;
    modern.then(value => { r.promise.download = { state: 'fulfilled', value }; }, error => {
        r.promise.download = { state: 'rejected', error }; r.events.push({ name: 'rejected' });
    });
    await new Promise(resolve => setTimeout(resolve, 2000));
    if (!r.promise.download) { r.status = 'blocked'; r.reason = 'Download permission/settings interaction has not completed; no terminal error observed.'; }
    fetch('/evidence', { method: 'POST', body: JSON.stringify(r) });
})();
