// ==UserScript==
// @name P0 explicit Cookie contract
// @match http://127.0.0.1/*
// @connect 127.0.0.1
// @grant GM.xmlHttpRequest
// @run-at document-end
// ==/UserScript==
(async function () {
    const r = { schemaVersion: 1, caseId: 'p0-cookies', immediateReturn: { request: 'Promise' }, promise: {}, events: [], effects: {} };
    document.cookie = 'same=jar; Path=/'; document.cookie = 'jarOnly=jar; Path=/';
    document.cookie = 'pathCookie=root; Path=/'; document.cookie = 'pathCookie=path; Path=/cookie-echo';
    const cases = {
        jar: {}, header: { headers: { Cookie: 'same=header; headerOnly=h' } },
        details: { cookie: 'same=details; detailsOnly=d' },
        both: { headers: { Cookie: 'same=header; headerOnly=h' }, cookie: 'same=details; detailsOnly=d' },
        values: { cookie: 'same=first; same=last; empty=; equals=a=b=c' },
        anonymous: { anonymous: true, headers: { Cookie: 'same=header' }, cookie: 'same=details; explicit=e' },
        redirect: { cookie: 'same=details', url: location.origin + '/cookie-redirect' }
    };
    for (const [name, options] of Object.entries(cases)) {
        try { r.effects[name] = (await GM.xmlHttpRequest({ url: location.origin + '/cookie-echo', ...options })).responseText; }
        catch (e) { r.effects[name] = 'error:' + String(e); }
    }
    fetch('/evidence', { method: 'POST', body: JSON.stringify(r) });
})();
