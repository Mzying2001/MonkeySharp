            const notSupported = function (message) {
                const error = new Error(message);
                error.code = "MSP006_NOT_SUPPORTED";
                return error;
            };

            const xhrError = function (error, fallback) {
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
                    details: error && error.message ? error.message : String(error || "The HTTP request failed."),
                    code: code || null
                };
            };

            const objectTag = value => Object.prototype.toString.call(value);

            const normalizeHttpUrl = function (value) {
                const tag = objectTag(value);
                if (tag === "[object Blob]" || tag === "[object File]") {
                    throw notSupported("Blob and File request URLs are not supported.");
                }
                if (typeof value === "string") {
                    if (typeof root.URL === "function") return new root.URL(value, root.location && root.location.href).href;
                    return value;
                }
                if (tag === "[object URL]") return value.href;
                throw new TypeError("details.url must be a string or URL.");
            };

            const setDefaultHeader = function (headers, name, value) {
                if (!value) return;
                const exists = Object.keys(headers).some(header => header.toLowerCase() === name.toLowerCase());
                if (!exists) headers[name] = value;
            };

            const serializeHttpBody = async function (value, binary) {
                if (typeof value === "undefined" || value === null) return { bytes: null, contentType: null };
                if (typeof value === "string") {
                    if (binary) {
                        const bytes = new Uint8Array(value.length);
                        for (let index = 0; index < value.length; index += 1) bytes[index] = value.charCodeAt(index) & 0xff;
                        return { bytes: bytes, contentType: null };
                    }
                    return { bytes: new TextEncoder().encode(value), contentType: null };
                }
                const tag = objectTag(value);
                if (tag === "[object ArrayBuffer]") {
                    return { bytes: new Uint8Array(value.slice(0)), contentType: null };
                }
                if (ArrayBuffer.isView(value)) {
                    return {
                        bytes: new Uint8Array(value.buffer.slice(value.byteOffset, value.byteOffset + value.byteLength)),
                        contentType: null
                    };
                }
                if (tag === "[object Blob]" || tag === "[object File]") {
                    return { blob: value, contentType: value.type || null };
                }
                if (tag === "[object FormData]") {
                    if (typeof root.Response !== "function") throw notSupported("This browser cannot serialize FormData.");
                    const encoded = new root.Response(value);
                    return {
                        blob: await encoded.blob(),
                        contentType: encoded.headers.get("content-type")
                    };
                }
                if (tag === "[object URLSearchParams]") {
                    return {
                        bytes: new TextEncoder().encode(value.toString()),
                        contentType: "application/x-www-form-urlencoded;charset=UTF-8"
                    };
                }
                const prototype = Object.getPrototypeOf(value);
                if (Array.isArray(value) || prototype === Object.prototype || prototype === null) {
                    return {
                        bytes: new TextEncoder().encode(JSON.stringify(value)),
                        contentType: "application/json;charset=UTF-8"
                    };
                }
                throw new TypeError("details.data has an unsupported type.");
            };

            const decodeHttpChunk = function (encoded) {
                const binary = atob(encoded || "");
                const bytes = new Uint8Array(binary.length);
                for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
                return bytes;
            };

            const invokeHttpCallback = function (handler, name, value) {
                const callback = handler.callbacks[name];
                if (typeof callback !== "function") return;
                try {
                    callback.call(handler.state, typeof value === "undefined" ? handler.state : value);
                } catch (error) {
                    if (root.console && typeof root.console.error === "function") {
                        root.console.error("[MonkeySharp] XHR callback failed", error);
                    }
                }
            };

            const httpProgress = function (handler, data) {
                return Object.assign({}, handler.state, {
                    lengthComputable: data.total !== null && typeof data.total !== "undefined",
                    loaded: data.loaded,
                    total: data.total === null || typeof data.total === "undefined" ? 0 : data.total
                });
            };

            const updateHttpState = function (handler, data) {
                handler.state.readyState = data.readyState;
                handler.state.status = data.status;
                handler.state.statusText = data.statusText;
                if (data.finalUrl) handler.state.finalUrl = data.finalUrl;
                handler.state.responseHeaders = data.responseHeaders || "";
            };

            const parseHttpDocument = function (text, mime) {
                if (typeof DOMParser !== "function") return null;
                const type = /^text\/html(?:;|$)/i.test(mime || "") ? "text/html" : "text/xml";
                const document = new DOMParser().parseFromString(text, type);
                if (type !== "text/html" && typeof document.querySelector === "function" && document.querySelector("parsererror")) return null;
                return document;
            };

            const rendererBlobRequest = function (record, details) {
                const type = details.responseXML === true ? "document" : details.responseType || "text";
                const fetchMode = details.fetch === true || details.anonymous === true || typeof details.redirect !== "undefined";
                if (!["text", "json", "arraybuffer", "blob", "stream", "document", "xml"].includes(type)) throw new TypeError("responseType is not supported.");
                for (const option of ["proxy", "cookiePartition", "cookie", "user", "password", "data"]) if (typeof details[option] !== "undefined") throw notSupported(option + " is not supported for a renderer Blob/File URL.");
                if (details.method && details.method.toUpperCase() !== "GET") throw notSupported("Renderer Blob/File requests support GET only.");
                if (details.synchronous === true || details.async === false) throw notSupported("Synchronous requests are unavailable.");
                if (details.headers && Object.keys(details.headers).length) throw notSupported("Headers are unavailable for a renderer Blob/File URL.");
                if (details.signal && (typeof details.signal.addEventListener !== "function" || typeof details.signal.removeEventListener !== "function")) throw new TypeError("signal must be an AbortSignal.");
                if (typeof details.timeout !== "undefined" && (!Number.isInteger(details.timeout) || details.timeout < 0)) throw new TypeError("timeout must not be negative.");
                const callbacks = {};
                for (const name of ["onloadstart", "onreadystatechange", "onprogress", "onload", "onerror", "onabort", "ontimeout", "onloadend"]) {
                    if (typeof details[name] !== "undefined" && typeof details[name] !== "function") throw new TypeError(name + " must be a function.");
                    callbacks[name] = details[name];
                }
                const controller = new AbortController();
                const state = { context: details.context, readyState: 0, status: 0, statusText: "", response: null, responseXML: null, responseText: "", responseHeaders: "", finalUrl: "" };
                const handler = { callbacks, state };
                let terminal = false, ownedUrl, streamController, timer;
                let rejectOperation;
                const finish = function (error) {
                    if (terminal) return;
                    terminal = true;
                    if (timer) clearTimeout(timer);
                    state.readyState = 4;
                    invokeHttpCallback(handler, "onreadystatechange");
                    if (error) {
                        if (streamController) try { streamController.error(error); } catch (_) { }
                        invokeHttpCallback(handler, error.code === "MSP009_TIMEOUT" ? "ontimeout" : error.name === "AbortError" ? "onabort" : "onerror", error);
                        rejectOperation(error);
                    } else invokeHttpCallback(handler, "onload");
                    invokeHttpCallback(handler, "onloadend");
                    if (ownedUrl) root.URL.revokeObjectURL(ownedUrl);
                    if (details.signal) details.signal.removeEventListener("abort", abort);
                    record.pageListeners.delete(lifecycle);
                };
                const abort = () => { controller.abort(); finish(Object.assign(new Error("The renderer request was aborted."), { name: "AbortError", code: "MSP010_CANCELED" })); };
                const lifecycle = { remove: abort };
                record.pageListeners.add(lifecycle);
                const promise = new Promise((resolve, reject) => {
                    rejectOperation = reject;
                    if (!fetchMode && details.timeout > 0) timer = setTimeout(() => { controller.abort(); finish(Object.assign(new Error("The renderer request timed out."), { code: "MSP009_TIMEOUT" })); }, details.timeout);
                    Promise.resolve().then(async () => {
                        if (terminal) return;
                        let blob;
                        if (objectTag(details.url) === "[object Blob]" || objectTag(details.url) === "[object File]") {
                            blob = details.url; ownedUrl = root.URL.createObjectURL(blob); state.finalUrl = ownedUrl;
                        } else {
                            state.finalUrl = normalizeHttpUrl(details.url);
                            const response = await root.fetch(state.finalUrl, { signal: controller.signal });
                            if (!response.ok) throw new Error("The renderer could not resolve the blob URL.");
                            blob = await response.blob();
                        }
                        if (blob.size > (record.bridgeLimits.binaryTransferBytes || 256 * 1024 * 1024)) throw Object.assign(new Error("Renderer body exceeds the transfer budget."), { code: "MSP008_PAYLOAD_TOO_LARGE" });
                        state.status = 200; state.statusText = "OK"; state.responseHeaders = "Content-Type: " + blob.type + "\r\n";
                        const mime = details.overrideMimeType || blob.type || "application/octet-stream";
                        const charset = /charset\s*=\s*["']?([^;"' ]+)/i.exec(mime);
                        let decoder;
                        try { decoder = new TextDecoder(charset ? charset[1] : "utf-8"); } catch (_) { decoder = new TextDecoder(); }
                        let offset = 0;
                        const next = async () => {
                            if (controller.signal.aborted) throw Object.assign(new Error("The renderer request was aborted."), { name: "AbortError" });
                            const bytes = new Uint8Array(await blob.slice(offset, Math.min(offset + 65536, blob.size)).arrayBuffer());
                            offset += bytes.length;
                            return bytes;
                        };
                        if (type === "stream") state.response = new ReadableStream({
                            start(value) { streamController = value; },
                            async pull(value) {
                                try { const bytes = await next(); if (bytes.length) value.enqueue(bytes); if (offset >= blob.size) { value.close(); finish(); } }
                                catch (error) { finish(error); }
                            }, cancel: abort
                        });
                        state.readyState = 1; invokeHttpCallback(handler, "onloadstart"); if (!fetchMode) invokeHttpCallback(handler, "onreadystatechange");
                        state.readyState = 2; if (!fetchMode) invokeHttpCallback(handler, "onreadystatechange");
                        if (type === "stream") { state.responseText = undefined; resolve(state); return; }
                        let text = "";
                        while (offset < blob.size) { const bytes = await next(); text += decoder.decode(bytes, { stream: true }); state.readyState = 3; if (!fetchMode) invokeHttpCallback(handler, "onprogress", httpProgress(handler, { loaded: offset, total: blob.size })); }
                        text += decoder.decode(); state.responseText = text;
                        if (type === "arraybuffer") { state.responseText = undefined; state.response = await blob.arrayBuffer(); }
                        else if (type === "blob") { state.responseText = undefined; state.response = blob.slice(0, blob.size, mime); }
                        else if (type === "json") { try { state.response = JSON.parse(text); } catch (_) { state.response = null; } }
                        else if (type === "document" || type === "xml") { state.responseXML = parseHttpDocument(text, mime); state.response = type === "document" ? state.responseXML : text; }
                        else state.response = text;
                        if (!terminal) { finish(); resolve(state); }
                    }).catch(finish);
                });
                promise.abort = abort;
                if (details.signal) { if (details.signal.aborted) abort(); else details.signal.addEventListener("abort", abort, { once: true }); }
                return promise;
            };

            const addHttpApi = function (record, enabled, api) {
                if (enabled("GM.xmlHttpRequest")) {
                    api.xmlHttpRequest = function (details) {
                        if (!details || typeof details !== "object") throw new TypeError("details must be an object.");
                        if (objectTag(details.url) === "[object Blob]" || objectTag(details.url) === "[object File]" ||
                            typeof details.url === "string" && /^blob:/i.test(details.url) || objectTag(details.url) === "[object URL]" && details.url.protocol === "blob:")
                            return rendererBlobRequest(record, details);
                        const requestUrl = normalizeHttpUrl(details.url);
                        if (typeof details.proxy !== "undefined") throw notSupported("proxy is not supported by CefSharp.");
                        if (typeof details.cookiePartition !== "undefined") {
                            throw notSupported("cookiePartition is not supported by CefSharp.");
                        }
                        const binaryRequest = details.binary === true;
                        if (typeof details.binary !== "undefined" && typeof details.binary !== "boolean") {
                            throw new TypeError("binary must be a boolean.");
                        }
                        if (details.synchronous === true || details.async === false) {
                            throw notSupported("synchronous XHR is not supported by MonkeySharp.");
                        }
                        if (details.responseXML !== undefined && typeof details.responseXML !== "boolean") {
                            throw new TypeError("responseXML must be a boolean.");
                        }
                        const xhrId = Number.isInteger(details.__monkeySharpXhrId)
                            ? details.__monkeySharpXhrId : nextListenerId++;
                        const callbacks = {
                            onloadstart: details.onloadstart,
                            onreadystatechange: details.onreadystatechange,
                            onload: details.onload,
                            onerror: details.onerror,
                            ontimeout: details.ontimeout,
                            onabort: details.onabort,
                            onloadend: details.onloadend,
                            onprogress: details.onprogress,
                            onuploadprogress: details.onuploadprogress
                        };
                        if (typeof details.upload !== "undefined") {
                            if (!details.upload || typeof details.upload !== "object") {
                                throw new TypeError("upload must be an object.");
                            }
                            if (typeof details.upload.onprogress !== "undefined") {
                                callbacks.onuploadprogress = details.upload.onprogress;
                            }
                        }
                        Object.keys(callbacks).forEach(name => {
                            if (typeof callbacks[name] !== "undefined" && typeof callbacks[name] !== "function") {
                                throw new TypeError(name + " must be a function.");
                            }
                        });
                        const parameters = { operation: "create", xhrId: xhrId, url: requestUrl };
                        if (typeof details.method !== "undefined") {
                            if (typeof details.method !== "string") throw new TypeError("method must be a string.");
                            parameters.method = details.method;
                        }
                        const headers = typeof details.headers === "undefined" ? {} : serializableValue(details.headers);
                        if (!headers || typeof headers !== "object" || Array.isArray(headers)) {
                            throw new TypeError("headers must be an object.");
                        }
                        parameters.headers = headers;
                        if (typeof details.timeout !== "undefined") {
                            if (!Number.isInteger(details.timeout) || details.timeout < 0) throw new TypeError("timeout must not be negative.");
                        }
                        ["cookie", "user", "password", "overrideMimeType"].forEach(name => {
                            if (typeof details[name] === "undefined") return;
                            if (typeof details[name] !== "string") throw new TypeError(name + " must be a string.");
                            parameters[name] = details[name];
                        });
                        if (typeof details.anonymous !== "undefined") {
                            if (typeof details.anonymous !== "boolean") throw new TypeError("anonymous must be a boolean.");
                            parameters.anonymous = details.anonymous;
                        }
                        ["nocache", "revalidate", "fetch"].forEach(name => {
                            if (typeof details[name] === "undefined") return;
                            if (typeof details[name] !== "boolean") throw new TypeError(name + " must be a boolean.");
                            parameters[name] = details[name];
                        });
                        parameters.fetch = details.fetch === true || details.anonymous === true || typeof details.redirect !== "undefined";
                        if (!parameters.fetch && details.timeout > 0) parameters.timeout = details.timeout;
                        if (typeof details.redirect !== "undefined") {
                            if (!["follow", "error", "manual"].includes(details.redirect)) {
                                throw new TypeError("redirect must be 'follow', 'error', or 'manual'.");
                            }
                            parameters.redirect = details.redirect;
                        }
                        const responseType = typeof details.responseType === "undefined" || details.responseType === ""
                            ? "text" : details.responseType;
                        const effectiveResponseType = details.responseXML === true ? "document" : responseType;
                        if (!["text", "json", "arraybuffer", "blob", "stream", "document", "xml"].includes(effectiveResponseType)) {
                            throw new TypeError("responseType is not supported.");
                        }
                        parameters.responseType = effectiveResponseType;
                        parameters.binary = binaryRequest;
                        if (details.signal && (typeof details.signal.addEventListener !== "function" ||
                            typeof details.signal.removeEventListener !== "function")) {
                            throw new TypeError("signal must be an AbortSignal.");
                        }
                        const controller = typeof AbortController === "function" ? new AbortController() : null;
                        let sessionId = null;
                        let externalAbort;
                        if (details.signal && controller) {
                            externalAbort = () => controller.abort();
                            if (details.signal.aborted) externalAbort();
                            else details.signal.addEventListener("abort", externalAbort, { once: true });
                        }
                        const signal = controller ? controller.signal : details.signal;
                        const state = {
                            context: details.context,
                            finalUrl: requestUrl,
                            readyState: 0,
                            response: null,
                            responseXML: null,
                            responseHeaders: "",
                            responseText: "",
                            status: 0,
                            statusText: ""
                        };
                        const handler = {
                            callbacks: callbacks,
                            state: state,
                            responseType: effectiveResponseType,
                            deferState4: effectiveResponseType !== "stream",
                            pendingState4: false,
                            streamController: null,
                            terminal: false,
                            cleanup: null
                        };
                        let released = false;
                        const release = function () {
                            if (!handler.streamController || handler.terminal || handler.failed || record.disposed) record.xhrHandlers.delete(xhrId);
                            if (released) return;
                            released = true;
                            if (details.signal && externalAbort) details.signal.removeEventListener("abort", externalAbort);
                            if (sessionId) call(record, "GM.xmlHttpRequest", {
                                operation: "release", sessionId: sessionId
                            }).catch(error => console.warn("[MonkeySharp] XHR release failed", error));
                        };
                        handler.cleanup = release;
                        record.xhrHandlers.set(xhrId, handler);
                        const abortRequest = function () {
                            if (sessionId) call(record, "GM.xmlHttpRequest", {
                                operation: "abort", sessionId: sessionId
                            }).catch(error => {
                                if (!error || error.code !== "MSP003_SESSION_EXPIRED") {
                                    console.warn("[MonkeySharp] XHR abort failed", error);
                                }
                            });
                            if (controller) controller.abort();
                        };
                        let responseStream = null;
                        let streamReady;
                        const streamSessionReady = new Promise(resolve => { streamReady = resolve; });
                        let streamOffset = 0;
                        if (effectiveResponseType === "stream") {
                            if (typeof ReadableStream !== "function") throw notSupported("ReadableStream is unavailable.");
                            responseStream = new ReadableStream({
                                start: function (streamController) { handler.streamController = streamController; },
                                async pull(streamController) {
                                    await streamSessionReady;
                                    if (handler.failed) return;
                                    try {
                                        const part = await call(record, "GM.xmlHttpRequest", { operation: "readBody", sessionId, offset: streamOffset }, signal);
                                        const bytes = decodeHttpChunk(part.chunk);
                                        if (bytes.length) { streamOffset += bytes.length; streamController.enqueue(bytes); }
                                        if (part.done) { handler.streamConsumed = true; streamController.close(); release(); }
                                    } catch (error) {
                                        handler.failed = true;
                                        try { streamController.error(error); } catch (_) { }
                                        if (!handler.terminal) { invokeHttpCallback(handler, "onerror", legacyError(error)); invokeHttpCallback(handler, "onloadend"); handler.terminal = true; }
                                        release();
                                    }
                                },
                                cancel: function () { abortRequest(); release(); }
                            });
                            state.response = responseStream;
                            state.responseText = undefined;
                        }
                        const operation = (async function () {
                            try {
                                if (binaryRequest && typeof details.data !== "undefined" && details.data !== null &&
                                    typeof details.data !== "string") {
                                    throw new TypeError("binary requests require a string body.");
                                }
                                const body = await serializeHttpBody(details.data, binaryRequest);
                                setDefaultHeader(parameters.headers, "Content-Type", body.contentType);
                                const bodyLength = body.blob ? body.blob.size : body.bytes ? body.bytes.length : 0;
                                if (bodyLength > (record.bridgeLimits.binaryTransferBytes || 256 * 1024 * 1024)) throw Object.assign(new Error("Request body exceeds the configured transfer budget."), { code: "MSP008_PAYLOAD_TOO_LARGE" });
                                const created = await call(record, "GM.xmlHttpRequest", parameters, signal);
                                sessionId = created.sessionId;
                                for (let offset = 0; offset < bodyLength; offset += 65536) {
                                    const segment = body.blob ? new Uint8Array(await body.blob.slice(offset, Math.min(offset + 65536, bodyLength)).arrayBuffer()) : body.bytes.subarray(offset, Math.min(offset + 65536, bodyLength));
                                    let binary = "";
                                    for (let index = 0; index < segment.length; index += 1) binary += String.fromCharCode(segment[index]);
                                    await call(record, "GM.xmlHttpRequest", { operation: "appendBody", sessionId, chunk: btoa(binary) }, signal);
                                }
                                const response = await call(record, "GM.xmlHttpRequest", {
                                    operation: "execute", sessionId: sessionId
                                }, signal);
                                state.status = response.status;
                                state.statusText = response.statusText;
                                state.finalUrl = response.finalUrl || state.finalUrl;
                                state.responseHeaders = response.responseHeaders || "";
                                if (effectiveResponseType === "stream") { streamReady(); return state; }
                                const chunks = [];
                                const binaryResponse = effectiveResponseType === "arraybuffer" || effectiveResponseType === "blob";
                                let decoder;
                                try { decoder = new TextDecoder(response.charset || "utf-8"); } catch (_) { decoder = new TextDecoder(); }
                                let responseText = "";
                                let length = 0;
                                while (length < response.bodyLength) {
                                    const result = await call(record, "GM.xmlHttpRequest", {
                                        operation: "readBody", sessionId: sessionId, offset: length
                                    }, signal);
                                    const chunk = decodeHttpChunk(result.chunk);
                                    if (binaryResponse) chunks.push(chunk); else responseText += decoder.decode(chunk, { stream: true });
                                    length += chunk.length;
                                    if (result.done) break;
                                }
                                const responseBytes = new Uint8Array(binaryResponse ? length : 0);
                                let bodyOffset = 0;
                                chunks.forEach(chunk => { responseBytes.set(chunk, bodyOffset); bodyOffset += chunk.length; });
                                if (!binaryResponse) responseText += decoder.decode();
                                if (effectiveResponseType === "json") {
                                    state.responseText = responseText;
                                    try { state.response = responseText.length === 0 ? null : JSON.parse(responseText); }
                                    catch (_) { state.response = null; }
                                } else if (effectiveResponseType === "arraybuffer") {
                                    state.responseText = undefined;
                                    state.response = responseBytes.buffer;
                                } else if (effectiveResponseType === "blob") {
                                    state.responseText = undefined;
                                    state.response = new Blob([responseBytes], {
                                        type: response.mimeType || "application/octet-stream"
                                    });
                                } else if (effectiveResponseType === "document" || effectiveResponseType === "xml") {
                                    state.responseText = responseText;
                                    if (typeof DOMParser === "function") {
                                        try {
                                            state.responseXML = parseHttpDocument(responseText, response.mimeType);
                                            state.response = effectiveResponseType === "document" ? state.responseXML : responseText;
                                        } catch (_) {
                                            state.responseXML = null;
                                            state.response = effectiveResponseType === "document" ? null : responseText;
                                        }
                                    } else {
                                        state.responseXML = null;
                                        state.response = effectiveResponseType === "document" ? null : responseText;
                                    }
                                } else {
                                    state.responseText = responseText;
                                    state.response = responseText;
                                }
                                if (handler.pendingState4) invokeHttpCallback(handler, "onreadystatechange");
                                invokeHttpCallback(handler, "onload");
                                invokeHttpCallback(handler, "onloadend");
                                handler.terminal = true;
                                return state;
                            } catch (error) {
                                handler.failed = true;
                                streamReady();
                                if (handler.streamController) try { handler.streamController.error(error); } catch (_) { }
                                const callbackError = legacyError(error);
                                callbackError.name = error && error.name === "AbortError" ||
                                    error && error.code === "MSP010_CANCELED" ? "AbortError" : "Error";
                                if (callbackError.name === "AbortError") {
                                    invokeHttpCallback(handler, "onabort", callbackError);
                                } else if (callbackError.code === "MSP009_TIMEOUT") {
                                    invokeHttpCallback(handler, "ontimeout", callbackError);
                                } else invokeHttpCallback(handler, "onerror", callbackError);
                                invokeHttpCallback(handler, "onloadend", callbackError);
                                handler.terminal = true;
                                throw error;
                            } finally {
                                if (effectiveResponseType !== "stream" || handler.failed || handler.streamConsumed) release();
                            }
                        })();
                        operation.abort = abortRequest;
                        return operation;
                    };
                }
            };

            const addNetworkApi = function (record, enabled, api) {
                if (enabled("GM.cookie")) {
                    const invokeCookie = function (operation, details) {
                        if (typeof details === "undefined") details = {};
                        if (!details || typeof details !== "object" || Array.isArray(details)) return Promise.reject(new TypeError("cookie details must be an object."));
                        for (const option of ["partitionKey", "cookiePartition", "firstPartyDomain"]) if (Object.prototype.hasOwnProperty.call(details, option)) return Promise.reject(notSupported(option + " is not supported by this cookie backend."));
                        return call(record, "GM.cookie", { operation, details: serializableValue(details) });
                    };
                    api.cookie = Object.freeze({ list: details => invokeCookie("list", details),
                        set: details => invokeCookie("set", details).then(() => undefined),
                        delete: details => invokeCookie("delete", details).then(() => undefined) });
                }
                if (enabled("GM.webRequest")) {
                    api.webRequest = function (rules, listener) {
                        if (!Array.isArray(rules)) rules = [rules];
                        if (typeof listener !== "undefined" && typeof listener !== "function") {
                            throw new TypeError("listener must be a function.");
                        }
                        const listenerId = typeof listener === "function" ? nextListenerId++ : 0;
                        if (listenerId) record.webRequestHandlers.set(listenerId, listener);
                        const state = { id: null, removeRequested: false, removePromise: null, removeResolve: null, removeReject: null };
                        const sendRemove = function () {
                            if (!state.id) return state.removePromise || Promise.resolve(false);
                            const request = call(record, "GM.webRequest", { operation: "remove", id: state.id });
                            request.then(value => {
                                if (state.removeResolve) state.removeResolve(value);
                                if (listenerId) record.webRequestHandlers.delete(listenerId);
                            }, error => {
                                if (state.removeReject) state.removeReject(error);
                            });
                            return request;
                        };
                        const handle = {};
                        Object.defineProperties(handle, {
                            id: { enumerable: true, get: () => state.id }
                        });
                        handle.remove = function () {
                            if (state.removeRequested) return state.removePromise || Promise.resolve(false);
                            state.removeRequested = true;
                            state.removePromise = new Promise((resolve, reject) => {
                                state.removeResolve = resolve;
                                state.removeReject = reject;
                            });
                            if (state.id) sendRemove();
                            return state.removePromise;
                        };
                        call(record, "GM.webRequest", {
                            operation: "register",
                            rules: serializableValue(rules),
                            listenerId: listenerId || undefined
                        }).then(result => {
                            state.id = result && result.id;
                            if (state.removeRequested) sendRemove();
                        }).catch(error => {
                            if (listenerId) record.webRequestHandlers.delete(listenerId);
                            if (state.removeReject) state.removeReject(error);
                            else console.error("[MonkeySharp] webRequest registration failed", error);
                        });
                        return handle;
                    };
                }
            };

            const addLegacyHttp = function (api, facade) {
                if (api.xmlHttpRequest) facade.GM_xmlhttpRequest = function (details) {
                    if (!details || typeof details !== "object") throw new TypeError("details must be an object.");
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
                    let request;
                    try {
                        request = api.xmlHttpRequest(Object.assign({}, details, {
                            __monkeySharpXhrId: xhrId,
                            __monkeySharpLegacy: true
                        }));
                    } catch (error) {
                        if (typeof callbacks.onerror === "function") {
                            try { callbacks.onerror(legacyError(error)); }
                            catch (callbackError) { console.error("[MonkeySharp] legacy XHR error callback failed", callbackError); }
                        } else {
                            console.error("[MonkeySharp] legacy XHR failed", error);
                        }
                        return { abort: function () { return Promise.resolve(false); } };
                    }
                    const handle = { abort: function () { request.abort(); } };
                    request
                        .catch(error => {
                            if (error && error.name === "AbortError") return;
                            if (!callbacks.onerror && !callbacks.ontimeout) {
                                console.error("[MonkeySharp] legacy XHR failed", error);
                            }
                        });
                    return handle;
                };
            };

            const addLegacyNetwork = function (record, api, facade) {
                if (api.cookie) {
                    const cookieCallback = function (operation, details, callback) {
                        if (typeof details === "function") { callback = details; details = undefined; }
                        if (typeof callback !== "undefined" && typeof callback !== "function") throw new TypeError("callback must be a function.");
                        const invoke = args => invokeHostCallback(record, typeof callback === "function" ? () => callback(...args) : null, undefined, undefined, "cookie " + operation);
                        api.cookie[operation](details).then(value => invoke(operation === "list" ? [value, null] : []), error => {
                            const message = String(error && error.message || error);
                            if (callback) invoke(operation === "list" ? [null, message] : [message]);
                            else console.error("[MonkeySharp] cookie " + operation + " failed", error);
                        });
                    };
                    facade.GM_cookie = Object.freeze(Object.assign(function () { throw new TypeError("Use GM_cookie.list, set or delete."); }, { list: (details, callback) => cookieCallback("list", details, callback),
                        set: (details, callback) => cookieCallback("set", details, callback),
                        delete: (details, callback) => cookieCallback("delete", details, callback) }));
                }
                if (api.webRequest) facade.GM_webRequest = function (rules, listener) {
                    return api.webRequest(rules, listener);
                };
            };
