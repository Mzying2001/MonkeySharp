// ==UserScript==
// @name Private context condition
// @match http://127.0.0.1:__PORT__/compat/conditions*
// @run-in incognito-tabs
// @run-at document-end
// @grant none
// ==/UserScript==
document.documentElement.dataset.privateCondition = 'ran';
