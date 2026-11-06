const test = require("node:test");
const assert = require("node:assert/strict");
const { install, waitFor, fixtureValue } = require("./storage-harness");
for (const chunked of [false, true]) {
    test(`bulk JSON values cannot be confused with the bridge sentinel (chunked=${chunked})`, async () => {
        const { api } = await install({ chunked });
        const changes = [];
        await api.addValueChangeListener("key", (...args) => changes.push(args));
        await api.setValues({ key: { $monkeySharpType: "undefined" } });
        assert.deepEqual({ ...await api.getValues(["key"]) }, { key: { $monkeySharpType: "undefined" } });
        await api.deleteValues(["key"]);
        assert.deepEqual(changes, [["key", undefined, { $monkeySharpType: "undefined" }, false],
            ["key", { $monkeySharpType: "undefined" }, undefined, false]]);
    });

    test(`bulk reads select keys, defaults, or all JSON values (chunked=${chunked})`, async () => {
        const initial = JSON.parse('{"nullable":null,"nested":{"items":[1,true]},"__proto__":{"safe":true},"constructor":42}');
        const { api } = await install({ values: initial, chunked });
        assert.deepEqual({ ...await api.getValues(["nullable", "missing", "nested", "nested"]) },
            { nullable: null, nested: { items: [1, true] } });
        assert.deepEqual({ ...await api.getValues({ nullable: "unused", missing: { enabled: false }, constructor: undefined }) },
            { nullable: null, missing: { enabled: false }, constructor: 42 });
        assert.deepEqual({ ...await api.getValues({ absent: undefined }) }, { absent: undefined });
        for (const argument of [undefined, null]) assert.deepEqual({ ...await api.getValues(argument) }, initial);
        assert.deepEqual({ ...await api.getValues() }, initial);
        assert.deepEqual({ ...await api.getValues([]) }, {});
        assert.deepEqual({ ...await api.getValues({}) }, {});
        const all = await api.getValues();
        assert.equal(Object.getPrototypeOf(all), null);
        assert.equal(Object.hasOwn(all, "__proto__"), true);
        all.nested.items.push(3);
        assert.deepEqual((await api.getValues(["nested"])).nested.items, [1, true]);
        await api.setValues({ $monkeySharpType: "undefined" });
        assert.deepEqual({ ...await api.getValues(["$monkeySharpType"]) }, { $monkeySharpType: "undefined" });
    });

    test(`modern bulk mutations share the storage family and use one request per batch (chunked=${chunked})`, async () => {
        const { api, requests, values } = await install({ chunked,
            grants: ["GM.getValues", "GM.setValues", "GM.deleteValues"] });
        assert.equal(typeof api.getValue, "function");
        assert.equal(typeof api.setValue, "function");
        const input = JSON.parse('{"first":{"count":1},"__proto__":"ordinary","constructor":null}');
        const writing = api.setValues(input);
        assert.equal(typeof writing.then, "function");
        input.first.count = 99;
        assert.equal(await writing, undefined);
        assert.deepEqual({ ...await api.getValues(["first", "__proto__", "constructor"]) },
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
    assert.deepEqual({ ...get(["first", "temporary"]) }, { first: 2, temporary: null });
    const single = api.setValue("first", 3);
    assert.equal(remove(["temporary", "temporary"]), undefined);
    assert.deepEqual({ ...get() }, { first: 3 });
    assert.deepEqual({ ...await api.getValues() }, { first: 3 });
    await single;
    await waitFor(() => requests.some(request => request.method === "GM.deleteValues"));
    assert.deepEqual(requests.filter(request => ["GM.setValues", "GM.setValue", "GM.deleteValues"].includes(request.method))
        .map(request => request.method), ["GM.setValues", "GM.setValue", "GM.deleteValues"]);
    assert.deepEqual(changes, [["first", 1, 2, false], ["first", 2, 3, false]]);
});

test("invalid bulk inputs fail before any optimistic or durable mutation", async () => {
    const { api, get, set, delete: remove, requests } = await install({ values: { first: 1 } });
    const circular = {}; circular.self = circular;
    for (const input of [null, [], 2,
        { first: 2, bad: () => {} }, { first: 2, bad: Symbol() }, { first: 2, bad: 1n }, { first: 2, bad: circular }]) {
        assert.throws(() => set(input), TypeError);
        await assert.rejects(api.setValues(input), TypeError);
    }
    for (const input of [null, {}, "first", ["first", 2], ["first", , "last"]]) {
        assert.throws(() => remove(input), TypeError);
        await assert.rejects(api.deleteValues(input), TypeError);
    }
    for (const input of [42, "first", ["first", null]]) {
        assert.throws(() => get(input), TypeError);
        await assert.rejects(api.getValues(input), TypeError);
    }
    assert.deepEqual({ ...get() }, { first: 1 });
    assert.equal(requests.length, 0);
});

for (const operation of ["set", "delete"]) {
    test(`atomic bulk ${operation} failure rejects the whole batch and replays queued mutations`, async () => {
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
        assert.deepEqual({ ...get() }, { first: 9, fail: 2, last: 3 });
        assert.deepEqual({ ...values }, { first: "n9", fail: "n2", last: "n3" });
        assert.deepEqual(changes, [["first", 1, 9, false]]);
        assert.equal(requests.filter(request => request.method === "runtime.getStorageSnapshot").length, 1);
    });
}

test("legacy failures report diagnostics and recover without returning a Promise", async () => {
    const { set, get, requests } = await install({ values: { first: 1, fail: 2 }, failOnKey: "fail" });
    assert.equal(set({ first: 10, fail: 20 }), undefined);
    assert.deepEqual({ ...get() }, { first: 10, fail: 20 });
    await waitFor(() => requests.some(request => request.method === "runtime.reportError"));
    assert.deepEqual({ ...get() }, { first: 1, fail: 2 });
});

test("bulk commit notifications work with a chunked snapshot and tolerate delivery after the response", async () => {
    const { api, notifications, deliver } = await install({ chunked: true, deferNotifications: true });
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
    const notification = { type: "notification", protocol: 2, executionId, deliveryToken: executionId,
        event: "storage-sync", data: { key: "key", oldValue: null, oldValueExists: false, newValue: fixtureValue({ enabled: true }), newValueExists: true,
            sequence: 1, originExecutionId: "another-execution", mutationMethod: "GM.setValues" } };
    deliver(notification);
    deliver({ ...notification, event: "value-change", data: { ...notification.data, listenerId, remote: true } });
    assert.deepEqual({ ...get() }, { key: { enabled: true } });
    assert.deepEqual(changes, [["key", undefined, { enabled: true }, true]]);
});

test("bulk legacy aliases require explicit names and a ready snapshot", async () => {
    globalThis.GM_getValues = () => "page";
    globalThis.GM_setValues = () => "page";
    globalThis.GM_deleteValues = () => "page";
    for (const options of [{ declaredGrants: ["GM.getValue"] },
        { grants: ["GM.getValue", "GM.setValue", "GM.deleteValue"] }]) {
        const harness = await install(options);
        assert.equal(harness.get, undefined);
        assert.equal(harness.set, undefined);
        assert.equal(harness.delete, undefined);
    }
    const { api } = await install({ chunked: true });
    assert.equal(await api.setValues({ key: 1 }), undefined);
    assert.deepEqual({ ...await api.getValues(["key"]) }, { key: 1 });
});
