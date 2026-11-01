const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const template = fs.readFileSync(path.resolve(__dirname, "../../Mzying2001.MonkeySharp.Core/Js/Init.js"), "utf8");
const undefinedValue = { $monkeySharpType: "undefined" };
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
    const values = Object.assign(Object.create(null), options.values || {});
    const requests = [];
    const listeners = new Map();
    const notifications = [];
    let sequence = 0;
    let failOnKey = options.failOnKey;
    const deliver = notification => globalThis.__MonkeySharpRuntime.receive(notification);
    const changed = (request, key, oldValue, newValue, oldValueExists, newValueExists) => {
        const data = { key, oldValue, newValue, originExecutionId: executionId,
            mutationId: request.requestId, mutationMethod: request.method, sequence: ++sequence,
            oldValueExists, newValueExists };
        const notification = { type: "notification", protocol: 1, executionId,
            deliveryToken: executionId, event: "storage-sync", data };
        if (options.deferNotifications) notifications.push(notification);
        else deliver(notification);
        listeners.forEach((listenerKey, listenerId) => {
            if (key === listenerKey) deliver({ ...notification, event: "value-change", data: { ...data, listenerId, remote: true } });
        });
    };
    globalThis.__MonkeySharpBridge = {
        dispatch: async requestJson => {
            const request = JSON.parse(requestJson);
            const response = result => JSON.stringify({ type: "response", protocol: 1,
                requestId: request.requestId, ok: true, result });
            if (request.type === "hello") return JSON.stringify({
                type: "hello-result", protocol: 1, ok: true, limits: {}, apis: options.grants || methods,
                compatibility: { storage: { complete: options.mirror !== false, values } }
            });
            requests.push(request);
            const parameters = request.params;
            if (request.method === "runtime.getStorageSnapshot") return response({ complete: true, values });
            if (request.method === "GM.addValueChangeListener") listeners.set(parameters.listenerId, parameters.key);
            if (request.method === "GM.getValues") {
                const result = Object.create(null);
                (parameters.keys || Object.keys(values)).forEach(key => {
                    if (Object.hasOwn(values, key)) result[key] = values[key];
                    else if (parameters.defaults && Object.hasOwn(parameters.defaults, key)) result[key] = parameters.defaults[key];
                });
                return response(result);
            }
            if (request.method === "GM.getValue") return response(Object.hasOwn(values, parameters.key)
                ? values[parameters.key] : (parameters.defaultValue ?? undefinedValue));
            if (["GM.setValues", "GM.deleteValues", "GM.setValue", "GM.deleteValue"].includes(request.method)) {
                const set = request.method === "GM.setValue" || request.method === "GM.setValues";
                const bulk = request.method.endsWith("Values");
                const keys = bulk ? (set ? Object.keys(parameters.values) : parameters.keys) : [parameters.key];
                for (const key of keys) {
                    if (failOnKey === key) {
                        failOnKey = null;
                        return JSON.stringify({ type: "response", protocol: 1, requestId: request.requestId,
                            ok: false, error: { code: "MSP999_INTERNAL", message: "Simulated partial failure" } });
                    }
                    const existed = Object.hasOwn(values, key);
                    const oldValue = existed ? values[key] : undefinedValue;
                    if (set) values[key] = bulk ? parameters.values[key] : parameters.value;
                    else delete values[key];
                    if (set || existed) changed(request, key, oldValue, set ? values[key] : undefinedValue, existed, set);
                }
            }
            return response(undefinedValue);
        }
    };
    const payload = { protocol: 1, documentId: executionId, frameId: "main", runAt: "DocumentEnd", invocations: [{
        executionId, scriptKey: "99999999-9999-9999-9999-999999999999", capability: executionId,
        deliveryToken: executionId, grants: options.grants || methods, info: {},
        compatibility: { profile: options.strict ? "ModernStrict" : "LegacyCompatible", strict: Boolean(options.strict),
            legacyGlobals: !options.strict && options.legacyGlobals !== false },
        source: `globalThis.__bulkExposure = { id: ${JSON.stringify(executionId)}, api: GM,
            get: GM_getValues, set: GM_setValues, delete: GM_deleteValues };`
    }] };
    vm.runInThisContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", Buffer.from(JSON.stringify(payload)).toString("base64")));
    await waitFor(() => globalThis.__bulkExposure && globalThis.__bulkExposure.id === executionId);
    return { ...globalThis.__bulkExposure, values, requests, notifications, deliver, executionId };
}

