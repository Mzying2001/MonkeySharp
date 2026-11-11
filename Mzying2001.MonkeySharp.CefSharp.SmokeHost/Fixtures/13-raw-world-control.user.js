// ==UserScript==
// @name Raw world control
// @match http://127.0.0.1:__PORT__/compat/isolated*
// @sandbox raw
// @run-at document-start
// @grant none
// ==/UserScript==
window.__rawControlCount = (window.__rawControlCount || 0) + 1;
document.documentElement.dataset.rawControlCount = String(window.__rawControlCount);
