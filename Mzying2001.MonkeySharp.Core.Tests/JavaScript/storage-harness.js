const completePlan = require("./test-plan");
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const template = require("./bootstrap-template");
const undefinedValue = { $monkeySharpType: "undefined" };
const fixtureValue = value => typeof value === "undefined" ? "u" : value === null || typeof value === "object"
    ? "o" + JSON.stringify(value) : ({ string: "s", number: "n", boolean: "b" })[typeof value] + String(value);
const methods = ["GM.getValue", "GM.setValue", "GM.deleteValue", "GM.listValues",
    "GM.getValues", "GM.setValues", "GM.deleteValues", "GM.addValueChangeListener", "GM.removeValueChangeListener"];
let nextExecution = 0;

async function waitFor(predicate) {
    const deadline = Date.now() + 2000;
    while (!predicate()) {
        if (Date.now() > deadline) throw new Error("Timed out waiting for bulk storage.");
        await new Promise(resolve => setTimeout(resolve, 5));
    }
}

async function install(options = {}) {
    const executionId = "bulk-storage-" + ++nextExecution;
    const values = Object.assign(Object.create(null), Object.fromEntries(Object.entries(options.values || {}).map(([key, value]) => [key, fixtureValue(value)])));
    const entries = () => Object.entries(values);
    const requests = [];
    const listeners = new Map();
    const notifications = [];
    let sequence = 0;
    const journal = [];
    const failOnKeys = options.failOnKeys || (options.failOnKey === undefined ? [] : [options.failOnKey]);
    const deliver = notification => globalThis.__MonkeySharpRuntime.receive(notification);
    const changed = (request, key, oldValue, newValue, oldValueExists, newValueExists) => {
        const data = { key, oldValue, newValue, originExecutionId: request.originExecutionId || executionId,
            mutationId: request.requestId, mutationMethod: request.method, sequence: ++sequence,
            oldValueExists, newValueExists };
        journal.push(data);
        const notification = { type: "notification", protocol: 2, executionId,
            deliveryToken: executionId, event: "storage-sync", data };
        if (options.deferNotifications) notifications.push(notification);
        else deliver(notification);
        listeners.forEach((listenerKey, listenerId) => {
            if (key === listenerKey) {
                const event = { ...notification, event: "value-change", data: { ...data, listenerId, remote: true } };
                if (options.deferNotifications) notifications.push(event); else deliver(event);
            }
        });
    };
    globalThis.__MonkeySharpBridge = {
        dispatch: async requestJson => {
            const request = JSON.parse(requestJson);
            const response = result => JSON.stringify({ type: "response", protocol: 2,
                requestId: request.requestId, ok: true, result });
            const snapshot = { complete: true, sequence, entries: entries() };
            const json = JSON.stringify(snapshot);
            if (request.method === "runtime.getStorageChunk") return response({ index: request.params.index,
                snapshotId: executionId, chunk: json.slice(request.params.index * 23, (request.params.index + 1) * 23) });
            if (request.method === "runtime.getStorageChanges") return response({ complete: true, sequence, changes: journal.filter(change => change.sequence > request.params.afterSequence) });
            if (request.type === "hello") return JSON.stringify({
                type: "hello-result", protocol: 2, ok: true, limits: {}, apis: methods,
                compatibility: { storage: options.chunked ? { complete: false, sequence, snapshotId: executionId, chunkCount: Math.ceil(json.length / 23), jsonLength: json.length } : snapshot }
            });
            requests.push(request);
            const parameters = request.params;
            if (request.method === "runtime.getStorageSnapshot") return response(snapshot);
            if (request.method === "GM.addValueChangeListener") {
                if (options.failRegistration) return JSON.stringify({ type: "response", protocol: 2, requestId: request.requestId,
                    ok: false, error: { code: "MSP999_INTERNAL", message: "Registration failed" } });
                listeners.set(parameters.listenerId, parameters.key);
            }
            if (request.method === "GM.removeValueChangeListener") listeners.delete(parameters.listenerId);
            if (request.method === "GM.listValues") return response(Object.keys(values));
            if (request.method === "GM.getValues") {
                if (options.beforeRead) await options.beforeRead();
                const result = Object.create(null);
                (parameters.keys || Object.keys(values)).forEach(key => {
                    if (Object.hasOwn(values, key)) result[key] = values[key];
                });
                return response(result);
            }
            if (request.method === "GM.getValue") return response({ exists: Object.hasOwn(values, parameters.key),
                value: values[parameters.key] ?? null });
            if (["GM.setValues", "GM.deleteValues", "GM.setValue", "GM.deleteValue"].includes(request.method)) {
                if (options.beforeMutation) await options.beforeMutation(request);
                const set = request.method === "GM.setValue" || request.method === "GM.setValues";
                const bulk = request.method.endsWith("Values");
                const keys = bulk ? (set ? Object.keys(parameters.values) : parameters.keys) : [parameters.key];
                if (keys.includes(failOnKeys[0])) {
                    failOnKeys.shift();
                    return JSON.stringify({ type: "response", protocol: 2, requestId: request.requestId,
                        ok: false, error: { code: "MSP999_INTERNAL", message: "Simulated atomic batch failure" } });
                }
                for (const key of keys) {
                    const existed = Object.hasOwn(values, key);
                    const oldValue = existed ? values[key] : null;
                    if (set) values[key] = bulk ? parameters.values[key] : parameters.value;
                    else delete values[key];
                    if (set ? !existed || oldValue !== values[key] : existed) changed(request, key, oldValue, set ? values[key] : null, existed, set);
                }
            }
            return response(["GM.setValue", "GM.deleteValue", "GM.setValues", "GM.deleteValues"].includes(request.method) ? { sequence } : undefinedValue);
        }
    };
    const payload = { protocol: 2, documentId: executionId, frameId: "main", runAt: "DocumentEnd", invocations: [{
        executionId, scriptKey: "99999999-9999-9999-9999-999999999999", capability: executionId,
        deliveryToken: executionId, ...(options.declaredGrants ? { declaredGrants: options.declaredGrants } : {}), grants: options.grants || methods, info: {},
        source: `globalThis.__bulkExposure = { id: ${JSON.stringify(executionId)}, api: GM,
            get: GM_getValues, set: GM_setValues, delete: GM_deleteValues,
            single: { get: GM_getValue, set: GM_setValue, delete: GM_deleteValue, list: GM_listValues,
                add: GM_addValueChangeListener, remove: GM_removeValueChangeListener } };` + (options.source || "")
    }] };
    vm.runInThisContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", Buffer.from(JSON.stringify(completePlan(payload))).toString("base64")));
    await waitFor(() => globalThis.__bulkExposure && globalThis.__bulkExposure.id === executionId);
    return { ...globalThis.__bulkExposure, values, requests, notifications, deliver, executionId,
        externalSet(key, value) {
            const oldValueExists = Object.hasOwn(values, key), oldValue = values[key];
            values[key] = fixtureValue(value);
            changed({ requestId: "external-" + (sequence + 1), method: "host", originExecutionId: "another-execution" }, key, oldValue, values[key], oldValueExists, true);
        }
    };
}


module.exports = { install, waitFor, fixtureValue };
