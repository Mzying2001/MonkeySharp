            const applyElementAttributes = function (element, attributes) {
                if (!attributes || typeof attributes !== "object") return;
                Object.keys(attributes).forEach(name => {
                    const value = attributes[name];
                    if (name === "textContent" || name === "innerHTML") {
                        element[name] = value == null ? "" : String(value);
                    } else if (name === "style") {
                        if (element.style && typeof element.style.cssText !== "undefined") {
                            element.style.cssText = String(value == null ? "" : value);
                        } else {
                            element[name] = value;
                        }
                    } else if (name.slice(0, 2).toLowerCase() === "on" && typeof value === "function") {
                        element[name] = value;
                    } else if (name in element && name.indexOf("-") < 0 && name.indexOf(":") < 0) {
                        try { element[name] = value; }
                        catch (_) { element.setAttribute(name, String(value)); }
                    } else {
                        element.setAttribute(name, String(value));
                    }
                });
            };

            const listenerCapture = options => typeof options === "boolean" ? options : Boolean(options && options.capture);
            const removePageListener = function (record, type, listener, options) {
                const capture = listenerCapture(options);
                record.pageListeners.forEach(entry => { if (entry.type === String(type) && entry.listener === listener && entry.capture === capture) entry.remove(); });
            };
            const addPageListener = function (record, type, listener, options) {
                if (listener == null || record.disposed) return;
                if (typeof listener !== "function" && typeof listener !== "object") throw new TypeError("listener must be a function or event listener object.");
                type = String(type);
                const capture = listenerCapture(options);
                if (Array.from(record.pageListeners).some(entry => entry.type === type && entry.listener === listener && entry.capture === capture)) return;
                const signal = options && typeof options === "object" && options.signal;
                if (signal && (typeof signal.addEventListener !== "function" || typeof signal.removeEventListener !== "function")) throw new TypeError("signal must be an AbortSignal.");
                if (signal && signal.aborted) return;
                const entry = { type, listener, capture, active: true, seen: new WeakSet() };
                const deliver = function (event, original) {
                    if (!entry.active || record.disposed || entry.seen.has(original)) return;
                    entry.seen.add(original);
                    if (options && options.once) entry.remove();
                    if (typeof listener === "function") listener.call(record.windowFacade, event);
                    else if (typeof listener.handleEvent === "function") listener.handleEvent(event);
                };
                const wrapped = event => deliver(event, event);
                entry.remove = function () {
                    entry.active = false;
                    root.removeEventListener.call(root, type, wrapped, capture);
                    if (signal) signal.removeEventListener("abort", entry.remove);
                    record.pageListeners.delete(entry);
                };
                record.pageListeners.add(entry);
                root.addEventListener.call(root, type, wrapped, { capture, passive: Boolean(options && options.passive) });
                if (signal) signal.addEventListener("abort", entry.remove, { once: true });
                const cache = root.__MonkeySharpLifecycleEvents;
                if (cache && cache.documentId === record.proof.documentId) cache.snapshot(type, record.runAt, record.invocationMoment).forEach(item => {
                    Promise.resolve().then(() => {
                        if (!entry.active || record.disposed) return;
                        try {
                            Object.defineProperty(item.event, "currentTarget", { value: record.windowFacade, configurable: true });
                            deliver(item.event, item.original);
                        } catch (error) { console.error("[MonkeySharp] replayed event listener failed", error); }
                    });
                });
            };

            const createWindowFacade = function (record, invocation, availableApis) {
                const grants = new Set(invocation.grants);
                const closeEnabled = grants.has("window.close") && availableApis.has("window.close");
                const focusEnabled = grants.has("window.focus") && availableApis.has("window.focus");
                const windowAction = method => function () {
                    call(record, method, {}).catch(error => console.error("[MonkeySharp] window action failed", error));
                };
                const close = windowAction("window.close");
                const focus = windowAction("window.focus");
                const urlEnabled = grants.has("window.onurlchange");
                const isUrlEvent = value => value === "urlchange";
                const nativeFunctionCache = new Map();
                const bindNativeFunction = function (property, value) {
                    if (typeof value !== "function" || Object.prototype.hasOwnProperty.call(value, "prototype")) {
                        return value;
                    }
                    const cached = nativeFunctionCache.get(property);
                    if (cached && cached.value === value) return cached.bound;
                    const bound = Function.prototype.bind.call(value, root);
                    nativeFunctionCache.set(property, { value: value, bound: bound });
                    return bound;
                };
                const windowTarget = Object.create(root);
                return new Proxy(windowTarget, {
                    get: function (target, property, receiver) {
                        if (property === "close") return closeEnabled ? close : undefined;
                        if (property === "focus") return focusEnabled ? focus : undefined;
                        if (property === "onurlchange") return urlEnabled ? record.urlChangeHandler : undefined;
                        if (property === "addEventListener") {
                            return function (type, listener, options) {
                                if (urlEnabled && isUrlEvent(type)) {
                                    if (typeof listener !== "function") throw new TypeError("listener must be a function.");
                                    record.urlChangeHandlers.add(listener);
                                    return;
                                }
                                return addPageListener(record, type, listener, options);
                            };
                        }
                        if (property === "removeEventListener") {
                            return function (type, listener, options) {
                                if (urlEnabled && isUrlEvent(type)) {
                                    record.urlChangeHandlers.delete(listener);
                                    return;
                                }
                                return removePageListener(record, type, listener, options);
                            };
                        }
                        const value = Reflect.get(root, property, root);
                        if ((property === "top" || property === "parent" || property === "self" ||
                            property === "window" || property === "globalThis") && value === root) {
                            return receiver;
                        }
                        return bindNativeFunction(property, value);
                    },
                    set: function (target, property, value, receiver) {
                        if (property === "onurlchange" && urlEnabled) {
                            if (value !== null && typeof value !== "function") throw new TypeError("onurlchange must be a function or null.");
                            record.urlChangeHandler = value;
                            return true;
                        }
                        return Reflect.set(root, property, value, root);
                    }
                });
            };

            const addPageApi = function (record, enabled, api) {
                if (enabled("GM.log")) api.log = async value => call(record, "GM.log", { value: serializableValue(value) });
            };

            const addDomApi = function (enabled, api) {
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
                        applyElementAttributes(element, attributes);
                        parent.appendChild(element);
                        return element;
                    };
                }
            };

            const addResourceApi = function (record, enabled, api) {
                if (enabled("GM.getResourceText")) {
                    api.getResourceText = async function (name) {
                        if (typeof name !== "string" || name.length === 0) throw new TypeError("name must be a non-empty string.");
                        if (record.resourceMirror && Object.prototype.hasOwnProperty.call(record.resourceMirror, name)) {
                            return record.resourceMirror[name].text;
                        }
                        return call(record, "GM.getResourceText", { name: name });
                    };
                }
                if (enabled("GM.getResourceUrl")) {
                    api.getResourceUrl = async function (name) {
                        if (typeof name !== "string" || name.length === 0) throw new TypeError("name must be a non-empty string.");
                        if (record.resourceMirror && Object.prototype.hasOwnProperty.call(record.resourceMirror, name)) {
                            return record.resourceMirror[name].url;
                        }
                        return call(record, "GM.getResourceUrl", { name: name });
                    };
                }
            };

            const addLegacyLog = function (api, facade) {
                if (api.log) {
                    facade.GM_log = function (value) {
                        if (root.console && typeof root.console.log === "function") root.console.log(value);
                        api.log(value).catch(error => console.error("[MonkeySharp] legacy log failed", error));
                    };
                }
            };

            const addLegacyPage = function (record, api, facade) {
                if (record.resourceMirror) {
                    if (api.getResourceText) facade.GM_getResourceText = function (name) {
                        if (typeof name !== "string" || name.length === 0) throw new TypeError("name must be a non-empty string.");
                        if (!Object.prototype.hasOwnProperty.call(record.resourceMirror, name)) {
                            throw new Error("The resource snapshot is unavailable.");
                        }
                        return record.resourceMirror[name].text;
                    };
                    if (api.getResourceUrl) facade.GM_getResourceURL = function (name) {
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
                    applyElementAttributes(element, attributes);
                    parent.appendChild(element);
                    return element;
                };
            };
