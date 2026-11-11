(function () {
    "use strict";
    const binary = atob("__MONKEYSHARP_LIFECYCLE_BASE64__");
    const bytes = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
    const proof = JSON.parse(new TextDecoder().decode(bytes));

    /*__MONKEYSHARP_LIFECYCLE_EVENTS__*/
    if (globalThis.__MonkeySharpLifecycle) globalThis.__MonkeySharpLifecycle.dispose();
    const cache = createLifecycleEvents(proof.documentId);
    Object.defineProperty(globalThis, "__MonkeySharpLifecycleEvents", { value: cache, configurable: true });
    let disposed = false;
    let bodyTimer;
    const cleanup = [];
    const listen = function (target, type, handler, options) {
        target.addEventListener(type, handler, options);
        cleanup.push(() => target.removeEventListener(type, handler, options));
    };
    Object.defineProperty(globalThis, "__MonkeySharpLifecycle", { configurable: true, value: Object.freeze({
        documentId: proof.documentId,
        dispose() {
            disposed = true;
            if (bodyTimer) clearInterval(bodyTimer);
            cleanup.splice(0).forEach(action => action());
            cache.dispose();
        }
    }) });

    const start = async function () {
        if (globalThis.CefSharp && typeof globalThis.CefSharp.BindObjectAsync === "function") {
            await globalThis.CefSharp.BindObjectAsync("__MonkeySharpBridge");
        }
        if (disposed) return;
        const bridge = globalThis.__MonkeySharpBridge;
        if (!bridge || typeof bridge.lifecycle !== "function") {
            throw new Error("MonkeySharp lifecycle bridge is unavailable.");
        }
        const signal = bridge.lifecycle.bind(bridge);
        const send = stage => disposed ? Promise.resolve() : signal(JSON.stringify(Object.assign({ stage: stage }, proof)))
            .catch(error => console.warn("[MonkeySharp] lifecycle signal failed", error));

        let lastUrl = globalThis.location && globalThis.location.href;
        const checkUrl = function () {
            const current = globalThis.location && globalThis.location.href;
            if (!current || current === lastUrl) return;
            const oldUrl = lastUrl;
            lastUrl = current;
            if (globalThis.__MonkeySharpRuntime && typeof globalThis.__MonkeySharpRuntime.urlChanged === "function") {
                globalThis.__MonkeySharpRuntime.urlChanged({ url: current, oldURL: oldUrl });
            }
            signal(JSON.stringify(Object.assign({ stage: "url-changed", url: current, oldURL: oldUrl }, proof)))
                .catch(error => console.warn("[MonkeySharp] URL lifecycle signal failed", error));
        };
        ["popstate", "hashchange"].forEach(name => listen(globalThis, name, checkUrl));
        ["pushState", "replaceState"].forEach(name => {
            const original = globalThis.history && globalThis.history[name];
            if (typeof original !== "function") return;
            const wrapped = function () {
                const result = original.apply(this, arguments);
                checkUrl();
                return result;
            };
            globalThis.history[name] = wrapped;
            cleanup.push(() => { if (globalThis.history[name] === wrapped) globalThis.history[name] = original; });
        });

        let bodySent = false;
        const sendBody = function () {
            if (!bodySent && document.body) {
                bodySent = true;
                cache.body();
                send("body");
                return true;
            }
            return false;
        };
        if (!sendBody()) {
            bodyTimer = setInterval(function () {
                if (sendBody()) clearInterval(bodyTimer);
            }, 5);
        }

        if (document.readyState === "loading") {
            listen(document, "DOMContentLoaded", () => { send("dom-content-loaded"); setTimeout(() => send("idle"), 0); }, { once: true });
        } else {
            send("dom-content-loaded");
            setTimeout(() => send("idle"), 0);
        }
        if (document.readyState === "complete") {
            send("load");
        } else {
            listen(globalThis, "load", () => send("load"), { once: true });
        }
    };

    listen(globalThis, "pageshow", event => {
        if (event.persisted) {
            if (globalThis.__MonkeySharpRuntime) globalThis.__MonkeySharpRuntime.endDocument(proof.documentId);
            globalThis.__MonkeySharpLifecycle.dispose();
        }
    });
    start().catch(error => console.error("[MonkeySharp] lifecycle setup failed", error));
})();
