// ==UserScript==
// @name Raw CSP effects
// @match http://127.0.0.1:__PORT__/compat/csp
// @sandbox raw
// @grant none
// @run-at document-end
// ==/UserScript==
document.documentElement.dataset.cspRaw = 'executed';
