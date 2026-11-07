const result = {
    schemaVersion: 1, caseId: "__CASE__", events: [{ name: "source", thisIsWindow: this === window }], promise: {}, effects: {},
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