for (const mirror of [true, false]) {
    test(`bulk JSON values cannot be confused with the bridge sentinel (mirror=${mirror})`, async () => {
        const { api } = await install({ mirror });
        const changes = [];
        await api.addValueChangeListener("key", (...args) => changes.push(args));
        await api.setValues({ key: { $monkeySharpType: "undefined" } });
        assert.deepEqual(await api.getValues(["key"]), { key: { $monkeySharpType: "undefined" } });
        await api.deleteValues(["key"]);
        assert.deepEqual(changes, [["key", undefined, { $monkeySharpType: "undefined" }, false],
            ["key", { $monkeySharpType: "undefined" }, undefined, false]]);
    });

    test(`bulk reads select keys, defaults, or all JSON values (mirror=${mirror})`, async () => {
        const initial = JSON.parse('{"nullable":null,"nested":{"items":[1,true]},"__proto__":{"safe":true},"constructor":42}');
        const { api } = await install({ values: initial, mirror });
        assert.deepEqual(await api.getValues(["nullable", "missing", "nested", "nested"]),
            { nullable: null, nested: { items: [1, true] } });
        assert.deepEqual(await api.getValues({ nullable: "unused", missing: { enabled: false }, constructor: undefined }),
            { nullable: null, missing: { enabled: false }, constructor: 42 });
        assert.deepEqual(await api.getValues({ absent: undefined }), {});
        for (const argument of [undefined, null]) assert.deepEqual(await api.getValues(argument), initial);
        assert.deepEqual(await api.getValues(), initial);
        assert.deepEqual(await api.getValues([]), {});
        assert.deepEqual(await api.getValues({}), {});
        const all = await api.getValues();
        assert.equal(Object.getPrototypeOf(all), Object.prototype);
        assert.equal(Object.hasOwn(all, "__proto__"), true);
        all.nested.items.push(3);
        assert.deepEqual((await api.getValues(["nested"])).nested.items, [1, true]);
        await api.setValues({ $monkeySharpType: "undefined" });
        assert.deepEqual(await api.getValues(["$monkeySharpType"]), { $monkeySharpType: "undefined" });
    });

    test(`modern bulk mutations use their own grants and one request per batch (mirror=${mirror})`, async () => {
        const { api, requests, values } = await install({ mirror,
            grants: ["GM.getValues", "GM.setValues", "GM.deleteValues"] });
        assert.equal(api.getValue, undefined);
        assert.equal(api.setValue, undefined);
        const input = JSON.parse('{"first":{"count":1},"__proto__":"ordinary","constructor":null}');
        const writing = api.setValues(input);
        assert.equal(typeof writing.then, "function");
        input.first.count = 99;
        assert.equal(await writing, undefined);
        assert.deepEqual(await api.getValues(["first", "__proto__", "constructor"]),
            JSON.parse('{"first":{"count":1},"__proto__":"ordinary","constructor":null}'));
        assert.equal(await api.deleteValues(["first", "first", "absent"]), undefined);
        assert.equal(Object.hasOwn(values, "first"), false);
        assert.equal(requests.filter(request => request.method === "GM.setValues").length, 1);
        assert.equal(requests.filter(request => request.method === "GM.deleteValues").length, 1);
        assert.deepEqual(requests.find(request => request.method === "GM.deleteValues").params.keys, ["first", "absent"]);
    });
}

test("legacy bulk APIs mutate synchronously and share the single-value commit queue", async () => {
    const { api, get, set, delete: remove, requests } = await install({ values: { first: 1 } });
    const changes = [];
    await api.addValueChangeListener("first", (...args) => changes.push(args));
    assert.equal(set({ first: 2, temporary: null }), undefined);
    assert.deepEqual(get(["first", "temporary"]), { first: 2, temporary: null });
    const single = api.setValue("first", 3);
    assert.equal(remove(["temporary", "temporary"]), undefined);
    assert.deepEqual(get(), { first: 3 });
    assert.deepEqual(await api.getValues(), { first: 3 });
    await single;
    await waitFor(() => requests.some(request => request.method === "GM.deleteValues"));
    assert.deepEqual(requests.filter(request => ["GM.setValues", "GM.setValue", "GM.deleteValues"].includes(request.method))
        .map(request => request.method), ["GM.setValues", "GM.setValue", "GM.deleteValues"]);
    assert.deepEqual(changes, [["first", 1, 2, false], ["first", 2, 3, false]]);
});

