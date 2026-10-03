(function () {
    "use strict";
    const binary = atob("__MONKEYSHARP_LIFECYCLE_BASE64__");
    const bytes = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
    const proof = JSON.parse(new TextDecoder().decode(bytes));

    const start = async function () {
        if (globalThis.CefSharp && typeof globalThis.CefSharp.BindObjectAsync === "function") {
            await globalThis.CefSharp.BindObjectAsync("__MonkeySharpBridge");
        }
        const bridge = globalThis.__MonkeySharpBridge;
        if (!bridge || typeof bridge.lifecycle !== "function") {
            throw new Error("MonkeySharp lifecycle bridge is unavailable.");
        }
        const signal = bridge.lifecycle.bind(bridge);
        const send = stage => signal(JSON.stringify(Object.assign({ stage: stage }, proof)))
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
        ["popstate", "hashchange"].forEach(name => globalThis.addEventListener(name, checkUrl));
        ["pushState", "replaceState"].forEach(name => {
            const original = globalThis.history && globalThis.history[name];
            if (typeof original !== "function") return;
            globalThis.history[name] = function () {
                const result = original.apply(this, arguments);
                checkUrl();
                return result;
            };
        });

        let bodySent = false;
        const sendBody = function () {
            if (!bodySent && document.body) {
                bodySent = true;
                send("body");
                return true;
            }
            return false;
        };
        if (!sendBody()) {
            const timer = setInterval(function () {
                if (sendBody()) clearInterval(timer);
            }, 5);
        }

        if (document.readyState === "loading") {
            document.addEventListener("DOMContentLoaded", () => send("dom-content-loaded"), { once: true });
        } else {
            send("dom-content-loaded");
        }
        if (document.readyState === "complete") {
            send("load");
        } else {
            globalThis.addEventListener("load", () => send("load"), { once: true });
        }
    };

    start().catch(error => console.error("[MonkeySharp] lifecycle setup failed", error));
})();
