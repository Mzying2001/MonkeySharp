            const storageRecord = entries => Object.assign(Object.create(null), Object.fromEntries(entries));

            const loadStorageSnapshot = async function (record, snapshot) {
                if (snapshot && Number.isInteger(snapshot.chunkCount) && snapshot.chunkCount > 0) {
                    const chunks = [];
                    for (let index = 0; index < snapshot.chunkCount; index += 1) {
                        const part = await call(record, "runtime.getStorageChunk", { snapshotId: snapshot.snapshotId, index });
                        if (!part || part.index !== index || part.snapshotId !== snapshot.snapshotId || typeof part.chunk !== "string") throw new Error("Incomplete storage snapshot.");
                        chunks.push(part.chunk);
                    }
                    const json = chunks.join("");
                    if (json.length !== snapshot.jsonLength) throw new Error("Truncated storage snapshot.");
                    snapshot = JSON.parse(json);
                }
                if (!snapshot || snapshot.complete !== true || !Number.isSafeInteger(snapshot.sequence) || snapshot.sequence < 0 ||
                    !Array.isArray(snapshot.entries) || snapshot.entries.some(entry => !Array.isArray(entry) || entry.length !== 2 || typeof entry[0] !== "string" || typeof entry[1] !== "string") ||
                    new Set(snapshot.entries.map(entry => entry[0])).size !== snapshot.entries.length) throw new Error("A complete atomic storage snapshot is required before running this script.");
                if (record.disposed) throw new Error("The script execution ended.");
                snapshot.entries.forEach(entry => decodeStoredValue(entry[1]));
                record.storageConfirmed = storageRecord(snapshot.entries);
                rebuildStorageMirror(record);
                record.lastStorageSequence = snapshot.sequence;
            };

            const applyStorageSync = function (record, data) {
                if (data.sequence <= record.lastStorageSequence) return;
                if (data.sequence !== record.lastStorageSequence + 1) throw new Error("Storage change sequence has a gap.");
                if (data.newValueExists) record.storageConfirmed[data.key] = data.newValue;
                else delete record.storageConfirmed[data.key];
                record.pendingStorageMutations.forEach(mutation => {
                    if (data.originExecutionId === record.executionId && mutation.mutationId === data.mutationId && mutation.key === data.key) mutation.confirmed = true;
                });
                record.lastStorageSequence = data.sequence;
                rebuildStorageMirror(record);
                notifyValueListeners(record, data, false);
            };

            const prepareStorage = async function (record, initialSnapshot) {
                record.storageReady = false;
                if (initialSnapshot) await loadStorageSnapshot(record, initialSnapshot);
                for (let attempt = 0; attempt < 3; attempt += 1) {
                    const replay = await call(record, "runtime.getStorageChanges", { afterSequence: record.lastStorageSequence });
                    if (record.disposed) throw new Error("The script execution ended.");
                    if (replay && replay.complete === true && Array.isArray(replay.changes)) {
                        replay.changes.forEach(data => applyStorageSync(record, data));
                        if (record.lastStorageSequence !== replay.sequence) throw new Error("Incomplete storage change replay.");
                        record.storageReady = true;
                        const queued = record.storageNotifications.splice(0);
                        queued.sort((left, right) => left.data.sequence - right.data.sequence).forEach(notification => receive(notification));
                        return;
                    }
                    await loadStorageSnapshot(record, await call(record, "runtime.getStorageSnapshot", {}));
                }
                throw new Error("Storage replay repeatedly exceeded retention; retry this execution.");
            };

            const resynchronizeStorage = function (record, freshSnapshot) {
                if (!record.storageResync) record.storageResync = (async function () {
                    const snapshot = freshSnapshot ? await call(record, "runtime.getStorageSnapshot", {}) : null;
                    await prepareStorage(record, snapshot);
                })().catch(error => {
                    if (record.disposed) return;
                    console.error("[MonkeySharp] storage resynchronization failed", error);
                    throw error;
                }).finally(() => {
                    record.storageResync = null;
                    if (record.storageReady && record.storageNotifications.length) resynchronizeStorage(record).catch(() => {});
                });
                return record.storageResync;
            };

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
                    await resynchronizeStorage(record, true);
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

            const rebuildStorageMirror = function (record) {
                record.storageMirror = storageRecord(Object.entries(record.storageConfirmed || {}));
                record.pendingStorageMutations.forEach(mutation => { if (!mutation.confirmed) applyStorageMutation(record, mutation); });
            };

            const notifyValueListeners = function (record, data, local) {
                if (!local && data.originExecutionId === record.executionId) return;
                if (data.oldValueExists === data.newValueExists && (!data.oldValueExists || data.oldValue === data.newValue)) return;
                record.handlers.forEach((callback, listenerId) => {
                    if (record.valueListenerKeys.get(listenerId) !== data.key || data.listenerId > 0 && data.listenerId !== listenerId) return;
                    if (data.sequence && data.sequence <= (record.valueChangeSequences.get(listenerId) || 0)) return;
                    if (data.sequence) record.valueChangeSequences.set(listenerId, data.sequence);
                    try { callback(data.key, decodeStorageValue(data, "oldValue"), decodeStorageValue(data, "newValue"), !local); }
                    catch (error) {
                        console.error("[MonkeySharp] value change listener failed", error);
                        call(record, "runtime.reportError", { message: String(error && error.message || error) }).catch(() => {});
                    }
                });
            };

            const enqueueMutation = function (record, method, parameters, storageMutation) {
                if (record.disposed) return Promise.reject(new Error("The script execution ended."));
                const mutations = Array.isArray(storageMutation) ? storageMutation : (storageMutation ? [storageMutation] : []);
                const requestId = uuid();
                mutations.forEach(mutation => { mutation.mutationId = requestId; });
                const operation = record.mutationTail.then(async () => {
                    if (record.disposed) throw new Error("The script execution ended.");
                    const result = await call(record, method, parameters, undefined, requestId);
                    if (record.disposed) throw new Error("The script execution ended.");
                    if (mutations.length) {
                        if (!result || !Number.isSafeInteger(result.sequence) || result.sequence < 0) throw new Error("Invalid storage commit acknowledgement.");
                        if (result.sequence > record.lastStorageSequence) await resynchronizeStorage(record);
                    }
                    mutations.forEach(mutation => removePendingStorageMutation(record, mutation));
                    if (mutations.length) rebuildStorageMirror(record);
                    return result;
                }).catch(error => {
                    mutations.forEach(mutation => removePendingStorageMutation(record, mutation));
                    if (mutations.length) rebuildStorageMirror(record);
                    throw error;
                });
                record.mutationTail = operation.catch(error => record.disposed ? undefined : reportMutationFailure(record, method, error));
                mutations.forEach(mutation => notifyValueListeners(record, { key: mutation.key,
                    oldValueExists: mutation.existed, oldValue: mutation.oldValue,
                    newValueExists: mutation.operation === "set", newValue: mutation.value }, true));
                return operation;
            };

            const applyStorageMutation = function (record, mutation) {
                if (mutation.operation === "set") record.storageMirror[mutation.key] = mutation.value;
                else delete record.storageMirror[mutation.key];
            };

            const beginStorageMutation = function (record, operation, key, value) {
                if (record.disposed) throw new Error("The script execution ended.");
                const existed = Object.prototype.hasOwnProperty.call(record.storageMirror, key);
                const mutation = { operation, key, value, existed, oldValue: existed ? record.storageMirror[key] : undefined };
                record.pendingStorageMutations.push(mutation);
                applyStorageMutation(record, mutation);
                return mutation;
            };

            const removePendingStorageMutation = function (record, mutation) {
                const index = record.pendingStorageMutations.indexOf(mutation);
                if (index >= 0) record.pendingStorageMutations.splice(index, 1);
            };

            const requireStorageKey = function (key, record) {
                if (record && record.disposed) throw new Error("The script execution ended.");
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
                requireStorageKey(key, record);
                if (typeof callback !== "function") throw new TypeError("callback must be a function.");
                const listenerId = nextListenerId++;
                record.handlers.set(listenerId, callback);
                record.valueListenerKeys.set(listenerId, key);
                const registered = enqueueMutation(record, "GM.addValueChangeListener", { listenerId, key }).catch(error => {
                    forgetValueListener(record, listenerId);
                    throw error;
                });
                return { listenerId, registered };
            };

            const forgetValueListener = function (record, listenerId) {
                record.handlers.delete(listenerId);
                record.valueListenerKeys.delete(listenerId);
                record.valueChangeSequences.delete(listenerId);
            };

            const requireListenerId = function (listenerId) {
                if (!Number.isInteger(listenerId) || listenerId <= 0) throw new TypeError("listenerId must be a positive integer.");
            };

            const writeStorageValues = function (record, method, parameters) {
                const set = method === "GM.setValues";
                const keys = set ? Object.keys(parameters.values) : parameters.keys;
                const mutations = keys.map(key => beginStorageMutation(
                    record, set ? "set" : "delete", key, set ? parameters.values[key] : undefined));
                return enqueueMutation(record, method, parameters, mutations);
            };

            const addStorageApi = function (record, enabled, api) {
                if (enabled("GM.getValue")) api.getValue = async function (key, defaultValue) {
                    requireStorageKey(key, record);
                    return Object.prototype.hasOwnProperty.call(record.storageMirror, key) ? decodeStoredValue(record.storageMirror[key]) : defaultValue;
                };
                if (enabled("GM.setValue")) api.setValue = async function (key, value) {
                    requireStorageKey(key, record);
                    const encoded = encodeStoredValue(value);
                    const mutation = beginStorageMutation(record, "set", key, encoded);
                    await enqueueMutation(record, "GM.setValue", { key, value: encoded }, mutation);
                };
                if (enabled("GM.deleteValue")) api.deleteValue = async function (key) {
                    requireStorageKey(key, record);
                    const mutation = beginStorageMutation(record, "delete", key);
                    await enqueueMutation(record, "GM.deleteValue", { key }, mutation);
                };
                if (enabled("GM.listValues")) api.listValues = async () => { if (record.disposed) throw new Error("The script execution ended."); return Object.keys(record.storageMirror); };
                if (enabled("GM.getValues")) api.getValues = async function (keysOrDefaults) {
                    if (record.disposed) throw new Error("The script execution ended.");
                    const selection = storageSelection(keysOrDefaults);
                    const values = record.storageMirror;
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
                    forgetValueListener(record, listenerId);
                    await enqueueMutation(record, "GM.removeValueChangeListener", { listenerId });
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
                if (api.getValues) facade.GM_getValues = keysOrDefaults => { if (record.disposed) throw new Error("The script execution ended."); return readStorageValues(record.storageMirror, storageSelection(keysOrDefaults)); };
                if (api.setValues) facade.GM_setValues = function (values) {
                    writeStorageValues(record, "GM.setValues", { values: storageValues(values) });
                };
                if (api.deleteValues) facade.GM_deleteValues = function (keys) {
                    writeStorageValues(record, "GM.deleteValues", { keys: storageKeys(keys) });
                };
                if (api.getValue) facade.GM_getValue = function (key, defaultValue) {
                    requireStorageKey(key, record);
                    return Object.prototype.hasOwnProperty.call(record.storageMirror, key)
                        ? decodeStoredValue(record.storageMirror[key]) : defaultValue;
                };
                if (api.setValue) facade.GM_setValue = function (key, value) {
                    requireStorageKey(key, record);
                    const encoded = encodeStoredValue(value);
                    const mutation = beginStorageMutation(record, "set", key, encoded);
                    enqueueMutation(record, "GM.setValue", { key, value: encoded }, mutation);
                };
                if (api.deleteValue) facade.GM_deleteValue = function (key) {
                    requireStorageKey(key, record);
                    const mutation = beginStorageMutation(record, "delete", key);
                    enqueueMutation(record, "GM.deleteValue", { key }, mutation);
                };
                if (api.listValues) facade.GM_listValues = () => { if (record.disposed) throw new Error("The script execution ended."); return Object.keys(record.storageMirror); };
            };
