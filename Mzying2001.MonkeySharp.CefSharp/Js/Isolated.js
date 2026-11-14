(function () {
    "use strict";
    const binary = atob("__MONKEYSHARP_ISOLATED_BASE64__");
    const bytes = Uint8Array.from(binary, value => value.charCodeAt(0));
    const proof = JSON.parse(new TextDecoder().decode(bytes));
    const root = globalThis;
    const pending = new Map();
    const stages = [];
    /*__MONKEYSHARP_LIFECYCLE_EVENTS__*/
    const lifecycleEvents = createLifecycleEvents(proof.documentId);
    let sendNative;
    let disposed = false;
    let nextId = 0;
    let bodySent = false;
    let endSent = false;
    const send = value => sendNative ? sendNative(JSON.stringify(value)) : stages.push(value);
    const stage = name => { if (!disposed) send({ kind: "stage", stage: name }); };
    const body = function () {
        if (bodySent || !document.body) return;
        bodySent = true;
        lifecycleEvents.body();
        stage("body");
        if (observer) observer.disconnect();
    };
    const ended = function () {
        if (endSent) return;
        endSent = true;
        stage("end");
        setTimeout(() => stage("idle"), 0);
    };
    const observer = new MutationObserver(body);
    observer.observe(document, { childList: true, subtree: true });
    body();
    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", ended, { once: true });
    else ended();
    if (document.readyState === "complete") stage("load");
    else root.addEventListener("load", () => stage("load"), { once: true });
    root.addEventListener("pageshow", event => { if (event.persisted) stage("bfcache"); });
    const transport = Object.freeze({
        generation: proof.generation,
        ready() {
            if (disposed || sendNative) return;
            sendNative = root[proof.binding].bind(root);
            stages.splice(0).forEach(value => sendNative(JSON.stringify(value)));
        },
        complete(id, json) {
            const item = pending.get(id);
            if (!item) return;
            pending.delete(id);
            item.resolve(json);
        },
        dispose() {
            disposed = true;
            observer.disconnect();
            lifecycleEvents.dispose();
            pending.forEach(item => item.reject(new Error("The isolated document ended.")));
            pending.clear();
            if (root.__MonkeySharpRuntime) root.__MonkeySharpRuntime.endDocument(proof.documentId);
        },
        dispatch(json) {
            if (disposed) return Promise.reject(new Error("The isolated document ended."));
            if (pending.size >= proof.maxPending) return Promise.reject(new Error("Too many isolated bridge requests."));
            const id = String(++nextId);
            let request;
            try { request = JSON.parse(json); } catch (error) { return Promise.reject(error); }
            return new Promise((resolve, reject) => {
                // Core starts ordinary deadlines after consent and owns HTTP deadlines.
                // A transport timer here would also expire while the user is deciding.
                pending.set(id, { resolve, reject });
                send({ kind: "api", id, request: json });
            });
        }
    });
    Object.defineProperty(root, "__MonkeySharpIsolatedTransport", { value: transport });
    Object.defineProperty(root, "__MonkeySharpLifecycleEvents", { value: lifecycleEvents });

})();
