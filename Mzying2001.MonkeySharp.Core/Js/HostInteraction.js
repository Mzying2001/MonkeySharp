            const downloadError = function (error, fallback) {
                if (error && typeof error === "object" && typeof error.error === "string") {
                    return { error: error.error, details: error.details || null, code: error.code || null };
                }
                const code = error && error.code;
                const mapped = {
                    MSP003_SESSION_EXPIRED: "not_enabled",
                    MSP004_GRANT_DENIED: "not_permitted",
                    MSP005_PERMISSION_DENIED: "not_whitelisted",
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

            const addHostApi = function (record, enabled, api) {
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
                            imageUrl: details.imageUrl || details.image || null,
                            highlight: Boolean(details.highlight),
                            silent: Boolean(details.silent),
                            timeout: typeof details.timeout === "number" ? details.timeout : null
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
                    record.openTab = function (url, options) {
                        if (typeof url !== "string") throw new TypeError("url must be a string.");
                        options = options || {};
                        const state = {
                            id: null,
                            closed: false,
                            terminal: false,
                            closeRequested: false,
                            closePromise: null,
                            closeResolve: null,
                            closeReject: null,
                            onclose: null,
                            closeSent: false
                        };
                        const sendClose = function () {
                            if (!state.id || state.closeSent) return state.closePromise || Promise.resolve(false);
                            state.closeSent = true;
                            const request = call(record, "GM.openInTab", { close: true, tabId: state.id });
                            request.then(value => {
                                if (state.closeResolve) state.closeResolve(value);
                            }, error => {
                                if (state.closeReject) state.closeReject(error);
                            });
                            return request;
                        };
                        const handle = {};
                        Object.defineProperties(handle, {
                            id: { enumerable: true, get: () => state.id },
                            closed: { enumerable: true, get: () => state.closed },
                            onclose: {
                                enumerable: true,
                                get: () => state.onclose,
                                set: value => { state.onclose = value; }
                            }
                        });
                        handle.close = function () {
                            if (state.terminal || state.closed || state.closeRequested) return state.closePromise || Promise.resolve(false);
                            state.closeRequested = true;
                            state.closePromise = new Promise((resolve, reject) => {
                                state.closeResolve = resolve;
                                state.closeReject = reject;
                            });
                            if (state.id) sendClose();
                            return state.closePromise;
                        };
                        record.tabHandlers.push(state);
                        const ready = call(record, "GM.openInTab", {
                            url: url,
                            active: typeof options.active === "boolean" ? options.active : false,
                            insert: Boolean(options.insert),
                            setParent: Boolean(options.setParent)
                        }).then(result => {
                            state.id = result && result.id;
                            if (state.closeRequested) sendClose();
                            return handle;
                        }).catch(error => {
                            if (state.closeReject) state.closeReject(error);
                            throw error;
                        });
                        return { handle, ready };
                    };
                    api.openInTab = function (url, options) { return record.openTab(url, options).ready; };
                }
                if (enabled("GM.download")) {
                    api.download = async function (details, state) {
                        if (typeof details === "string") details = { url: details };
                        if (!details || typeof details !== "object") throw new TypeError("details must be an object.");
                        const clientId = uuid();
                        state = state || { clientId: clientId, id: null, abortRequested: false, terminal: false, callbacks: {} };
                        state.clientId = clientId;
                        record.downloadHandlers.set(clientId, state);
                        const request = {
                            operation: "start",
                            clientId: clientId,
                            url: details.url,
                            name: details.name || null,
                            headers: details.headers ? serializableValue(details.headers) : {},
                            saveAs: Boolean(details.saveAs),
                            conflictAction: details.conflictAction || "uniquify",
                            timeout: typeof details.timeout === "number" ? details.timeout : undefined
                        };
                        const handle = {
                            id: null,
                            abort: function () {
                                if (state.terminal || state.aborted) return Promise.resolve(false);
                                state.aborted = true;
                                state.abortRequested = true;
                                if (!state.id) return Promise.resolve(false);
                                return call(record, "GM.download", { operation: "abort", downloadId: state.id });
                            }
                        };
                        state.handle = handle;
                        try {
                            const result = await call(record, "GM.download", request);
                            state.id = result && result.id;
                            handle.id = state.id;
                            if (state.abortRequested) await handle.abort();
                            return handle;
                        } catch (error) {
                            record.downloadHandlers.delete(clientId);
                            throw downloadError(error);
                        }
                    };
                }
                if (enabled("GM.getTab")) api.getTab = async () => call(record, "GM.getTab", {});
                if (enabled("GM.saveTab")) {
                    api.saveTab = async value => call(record, "GM.saveTab", { value: serializableValue(value) });
                }
                if (enabled("GM.getTabs")) api.getTabs = async () => call(record, "GM.getTabs", {});
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
                    api.getTab().then(value => callback(value, null)).catch(error => callback(null, legacyError(error)));
                };
                if (api.getTabs) facade.GM_getTabs = function (callback) {
                    if (typeof callback !== "function") throw new TypeError("callback must be a function.");
                    api.getTabs().then(value => callback(value, null)).catch(error => callback(null, legacyError(error)));
                };
                if (api.setClipboard) facade.GM_setClipboard = function (text, type) {
                    api.setClipboard(text, type).catch(error => console.error("[MonkeySharp] legacy clipboard failed", error));
                };
                if (api.notification) facade.GM_notification = function (details, title, image, onclick) {
                    if (typeof details === "string") {
                        const options = { text: details };
                        if (typeof title === "function") {
                            options.ondone = title;
                        } else if (arguments.length > 1) {
                            if (typeof title !== "undefined") options.title = title;
                            if (typeof image !== "undefined") options.image = image;
                            if (typeof onclick === "function") options.onclick = onclick;
                        }
                        details = options;
                    } else {
                        details = Object.assign({}, details || {});
                        if (typeof title === "function") details.ondone = title;
                    }
                    api.notification(details).catch(error => console.error("[MonkeySharp] legacy notification failed", error));
                };
                if (api.openInTab) facade.GM_openInTab = function (url, options) {
                    if (typeof options === "boolean") options = { active: !options };
                    options = options || {};
                    if (typeof options.active !== "boolean" && typeof options.loadInBackground === "boolean") {
                        options = Object.assign({}, options, { active: !options.loadInBackground });
                    }
                    const operation = record.openTab(url, options);
                    operation.ready.catch(error => console.error("[MonkeySharp] legacy tab open failed", error));
                    return operation.handle;
                };
                if (api.download) facade.GM_download = function (details, onload, onerror) {
                    if (typeof details === "string" && typeof onload === "string") {
                        details = { url: details, name: onload };
                        onload = undefined;
                    }
                    if (typeof details === "string") details = { url: details };
                    if (!details || typeof details !== "object") throw new TypeError("details must be an object.");
                    ["onload", "onerror", "onprogress", "ontimeout"].forEach(name => {
                        if (typeof details[name] !== "undefined" && typeof details[name] !== "function") {
                            throw new TypeError(name + " must be a function.");
                        }
                    });
                    const state = { id: null, callbacks: {
                        onload: onload || details.onload,
                        onerror: onerror || details.onerror,
                        onprogress: details.onprogress,
                        ontimeout: details.ontimeout
                    } };
                    const handlePromise = api.download(details, state);
                    handlePromise.catch(error => {
                        if (typeof state.callbacks.onerror === "function") state.callbacks.onerror(downloadError(error));
                        else console.error("[MonkeySharp] legacy download failed", error);
                    });
                    return {
                        abort: function () {
                            if (state.abortRequested) return Promise.resolve(false);
                            state.abortRequested = true;
                            return state.handle ? state.handle.abort() : Promise.resolve(false);
                        },
                        get id() { return state.id; }
                    };
                };
            };
