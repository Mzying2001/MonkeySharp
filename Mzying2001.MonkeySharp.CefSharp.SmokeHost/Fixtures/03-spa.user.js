// ==UserScript==
// @name Compatibility SPA
// @match http://127.0.0.1:__PORT__/compat/spa*
// @grant window.onurlchange
// @run-at document-end
// ==/UserScript==
const result = globalThis.__compatSpa = {
    runs: (globalThis.__compatSpa || {}).runs + 1 || 1, events: [], listeners: []
};
window.onurlchange = function (event) { result.events.push({ url: event.url, oldURL: event.oldURL }); };
window.addEventListener('urlchange', function (event) { result.listeners.push({ url: event.url, oldURL: event.oldURL }); });
result.done = true;
