// ==UserScript==
// @name Compatibility request effects
// @match http://127.0.0.1:__PORT__/compat/webrequest
// @match http://127.0.0.1:__PORT__/compat/net/*
// @grant GM.webRequest
// @run-at document-end
// @grant GM_webRequest
// ==/UserScript==
(async function () {
    if (location.pathname !== '/compat/webrequest') return;
    const result = globalThis.__compatCase = { checks: {}, events: [] };
    const wait = async function (predicate) { while (!predicate()) await new Promise(function (resolve) { setTimeout(resolve, 25); }); };
    const callback = function (info, message, details) { result.events.push({ url: info.url, message: message, target: details.redirectUrl }); };
    try {
        const base = location.origin + '/compat/net/';
        const blocked = GM.webRequest([{ selector: base + 'blocked', action: 'cancel' }], callback);
        await wait(function () { return !!blocked.id; });
        let rejected = false;
        try { await fetch(base + 'blocked', { cache: 'no-store' }); } catch (error) { rejected = true; }
        await wait(function () { return result.events.some(function (event) { return event.url === base + 'blocked' && event.message === 'cancel'; }); });
        result.checks.cancel = rejected;
        const redirected = GM_webRequest([{ selector: base + 'original', action: base + 'target' }], callback);
        await wait(function () { return !!redirected.id; });
        result.checks.redirect = await (await fetch(base + 'original', { cache: 'no-store' })).text() === 'target';
        await wait(function () { return result.events.some(function (event) { return event.url === base + 'original' && event.message === 'redirect' && event.target === base + 'target'; }); });
        result.checks.remove = await blocked.remove() === true && await redirected.remove() === true;
        const eventCount = result.events.length;
        result.checks.restored = await (await fetch(base + 'blocked', { cache: 'no-store' })).text() === 'blocked';
        result.checks.noCallbackAfterRemove = result.events.length === eventCount;
        const cleanup = GM.webRequest([{ selector: base + 'cleanup', action: 'cancel' }]);
        await wait(function () { return !!cleanup.id; });
        result.done = true;
    } catch (error) { result.error = String(error); }
})();
