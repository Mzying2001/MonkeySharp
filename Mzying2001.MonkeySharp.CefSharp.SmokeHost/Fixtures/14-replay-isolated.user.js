// ==UserScript==
// @name Replay isolated
// @match http://127.0.0.1:__PORT__/compat/replay
// @sandbox DOM
// @grant GM_getValue
// @run-at document-start
// ==/UserScript==
const events = [];
const publish = () => document.documentElement.setAttribute('data-replay-isolated', JSON.stringify({ done: events.length === 2, events }));
window.addEventListener('DOMContentLoaded', event => { events.push({ type: event.type, trusted: event.isTrusted }); publish(); }, { once: true });
window.addEventListener('load', event => { events.push({ type: event.type, trusted: event.isTrusted }); publish(); }, { once: true });
