const test = require("node:test");
const assert = require("node:assert/strict");
const { install, waitFor } = require("./storage-harness");

test("legacy storage reads and writes the mirror synchronously but notifies only after commit", async () => {
    const { single, requests } = await install({ values: { count: 2, nullable: null } });
    const changes = [];
    const id = single.add("count", (...args) => changes.push(args));
    assert.equal(typeof id, "number");
    assert.equal(single.get("nullable", "fallback"), null);
    assert.equal(single.set("count", 3), undefined);
    assert.equal(single.get("count"), 3);
    assert.deepEqual(single.list(), ["count", "nullable"]);
    assert.deepEqual(changes, []);
    await waitFor(() => requests.some(request => request.method === "GM.setValue"));
    assert.deepEqual(changes, [["count", 2, 3, false]]);
});

test("failed single writes restore stored undefined without a false local notification", async () => {
    const { single, api, requests } = await install({ values: { "": undefined }, failOnKey: "" });
    const changes = [];
    await api.addValueChangeListener("", (...args) => changes.push(args));
    single.set("", 2);
    assert.equal(single.get(""), 2);
    await waitFor(() => requests.some(request => request.method === "runtime.reportError"));
    assert.equal(single.get("", "fallback"), undefined);
    assert.deepEqual(single.list(), [""]);
    assert.deepEqual(changes, []);
    await api.setValue("", 3);
    assert.deepEqual(changes, [["", undefined, 3, false]]);
});

test("storage reconcile reapplies optimistic single mutations still waiting in the queue", async () => {
    const { single, api, requests } = await install({ values: { count: 1 }, failOnKey: "count" });
    const changes = [];
    await api.addValueChangeListener("count", (...args) => changes.push(args));
    const failed = assert.rejects(api.setValue("count", 2), { code: "MSP999_INTERNAL" });
    const queued = api.setValue("count", 3);
    assert.equal(single.get("count"), 3);
    await failed;
    await queued;
    assert.equal(single.get("count"), 3);
    assert.deepEqual(changes, [["count", 1, 3, false]]);
    assert.equal(requests.filter(request => request.method === "runtime.getStorageSnapshot").length, 1);
});

test("remote empty-key updates preserve undefined and ignore stale mirror notifications", async () => {
    const { single, api, deliver, executionId } = await install();
    const changes = [];
    const listenerId = await api.addValueChangeListener("", (...args) => changes.push(args));
    const notification = { type: "notification", protocol: 2, executionId, deliveryToken: executionId,
        event: "storage-sync", data: { key: "", oldValue: null, oldValueExists: false,
            newValue: "u", newValueExists: true, sequence: 2, originExecutionId: "remote" } };
    deliver(notification);
    deliver({ ...notification, event: "value-change", data: { ...notification.data, listenerId } });
    deliver({ ...notification, data: { ...notification.data, newValue: "n99", sequence: 1 } });
    assert.equal(single.get("", "fallback"), undefined);
    assert.deepEqual(single.list(), [""]);
    assert.deepEqual(changes, [["", undefined, undefined, true]]);
});
