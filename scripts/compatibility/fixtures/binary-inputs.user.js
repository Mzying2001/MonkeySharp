// ==UserScript==
// @name Binary inputs reference
// @namespace MonkeySharp compatibility
// @match http://127.0.0.1/*
// @run-at document-end
// @grant GM.xmlHttpRequest
// ==/UserScript==
(async function () {
    const result = { schemaVersion: 1, caseId: "binary-inputs", immediateReturn: {}, events: [], promise: {}, effects: {} };
    const context = {};
    const bytes = new TextEncoder().encode("blob text 你好");
    const source = new Blob([bytes], { type: "text/plain;charset=utf-8" });
    const request = GM.xmlHttpRequest({ url: source, context, onload(response) {
        result.effects.callbackThis = this === response;
        result.effects.contextIdentity = response.context === context;
        result.events.push({ name: "load" });
    } });
    result.immediateReturn.abort = typeof request.abort;
    try {
        const response = await request;
        result.effects.blobText = response.responseText;
        const url = URL.createObjectURL(new File([new Uint8Array([0, 255, 1])], "bytes.bin"));
        try { result.effects.urlBytes = Array.from(new Uint8Array((await GM.xmlHttpRequest({ url, responseType: "arraybuffer" })).response)); }
        finally { URL.revokeObjectURL(url); }
        result.effects.fileText = (await GM.xmlHttpRequest({ url: new File(["file text"], "text.txt", { type: "text/plain" }) })).responseText;
    } catch (error) { result.effects.error = String(error); }
    fetch("/evidence", { method: "POST", body: JSON.stringify(result) });
})();
