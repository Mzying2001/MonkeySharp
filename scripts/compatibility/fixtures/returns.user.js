// ==UserScript==
// @name MonkeySharp differential returns
// @namespace https://github.com/Mzying2001/MonkeySharp
// @version 1.0.0
// @match http://127.0.0.1/*
// @run-at document-end
// @grant GM_info
// @grant GM.info
// @grant GM_addStyle
// @grant GM.addStyle
// @grant GM_addElement
// @grant GM.addElement
// @grant GM_registerMenuCommand
// @grant GM.registerMenuCommand
// @grant GM_unregisterMenuCommand
// @grant GM.unregisterMenuCommand
// @grant GM_notification
// @grant GM.notification
// @grant GM_openInTab
// @grant GM.openInTab
// ==/UserScript==
(function () {
    const result = { schemaVersion: 1, caseId: "returns", immediateReturn: {}, events: [], promise: {}, effects: {} };
    const shape = value => ({
        type: typeof value,
        prototype: value == null ? null : Object.prototype.toString.call(value),
        then: !!value && typeof value.then === "function",
        abort: !!value && typeof value.abort === "function",
        close: !!value && typeof value.close === "function",
        closed: value && typeof value.closed === "boolean" ? value.closed : null
    });
    const event = (name, value) => result.events.push({ name, value });
    const probe = function (name, action, effect) {
        try {
            const value = action();
            result.immediateReturn[name] = shape(value);
            event(name + ":return", shape(value));
            if (value && typeof value.then === "function") {
                result.promise[name] = { state: "pending" };
                value.then(resolved => {
                    result.promise[name] = { state: "fulfilled", shape: shape(resolved), value: typeof resolved === "boolean" ? resolved : null };
                    event(name + ":fulfilled", shape(resolved));
                    if (effect) effect(resolved);
                }, error => {
                    result.promise[name] = { state: "rejected", error: String(error) };
                    event(name + ":rejected", String(error));
                });
            } else if (effect) effect(value);
            return value;
        } catch (error) { event(name + ":throw", String(error)); }
    };
    result.info = { handler: GM_info.scriptHandler, version: GM_info.version, userAgent: navigator.userAgent };
    probe("GM_addStyle", () => GM_addStyle("body { --legacy-probe: 17; }"), value => { result.effects.legacyStyle = !!value && value.nodeName === "STYLE"; });
    probe("GM.addStyle", () => GM.addStyle("body { --modern-probe: 23; }"), value => { result.effects.modernStyle = !!value && value.nodeName === "STYLE"; });
    probe("GM_addElement", () => GM_addElement("span", { id: "legacy-probe", textContent: "legacy" }), value => { result.effects.legacyElement = !!value && value.id === "legacy-probe"; });
    probe("GM.addElement", () => GM.addElement("span", { id: "modern-probe", textContent: "modern" }), value => { result.effects.modernElement = !!value && value.id === "modern-probe"; });
    const legacyId = probe("GM_registerMenuCommand", () => GM_registerMenuCommand("legacy probe", e => event("legacy-menu-click", { type: e.type, trusted: e.isTrusted })));
    probe("GM_unregisterMenuCommand", () => GM_unregisterMenuCommand(legacyId));
    probe("GM.registerMenuCommand", () => GM.registerMenuCommand("modern probe", e => event("modern-menu-click", { type: e.type, trusted: e.isTrusted })), id => probe("GM.unregisterMenuCommand", () => GM.unregisterMenuCommand(id)));
    // Tab probes only run on the main page; their target never matches this script.
    const target = "about:blank";
    probe("GM_openInTab", () => GM_openInTab(target, { active: false }), tab => { tab.onclose = () => event("legacy-tab-closed", { closed: tab.closed }); setTimeout(() => tab.close(), 100); });
    probe("GM.openInTab", () => GM.openInTab(target, { active: false }), tab => { tab.onclose = () => event("modern-tab-closed", { closed: tab.closed }); setTimeout(() => tab.close(), 100); });
    probe("GM_notification", () => GM_notification({ text: "Legacy return probe", timeout: 200, ondone: () => event("legacy-notification-done") }));
    probe("GM.notification", () => GM.notification({ text: "Modern return probe", timeout: 200, ondone: () => event("modern-notification-done") }));
    Promise.resolve().then(() => event("microtask"));
    setTimeout(() => {
        result.effects.styles = [getComputedStyle(document.body).getPropertyValue("--legacy-probe").trim(), getComputedStyle(document.body).getPropertyValue("--modern-probe").trim()];
        result.effects.nodes = [!!document.getElementById("legacy-probe"), !!document.getElementById("modern-probe")];
        const pre = document.createElement("pre");
        pre.id = "compatibility-evidence";
        pre.textContent = JSON.stringify(result);
        document.body.appendChild(pre);
        fetch("/evidence", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(result) });
    }, 1500);
})();
