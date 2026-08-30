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

            const cloneValue = function (value) {
                return serializableValue(value);
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

            const reportMutationFailure = async function (record, method, error) {
                record.storageMirrorStale = true;
                try {
                    const snapshot = await call(record, "runtime.getStorageSnapshot", {});
                    if (snapshot && snapshot.complete && snapshot.values) {
                        record.storageMirror = Object.assign({}, snapshot.values);
                        record.storageMirrorStale = false;
                    }
                } catch (snapshotError) {
                    if (root.console && typeof root.console.warn === "function") {
                        root.console.warn("[MonkeySharp] compatibility storage reconcile failed", snapshotError);
                    }
                }
                try {
                    await call(record, "runtime.reportError", {
                        code: "MSC413_COMPATIBILITY_MUTATION_FAILED",
                        operation: method,
                        message: String(error && error.message || error)
                    });
                } catch (diagnosticError) {
                    if (root.console && typeof root.console.warn === "function") {
                        root.console.warn("[MonkeySharp] compatibility storage diagnostic failed", diagnosticError);
                    }
                }
            };

            const enqueueMutation = function (record, method, parameters) {
                const operation = record.mutationTail.then(() => call(record, method, parameters));
                record.mutationTail = operation.catch(error => {
                    return reportMutationFailure(record, method, error);
                });
                return operation;
            };

            const notifyLocalValueChange = function (record, key, oldValue, newValue) {
                record.valueListenerKeys.forEach((listenerKey, listenerId) => {
                    if (listenerKey !== key) return;
                    const handler = record.handlers.get(listenerId);
                    if (!handler) return;
                    try {
                        handler(key, oldValue, newValue, false);
                    } catch (error) {
                        if (root.console && typeof root.console.error === "function") {
                            root.console.error("[MonkeySharp] callback failed", error);
                        }
                    }
                });
            };

            const detailsWithSignal = function (details, signal, xhrId) {
                const copy = Object.assign({}, details, { __monkeySharpXhrId: xhrId, __monkeySharpLegacy: true });
                if (signal) copy.signal = signal;
                return copy;
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
                        if (record.storageMirror) {
                            if (Object.prototype.hasOwnProperty.call(record.storageMirror, key)) {
                                return cloneValue(record.storageMirror[key]);
                            }
                            return arguments.length > 1 && typeof defaultValue !== "undefined"
                                ? cloneValue(defaultValue) : undefined;
                        }
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
                        const cloned = cloneValue(value);
                        if (record.storageMirror) {
                            const oldValue = Object.prototype.hasOwnProperty.call(record.storageMirror, key)
                                ? cloneValue(record.storageMirror[key]) : undefined;
                            record.storageMirror[key] = cloned;
                            notifyLocalValueChange(record, key, oldValue, cloneValue(cloned));
                            await enqueueMutation(record, "GM.setValue", { key: key, value: cloned });
                            return;
                        }
                        await call(record, "GM.setValue", { key: key, value: cloned });
                    };
                }
                if (enabled("GM.deleteValue")) {
                    api.deleteValue = async function (key) {
                        if (typeof key !== "string" || key.length === 0) throw new TypeError("key must be a non-empty string.");
                        if (record.storageMirror) {
                            const existed = Object.prototype.hasOwnProperty.call(record.storageMirror, key);
                            const oldValue = existed ? cloneValue(record.storageMirror[key]) : undefined;
                            if (existed) delete record.storageMirror[key];
                            if (existed) notifyLocalValueChange(record, key, oldValue, undefined);
                            await enqueueMutation(record, "GM.deleteValue", { key: key });
                            return existed;
                        }
                        return call(record, "GM.deleteValue", { key: key });
                    };
                }
                if (enabled("GM.listValues")) api.listValues = async () => record.storageMirror
                    ? Object.keys(record.storageMirror).sort()
                    : call(record, "GM.listValues", {});
                if (enabled("GM.addValueChangeListener")) {
                    api.addValueChangeListener = function (key, callback) {
                        if (typeof key !== "string" || key.length === 0) throw new TypeError("key must be a non-empty string.");
                        if (typeof callback !== "function") throw new TypeError("callback must be a function.");
                        const listenerId = nextListenerId++;
                        record.handlers.set(listenerId, callback);
                        record.valueListenerKeys.set(listenerId, key);
                        call(record, "GM.addValueChangeListener", { listenerId: listenerId, key: key }).catch(error => {
                            record.handlers.delete(listenerId);
                            record.valueListenerKeys.delete(listenerId);
                            console.error("[MonkeySharp]", error);
                        });
                        return listenerId;
                    };
                }
                if (enabled("GM.removeValueChangeListener")) {
                    api.removeValueChangeListener = async function (listenerId) {
                        if (!Number.isInteger(listenerId) || listenerId <= 0) throw new TypeError("listenerId must be a positive integer.");
                        const removed = await call(record, "GM.removeValueChangeListener", { listenerId: listenerId });
                        if (removed) {
                            record.handlers.delete(listenerId);
                            record.valueListenerKeys.delete(listenerId);
                        }
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
                        if (record.resourceMirror && Object.prototype.hasOwnProperty.call(record.resourceMirror, name)) {
                            return record.resourceMirror[name].text;
                        }
                        return call(record, "GM.getResourceText", { name: name });
                    };
                }
                if (enabled("GM.getResourceURL")) {
                    api.getResourceURL = async function (name) {
                        if (typeof name !== "string" || name.length === 0) throw new TypeError("name must be a non-empty string.");
                        if (record.resourceMirror && Object.prototype.hasOwnProperty.call(record.resourceMirror, name)) {
                            return record.resourceMirror[name].url;
                        }
                        return call(record, "GM.getResourceURL", { name: name });
                    };
                }
                if (enabled("GM.xmlHttpRequest")) {
                    api.xmlHttpRequest = async function (details) {
                        if (!details || typeof details !== "object" || typeof details.url !== "string") {
                            throw new TypeError("details.url must be a string.");
                        }
                        const xhrId = Number.isInteger(details.__monkeySharpXhrId)
                            ? details.__monkeySharpXhrId : nextListenerId++;
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
                            if (!details.__monkeySharpLegacy && callbacks.onload) callbacks.onload(response);
                            return response;
                        } catch (error) {
                            if (!details.__monkeySharpLegacy) {
                                if (error.code === "MSP009_TIMEOUT" && callbacks.ontimeout) callbacks.ontimeout(error);
                                else if (callbacks.onerror) callbacks.onerror(error);
                            }
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
                        const result = await call(record, "GM.notification", {
                            title: details.title || null,
                            text: details.text,
                            imageUrl: details.imageUrl || details.image || null
                        });
                        if (result && result.id) {
                            record.notificationHandlers.set(result.id, {
                                onclick: typeof details.onclick === "function" ? details.onclick : null,
                                ondone: typeof details.ondone === "function" ? details.ondone : null
                            });
                        }
                        return result;
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
                if (enabled("GM.cookie")) {
                    api.cookie = Object.freeze({
                        list: details => call(record, "GM.cookie", { operation: "list", details: serializableValue(details || {}) }),
                        set: details => call(record, "GM.cookie", { operation: "set", details: serializableValue(details || {}) }),
                        delete: details => call(record, "GM.cookie", { operation: "delete", details: serializableValue(details || {}) }),
                        addListener: function (details, callback) {
                            if (typeof details === "function") { callback = details; details = {}; }
                            if (typeof callback !== "function") throw new TypeError("callback must be a function.");
                            const listenerId = nextListenerId++;
                            record.cookieHandlers.set(listenerId, callback);
                            call(record, "GM.cookie", {
                                operation: "addListener", listenerId: listenerId,
                                details: serializableValue(details || {})
                            }).catch(error => {
                                record.cookieHandlers.delete(listenerId);
                                console.error("[MonkeySharp] cookie listener registration failed", error);
                            });
                            return listenerId;
                        },
                        removeListener: async function (listenerId) {
                            const removed = await call(record, "GM.cookie", { operation: "removeListener", listenerId: listenerId });
                            if (removed) record.cookieHandlers.delete(listenerId);
                            return removed;
                        }
                    });
                }
                return Object.freeze(api);
            };

            const legacyNames = [
                "GM_info", "GM_log", "GM_getValue", "GM_setValue", "GM_deleteValue", "GM_listValues",
                "GM_addValueChangeListener", "GM_removeValueChangeListener", "GM_addStyle", "GM_addElement",
                "GM_getResourceText", "GM_getResourceURL", "GM_xmlhttpRequest", "GM_registerMenuCommand",
                "GM_unregisterMenuCommand", "GM_notification", "GM_setClipboard", "GM_openInTab",
                "GM_download", "GM_getTab", "GM_saveTab", "GM_getTabs"
                , "GM_cookie"
            ];

            const createLegacyFacade = function (record, invocation, api) {
                const facade = {};
                if (!invocation.compatibility || !invocation.compatibility.legacyGlobals || !api) return facade;
                if (api.info) facade.GM_info = api.info;
                if (api.log) {
                    facade.GM_log = function (value) {
                        if (root.console && typeof root.console.log === "function") root.console.log(value);
                        api.log(value).catch(error => console.error("[MonkeySharp] legacy log failed", error));
                    };
                }
                const aliases = {
                    GM_addValueChangeListener: "addValueChangeListener",
                    GM_xmlhttpRequest: "xmlHttpRequest",
                    GM_registerMenuCommand: "registerMenuCommand", GM_unregisterMenuCommand: "unregisterMenuCommand",
                    GM_notification: "notification", GM_setClipboard: "setClipboard", GM_openInTab: "openInTab",
                    GM_download: "download", GM_getTab: "getTab", GM_saveTab: "saveTab", GM_getTabs: "getTabs"
                };
                Object.keys(aliases).forEach(name => {
                    const member = api[aliases[name]];
                    if (typeof member === "function") facade[name] = member;
                });
                if (record.storageMirror) {
                    const requireKey = key => {
                        if (typeof key !== "string" || key.length === 0) {
                            throw new TypeError("key must be a non-empty string.");
                        }
                        return key;
                    };
                    if (api.getValue) facade.GM_getValue = function (key, defaultValue) {
                        key = requireKey(key);
                        return Object.prototype.hasOwnProperty.call(record.storageMirror, key)
                            ? cloneValue(record.storageMirror[key])
                            : (arguments.length > 1 && typeof defaultValue !== "undefined"
                                ? cloneValue(defaultValue) : undefined);
                    };
                    if (api.setValue) facade.GM_setValue = function (key, value) {
                        key = requireKey(key);
                        const cloned = cloneValue(value);
                        const oldValue = Object.prototype.hasOwnProperty.call(record.storageMirror, key)
                            ? cloneValue(record.storageMirror[key]) : undefined;
                        record.storageMirror[key] = cloned;
                        notifyLocalValueChange(record, key, oldValue, cloneValue(cloned));
                        enqueueMutation(record, "GM.setValue", { key: key, value: cloned });
                    };
                    if (api.deleteValue) facade.GM_deleteValue = function (key) {
                        key = requireKey(key);
                        const existed = Object.prototype.hasOwnProperty.call(record.storageMirror, key);
                        const oldValue = existed ? cloneValue(record.storageMirror[key]) : undefined;
                        if (existed) delete record.storageMirror[key];
                        if (existed) notifyLocalValueChange(record, key, oldValue, undefined);
                        enqueueMutation(record, "GM.deleteValue", { key: key });
                    };
                    if (api.listValues) facade.GM_listValues = function () {
                        return Object.keys(record.storageMirror).sort();
                    };
                    if (api.removeValueChangeListener) facade.GM_removeValueChangeListener = function (listenerId) {
                        if (!Number.isInteger(listenerId) || listenerId <= 0) {
                            throw new TypeError("listenerId must be a positive integer.");
                        }
                        const removed = record.handlers.delete(listenerId);
                        record.valueListenerKeys.delete(listenerId);
                        if (removed) enqueueMutation(record, "GM.removeValueChangeListener", { listenerId: listenerId });
                        return removed;
                    };
                }
                if (record.resourceMirror) {
                    if (api.getResourceText) facade.GM_getResourceText = function (name) {
                        if (typeof name !== "string" || name.length === 0) throw new TypeError("name must be a non-empty string.");
                        if (!Object.prototype.hasOwnProperty.call(record.resourceMirror, name)) {
                            throw new Error("The resource snapshot is unavailable.");
                        }
                        return record.resourceMirror[name].text;
                    };
                    if (api.getResourceURL) facade.GM_getResourceURL = function (name) {
                        if (typeof name !== "string" || name.length === 0) throw new TypeError("name must be a non-empty string.");
                        if (!Object.prototype.hasOwnProperty.call(record.resourceMirror, name)) {
                            throw new Error("The resource snapshot is unavailable.");
                        }
                        return record.resourceMirror[name].url;
                    };
                }
                if (api.addStyle) facade.GM_addStyle = function (css) {
                    if (typeof css !== "string") throw new TypeError("css must be a string.");
                    const style = document.createElement("style");
                    style.textContent = css;
                    (document.head || document.documentElement).appendChild(style);
                    return style;
                };
                if (api.addElement) facade.GM_addElement = function (parent, tagName, attributes) {
                    if (typeof parent === "string") {
                        attributes = tagName;
                        tagName = parent;
                        parent = document.body || document.documentElement;
                    }
                    if (!parent || typeof parent.appendChild !== "function") throw new TypeError("parent must be a DOM node.");
                    if (typeof tagName !== "string" || tagName.length === 0) throw new TypeError("tagName must be a string.");
                    const element = document.createElement(tagName);
                    if (attributes && typeof attributes === "object") Object.keys(attributes).forEach(name => {
                        if (name === "textContent") element.textContent = String(attributes[name]);
                        else element.setAttribute(name, String(attributes[name]));
                    });
                    parent.appendChild(element);
                    return element;
                };
                if (api.xmlHttpRequest) facade.GM_xmlhttpRequest = function (details) {
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
                    const controller = typeof AbortController === "function" ? new AbortController() : null;
                    const handle = { abort: function () { if (controller) controller.abort(); } };
                    record.xhrHandlers.set(xhrId, callbacks);
                    api.xmlHttpRequest(detailsWithSignal(details, controller && controller.signal, xhrId))
                        .then(response => { if (callbacks.onload) callbacks.onload(response); })
                        .catch(error => {
                            if (error && error.name === "AbortError") return;
                            if (error && error.code === "MSP009_TIMEOUT" && callbacks.ontimeout) callbacks.ontimeout(error);
                            else if (callbacks.onerror) callbacks.onerror(error);
                        })
                        .finally(() => record.xhrHandlers.delete(xhrId));
                    return handle;
                };
                if (api.registerMenuCommand) facade.GM_registerMenuCommand = function (name, callback, accessKey) {
                    if (typeof name !== "string" || name.length === 0) throw new TypeError("name must be a non-empty string.");
                    if (typeof callback !== "function") throw new TypeError("callback must be a function.");
                    if (typeof accessKey !== "undefined" && typeof accessKey !== "string") throw new TypeError("accessKey must be a string.");
                    const commandId = nextListenerId++;
                    record.menuHandlers.set(commandId, callback);
                    call(record, "GM.registerMenuCommand", {
                        commandId: commandId,
                        name: name,
                        accessKey: accessKey || null
                    }).catch(error => {
                        record.menuHandlers.delete(commandId);
                        console.error("[MonkeySharp] legacy menu registration failed", error);
                    });
                    return commandId;
                };
                if (api.unregisterMenuCommand) facade.GM_unregisterMenuCommand = function (commandId) {
                    if (!Number.isInteger(commandId) || commandId <= 0) throw new TypeError("commandId must be positive.");
                    const removed = record.menuHandlers.delete(commandId);
                    api.unregisterMenuCommand(commandId).catch(error => console.error("[MonkeySharp] legacy menu removal failed", error));
                    return removed;
                };
                if (api.saveTab) facade.GM_saveTab = function (value) {
                    api.saveTab(value).catch(error => console.error("[MonkeySharp] legacy saveTab failed", error));
                };
                if (api.getTab) facade.GM_getTab = function (callback) {
                    if (typeof callback !== "function") throw new TypeError("callback must be a function.");
                    api.getTab().then(callback).catch(error => console.error("[MonkeySharp] legacy getTab failed", error));
                };
                if (api.getTabs) facade.GM_getTabs = function (callback) {
                    if (typeof callback !== "function") throw new TypeError("callback must be a function.");
                    api.getTabs().then(callback).catch(error => console.error("[MonkeySharp] legacy getTabs failed", error));
                };
                if (api.setClipboard) facade.GM_setClipboard = function (text, type) {
                    api.setClipboard(text, type).catch(error => console.error("[MonkeySharp] legacy clipboard failed", error));
                };
                if (api.notification) facade.GM_notification = function (details, ondone) {
                    if (typeof details === "string") details = { text: details };
                    details = Object.assign({}, details || {}, { ondone: ondone });
                    api.notification(details).catch(error => console.error("[MonkeySharp] legacy notification failed", error));
                };
                if (api.openInTab) facade.GM_openInTab = function (url, options) {
                    const state = { closed: false, id: null, closeRequested: false };
                    const handle = {
                        close: function () {
                            state.closeRequested = true;
                            if (!state.id) return;
                            call(record, "GM.openInTab", { close: true, tabId: state.id })
                                .then(() => { state.closed = true; })
                                .catch(error => console.error("[MonkeySharp] legacy tab close failed", error));
                        },
                        get closed() { return state.closed; }
                    };
                    api.openInTab(url, options).then(result => {
                        state.id = result && result.id;
                        if (state.closeRequested && state.id) handle.close();
                    })
                        .catch(error => console.error("[MonkeySharp] legacy tab open failed", error));
                    record.tabHandlers.push(state);
                    return handle;
                };
                if (api.download) facade.GM_download = function (details, onload, onerror) {
                    if (typeof details === "string") details = { url: details };
                    if (!details || typeof details !== "object") throw new TypeError("details must be an object.");
                    ["onload", "onerror", "onprogress"].forEach(name => {
                        if (typeof details[name] !== "undefined" && typeof details[name] !== "function") {
                            throw new TypeError(name + " must be a function.");
                        }
                    });
                    const state = { id: null, callbacks: { onload: onload || details.onload, onerror: onerror || details.onerror, onprogress: details.onprogress }, aborted: false };
                    const handle = { abort: function () { state.aborted = true; if (state.id) record.downloadHandlers.get(state.id)?.abort(); } };
                    api.download(details).then(result => {
                        state.id = result && result.id;
                        if (state.id) record.downloadHandlers.set(state.id, state);
                    }).catch(error => {
                        if (typeof state.callbacks.onerror === "function") state.callbacks.onerror(error);
                        else console.error("[MonkeySharp] legacy download failed", error);
                    });
                    return handle;
                };
                if (api.cookie) facade.GM_cookie = {
                    list: function (details, callback) {
                        if (typeof details === "function") { callback = details; details = {}; }
                        api.cookie.list(details).then(value => { if (typeof callback === "function") callback(value); })
                            .catch(error => console.error("[MonkeySharp] legacy cookie list failed", error));
                    },
                    set: function (details, callback) {
                        api.cookie.set(details).then(value => { if (typeof callback === "function") callback(value); })
                            .catch(error => console.error("[MonkeySharp] legacy cookie set failed", error));
                    },
                    delete: function (details, callback) {
                        api.cookie.delete(details).then(value => { if (typeof callback === "function") callback(value); })
                            .catch(error => console.error("[MonkeySharp] legacy cookie delete failed", error));
                    },
                    addListener: function (details, callback) { return api.cookie.addListener(details, callback); },
                    removeListener: function (listenerId) {
                        api.cookie.removeListener(listenerId).catch(error => console.error("[MonkeySharp] legacy cookie listener removal failed", error));
                    }
                };
                return facade;
            };

            const runInvocation = async function (plan, invocation, dispatch) {
                const proof = createProof(plan, invocation);
                const record = {
                    proof: proof,
                    executionId: invocation.executionId,
                    dispatch: dispatch,
                    deliveryToken: invocation.deliveryToken,
                    handlers: new Map(),
                    menuHandlers: new Map(),
                    xhrHandlers: new Map(),
                    downloadHandlers: new Map(),
                    notificationHandlers: new Map(),
                    tabHandlers: [],
                    cookieHandlers: new Map(),
                    valueListenerKeys: new Map(),
                    mutationTail: Promise.resolve(),
                    storageMirror: null,
                    storageMirrorStale: false,
                    lastStorageSequence: 0,
                    resourceMirror: null
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
                    if (hello.compatibility && hello.compatibility.storage &&
                        hello.compatibility.storage.complete &&
                        hello.compatibility.storage.values) {
                        record.storageMirror = Object.assign({}, hello.compatibility.storage.values);
                    }
                    if (hello.compatibility && hello.compatibility.resources &&
                        hello.compatibility.resources.complete && hello.compatibility.resources.values) {
                        record.resourceMirror = Object.assign({}, hello.compatibility.resources.values);
                    }
                }
                const gm = grantNone ? undefined : createApi(record, invocation, availableApis);
                const compatibility = invocation.compatibility || { strict: true, legacyGlobals: false };
                const legacy = createLegacyFacade(record, Object.assign({}, invocation, { compatibility: compatibility }), gm);
                const unsafeWindow = invocation.grants.includes("unsafeWindow") ? root : undefined;
                try {
                    const names = ["GM", "unsafeWindow", "window"].concat(legacyNames);
                    const values = [gm, unsafeWindow, root].concat(legacyNames.map(name => legacy[name]));
                    const prefix = compatibility.strict ? "\"use strict\";\n" : "";
                    const execute = new Function(...names, prefix + invocation.source);
                    await execute.call(compatibility.strict ? undefined : root, ...values);
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
                    if (notification.event === "storage-sync") {
                        if (record.storageMirror && data.key) {
                            if (data.sequence && data.sequence <= record.lastStorageSequence) return true;
                            const incoming = decodeResult(data.newValue);
                            if (typeof incoming === "undefined") delete record.storageMirror[data.key];
                            else record.storageMirror[data.key] = cloneValue(incoming);
                            if (data.sequence) record.lastStorageSequence = data.sequence;
                        }
                        return true;
                    }
                    if (notification.event === "value-change") {
                        if (record.storageMirror && data.key) {
                            const incoming = decodeResult(data.newValue);
                            if (typeof incoming === "undefined") delete record.storageMirror[data.key];
                            else record.storageMirror[data.key] = cloneValue(incoming);
                            if (data.sequence && data.sequence > record.lastStorageSequence) {
                                record.lastStorageSequence = data.sequence;
                            }
                        }
                        if (data.originExecutionId === record.executionId) return true;
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
                    if (notification.event === "notification-click" || notification.event === "notification-done") {
                        const handler = record.notificationHandlers.get(data.notificationId);
                        if (!handler) return false;
                        const callback = notification.event === "notification-click" ? handler.onclick : handler.ondone;
                        if (callback) callback();
                        if (notification.event === "notification-done") record.notificationHandlers.delete(data.notificationId);
                        return true;
                    }
                    if (notification.event === "tab-closed") {
                        record.tabHandlers.forEach(state => {
                            if (state.id === data.tabId) state.closed = true;
                        });
                        return true;
                    }
                    if (notification.event === "download-progress" || notification.event === "download-complete" ||
                        notification.event === "download-error" || notification.event === "download-aborted") {
                        const state = record.downloadHandlers.get(data.downloadId);
                        if (!state) return false;
                        if (notification.event === "download-progress") {
                            if (state.callbacks.onprogress) state.callbacks.onprogress({ loaded: data.loaded, total: data.total });
                        } else if (notification.event === "download-complete") {
                            if (state.callbacks.onload) state.callbacks.onload({ id: data.downloadId });
                            record.downloadHandlers.delete(data.downloadId);
                        } else {
                            if (state.callbacks.onerror) state.callbacks.onerror(new Error(data.message || "The download failed."));
                            record.downloadHandlers.delete(data.downloadId);
                        }
                        return true;
                    }
                    if (notification.event === "cookie-change") {
                        const callback = record.cookieHandlers.get(data.listenerId);
                        if (!callback) return false;
                        callback(data.cookie, data.cause, Boolean(data.removed));
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
