// ==UserScript==
// @name Grant exposure legacy
// @namespace https://github.com/Mzying2001/MonkeySharp
// @version 1
// @match http://127.0.0.1/*
// @run-at document-end
// @grant GM_getValue
// ==/UserScript==
const result = {
    schemaVersion: 1, caseId: "grants-legacy", events: [{ name: "source", thisIsWindow: this === window }], promise: {}, effects: {},
    immediateReturn: {
        GM: typeof GM,
        GM_info: typeof GM_info,
        "GM.info": typeof GM === "undefined" ? "undefined" : typeof GM.info,
        GM_getValue: typeof GM_getValue,
        "GM.getValue": typeof GM === "undefined" ? "undefined" : typeof GM.getValue,
        GM_setValue: typeof GM_setValue,
        "GM.setValue": typeof GM === "undefined" ? "undefined" : typeof GM.setValue,
        unsafeWindow: typeof unsafeWindow
    }
};
result.info = { handler: GM_info.scriptHandler, version: GM_info.version, userAgent: navigator.userAgent };
result.effects.grants = GM_info.script.grant;
fetch("/evidence", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(result) });
