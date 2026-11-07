// ==UserScript==
// @name Default matching reference
// @namespace MonkeySharp compatibility
// @run-at document-end
// @grant none
// ==/UserScript==
fetch("/evidence", { method: "POST", body: JSON.stringify({ schemaVersion: 1, caseId: "matching-default", events: [], promise: {}, immediateReturn: { source: "executed" }, effects: { defaultSelector: true } }) });
