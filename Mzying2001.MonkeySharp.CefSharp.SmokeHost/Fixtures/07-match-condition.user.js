// ==UserScript==
// @name Match condition
// @include *://127.0.0.1:__PORT__/COMPAT/CONDITIONS*
// @exclude *://127.0.0.1:__PORT__/compat/conditions-blocked*
// @run-at document-end
// @grant none
// ==/UserScript==
document.documentElement.dataset.matchCondition = 'ran';
