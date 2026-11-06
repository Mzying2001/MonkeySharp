// ==UserScript==
// @name Compatibility cookie lifecycle
// @match http://127.0.0.1:__PORT__/compat/cookie
// @grant GM.cookie
// @grant GM.xmlHttpRequest
// @connect 127.0.0.1
// @run-at document-end
// @grant GM_cookie
// ==/UserScript==
(async function () {
    const result = globalThis.__compatCase = { checks: {} };
    const wait = async function (predicate) { while (!predicate()) await new Promise(function (resolve) { setTimeout(resolve, 25); }); };
    const legacy = function (operation, details) {
        return new Promise(function (resolve, reject) {
            GM_cookie[operation](details, function (value, error) {
                if (operation === 'list') { if (error) reject(new Error(error)); else resolve(value); }
                else if (value) reject(new Error(value)); else resolve();
            });
        });
    };
    try {
        const details = { url: location.href, name: 'compat-cookie', value: 'first', path: '/compat/' };
        await GM.cookie.set(details);
        result.checks.setList = (await GM.cookie.list({ url: location.href })).some(function (cookie) { return cookie.name === details.name && cookie.value === 'first'; });
        const seen = [];
        const listener = GM.cookie.addListener({ url: location.href }, function (cookie) { if (cookie.name === details.name) seen.push(cookie.value); });
        // Registration is asynchronous and exposes no readiness Promise. Probe
        // until an observable event confirms it before testing a single mutation.
        while (seen.indexOf('probe') < 0) {
            await GM.cookie.set(Object.assign({}, details, { value: 'probe' }));
            await new Promise(function (resolve) { setTimeout(resolve, 25); });
        }
        await legacy('set', Object.assign({}, details, { value: 'changed' }));
        await wait(function () { return seen.indexOf('changed') >= 0; });
        result.checks.legacy = (await legacy('list', { url: location.href })).some(function (cookie) { return cookie.name === details.name && cookie.value === 'changed'; });
        const echo = await GM.xmlHttpRequest({ url: location.origin + '/compat/cookie-echo' });
        const anonymous = await GM.xmlHttpRequest({ url: location.origin + '/compat/cookie-echo', anonymous: true });
        result.checks.context = echo.responseText.indexOf('compat-cookie=changed') >= 0 && anonymous.responseText.indexOf('compat-cookie=') < 0;
        result.checks.listener = await GM.cookie.removeListener(listener) === true;
        const count = seen.length;
        let barrier = false;
        const sentinel = GM.cookie.addListener({ url: location.href }, function (cookie) { if (cookie.name === details.name && cookie.value === 'after-remove') barrier = true; });
        while (!barrier) {
            await GM.cookie.set(Object.assign({}, details, { value: 'after-remove' }));
            await new Promise(function (resolve) { setTimeout(resolve, 25); });
        }
        result.checks.removed = seen.length === count;
        await GM.cookie.removeListener(sentinel);
        await legacy('delete', details);
        result.checks.deleted = !(await GM.cookie.list({ url: location.href })).some(function (cookie) { return cookie.name === details.name; });
        result.done = true;
    } catch (error) { result.error = String(error); }
})();
