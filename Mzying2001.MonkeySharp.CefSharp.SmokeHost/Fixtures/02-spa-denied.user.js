// ==UserScript==
// @name Compatibility SPA without URL grant
// @match http://127.0.0.1:__PORT__/compat/spa*
// @grant none
// @run-at document-end
// ==/UserScript==
const denied = globalThis.__compatSpaDenied = { hidden: typeof window.onurlchange === 'undefined', events: 0 };
window.addEventListener('urlchange', function () { denied.events++; });
