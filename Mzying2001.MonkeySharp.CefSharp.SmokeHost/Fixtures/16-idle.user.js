// ==UserScript==
// @name Idle before load
// @match http://127.0.0.1:__PORT__/compat/replay
// @run-at document-idle
// @grant none
// ==/UserScript==
document.documentElement.dataset.idleBeforeLoad = String(document.readyState !== 'complete');
