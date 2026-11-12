// ==UserScript==
// @name cookie return probe
// @namespace MonkeySharp.Reference
// @match http://127.0.0.1/*
// @grant GM_cookie
// @grant GM.cookie
// @run-at document-end
// ==/UserScript==
(async function () {
    const evidence = { schemaVersion: 1, caseId: 'cookie-returns', immediateReturn: {}, events: [], promise: {}, effects: {} };
    const capture = async function (name, action) {
        try {
            const result = action();
            evidence.immediateReturn[name] = { type: typeof result, then: Boolean(result && typeof result.then === 'function') };
            if (result && typeof result.then === 'function') {
                try { const value = await result; evidence.promise[name] = { state: 'fulfilled', type: typeof value, value }; }
                catch (error) { evidence.promise[name] = { state: 'rejected', error: String(error) }; }
            }
        } catch (error) { evidence.immediateReturn[name] = { error: String(error) }; }
    };
    evidence.effects.modern = typeof GM.cookie;
    evidence.effects.legacy = typeof GM_cookie;
    if (typeof GM.cookie === 'object') {
        await capture('modern-set', () => GM.cookie.set({ name: 'reference', value: 'yes' }));
        await capture('modern-list', () => GM.cookie.list({ name: 'reference' }));
        await capture('modern-delete', () => GM.cookie.delete({ name: 'reference' }));
        await GM.cookie.set({url:location.origin+'/nested/page',name:'default-path',value:'yes'});
        evidence.effects.defaultPath = (await GM.cookie.list({url:location.origin+'/nested/page',name:'default-path'}))[0].path;
        await GM.cookie.delete({url:location.origin+'/nested/page',name:'default-path'});
    }
    evidence.effects.legacyMembers = typeof GM_cookie === 'undefined' ? [] : Object.keys(GM_cookie);
    if (typeof GM_cookie === 'object' || typeof GM_cookie === 'function') {
        await capture('legacy-set', () => GM_cookie.set({ name: 'reference', value: 'yes' }));
        await capture('legacy-list', () => GM_cookie.list({ name: 'reference' }));
        await capture('legacy-delete', () => GM_cookie.delete({ name: 'reference' }));
    }
    if (!Object.keys(evidence.immediateReturn).length) evidence.immediateReturn.available = false;
    await fetch('/evidence', { method: 'POST', body: JSON.stringify(evidence) });
})();
