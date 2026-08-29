(function () {
    "use strict";

    const decodePayload = function (base64) {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
        return JSON.parse(new TextDecoder().decode(bytes));
    };

    const payload = decodePayload("__MONKEYSHARP_PAYLOAD_BASE64__");
    const root = globalThis;
    const runtimeName = "__MonkeySharpRuntime";
    let runtime = root[runtimeName];

    if (!runtime) {
        runtime = (function () {
            const executions = new Map();
            let nextListenerId = 1;

            const uuid = function () {
                if (root.crypto && typeof root.crypto.randomUUID === "function") return root.crypto.randomUUID();
                const bytes = new Uint8Array(16);
                root.crypto.getRandomValues(bytes);
                bytes[6] = (bytes[6] & 0x0f) | 0x40;
                bytes[8] = (bytes[8] & 0x3f) | 0x80;
                const hex = Array.from(bytes, value => value.toString(16).padStart(2, "0"));
                return hex.slice(0, 4).join("") + "-" + hex.slice(4, 6).join("") + "-" +
                    hex.slice(6, 8).join("") + "-" + hex.slice(8, 10).join("") + "-" + hex.slice(10).join("");
            };

            const decodeResult = value => value && value.$monkeySharpType === "undefined" ? undefined : value;

            const serializableValue = function (value) {
                if (typeof value === "undefined" || typeof value === "function" || typeof value === "symbol") {
                    throw new TypeError("The value is not JSON serializable.");
                }
                try {
                    const json = JSON.stringify(value);
                    if (typeof json === "undefined") throw new TypeError("The value is not JSON serializable.");
                    return JSON.parse(json);
                } catch (error) {
                    throw new TypeError("The value is not JSON serializable: " + error.message);
                }
            };

            const resolveDispatch = async function () {
                if (root.CefSharp && typeof root.CefSharp.BindObjectAsync === "function") {
                    await root.CefSharp.BindObjectAsync("__MonkeySharpBridge");
                }
                const bridge = root.__MonkeySharpBridge;
                return bridge && typeof bridge.dispatch === "function" ? bridge.dispatch.bind(bridge) : null;
            };

            const createProof = function (plan, invocation) {
                return Object.freeze({
                    protocol: plan.protocol,
                    documentId: plan.documentId,
                    scriptKey: invocation.scriptKey,
                    capability: invocation.capability
                });
            };

            const send = async function (dispatch, envelope) {
                const responseJson = await dispatch(JSON.stringify(envelope));
                const response = typeof responseJson === "string" ? JSON.parse(responseJson) : responseJson;
                if (!response || response.protocol !== 1) throw new Error("MonkeySharp returned an invalid bridge response.");
                return response;
            };

            const call = async function (record, method, parameters, signal) {
                const requestId = uuid();
                const envelope = Object.assign({
                    type: "request",
                    requestId: requestId,
                    method: method,
                    params: parameters
                }, record.proof);
                let abortHandler;
                let response;
                try {
                    const dispatchPromise = send(record.dispatch, envelope);
                    response = signal ? await Promise.race([
                        dispatchPromise,
                        new Promise((resolve, reject) => {
                            abortHandler = function () {
                                send(record.dispatch, Object.assign({ type: "cancel", requestId: requestId }, record.proof))
                                    .catch(error => console.warn("[MonkeySharp] cancel failed", error));
                                const error = new Error("The request was canceled.");
                                error.name = "AbortError";
                                reject(error);
                            };
                            if (signal.aborted) abortHandler();
                            else signal.addEventListener("abort", abortHandler, { once: true });
                        })
                    ]) : await dispatchPromise;
                } finally {
                    if (signal && abortHandler) signal.removeEventListener("abort", abortHandler);
                }
                if (response.type !== "response" || response.requestId !== requestId) {
                    throw new Error("MonkeySharp returned a response for a different request.");
                }
                if (!response.ok) {
                    const error = new Error(response.error && response.error.message || "MonkeySharp API request failed.");
                    error.code = response.error && response.error.code;
                    throw error;
                }
                return decodeResult(response.result);
            };

            const createApi = function (record, invocation, availableApis) {
                const grants = new Set(invocation.grants);
                const enabled = name => grants.has(name) && availableApis.has(name);
                const api = {};
                if (enabled("GM.info")) {
                    Object.defineProperty(api, "info", { value: Object.freeze(invocation.info), enumerable: true });
                }
                if (enabled("GM.log")) api.log = async value => call(record, "GM.log", { value: serializableValue(value) });
                if (enabled("GM.getValue")) {
                    api.getValue = async function (key, defaultValue) {
                        if (typeof key !== "string" || key.length === 0) throw new TypeError("key must be a non-empty string.");
                        const parameters = { key: key };
                        if (arguments.length > 1 && typeof defaultValue !== "undefined") {
                            parameters.defaultValue = serializableValue(defaultValue);
                        }
                        return call(record, "GM.getValue", parameters);
                    };
                }
                if (enabled("GM.setValue")) {
                    api.setValue = async function (key, value) {
                        if (typeof key !== "string" || key.length === 0) throw new TypeError("key must be a non-empty string.");
                        await call(record, "GM.setValue", { key: key, value: serializableValue(value) });
                    };
                }
                if (enabled("GM.deleteValue")) {
                    api.deleteValue = async function (key) {
                        if (typeof key !== "string" || key.length === 0) throw new TypeError("key must be a non-empty string.");
                        return call(record, "GM.deleteValue", { key: key });
                    };
                }
                if (enabled("GM.listValues")) api.listValues = async () => call(record, "GM.listValues", {});
                if (enabled("GM.addValueChangeListener")) {
                    api.addValueChangeListener = function (key, callback) {
                        if (typeof key !== "string" || key.length === 0) throw new TypeError("key must be a non-empty string.");
                        if (typeof callback !== "function") throw new TypeError("callback must be a function.");
                        const listenerId = nextListenerId++;
                        record.handlers.set(listenerId, callback);
                        call(record, "GM.addValueChangeListener", { listenerId: listenerId, key: key }).catch(error => {
                            record.handlers.delete(listenerId);
                            console.error("[MonkeySharp]", error);
                        });
                        return listenerId;
                    };
                }
                if (enabled("GM.removeValueChangeListener")) {
                    api.removeValueChangeListener = async function (listenerId) {
                        if (!Number.isInteger(listenerId) || listenerId <= 0) throw new TypeError("listenerId must be a positive integer.");
                        const removed = await call(record, "GM.removeValueChangeListener", { listenerId: listenerId });
                        if (removed) record.handlers.delete(listenerId);
                        return removed;
                    };
                }
                if (enabled("GM.addStyle")) {
                    api.addStyle = async function (css) {
                        if (typeof css !== "string") throw new TypeError("css must be a string.");
                        const style = document.createElement("style");
                        style.textContent = css;
                        (document.head || document.documentElement).appendChild(style);
                        return style;
                    };
                }
                if (enabled("GM.addElement")) {
                    api.addElement = async function (parent, tagName, attributes) {
                        if (typeof parent === "string") {
                            attributes = tagName;
                            tagName = parent;
                            parent = document.body || document.documentElement;
                        }
                        if (!parent || typeof parent.appendChild !== "function") throw new TypeError("parent must be a DOM node.");
                        if (typeof tagName !== "string" || tagName.length === 0) throw new TypeError("tagName must be a string.");
                        const element = document.createElement(tagName);
                        if (attributes && typeof attributes === "object") {
                            Object.keys(attributes).forEach(name => {
                                if (name === "textContent") element.textContent = String(attributes[name]);
                                else element.setAttribute(name, String(attributes[name]));
                            });
                        }
                        parent.appendChild(element);
                        return element;
                    };
                }
                if (enabled("GM.getResourceText")) {
                    api.getResourceText = async function (name) {
                        if (typeof name !== "string" || name.length === 0) throw new TypeError("name must be a non-empty string.");
                        return call(record, "GM.getResourceText", { name: name });
                    };
                }
                if (enabled("GM.getResourceURL")) {
                    api.getResourceURL = async function (name) {
                        if (typeof name !== "string" || name.length === 0) throw new TypeError("name must be a non-empty string.");
                        return call(record, "GM.getResourceURL", { name: name });
                    };
                }
                if (enabled("GM.xmlHttpRequest")) {
                    api.xmlHttpRequest = async function (details) {
                        if (!details || typeof details !== "object" || typeof details.url !== "string") {
                            throw new TypeError("details.url must be a string.");
                        }
                        const xhrId = nextListenerId++;
                        const callbacks = {
                            onload: details.onload,
                            onerror: details.onerror,
                            ontimeout: details.ontimeout,
                            onprogress: details.onprogress
                        };
                        Object.keys(callbacks).forEach(name => {
                            if (typeof callbacks[name] !== "undefined" && typeof callbacks[name] !== "function") {
                                throw new TypeError(name + " must be a function.");
                            }
                        });
                        const parameters = { xhrId: xhrId, url: details.url };
                        ["method", "data"].forEach(name => {
                            if (typeof details[name] !== "undefined") {
                                if (typeof details[name] !== "string") throw new TypeError(name + " must be a string.");
                                parameters[name] = details[name];
                            }
                        });
                        if (typeof details.headers !== "undefined") parameters.headers = serializableValue(details.headers);
                        if (typeof details.timeout !== "undefined") {
                            if (!Number.isInteger(details.timeout) || details.timeout <= 0) throw new TypeError("timeout must be positive.");
                            parameters.timeout = details.timeout;
                        }
                        record.xhrHandlers.set(xhrId, callbacks);
                        try {
                            const response = await call(record, "GM.xmlHttpRequest", parameters, details.signal);
                            if (callbacks.onload) callbacks.onload(response);
                            return response;
                        } catch (error) {
                            if (error.code === "MSP009_TIMEOUT" && callbacks.ontimeout) callbacks.ontimeout(error);
                            else if (callbacks.onerror) callbacks.onerror(error);
                            throw error;
                        } finally {
                            record.xhrHandlers.delete(xhrId);
                        }
                    };
                }
                if (enabled("GM.registerMenuCommand")) {
                    api.registerMenuCommand = async function (name, callback, accessKey) {
                        if (typeof name !== "string" || name.length === 0) throw new TypeError("name must be a non-empty string.");
                        if (typeof callback !== "function") throw new TypeError("callback must be a function.");
                        if (typeof accessKey !== "undefined" && typeof accessKey !== "string") throw new TypeError("accessKey must be a string.");
                        const commandId = nextListenerId++;
                        record.menuHandlers.set(commandId, callback);
                        try {
                            await call(record, "GM.registerMenuCommand", {
                                commandId: commandId,
                                name: name,
                                accessKey: accessKey || null
                            });
                            return commandId;
                        } catch (error) {
                            record.menuHandlers.delete(commandId);
                            throw error;
                        }
                    };
                }
                if (enabled("GM.unregisterMenuCommand")) {
                    api.unregisterMenuCommand = async function (commandId) {
                        if (!Number.isInteger(commandId) || commandId <= 0) throw new TypeError("commandId must be positive.");
                        const removed = await call(record, "GM.unregisterMenuCommand", { commandId: commandId });
                        if (removed) record.menuHandlers.delete(commandId);
                        return removed;
                    };
                }
                if (enabled("GM.notification")) {
                    api.notification = async function (details) {
                        if (typeof details === "string") details = { text: details };
                        if (!details || typeof details !== "object") throw new TypeError("details must be an object.");
                        return call(record, "GM.notification", {
                            title: details.title || null,
                            text: details.text,
                            imageUrl: details.imageUrl || details.image || null
                        });
                    };
                }
                if (enabled("GM.setClipboard")) {
                    api.setClipboard = async function (text, type) {
                        if (typeof text !== "string") throw new TypeError("text must be a string.");
                        if (typeof type !== "undefined" && typeof type !== "string") throw new TypeError("type must be a string.");
                        return call(record, "GM.setClipboard", { text: text, type: type || "text/plain" });
                    };
                }
                if (enabled("GM.openInTab")) {
                    api.openInTab = async function (url, options) {
                        if (typeof url !== "string") throw new TypeError("url must be a string.");
                        options = options || {};
                        return call(record, "GM.openInTab", {
                            url: url,
                            active: typeof options.active === "boolean" ? options.active : true,
                            insert: Boolean(options.insert),
                            setParent: Boolean(options.setParent)
                        });
                    };
                }
                if (enabled("GM.download")) {
                    api.download = async function (details) {
                        if (typeof details === "string") details = { url: details };
                        if (!details || typeof details !== "object") throw new TypeError("details must be an object.");
                        return call(record, "GM.download", {
                            url: details.url,
                            name: details.name || null,
                            saveAs: Boolean(details.saveAs)
                        });
                    };
                }
                if (enabled("GM.getTab")) api.getTab = async () => call(record, "GM.getTab", {});
                if (enabled("GM.saveTab")) {
                    api.saveTab = async value => call(record, "GM.saveTab", { value: serializableValue(value) });
                }
                if (enabled("GM.getTabs")) api.getTabs = async () => call(record, "GM.getTabs", {});
                return Object.freeze(api);
            };

            const runInvocation = async function (plan, invocation, dispatch) {
                const proof = createProof(plan, invocation);
                const record = {
                    proof: proof,
                    dispatch: dispatch,
                    deliveryToken: invocation.deliveryToken,
                    handlers: new Map(),
                    menuHandlers: new Map(),
                    xhrHandlers: new Map()
                };
                executions.set(invocation.executionId, record);
                let availableApis = new Set();
                const grantNone = invocation.grants.length === 1 && invocation.grants[0] === "none";
                if (!grantNone) {
                    if (!dispatch) throw new Error("MonkeySharp bridge is unavailable.");
                    const hello = await send(dispatch, Object.assign({ type: "hello" }, proof));
                    if (hello.type !== "hello-result" || !hello.ok) {
                        throw new Error(hello.error && hello.error.message || "MonkeySharp bridge handshake failed.");
                    }
                    availableApis = new Set(hello.apis || []);
                }
                const gm = grantNone ? undefined : createApi(record, invocation, availableApis);
                const unsafeWindow = invocation.grants.includes("unsafeWindow") ? root : undefined;
                try {
                    const execute = new Function("GM", "unsafeWindow", "window", "\"use strict\";\n" + invocation.source);
                    await execute.call(undefined, gm, unsafeWindow, root);
                } catch (error) {
                    if (dispatch) {
                        await call(record, "runtime.reportError", {
                            message: String(error && error.message || error),
                            stack: error && error.stack ? String(error.stack) : null
                        }).catch(reportError => console.error("[MonkeySharp] error reporting failed", reportError));
                    }
                    throw error;
                }
            };

            const install = async function (plan) {
                if (!plan || plan.protocol !== 1 || !Array.isArray(plan.invocations)) {
                    throw new Error("MonkeySharp received an invalid injection plan.");
                }
                const dispatch = await resolveDispatch();
                for (const invocation of plan.invocations) {
                    await runInvocation(plan, invocation, dispatch).catch(error => console.error("[MonkeySharp]", error));
                }
            };

            const receive = function (notification) {
                if (typeof notification === "string") notification = JSON.parse(notification);
                if (!notification || notification.type !== "notification" || notification.protocol !== 1) return false;
                const record = executions.get(notification.executionId);
                if (!record || record.deliveryToken !== notification.deliveryToken) return false;
                const data = notification.data || {};
                try {
                    if (notification.event === "value-change") {
                        const handler = record.handlers.get(data.listenerId);
                        if (!handler) return false;
                        handler(data.key, decodeResult(data.oldValue), decodeResult(data.newValue), Boolean(data.remote));
                        return true;
                    }
                    if (notification.event === "menu-command") {
                        const handler = record.menuHandlers.get(data.commandId);
                        if (!handler) return false;
                        handler();
                        return true;
                    }
                    if (notification.event === "xhr-progress") {
                        const handlers = record.xhrHandlers.get(data.xhrId);
                        if (!handlers || !handlers.onprogress) return false;
                        handlers.onprogress({ loaded: data.loaded, total: data.total });
                        return true;
                    }
                    return false;
                } catch (error) {
                    console.error("[MonkeySharp] callback failed", error);
                    return false;
                }
            };

            return Object.freeze({ install: install, receive: receive });
        })();

        Object.defineProperty(root, runtimeName, {
            value: runtime,
            writable: false,
            configurable: false,
            enumerable: false
        });
    }

    runtime.install(payload).catch(error => console.error("[MonkeySharp]", error));
})();
