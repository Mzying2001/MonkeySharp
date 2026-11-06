            const storageRecord = entries => Object.assign(Object.create(null), Object.fromEntries(entries));

            const encodeStoredValue = function (value) {
                const ancestors = new Set();
                const normalize = function (item) {
                    if (item === null || typeof item === "string" || typeof item === "boolean" || typeof item === "undefined") return item;
                    if (typeof item === "number") return Number.isFinite(item) ? item : null;
                    if (typeof item !== "object") throw new TypeError("Unsupported userscript storage value.");
                    if (ancestors.has(item)) throw new TypeError("Circular userscript storage value.");
                    ancestors.add(item);
                    let result;
                    if (Array.isArray(item)) {
                        result = Array.from({ length: item.length }, (_, index) => {
                            const value = normalize(item[index]);
                            return typeof value === "undefined" ? null : value;
                        });
                    } else {
                        result = Object.create(null);
                        Object.keys(item).forEach(key => {
                            const value = normalize(item[key]);
                            if (typeof value !== "undefined") result[key] = value;
                        });
                    }
                    ancestors.delete(item);
                    return result;
                };
                switch (typeof value) {
                    case "undefined": return "u";
                    case "string": return "s" + value;
                    case "boolean": return "b" + value;
                    case "number": return "n" + String(value);
                    case "object": return "o" + JSON.stringify(normalize(value));
                    default: throw new TypeError("Unsupported userscript storage value.");
                }
            };

            const decodeStoredValue = function (value) {
                switch (value[0]) {
                    case "u": return undefined;
                    case "s": return value.slice(1);
                    case "b": return value.slice(1) === "true";
                    case "n": return Number(value.slice(1));
                    case "o": return JSON.parse(value.slice(1));
                    default: throw new TypeError("Invalid userscript storage encoding.");
                }
            };

            const decodeStorageValue = (data, name) => data[name + "Exists"] ? decodeStoredValue(data[name]) : undefined;

            const reportMutationFailure = async function (record, method, error) {
                try {
                    const snapshot = await call(record, "runtime.getStorageSnapshot", {});
                    if (snapshot && snapshot.complete && snapshot.entries && record.storageMirror) {
                        record.storageMirror = storageRecord(snapshot.entries);
                        record.pendingStorageMutations.forEach(mutation => {
                            if (!mutation.confirmed) applyStorageMutation(record, mutation);
                        });
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

            const enqueueMutation = function (record, method, parameters, storageMutation) {
                const mutations = Array.isArray(storageMutation) ? storageMutation : (storageMutation ? [storageMutation] : []);
                const requestId = uuid();
                mutations.forEach(mutation => { mutation.mutationId = requestId; });
                const operation = record.mutationTail
                    .then(() => call(record, method, parameters, undefined, requestId))
                    .then(result => {
                        mutations.forEach(mutation => removePendingStorageMutation(record, mutation));
                        return result;
                    }, error => {
                        mutations.forEach(mutation => removePendingStorageMutation(record, mutation));
                        mutations.slice().reverse().forEach(mutation => {
                            if (!mutation.confirmed) rollbackStorageMutation(record, mutation);
                        });
                        throw error;
                    });
                record.mutationTail = operation.catch(error => {
                    return reportMutationFailure(record, method, error);
                });
                return operation;
            };

            const applyStorageMutation = function (record, mutation) {
                const existed = Object.prototype.hasOwnProperty.call(record.storageMirror, mutation.key);
                mutation.oldValue = existed ? record.storageMirror[mutation.key] : undefined;
                if (mutation.operation === "set") {
                    record.storageMirror[mutation.key] = mutation.value;
                } else {
                    if (existed) delete record.storageMirror[mutation.key];
                }
                mutation.existed = existed;
            };

            const beginStorageMutation = function (record, operation, key, value) {
                const mutation = { operation: operation, key: key, value: value };
                applyStorageMutation(record, mutation);
                record.pendingStorageMutations.push(mutation);
                return mutation;
            };

            const removePendingStorageMutation = function (record, mutation) {
                const index = record.pendingStorageMutations.indexOf(mutation);
                if (index >= 0) record.pendingStorageMutations.splice(index, 1);
            };

            const rollbackStorageMutation = function (record, mutation) {
                if (!mutation.existed) delete record.storageMirror[mutation.key];
                else record.storageMirror[mutation.key] = mutation.oldValue;
                record.pendingStorageMutations.forEach(pending => {
                    if (!pending.confirmed && pending.key === mutation.key) applyStorageMutation(record, pending);
                });
            };

            const requireStorageKey = function (key) {
                if (typeof key !== "string") throw new TypeError("key must be a string.");
                return key;
            };

            const storageKeys = function (keys) {
                if (!Array.isArray(keys)) throw new TypeError("keys must be an array of strings.");
                return Array.from(new Set(Array.from(keys, requireStorageKey)));
            };

            const storageValues = function (values) {
                if (!values || typeof values !== "object" || Array.isArray(values)) throw new TypeError("values must be an object.");
                const result = Object.create(null);
                Object.keys(values).forEach(key => { result[key] = encodeStoredValue(values[key]); });
                return result;
            };

            const storageSelection = function (keysOrDefaults) {
                if (keysOrDefaults == null) return { keys: null };
                if (Array.isArray(keysOrDefaults)) return { keys: storageKeys(keysOrDefaults) };
                if (typeof keysOrDefaults !== "object") throw new TypeError("keysOrDefaults must be an array or object.");
                const keys = Object.keys(keysOrDefaults);
                const defaults = Object.create(null);
                keys.forEach(key => { defaults[key] = keysOrDefaults[key]; });
                return { keys, defaults };
            };

            const readStorageValues = function (values, selection) {
                const result = Object.create(null);
                (selection.keys || Object.keys(values)).forEach(key => {
                    if (Object.prototype.hasOwnProperty.call(values, key)) result[key] = decodeStoredValue(values[key]);
                    else if (selection.defaults && Object.prototype.hasOwnProperty.call(selection.defaults, key)) result[key] = selection.defaults[key];
                });
                return result;
            };

            const registerValueListener = function (record, key, callback) {
                requireStorageKey(key);
                if (typeof callback !== "function") throw new TypeError("callback must be a function.");
                const listenerId = nextListenerId++;
                record.handlers.set(listenerId, callback);
                const registered = enqueueMutation(record, "GM.addValueChangeListener", { listenerId, key }).catch(error => {
                    record.handlers.delete(listenerId);
                    throw error;
                });
                return { listenerId, registered };
            };

            const forgetValueListener = function (record, listenerId) {
                record.handlers.delete(listenerId);
                record.valueChangeSequences.delete(listenerId);
            };

            const requireListenerId = function (listenerId) {
                if (!Number.isInteger(listenerId) || listenerId <= 0) throw new TypeError("listenerId must be a positive integer.");
            };

            const writeStorageValues = function (record, method, parameters) {
                if (!record.storageMirror) return enqueueMutation(record, method, parameters);
                const set = method === "GM.setValues";
                const keys = set ? Object.keys(parameters.values) : parameters.keys;
                const mutations = keys.map(key => beginStorageMutation(
                    record, set ? "set" : "delete", key, set ? parameters.values[key] : undefined));
                return enqueueMutation(record, method, parameters, mutations);
            };

            const addStorageApi = function (record, enabled, api) {
                if (enabled("GM.getValue")) api.getValue = async function (key, defaultValue) {
                    requireStorageKey(key);
                    if (record.storageMirror) return Object.prototype.hasOwnProperty.call(record.storageMirror, key)
                        ? decodeStoredValue(record.storageMirror[key]) : defaultValue;
                    const stored = await call(record, "GM.getValue", { key });
                    return stored.exists ? decodeStoredValue(stored.value) : defaultValue;
                };
                if (enabled("GM.setValue")) api.setValue = async function (key, value) {
                    requireStorageKey(key);
                    const encoded = encodeStoredValue(value);
                    const mutation = record.storageMirror ? beginStorageMutation(record, "set", key, encoded) : null;
                    await enqueueMutation(record, "GM.setValue", { key, value: encoded }, mutation);
                };
                if (enabled("GM.deleteValue")) api.deleteValue = async function (key) {
                    requireStorageKey(key);
                    const mutation = record.storageMirror ? beginStorageMutation(record, "delete", key) : null;
                    await enqueueMutation(record, "GM.deleteValue", { key }, mutation);
                };
                if (enabled("GM.listValues")) api.listValues = async () => record.storageMirror
                    ? Object.keys(record.storageMirror)
                    : Object.keys(storageRecord((await call(record, "GM.listValues", {})).map(key => [key, true])));
                if (enabled("GM.getValues")) api.getValues = async function (keysOrDefaults) {
                    const selection = storageSelection(keysOrDefaults);
                    const values = record.storageMirror || await call(record, "GM.getValues", { keys: selection.keys });
                    return readStorageValues(values, selection);
                };
                if (enabled("GM.setValues")) api.setValues = async function (values) {
                    await writeStorageValues(record, "GM.setValues", { values: storageValues(values) });
                };
                if (enabled("GM.deleteValues")) api.deleteValues = async function (keys) {
                    await writeStorageValues(record, "GM.deleteValues", { keys: storageKeys(keys) });
                };
                if (enabled("GM.addValueChangeListener")) api.addValueChangeListener = async function (key, callback) {
                    const registration = registerValueListener(record, key, callback);
                    await registration.registered;
                    return registration.listenerId;
                };
                if (enabled("GM.removeValueChangeListener")) api.removeValueChangeListener = async function (listenerId) {
                    requireListenerId(listenerId);
                    await enqueueMutation(record, "GM.removeValueChangeListener", { listenerId });
                    forgetValueListener(record, listenerId);
                };
            };

            const addLegacyStorage = function (record, api, facade) {
                if (api.addValueChangeListener) facade.GM_addValueChangeListener = function (key, callback) {
                    const registration = registerValueListener(record, key, callback);
                    registration.registered.catch(error => console.error("[MonkeySharp] listener registration failed", error));
                    return registration.listenerId;
                };
                if (api.removeValueChangeListener) facade.GM_removeValueChangeListener = function (listenerId) {
                    requireListenerId(listenerId);
                    forgetValueListener(record, listenerId);
                    enqueueMutation(record, "GM.removeValueChangeListener", { listenerId });
                };
                if (record.storageMirror) {
                    if (api.getValues) facade.GM_getValues = keysOrDefaults => readStorageValues(record.storageMirror, storageSelection(keysOrDefaults));
                    if (api.setValues) facade.GM_setValues = function (values) {
                        writeStorageValues(record, "GM.setValues", { values: storageValues(values) });
                    };
                    if (api.deleteValues) facade.GM_deleteValues = function (keys) {
                        writeStorageValues(record, "GM.deleteValues", { keys: storageKeys(keys) });
                    };
                    if (api.getValue) facade.GM_getValue = function (key, defaultValue) {
                        requireStorageKey(key);
                        return Object.prototype.hasOwnProperty.call(record.storageMirror, key)
                            ? decodeStoredValue(record.storageMirror[key]) : defaultValue;
                    };
                    if (api.setValue) facade.GM_setValue = function (key, value) {
                        requireStorageKey(key);
                        const encoded = encodeStoredValue(value);
                        const mutation = beginStorageMutation(record, "set", key, encoded);
                        enqueueMutation(record, "GM.setValue", { key, value: encoded }, mutation);
                    };
                    if (api.deleteValue) facade.GM_deleteValue = function (key) {
                        requireStorageKey(key);
                        const mutation = beginStorageMutation(record, "delete", key);
                        enqueueMutation(record, "GM.deleteValue", { key }, mutation);
                    };
                    if (api.listValues) facade.GM_listValues = () => Object.keys(record.storageMirror);
                }
            };
