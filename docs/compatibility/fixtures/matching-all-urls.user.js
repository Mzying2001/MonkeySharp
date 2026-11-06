// ==UserScript==
// @name Unsupported all URLs reference
// @namespace MonkeySharp compatibility
// @run-at document-end
// @match <all_urls>
// @grant none
// ==/UserScript==
fetch("/evidence", { method: "POST", body: JSON.stringify({ caseId: "matching-all-urls", effects: { executed: true } }) });
