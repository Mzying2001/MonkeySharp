            const invokeHostCallback = function (record, callback, receiver, value, name) {
                if (typeof callback !== "function") return;
                try { callback.call(receiver, value); }
                catch (error) {
                    console.error("[MonkeySharp] " + name + " callback failed", error);
                    call(record, "runtime.reportError", { message: name + " callback failed: " + String(error && error.message || error) })
                        .catch(reportError => console.error("[MonkeySharp] callback reporting failed", reportError));
                }
            };

            const createDownloadOperation = function (record, input, name) {
                const clientId = uuid();
                const state = { clientId, terminal: false, abortRequested: false, abortSent: false,
                    abortInFlight: false, abortAttempts: 0, startSent: false, accepted: false, details: {}, callbacks: {} };
                const promise = new Promise((resolve, reject) => { state.resolve = resolve; state.reject = reject; });
                if (input && typeof input === "object") {
                    state.details = input;
                    for (const name of ["onload", "onerror", "onprogress", "ontimeout"]) if (typeof input[name] === "function") state.callbacks[name] = input[name];
                }
                const operation = { promise, hasErrorCallback: Boolean(state.callbacks.onerror || state.callbacks.ontimeout) };
                state.finish = function (kind, value) {
                    if (state.terminal) return;
                    state.terminal = true;
                    record.downloadHandlers.delete(clientId);
                    if (kind === "complete") {
                        invokeHostCallback(record, state.callbacks.onload, state.details, value, "download onload");
                        state.resolve(value);
                    } else {
                        const error = downloadError(value, kind === "timeout" ? "timeout" : "not_succeeded");
                        const callback = kind === "timeout" && state.callbacks.ontimeout ? state.callbacks.ontimeout : state.callbacks.onerror;
                        invokeHostCallback(record, callback, state.details, error, "download " + kind);
                        state.reject(error);
                    }
                };
                const sendAbort = function () {
                    if (!state.startSent || state.terminal || state.abortSent || state.abortInFlight) return;
                    state.abortInFlight = true;
                    state.abortAttempts++;
                    call(record, "GM.download", { operation: "abort", clientId }).then(result => {
                        state.abortInFlight = false;
                        state.abortSent = Boolean(result && result.accepted);
                        if (!state.abortSent && state.accepted && !state.terminal && state.abortAttempts < 2) sendAbort();
                    }, error => { state.abortInFlight = false; state.finish("error", error); });
                };
                const releaseBody = () => call(record, "GM.download", { operation: "releaseBody", clientId }).catch(() => {});
                promise.abort = function () {
                    if (state.terminal || state.abortRequested) return;
                    state.abortRequested = true;
                    if (state.binaryController) state.binaryController.abort();
                    if (state.bodyCreated) releaseBody();
                    if (!state.startSent) state.finish("aborted", { error: "not_succeeded", details: "The download was aborted before starting." });
                    else sendAbort();
                };
                record.downloadHandlers.set(clientId, state);
                Promise.resolve().then(async () => {
                    if (state.terminal) return;
                    const details = typeof input === "string" ? { url: input, name } : input;
                    if (!details || typeof details !== "object" || Array.isArray(details)) throw new TypeError("details must be an object or URL string.");
                    state.details = details;
                    for (const callback of ["onload", "onerror", "onprogress", "ontimeout"]) {
                        if (typeof details[callback] !== "undefined" && typeof details[callback] !== "function") throw new TypeError(callback + " must be a function.");
                        state.callbacks[callback] = details[callback];
                    }
                    operation.hasErrorCallback = Boolean(state.callbacks.onerror || state.callbacks.ontimeout);
                    let blob = objectTag(details.url) === "[object Blob]" || objectTag(details.url) === "[object File]" ? details.url : null;
                    let sourceUrl = blob ? null : normalizeHttpUrl(details.url);
                    if (!blob && !/^(?:https?:|blob:)/i.test(sourceUrl)) throw new TypeError("url must be an HTTP(S) or renderer blob URL.");
                    if (typeof details.name !== "undefined" && typeof details.name !== "string") throw new TypeError("name must be a string.");
                    for (const option of ["saveAs", "anonymous"]) if (typeof details[option] !== "undefined" && typeof details[option] !== "boolean") throw new TypeError(option + " must be a boolean.");
                    if (typeof details.timeout !== "undefined" && (!Number.isInteger(details.timeout) || details.timeout <= 0)) throw new TypeError("timeout must be a positive integer.");
                    if (typeof details.conflictAction !== "undefined" && !["uniquify", "overwrite", "prompt"].includes(details.conflictAction)) throw new TypeError("Invalid conflictAction.");
                    if (blob || /^blob:/i.test(sourceUrl)) {
                        if (details.headers && Object.keys(details.headers).length) throw notSupported("Headers are not supported for local binary downloads.");
                        state.binaryController = typeof AbortController === "function" ? new AbortController() : null;
                        const signal = state.binaryController && state.binaryController.signal;
                        let ownedUrl;
                        try {
                            if (!blob) {
                                if (typeof root.fetch !== "function") throw notSupported("This renderer cannot resolve blob URLs.");
                                const local = await root.fetch(sourceUrl, { signal });
                                if (!local.ok) throw new Error("The renderer could not resolve the blob URL.");
                                blob = await local.blob();
                            } else {
                                if (!root.URL || typeof root.URL.createObjectURL !== "function") throw notSupported("Object URLs are unavailable.");
                                ownedUrl = root.URL.createObjectURL(blob); sourceUrl = ownedUrl;
                            }
                            if (state.terminal) return;
                            const budget = record.bridgeLimits.binaryTransferBytes || 256 * 1024 * 1024;
                            if (blob.size > budget) throw Object.assign(new Error("The binary download exceeds the configured transfer budget."), { code: "MSP008_PAYLOAD_TOO_LARGE" });
                            state.bodyCreated = true;
                            await call(record, "GM.download", { operation: "createBody", clientId, url: sourceUrl, length: blob.size }, signal);
                            for (let offset = 0; offset < blob.size; offset += 65536) {
                                if (state.terminal) return;
                                const bytes = new Uint8Array(await blob.slice(offset, Math.min(offset + 65536, blob.size)).arrayBuffer());
                                let binary = "";
                                bytes.forEach(value => { binary += String.fromCharCode(value); });
                                await call(record, "GM.download", { operation: "appendBody", clientId, offset, chunk: btoa(binary) }, signal);
                            }
                            if (state.terminal) return;
                            state.startSent = true;
                            await call(record, "GM.download", { operation: "startBody", clientId, name: details.name || blob.name || "download.bin",
                                saveAs: Boolean(details.saveAs), anonymous: Boolean(details.anonymous), conflictAction: details.conflictAction || "uniquify", timeout: details.timeout }, signal);
                            state.accepted = true;
                            state.bodyCreated = false;
                            if (state.abortRequested) sendAbort();
                        } finally {
                            if (ownedUrl) root.URL.revokeObjectURL(ownedUrl);
                            if (state.bodyCreated) await releaseBody();
                        }
                    } else {
                        state.startSent = true;
                        await call(record, "GM.download", { operation: "start", clientId, url: sourceUrl,
                            name: details.name || null, headers: details.headers ? serializableValue(details.headers) : {},
                            saveAs: Boolean(details.saveAs), anonymous: Boolean(details.anonymous),
                            conflictAction: details.conflictAction || "uniquify", timeout: details.timeout });
                        state.accepted = true;
                        if (state.abortRequested) sendAbort();
                    }
                }).catch(error => state.finish(state.abortRequested ? "aborted" : "error", error));
                return operation;
            };

            const downloadError = function (error, fallback) {
                if (error && typeof error === "object" && typeof error.error === "string") {
                    return { error: error.error, details: error.details || null, code: error.code || null };
                }
                const code = error && error.code;
                const mapped = {
                    MSP003_SESSION_EXPIRED: "not_succeeded",
                    MSP004_GRANT_DENIED: "not_permitted",
                    MSP005_PERMISSION_DENIED: "not_permitted",
                    MSP006_NOT_SUPPORTED: "not_supported",
                    MSP009_TIMEOUT: "timeout",
                    MSP010_CANCELED: "not_succeeded"
                };
                return {
                    error: mapped[code] || fallback || "not_succeeded",
                    details: error && error.message ? error.message : String(error || "The download failed."),
                    code: code || null
                };
            };

            const queueMenu = function (record, task) {
                const pending = (record.menuQueue || Promise.resolve()).then(task);
                record.menuQueue = pending.catch(() => {});
                return pending;
            };
            const registerMenu = function (record, name, callback, options) {
                if (typeof name !== "string" || !name) throw new TypeError("name must be a non-empty string.");
                if (typeof callback !== "function") throw new TypeError("callback must be a function.");
                if (typeof options === "string") options = { accessKey: options };
                if (typeof options === "undefined") options = {};
                if (!options || typeof options !== "object" || Array.isArray(options)) throw new TypeError("menu options must be an object or access key.");
                for (const field of ["accessKey", "title"]) if (typeof options[field] !== "undefined" && typeof options[field] !== "string") throw new TypeError(field + " must be a string.");
                if (typeof options.autoClose !== "undefined" && typeof options.autoClose !== "boolean") throw new TypeError("autoClose must be a boolean.");
                if (typeof options.clickedFrame !== "undefined") throw notSupported("clickedFrame requires a newer reference contract.");
                if (typeof options.id === "undefined") while (record.menuHandlers.has(nextListenerId)) nextListenerId++;
                const id = typeof options.id === "undefined" ? nextListenerId++ : options.id;
                if (!((Number.isInteger(id) && id > 0) || (typeof id === "string" && id))) throw new TypeError("id must be a positive integer or nonempty string.");
                const previous = record.menuHandlers.get(id);
                record.menuHandlers.set(id, callback);
                const ready = queueMenu(record, () => call(record, "GM.registerMenuCommand", {
                    commandId: id, name, accessKey: options.accessKey || null, title: options.title || null, autoClose: options.autoClose !== false
                })).then(() => id, error => {
                    if (record.menuHandlers.get(id) === callback) {
                        if (previous) record.menuHandlers.set(id, previous); else record.menuHandlers.delete(id);
                    }
                    throw error;
                });
                return { id, ready };
            };
            const unregisterMenu = function (record, id) {
                if (!((Number.isInteger(id) && id > 0) || (typeof id === "string" && id))) throw new TypeError("id must be a positive integer or nonempty string.");
                record.menuHandlers.delete(id);
                return queueMenu(record, () => call(record, "GM.unregisterMenuCommand", { commandId: id })).then(() => undefined);
            };
            const notificationOperation = function (record, input, title, image, onclick) {
                let details;
                if (typeof input === "string") details = { text: input, title: typeof title === "string" ? title : undefined, image, onclick, ondone: typeof title === "function" ? title : undefined };
                else details = Object.assign({}, input);
                if (typeof input !== "string" && typeof title === "function") details.ondone = title;
                if (!input || (typeof input !== "string" && (typeof input !== "object" || Array.isArray(input)))) throw new TypeError("notification details must be an object or string.");
                if (!details.text && !details.highlight) throw new TypeError("text is required unless highlight is true.");
                for (const field of ["text", "title", "tag", "url", "image", "imageUrl"]) if (typeof details[field] !== "undefined" && typeof details[field] !== "string") throw new TypeError(field + " must be a string.");
                for (const field of ["highlight", "silent"]) if (typeof details[field] !== "undefined" && typeof details[field] !== "boolean") throw new TypeError(field + " must be a boolean.");
                for (const field of ["onclick", "ondone"]) if (typeof details[field] !== "undefined" && typeof details[field] !== "function") throw new TypeError(field + " must be a function.");
                if (typeof details.timeout !== "undefined" && (!Number.isInteger(details.timeout) || details.timeout < 0)) throw new TypeError("timeout must be a nonnegative integer.");
                const clientId = uuid();
                let resolve, reject;
                const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
                const state = { details, clicked: false, terminal: false, resolve, reject };
                record.notificationHandlers.set(clientId, state);
                const start = () => call(record, "GM.notification", { clientId, title: details.title || null, text: details.text || null,
                    imageUrl: details.imageUrl || details.image || null, tag: details.tag || null, url: details.url || null,
                    highlight: Boolean(details.highlight), silent: Boolean(details.silent), timeout: details.timeout
                });
                const previous = details.tag && record.notificationTails.get(details.tag);
                const started = previous ? previous.then(start) : start();
                const acknowledged = started.catch(error => { if (!state.terminal) { state.terminal = true; record.notificationHandlers.delete(clientId); reject(error); } });
                if (details.tag) {
                    record.notificationTails.set(details.tag, acknowledged);
                    acknowledged.finally(() => { if (record.notificationTails.get(details.tag) === acknowledged) record.notificationTails.delete(details.tag); });
                }
                return promise;
            };
            const setClipboard = function (record, text, info, callback) {
                if (typeof text !== "string") throw new TypeError("clipboard data must be a string.");
                if (typeof info === "function") { callback = info; info = undefined; }
                if (typeof callback !== "undefined" && typeof callback !== "function") throw new TypeError("callback must be a function.");
                let type = info;
                if (info && typeof info === "object" && !Array.isArray(info)) {
                    if (typeof info.type !== "undefined" && typeof info.type !== "string") throw new TypeError("info.type must be a string.");
                    if (typeof info.mimetype !== "undefined" && typeof info.mimetype !== "string") throw new TypeError("info.mimetype must be a string.");
                    type = info.mimetype || info.type;
                }
                if (typeof type === "undefined") type = "text";
                const mediaType = { text: "text/plain", html: "text/html", "text/plain": "text/plain", "text/html": "text/html" }[type];
                if (!mediaType) throw notSupported("Only text and HTML clipboard formats are supported.");
                return call(record, "GM.setClipboard", { text, type: mediaType }).then(() => {
                    invokeHostCallback(record, callback, undefined, undefined, "clipboard");
                });
            };

            const addAudioApi = function (record) {
                const perform = (operation, parameters) => call(record, "GM.audio", Object.assign({ operation }, parameters));
                return Object.freeze({
                    setMute(details) {
                        if (!details || typeof details.isMuted !== "boolean") return Promise.reject(new TypeError("isMuted must be a boolean."));
                        return perform("setMute", { isMuted: details.isMuted });
                    },
                    getState: () => perform("getState"),
                    addStateChangeListener(listener) {
                        if (typeof listener !== "function") return Promise.reject(new TypeError("listener must be a function."));
                        const previous = record.audioListeners.get(listener);
                        if (previous) return previous.ready;
                        const id = nextListenerId++;
                        const state = { id };
                        record.audioHandlers.set(id, listener);
                        record.audioListeners.set(listener, state);
                        state.ready = perform("addListener", { listenerId: id }).then(() => undefined, error => {
                            record.audioHandlers.delete(id);
                            if (record.audioListeners.get(listener) === state) record.audioListeners.delete(listener);
                            throw error;
                        });
                        return state.ready;
                    },
                    removeStateChangeListener(listener) {
                        if (typeof listener !== "function") return Promise.reject(new TypeError("listener must be a function."));
                        const state = record.audioListeners.get(listener);
                        if (!state) return Promise.resolve();
                        record.audioListeners.delete(listener); record.audioHandlers.delete(state.id);
                        return state.ready.then(() => perform("removeListener", { listenerId: state.id })).then(() => undefined);
                    }
                });
            };

            const addHostApi = function (record, enabled, api) {
                if (enabled("GM.audio")) api.audio = addAudioApi(record);
                if (enabled("GM.registerMenuCommand")) api.registerMenuCommand = async function (name, callback, options) { return registerMenu(record, name, callback, options).ready; };
                if (enabled("GM.unregisterMenuCommand")) api.unregisterMenuCommand = async id => unregisterMenu(record, id);
                if (enabled("GM.notification")) api.notification = async function (...args) { return notificationOperation(record, ...args); };
                if (enabled("GM.setClipboard")) api.setClipboard = async function (...args) { return setClipboard(record, ...args); };
                if (enabled("GM.openInTab")) {
                    record.openTab = function (url, options) {
                        if (typeof url !== "string") throw new TypeError("url must be a string.");
                        if (typeof root.URL === "function") url = new root.URL(url, root.location && root.location.href).href;
                        if (typeof options === "boolean") options = { active: !options };
                        if (typeof options === "undefined") options = {};
                        if (!options || typeof options !== "object" || Array.isArray(options)) throw new TypeError("options must be a boolean or object.");
                        for (const option of ["active", "loadInBackground", "setParent", "incognito"]) if (typeof options[option] !== "undefined" && typeof options[option] !== "boolean") throw new TypeError(option + " must be a boolean.");
                        if (typeof options.insert !== "undefined" && typeof options.insert !== "boolean" && (!Number.isInteger(options.insert) || options.insert < 0 || options.insert > 2147483647)) throw new TypeError("insert must be a boolean or nonnegative integer.");
                        const state = { clientId: uuid(), id: null, closed: false, terminal: false, closeRequested: false, closeSent: false, onclose: null };
                        state.finish = function () {
                            if (state.terminal) return;
                            state.closed = true; state.terminal = true;
                            const index = record.tabHandlers.indexOf(state);
                            if (index >= 0) record.tabHandlers.splice(index, 1);
                            if (typeof state.onclose === "function") {
                                try { state.onclose.call(handle); } catch (error) { console.error("[MonkeySharp] tab onclose failed", error); }
                            }
                        };
                        const sendClose = function () {
                            if (!state.id || state.closeSent || state.closed) return;
                            state.closeSent = true;
                            call(record, "GM.openInTab", { close: true, tabId: state.id }).catch(error => console.error("[MonkeySharp] tab close failed", error));
                        };
                        const handle = {};
                        Object.defineProperties(handle, {
                            closed: { enumerable: true, get: () => state.closed },
                            onclose: { enumerable: true, get: () => state.onclose,
                                set: value => { if (value !== null && typeof value !== "function") throw new TypeError("onclose must be a function or null."); state.onclose = value; } }
                        });
                        handle.close = function () {
                            if (state.terminal || state.closeRequested) return;
                            state.closeRequested = true;
                            sendClose();
                        };
                        record.tabHandlers.push(state);
                        const ready = call(record, "GM.openInTab", { clientId: state.clientId, url,
                            active: typeof options.active === "boolean" ? options.active : typeof options.loadInBackground === "boolean" ? !options.loadInBackground : false,
                            insert: typeof options.insert === "undefined" ? false : options.insert, setParent: Boolean(options.setParent), incognito: options.incognito
                        }).then(result => {
                            state.id = result && result.id;
                            if (result && result.closed) state.finish();
                            if (state.closeRequested) sendClose();
                            return handle;
                        }).catch(error => {
                            const index = record.tabHandlers.indexOf(state);
                            if (index >= 0) record.tabHandlers.splice(index, 1);
                            throw error;
                        });
                        return { handle, ready };
                    };
                    api.openInTab = function (url, options) { return record.openTab(url, options).ready; };
                }
                if (enabled("GM.download")) api.download = function (details, name) {
                    return createDownloadOperation(record, details, name).promise;
                };
                const tabStateRequest = function (action) {
                    const result = (record.tabStateTail || Promise.resolve()).then(action);
                    record.tabStateTail = result.catch(() => {});
                    return result;
                };
                if (enabled("GM.getTab")) api.getTab = () => tabStateRequest(() => call(record, "GM.getTab", {}));
                if (enabled("GM.saveTab")) api.saveTab = function (value) {
                    if (!value || typeof value !== "object" || Array.isArray(value)) throw new TypeError("Tab state must be an object.");
                    const snapshot = serializableValue(value);
                    return tabStateRequest(() => call(record, "GM.saveTab", { value: snapshot })).then(() => undefined);
                };
                if (enabled("GM.getTabs")) api.getTabs = () => tabStateRequest(() => call(record, "GM.getTabs", {}));
            };

            const addLegacyAliases = function (api, facade) {
                const aliases = {
                    GM_xmlhttpRequest: "xmlHttpRequest",
                    GM_registerMenuCommand: "registerMenuCommand", GM_unregisterMenuCommand: "unregisterMenuCommand",
                    GM_notification: "notification", GM_setClipboard: "setClipboard", GM_openInTab: "openInTab",
                    GM_download: "download", GM_getTab: "getTab", GM_saveTab: "saveTab", GM_getTabs: "getTabs"
                };
                Object.keys(aliases).forEach(name => {
                    const member = api[aliases[name]];
                    if (typeof member === "function") facade[name] = member;
                });
            };

            const addLegacyHost = function (record, api, facade) {
                if (api.audio) {
                    const invoke = function (name, args, callback, stateResult) {
                        if (callback !== undefined && typeof callback !== "function") throw new TypeError("callback must be a function.");
                        api.audio[name](...args).then(value => {
                            invokeHostCallback(record, callback, undefined, stateResult ? value : undefined, "audio " + name);
                        }, error => {
                            if (callback) invokeHostCallback(record, callback, undefined, stateResult ? undefined : String(error && error.message || error), "audio " + name);
                            else console.error("[MonkeySharp] audio " + name + " failed", error);
                        });
                    };
                    facade.GM_audio = Object.freeze(Object.assign(function () {}, {
                        setMute: (details, callback) => invoke("setMute", [details], callback, false),
                        getState: callback => invoke("getState", [], callback, true),
                        addStateChangeListener: (listener, callback) => invoke("addStateChangeListener", [listener], callback, false),
                        removeStateChangeListener: (listener, callback) => invoke("removeStateChangeListener", [listener], callback, false)
                    }));
                }
                if (api.registerMenuCommand) facade.GM_registerMenuCommand = function (name, callback, options) {
                    const operation = registerMenu(record, name, callback, options);
                    operation.ready.catch(error => console.error("[MonkeySharp] menu registration failed", error));
                    return operation.id;
                };
                if (api.unregisterMenuCommand) facade.GM_unregisterMenuCommand = function (id) {
                    unregisterMenu(record, id).catch(error => console.error("[MonkeySharp] menu removal failed", error));
                };
                if (api.saveTab) facade.GM_saveTab = function (value, callback) {
                    if (typeof callback !== "undefined" && typeof callback !== "function") throw new TypeError("callback must be a function.");
                    api.saveTab(value).then(() => { if (callback) callback(); }).catch(error => console.error("[MonkeySharp] legacy saveTab failed", error));
                };
                if (api.getTab) facade.GM_getTab = function (callback) {
                    if (typeof callback !== "function") throw new TypeError("callback must be a function.");
                    api.getTab().then(value => invokeHostCallback(record, () => callback(value, null), undefined, undefined, "getTab"),
                        error => invokeHostCallback(record, () => callback(null, legacyError(error)), undefined, undefined, "getTab"));
                };
                if (api.getTabs) facade.GM_getTabs = function (callback) {
                    if (typeof callback !== "function") throw new TypeError("callback must be a function.");
                    api.getTabs().then(value => invokeHostCallback(record, () => callback(value, null), undefined, undefined, "getTabs"),
                        error => invokeHostCallback(record, () => callback(null, legacyError(error)), undefined, undefined, "getTabs"));
                };
                if (api.setClipboard) facade.GM_setClipboard = function (...args) {
                    setClipboard(record, ...args).catch(error => console.error("[MonkeySharp] clipboard failed", error));
                };
                if (api.notification) facade.GM_notification = function (...args) {
                    notificationOperation(record, ...args).catch(error => console.error("[MonkeySharp] notification failed", error));
                };
                if (api.openInTab) facade.GM_openInTab = function (url, options) {
                    const operation = record.openTab(url, options);
                    operation.ready.catch(error => console.error("[MonkeySharp] legacy tab open failed", error));
                    return operation.handle;
                };
                if (api.download) facade.GM_download = function (details, name) {
                    const operation = createDownloadOperation(record, details, name);
                    operation.promise.catch(error => {
                        if (!operation.hasErrorCallback) console.error("[MonkeySharp] legacy download failed", error);
                    });
                    return { abort: operation.promise.abort };
                };
            };
