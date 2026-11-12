// ==UserScript==
// @name Compatibility request effects
// @match http://127.0.0.1:__PORT__/compat/webrequest
// @match http://127.0.0.1:__PORT__/compat/net/*
// @webRequest [{"selector":"*/compat/net/static-blocked.js","action":{"cancel":true}},{"selector":"*/compat/net/static-old.js","action":{"redirect":{"from":"/static-old\\.js$","to":"/static-target.js"}}}]
// @grant GM.webRequest
// @grant GM_webRequest
// @run-at document-end
// ==/UserScript==
(async function () {
    if (location.pathname !== '/compat/webrequest') return;
    const result = globalThis.__compatCase = { checks: {}, events: [] };
    const pause = () => new Promise(resolve => setTimeout(resolve, 200));
    const callback = (info, message, details) => result.events.push({ info, message, url: details.url, target: details.redirect_url });
    try {
        const base = location.origin + '/compat/net/';
        result.checks.staticFirstScript = !globalThis.staticBlocked && globalThis.staticTarget === true;
        result.checks.voidReturn = GM.webRequest([{ selector: base + 'blocked', action: { cancel: true } }], callback) === undefined;
        await pause();
        let rejected = false;
        try { await fetch(base + 'blocked', { cache: 'no-store' }); } catch (error) { rejected = true; }
        await pause();
        result.checks.cancel = rejected && result.events.some(event => event.info === 'cancel' && event.message === 'ok');
        GM_webRequest([{ selector: base + 'original', action: { redirect: { from: '/(original)$', to: '/target' } } }], callback);
        await pause();
        result.checks.replacement = await (await fetch(base + 'blocked', { cache: 'no-store' })).text() === 'blocked';
        result.checks.redirect = await (await fetch(base + 'original', { cache: 'no-store' })).text() === 'target';
        await pause();
        result.checks.capture = result.events.some(event => event.info === 'redirect' && event.message === 'ok' && event.target === base + 'target');
        GM.webRequest([]);
        await pause();
        const count = result.events.length;
        result.checks.restored = await (await fetch(base + 'original', { cache: 'no-store' })).text() === 'original';
        await pause();
        result.checks.noCallbackAfterRevoke = result.events.length === count;
        const socketUrl = location.origin.replace(/^http/, 'ws') + '/compat/net/websocket';
        GM.webRequest([{ selector: socketUrl, action: 'cancel' }], callback);
        await pause();
        result.websocket = await new Promise(resolve => {
            const socket = new WebSocket(socketUrl);
            socket.onopen = () => { socket.close(); resolve('open'); };
            socket.onerror = () => resolve('error');
            setTimeout(() => { socket.close(); resolve('timeout'); }, 2000);
        });
        GM.webRequest([{ selector: base + 'cleanup', action: 'cancel' }]);
        await pause();
        result.done = true;
    } catch (error) { result.error = String(error); }
})();
