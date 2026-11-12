// ==UserScript==
// @name Isolated manual frame
// @match http://127.0.0.1/compat/isolated*
// @sandbox DOM
// @run-at context-menu
// @grant GM_getValue
// ==/UserScript==
document.documentElement.dataset.manualFrameCount = String(Number(document.documentElement.dataset.manualFrameCount || 0) + 1);
document.documentElement.dataset.manualFrameValue = GM_getValue('missing', 'ready');
