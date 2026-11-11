// ==UserScript==
// @name Lifecycle idle reference
// @namespace MonkeySharp compatibility
// @match http://127.0.0.1/*
// @run-at document-idle
// @grant GM_getValue
// ==/UserScript==
const result = { caseId: "lifecycle-idle", schemaVersion: 1, events: [], promise: {}, immediateReturn: { readyState: document.readyState }, effects: {} };
result.events.push({ name: "source" });
for (const type of ["DOMContentLoaded", "load"]) {
    window.addEventListener(type, event => result.events.push({ name: "window:" + type, trusted: event.isTrusted }));
    document.addEventListener(type, event => result.events.push({ name: "document:" + type, trusted: event.isTrusted }));
}
result.events.push({ name: "registered" });
setTimeout(() => fetch("/evidence", { method: "POST", body: JSON.stringify(result) }), 150);
