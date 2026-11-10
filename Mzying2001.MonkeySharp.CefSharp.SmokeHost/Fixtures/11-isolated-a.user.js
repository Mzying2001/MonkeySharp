// ==UserScript==
// @name Isolated A
// @match http://127.0.0.1:__PORT__/compat/isolated*
// @sandbox DOM
// @grant GM.info
// @grant GM_getValue
// @grant GM_setValue
// @grant GM_getResourceText
// @grant GM.xmlHttpRequest
// @grant window.onurlchange
// @resource isolatedText https://content.example/isolated
// @connect 127.0.0.1
// @run-at document-start
// ==/UserScript==
(async function () {
    const state = { world: GM_info.compatibility.executionWorld, unsafe: typeof unsafeWindow, page: typeof pageOnlyCanary,
        resource: GM_getResourceText('isolatedText'), sandbox: GM_info.sandboxMode, changes: [], once: 0, aborted: 0, tail: 0 };
    globalThis.privateWorldValue = 'A';
    const publish = () => document.documentElement.setAttribute('data-isolated-a', JSON.stringify(state));
    try {
        GM_setValue(location.pathname, 41);
        state.synchronousValue = GM_getValue(location.pathname);
        await GM.setValue(location.pathname, 42);
        state.committedValue = GM_getValue(location.pathname);
        state.xhr = (await GM.xmlHttpRequest({ url: location.origin + '/xhr' })).responseText;
        const denied = JSON.parse(await globalThis.__MonkeySharpIsolatedTransport.dispatch(JSON.stringify({ protocol: 2,
            type: 'hello', documentId: 'another-document', scriptKey: GM_info.scriptKey, capability: 'forged' })));
        state.forgeryDenied = denied.ok === false && denied.error.code === 'MSP003_SESSION_EXPIRED';
        window.onurlchange = event => { state.changes.push(event.url); publish(); };
        window.addEventListener('urlchange', () => state.once++, { once: true });
        const abort = new AbortController();
        window.addEventListener('urlchange', () => state.aborted++, { signal: abort.signal }); abort.abort();
        window.addEventListener('urlchange', () => { throw new Error('Expected isolated URL callback failure'); });
        window.addEventListener('urlchange', () => { state.tail++; publish(); });
        await new Promise(resolve => setTimeout(resolve, 50));
        state.privateValue = globalThis.privateWorldValue;
        state.done = true;
    } catch (error) { state.error = String(error); }
    publish();
})();
