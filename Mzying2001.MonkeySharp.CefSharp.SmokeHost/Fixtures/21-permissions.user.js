// ==UserScript==
// @name Permission browser
// @match http://127.0.0.1:__PORT__/compat/permissions
// @grant GM.xmlHttpRequest
// @connect 127.0.0.1
// @run-at document-end
// ==/UserScript==
(async function () {
    const state = globalThis.__permissions = {};
    try {
        const request = url => GM.xmlHttpRequest({ url, timeout: 150 });
        const first = await Promise.all([request(location.origin + '/compat/permission-ok?a'), request(location.origin + '/compat/permission-ok?b')]);
        state.coalesced = first.every(response => response.responseText === 'permission-ok');
        state.once = (await request(location.origin + '/compat/permission-ok?later')).status === 200;
        try { await request(location.origin + '/compat/permission-redirect'); } catch (error) { state.redirectDenied = error.code === 'MSP005_PERMISSION_DENIED'; }
        try { await request('http://127.0.0.1:__OTHER_PORT__/compat/permission-denied'); } catch (error) { state.rememberedDeny = error.code === 'MSP005_PERMISSION_DENIED'; }
        try { await request('http://localhost:__OTHER_PORT__/compat/permission-denied'); } catch (error) { state.declarationDenied = error.code === 'MSP005_PERMISSION_DENIED'; }
        state.done = true;
    } catch (error) { state.error = String(error); }
})();
