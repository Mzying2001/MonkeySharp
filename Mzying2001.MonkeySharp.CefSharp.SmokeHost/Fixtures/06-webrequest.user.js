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
    let version = 0;
    const registered = () => fetch('/compat/barrier/' + (++version), { cache: 'no-store' }).then(response => {
        if (!response.ok) throw new Error('Registration barrier failed');
    });
    const observed = async predicate => {
        const deadline = performance.now() + 10000;
        while (!predicate()) {
            if (performance.now() >= deadline) throw new Error('webRequest callback was not observed');
            await new Promise(resolve => setTimeout(resolve, 10));
        }
    };
    const callback = (info, message, details) => result.events.push({ info, message, url: details.url, target: details.redirect_url });
    try {
        const base = location.origin + '/compat/net/';
        result.checks.staticFirstScript = !globalThis.staticBlocked && globalThis.staticTarget === true;
        result.checks.voidReturn = GM.webRequest([{ selector: base + 'blocked', action: { cancel: true } }], callback) === undefined;
        await registered();
        let rejected = false;
        try { await fetch(base + 'blocked', { cache: 'no-store' }); } catch (error) { rejected = true; }
        await observed(() => result.events.some(event => event.info === 'cancel'));
        result.checks.cancel = rejected && result.events.some(event => event.info === 'cancel' && event.message === 'ok');
        GM_webRequest([{ selector: base + 'original', action: { redirect: { from: '/(original)$', to: '/target' } } }], callback);
        await registered();
        result.checks.replacement = await (await fetch(base + 'blocked', { cache: 'no-store' })).text() === 'blocked';
        result.checks.redirect = await (await fetch(base + 'original', { cache: 'no-store' })).text() === 'target';
        await observed(() => result.events.some(event => event.info === 'redirect'));
        result.checks.capture = result.events.some(event => event.info === 'redirect' && event.message === 'ok' && event.target === base + 'target');
        GM.webRequest([]);
        await registered();
        const count = result.events.length;
        result.checks.restored = await (await fetch(base + 'original', { cache: 'no-store' })).text() === 'original';
        result.checks.noCallbackAfterRevoke = result.events.length === count;
        const socketUrl = location.origin.replace(/^http/, 'ws') + '/compat/net/websocket';
        GM.webRequest([{ selector: socketUrl, action: 'cancel' }], callback);
        await registered();
        result.websocket = await new Promise(resolve => {
            const socket = new WebSocket(socketUrl);
            socket.onopen = () => { socket.close(); resolve('open'); };
            socket.onerror = () => resolve('error');
            setTimeout(() => { socket.close(); resolve('timeout'); }, 2000);
        });
        GM.webRequest([{ selector: base + 'cleanup', action: 'cancel' }]);
        await registered();
        result.done = true;
    } catch (error) { result.error = String(error); }
})();