test("invalid bulk inputs fail before any optimistic or durable mutation", async () => {
    const { api, get, set, delete: remove, requests } = await install({ values: { first: 1 } });
    const circular = {}; circular.self = circular;
    for (const input of [null, [], 2, { first: 2, "": 3 }, { first: 2, bad: undefined },
        { first: 2, bad: () => {} }, { first: 2, bad: Symbol() }, { first: 2, bad: 1n }, { first: 2, bad: circular }]) {
        assert.throws(() => set(input), TypeError);
        await assert.rejects(api.setValues(input), TypeError);
    }
    for (const input of [null, {}, "first", ["first", ""], ["first", 2], ["first", , "last"]]) {
        assert.throws(() => remove(input), TypeError);
        await assert.rejects(api.deleteValues(input), TypeError);
    }
    for (const input of [42, "first", ["first", null], { "": 1 }]) {
        assert.throws(() => get(input), TypeError);
        await assert.rejects(api.getValues(input), TypeError);
    }
    assert.deepEqual(get(), { first: 1 });
    assert.equal(requests.length, 0);
});

for (const operation of ["set", "delete"]) {
    test(`partial bulk ${operation} failure keeps committed events and replays queued mutations`, async () => {
        const harness = await install({ values: { first: 1, fail: 2, last: 3 }, failOnKey: "fail" });
        const { api, get, values, requests } = harness;
        const changes = [];
        for (const key of ["first", "fail", "last"]) await api.addValueChangeListener(key, (...args) => changes.push(args));
        const pending = operation === "set" ? api.setValues({ first: 10, fail: 20, last: 30 })
            : api.deleteValues(["first", "fail", "last"]);
        const rejected = assert.rejects(pending, { code: "MSP999_INTERNAL" });
        const queued = api.setValue("first", 9);
        assert.equal(get(["first"]).first, 9);
        await rejected;
        await queued;
        await waitFor(() => requests.some(request => request.method === "runtime.reportError"));
        assert.deepEqual(get(), { first: 9, fail: 2, last: 3 });
        assert.deepEqual({ ...values }, { first: 9, fail: 2, last: 3 });
        const intermediate = operation === "set" ? 10 : undefined;
        assert.deepEqual(changes, [["first", 1, intermediate, false], ["first", intermediate, 9, false]]);
        assert.equal(requests.filter(request => request.method === "runtime.getStorageSnapshot").length, 1);
    });
}

test("legacy failures report diagnostics and recover without returning a Promise", async () => {
    const { set, get, requests } = await install({ values: { first: 1, fail: 2 }, failOnKey: "fail" });
    assert.equal(set({ first: 10, fail: 20 }), undefined);
    assert.deepEqual(get(), { first: 10, fail: 20 });
    await waitFor(() => requests.some(request => request.method === "runtime.reportError"));
    assert.deepEqual(get(), { first: 10, fail: 2 });
});

test("bulk commit notifications work without a mirror and tolerate delivery after the response", async () => {
    const { api, notifications, deliver } = await install({ mirror: false, deferNotifications: true });
    const changes = [];
    await api.addValueChangeListener("key", (...args) => changes.push(args));
    await api.setValues({ key: 1 });
    assert.deepEqual(changes, []);
    notifications.forEach(deliver);
    notifications.forEach(deliver);
    assert.deepEqual(changes, [["key", undefined, 1, false]]);
});

test("remote bulk changes update the mirror and notify each listener once", async () => {
    const { api, get, deliver, executionId } = await install();
    const changes = [];
    const listenerId = await api.addValueChangeListener("key", (...args) => changes.push(args));
    const notification = { type: "notification", protocol: 1, executionId, deliveryToken: executionId,
        event: "storage-sync", data: { key: "key", oldValue: undefinedValue, newValue: { enabled: true },
            sequence: 1, originExecutionId: "another-execution", mutationMethod: "GM.setValues" } };
    deliver(notification);
    deliver({ ...notification, event: "value-change", data: { ...notification.data, listenerId, remote: true } });
    assert.deepEqual(get(), { key: { enabled: true } });
    assert.deepEqual(changes, [["key", undefined, { enabled: true }, true]]);
});

test("bulk aliases follow grant, profile, legacy-global, and snapshot switches", async () => {
    globalThis.GM_getValues = () => "page";
    globalThis.GM_setValues = () => "page";
    globalThis.GM_deleteValues = () => "page";
    for (const options of [{ strict: true, mirror: false }, { mirror: false }, { legacyGlobals: false },
        { grants: ["GM.getValue", "GM.setValue", "GM.deleteValue"] }]) {
        const harness = await install(options);
        assert.equal(harness.get, undefined);
        assert.equal(harness.set, undefined);
        assert.equal(harness.delete, undefined);
    }
    const { api } = await install({ strict: true, mirror: false });
    assert.equal(await api.setValues({ key: 1 }), undefined);
    assert.deepEqual(await api.getValues(["key"]), { key: 1 });
});
