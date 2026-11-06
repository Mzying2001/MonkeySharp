// ==UserScript==
// @name Isolated B
// @match http://127.0.0.1:__PORT__/compat/isolated*
// @sandbox DOM
// @grant GM.info
// @run-at document-end
// ==/UserScript==
globalThis.privateWorldValue = 'B';
setTimeout(function () {
    document.documentElement.setAttribute('data-isolated-b', JSON.stringify({ done: true, value: globalThis.privateWorldValue,
        page: typeof pageOnlyCanary, world: GM_info.compatibility.executionWorld }));
}, 50);
