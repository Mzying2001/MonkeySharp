// ==UserScript==
// @name Isolated CSP effects
// @match http://127.0.0.1:__PORT__/compat/csp
// @sandbox DOM
// @grant GM.info
// @grant GM_addStyle
// @grant GM_addElement
// @run-at document-end
// ==/UserScript==
const node = GM_addElement('span', { id: 'isolated-csp-node', textContent: 'isolated source' });
GM_addStyle('#isolated-csp-node { color: rgb(11, 22, 33); }');
document.documentElement.setAttribute('data-csp-isolated', JSON.stringify({ done: true, dom: node.textContent === 'isolated source',
    color: getComputedStyle(node).color, world: GM_info.compatibility.executionWorld }));
