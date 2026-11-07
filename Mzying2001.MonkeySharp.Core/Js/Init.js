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

            const legacyError = function (error, fallback) {
                const value = xhrError(error, fallback);
                if (!value.code && error instanceof TypeError) value.code = "MSP007_INVALID_PARAMS";
                return value;
            };

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

            const deepFreeze = function (value) {
                if (!value || typeof value !== "object" || Object.isFrozen(value)) return value;
                Object.getOwnPropertyNames(value).forEach(name => deepFreeze(value[name]));
                return Object.freeze(value);
            };

            const resolveDispatch = async function () {
                if (root.__MonkeySharpIsolatedTransport) return root.__MonkeySharpIsolatedTransport.dispatch.bind(root.__MonkeySharpIsolatedTransport);
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
                if (!response || response.protocol !== 2) throw new Error("MonkeySharp returned an invalid bridge response.");
                return response;
            };

            const call = async function (record, method, parameters, signal, requestId) {
                if (record.disposed) throw Object.assign(new Error("The script execution ended."), { code: "MSP003_SESSION_EXPIRED" });
                if (signal && signal.aborted) {
                    const error = new Error("The request was canceled.");
                    error.name = "AbortError";
                    throw error;
                }
                requestId = requestId || uuid();
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
                // Storage results have their own typed encoding and explicit key existence.
                return method === "GM.getValue" || method === "GM.getValues" ? response.result : decodeResult(response.result);
            };

            /*__MONKEYSHARP_STORAGE_MODULE__*/
            /*__MONKEYSHARP_NETWORK_MODULE__*/
            /*__MONKEYSHARP_HOSTINTERACTION_MODULE__*/
            /*__MONKEYSHARP_PAGE_MODULE__*/

            const createApi = function (record, invocation, availableApis) {
                const grants = new Set(invocation.grants);
                const enabled = name => grants.has(name) && availableApis.has(name);
                const api = {};
                Object.defineProperty(api, "info", { value: deepFreeze(invocation.info), enumerable: true });
                addPageApi(record, enabled, api);
                addStorageApi(record, enabled, api);
                addDomApi(enabled, api);
                addResourceApi(record, enabled, api);
                addHttpApi(record, enabled, api);
                addHostApi(record, enabled, api);
                addNetworkApi(record, enabled, api);
                return Object.freeze(api);
            };

            const apiDescriptors = __MONKEYSHARP_API_CATALOG__;
            const legacyNames = apiDescriptors.map(api => api.legacy).filter(name => name && name !== "GM_info");

            const createLegacyFacade = function (record, invocation, api) {
                const facade = {};
                addLegacyLog(api, facade);
                addLegacyAliases(api, facade);
                addLegacyStorage(record, api, facade);
                addLegacyPage(record, api, facade);
                addLegacyHttp(api, facade);
                addLegacyHost(record, api, facade);
                addLegacyNetwork(record, api, facade);
                const declared = new Set(invocation.declaredGrants || []);
                return Object.fromEntries(Object.entries(facade).filter(([name]) => declared.has(name)));
            };

            const runInvocation = async function (plan, invocation, getDispatch) {
                const invocationMoment = root.performance && root.performance.now ? root.performance.now() : Date.now();
                const grantState = invocation.grantDeclarationState || "ExplicitList";
                const requiresBridge = grantState === "ExplicitList" &&
                    invocation.grants.some(name => name !== "none" && name !== "GM.info");
                const dispatch = requiresBridge ? await getDispatch() : null;
                const proof = createProof(plan, invocation);
                const record = {
                    proof: proof,
                    executionId: invocation.executionId,
                    dispatch: dispatch,
                    deliveryToken: invocation.deliveryToken,
                    handlers: new Map(),
                    valueListenerKeys: new Map(),
                    disposed: false,
                    runAt: plan.runAt,
                    invocationMoment,
                    pageListeners: new Set(),
                    menuHandlers: new Map(),
                    xhrHandlers: new Map(),
                    downloadHandlers: new Map(),
                    notificationHandlers: new Map(),
                    notificationTails: new Map(),
                    tabHandlers: [],
                    webRequestHandlers: new Map(),
                    mutationTail: Promise.resolve(),
                    storageMirror: null,
                    storageConfirmed: null,
                    storageReady: false,
                    storageNotifications: [],
                    valueChangeSequences: new Map(),
                    pendingStorageMutations: [],
                    lastStorageSequence: 0,
                    bridgeLimits: {},
                    resourceMirror: null
                    , urlChangeHandlers: new Set(), urlChangeHandler: null, windowFacade: null
                };
                executions.set(invocation.executionId, record);
                let availableApis = new Set();
                if (requiresBridge) {
                    if (!dispatch) throw new Error("MonkeySharp bridge is unavailable.");
                    const hello = await send(dispatch, Object.assign({ type: "hello" }, proof));
                    if (hello.type !== "hello-result" || !hello.ok) {
                        throw new Error(hello.error && hello.error.message || "MonkeySharp bridge handshake failed.");
                    }
                    availableApis = new Set(hello.apis || []);
                    record.bridgeLimits = hello.limits || {};
                    if (invocation.grants.some(name => /GM\.(getValue|setValue|deleteValue|listValues|getValues|setValues|deleteValues|addValueChangeListener|removeValueChangeListener)$/.test(name)))
                        await prepareStorage(record, hello.compatibility && hello.compatibility.storage || {});
                    if (hello.compatibility && hello.compatibility.resources && hello.compatibility.resources.chunkCount) {
                        const descriptor = hello.compatibility.resources;
                        const chunks = [];
                        for (let index = 0; index < descriptor.chunkCount; index += 1) {
                            const part = await call(record, "runtime.getResourceChunk", { index: index });
                            if (!part || part.index !== index || typeof part.chunk !== "string") throw new Error("Incomplete resource package.");
                            chunks.push(part.chunk);
                        }
                        const json = chunks.join("");
                        if (json.length !== descriptor.jsonLength) throw new Error("Incomplete resource package.");
                        hello.compatibility.resources = JSON.parse(json);
                    }
                    if (hello.compatibility && hello.compatibility.resources &&
                        hello.compatibility.resources.complete && hello.compatibility.resources.values) {
                        record.resourceMirror = Object.assign({}, hello.compatibility.resources.values);
                    }
                }
                if (invocation.grants.some(name => name === "GM.getResourceText" || name === "GM.getResourceUrl") && !record.resourceMirror) {
                    executions.delete(invocation.executionId);
                    throw new Error("The complete resource package must be ready before running this script.");
                }
                if (record.storageResync) await record.storageResync;
                if (record.disposed) return;
                if (record.storageMirror && !record.storageReady) throw new Error("Storage synchronization did not complete.");
                const gm = createApi(record, invocation, availableApis);
                record.windowFacade = createWindowFacade(record, invocation, availableApis);
                const legacy = createLegacyFacade(record, invocation, gm);
                const unsafeWindow = grantState !== "ExplicitNone" && plan.executionWorld !== "isolated" ? root : undefined;
                try {
                    const names = ["GM", "GM_info", "unsafeWindow", "window"].concat(legacyNames);
                    const values = [gm, gm.info, unsafeWindow, record.windowFacade].concat(legacyNames.map(name => legacy[name]));
                    const execute = new Function(...names, invocation.source);
                    const receiver = grantState === "ExplicitNone" ? {} : record.windowFacade;
                    await execute.call(receiver, ...values);
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
                if (!plan || plan.protocol !== 2 || !Array.isArray(plan.invocations)) {
                    throw new Error("MonkeySharp received an invalid injection plan.");
                }
                let dispatchPromise;
                const getDispatch = () => dispatchPromise || (dispatchPromise = resolveDispatch());
                for (const invocation of plan.invocations) {
                    await runInvocation(plan, invocation, getDispatch).catch(error => console.error("[MonkeySharp]", error));
                }
            };

            const receive = function (notification) {
                if (typeof notification === "string") notification = JSON.parse(notification);
                if (!notification || notification.type !== "notification" || notification.protocol !== 2) return false;
                const record = executions.get(notification.executionId);
                if (!record || record.deliveryToken !== notification.deliveryToken) return false;
                const data = notification.data || {};
                try {
                    if (notification.event === "execution-ended") {
                        endExecution(record);
                        return true;
                    }
                    if ((notification.event === "storage-sync" || notification.event === "value-change") && !record.storageReady) {
                        record.storageNotifications.push(notification);
                        return true;
                    }
                    if (notification.event === "storage-sync") {
                        if (data.sequence > record.lastStorageSequence + 1) {
                            record.storageNotifications.push(notification);
                            resynchronizeStorage(record).catch(() => {});
                            return true;
                        }
                        applyStorageSync(record, data);
                        return true;
                    }
                    if (notification.event === "value-change") {
                        if (data.sequence && data.sequence <= (record.valueChangeSequences.get(data.listenerId) || 0)) return true;
                        if (data.sequence > record.lastStorageSequence) {
                            record.storageNotifications.push(notification);
                            resynchronizeStorage(record).catch(() => {});
                            return true;
                        }
                        notifyValueListeners(record, data, false);
                        return true;
                    }
                    if (notification.event === "menu-command") {
                        const handler = record.menuHandlers.get(data.commandId);
                        if (!handler) return false;
                        invokeHostCallback(record, handler, undefined, typeof MouseEvent === "function" ? new MouseEvent("click") : { type: "click" }, "menu");
                        return true;
                    }
                    if (notification.event === "notification-click" || notification.event === "notification-done" || notification.event === "notification-error") {
                        const state = record.notificationHandlers.get(data.clientId);
                        if (!state || state.terminal) return false;
                        if (notification.event === "notification-click") {
                            if (state.clicked) return false;
                            state.clicked = true;
                            const event = typeof Event === "function" ? new Event("click", { cancelable: true }) : { defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };
                            invokeHostCallback(record, state.details.onclick, state.details, event, "notification onclick");
                            call(record, "GM.notification", { operation: "click-result", clientId: data.clientId, preventDefault: event.defaultPrevented })
                                .catch(error => console.error("[MonkeySharp] notification click acknowledgment failed", error));
                        } else {
                            state.terminal = true;
                            record.notificationHandlers.delete(data.clientId);
                            if (notification.event === "notification-error") state.reject(new Error(data.details || "Notification failed."));
                            else {
                                invokeHostCallback(record, state.details.ondone, state.details, undefined, "notification ondone");
                                state.resolve(Boolean(data.clicked || state.clicked));
                            }
                        }
                        return true;
                    }
                    if (notification.event === "tab-closed") {
                        record.tabHandlers.forEach(state => {
                            if (state.id === data.tabId && !state.closed) {
                                state.closed = true;
                                state.terminal = true;
                                if (typeof state.onclose === "function") {
                                    try { state.onclose(); } catch (error) { console.error("[MonkeySharp] tab onclose failed", error); }
                                }
                                if (state.closeResolve) state.closeResolve(true);
                            }
                        });
                        return true;
                    }
                    if (notification.event === "download-progress" || notification.event === "download-complete" ||
                        notification.event === "download-error" || notification.event === "download-aborted" || notification.event === "download-timeout") {
                        const state = record.downloadHandlers.get(data.clientId);
                        if (!state || state.terminal) return false;
                        if (notification.event === "download-progress") {
                            invokeHostCallback(record, state.callbacks.onprogress, state.details, { loaded: data.loaded, total: data.total }, "download progress");
                        } else if (notification.event === "download-complete") state.finish("complete", data.result || {});
                        else state.finish(notification.event === "download-timeout" ? "timeout" : notification.event === "download-aborted" ? "aborted" : "error", data);
                        return true;
                    }
                    if (notification.event === "webrequest-result") {
                        const callback = record.webRequestHandlers.get(data.listenerId);
                        if (!callback) return false;
                        callback(data.info, data.message, data.details);
                        return true;
                    }
                    if (notification.event.indexOf("xhr-") === 0) {
                        const handler = record.xhrHandlers.get(data.xhrId);
                        if (!handler) return false;
                        if (notification.event === "xhr-state") {
                            updateHttpState(handler, data);
                            if (data.readyState === 4 && handler.deferState4) handler.pendingState4 = true;
                            else invokeHttpCallback(handler, "onreadystatechange");
                            if (data.readyState === 1) invokeHttpCallback(handler, "onloadstart");
                        } else if (notification.event === "xhr-progress") {
                            invokeHttpCallback(handler, "onprogress", httpProgress(handler, data));
                        } else if (notification.event === "xhr-upload-progress") {
                            invokeHttpCallback(handler, "onuploadprogress", httpProgress(handler, data));
                        } else if (notification.event === "xhr-complete") {
                            if (handler.terminal) return true;
                            handler.streamClosed = true;
                            if (handler.pendingState4) invokeHttpCallback(handler, "onreadystatechange");
                            invokeHttpCallback(handler, "onload");
                            invokeHttpCallback(handler, "onloadend");
                            handler.terminal = true;
                            if (!handler.streamController || handler.streamConsumed) handler.cleanup();
                        } else if (notification.event === "xhr-error") {
                            if (handler.terminal) return true;
                            handler.failed = true;
                            const error = xhrError({ code: data.code, message: data.message });
                            error.name = data.code === "MSP010_CANCELED" ? "AbortError" : "Error";
                            if (data.code === "MSP010_CANCELED") error.name = "AbortError";
                            if (handler.streamController) {
                                try { handler.streamController.error(error); } catch (_) { }
                            }
                            if (error.name === "AbortError") invokeHttpCallback(handler, "onabort", error);
                            else if (error.code === "MSP009_TIMEOUT") invokeHttpCallback(handler, "ontimeout", error);
                            else invokeHttpCallback(handler, "onerror", error);
                            invokeHttpCallback(handler, "onloadend", error);
                            handler.terminal = true;
                            handler.cleanup();
                        }
                        return true;
                    }
                    return false;
                } catch (error) {
                    console.error("[MonkeySharp] callback failed", error);
                    return false;
                }
            };

            const endExecution = function (record) {
                record.disposed = true;
                record.pageListeners.forEach(listener => listener.remove());
                record.handlers.clear();
                record.valueListenerKeys.clear();
                record.valueChangeSequences.clear();
                record.pendingStorageMutations.length = 0;
                record.storageNotifications.length = 0;
                record.storageMirror = null;
                record.storageConfirmed = null;
                executions.delete(record.executionId);
            };

            const endDocument = function (documentId) {
                executions.forEach(record => { if (record.proof.documentId === documentId) endExecution(record); });
                if (root.__MonkeySharpLifecycle && root.__MonkeySharpLifecycle.documentId === documentId) root.__MonkeySharpLifecycle.dispose();
                return true;
            };

            const urlChanged = function (data) {
                if (!data || typeof data.url !== "string") return false;
                const info = { url: data.url, oldURL: typeof data.oldURL === "string" ? data.oldURL : "" };
                executions.forEach(record => {
                    if (!record.urlChangeHandler && record.urlChangeHandlers.size === 0) return;
                    try {
                        if (typeof record.urlChangeHandler === "function") record.urlChangeHandler.call(record.windowFacade, info);
                        record.urlChangeHandlers.forEach(handler => handler.call(record.windowFacade, info));
                    } catch (error) {
                        console.error("[MonkeySharp] urlchange callback failed", error);
                    }
                });
                return true;
            };

            return Object.freeze({ install: install, receive: receive, urlChanged: urlChanged, endDocument: endDocument });
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
