const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { install, waitFor } = require("./storage-harness");

test("original storage timing fixture matches the pinned Tampermonkey event order", async t => {
    let resolve;
    const evidence = new Promise(done => { resolve = done; });
    const previous = globalThis.fetch;
    globalThis.fetch = (_url, request) => { resolve(JSON.parse(request.body)); return Promise.resolve(); };
    t.after(() => { globalThis.fetch = previous; });
    const source = fs.readFileSync(path.resolve(__dirname, "../../scripts/compatibility/fixtures/storage-timing.user.js"), "utf8");
    const expected = JSON.parse(fs.readFileSync(path.resolve(__dirname, "../../scripts/compatibility/evidence/tampermonkey-5.5.1-storage-timing.json"), "utf8"));
    await install({ source });
    const actual = await evidence;
    assert.deepEqual(actual.events, expected.events);
    for (const key of ["immediateValue", "bulkPrototype", "defaultReference", "bulkDefaultReference"]) assert.deepEqual(actual.effects[key], expected.effects[key]);
});

test("consecutive failures rebuild from confirmed remote state and preserve the last pending write", async () => {
    let release;
    const barrier = new Promise(resolve => { release = resolve; });
    const h = await install({ values: { key: 1 }, failOnKeys: ["key", "key"], beforeMutation: () => barrier });
    const events = [];
    await h.api.addValueChangeListener("key", (...args) => events.push(args));
    const first = assert.rejects(h.api.setValue("key", 2), { code: "MSP999_INTERNAL" });
    const second = assert.rejects(h.api.setValue("key", 3), { code: "MSP999_INTERNAL" });
    const last = h.api.setValue("key", 4);
    h.externalSet("key", 8);
    assert.equal(h.single.get("key"), 4);
    release();
    await Promise.all([first, second, last]);
    assert.equal(h.single.get("key"), 4);
    assert.equal(h.values.key, "n4");
    assert.deepEqual(events, [["key", 1, 2, false], ["key", 2, 3, false], ["key", 3, 4, false], ["key", 1, 8, true]]);
});

test("listener reentrancy and exceptions preserve queue order and do not repeat local echoes", async () => {
    const h = await install({ deferNotifications: true });
    const events = [];
    h.single.add("key", () => { throw new Error("listener fixture"); });
    h.single.add("key", (_key, _old, value, remote) => {
        events.push([value, remote]);
        if (value === 1) h.single.set("key", 2);
    });
    h.single.set("key", 1);
    assert.equal(h.single.get("key"), 2);
    assert.deepEqual(events, [[1, false], [2, false]]);
    await waitFor(() => h.values.key === "n2");
    h.notifications.forEach(h.deliver);
    h.notifications.forEach(h.deliver);
    assert.deepEqual(events, [[1, false], [2, false]]);
});

test("remote listeners are active at registration and removed before asynchronous host acknowledgement", async () => {
    const h = await install();
    const events = [];
    const id = h.single.add("key", (...args) => events.push(args));
    h.externalSet("key", 1);
    const removal = h.api.removeValueChangeListener(id);
    h.externalSet("key", 2);
    await removal;
    assert.deepEqual(events, [["key", undefined, 1, true]]);
    assert.equal(h.single.get("key"), 2);
});

test("a gap replays lost remote listener changes once before later duplicate deliveries", async () => {
    const h = await install({ deferNotifications: true });
    const events = [];
    await h.api.addValueChangeListener("key", (...args) => events.push(args));
    h.externalSet("key", 1);
    h.externalSet("key", 2);
    h.deliver(h.notifications.filter(notification => notification.event === "storage-sync").at(-1));
    await waitFor(() => events.length === 2);
    h.notifications.forEach(h.deliver);
    assert.deepEqual(events, [["key", undefined, 1, true], ["key", 1, 2, true]]);
    assert.equal(h.single.get("key"), 2);
});

test("ending an execution clears storage listeners and prevents queued or later effects", async () => {
    const h = await install();
    const events = [];
    await h.api.addValueChangeListener("key", (...args) => events.push(args));
    const pending = assert.rejects(h.api.setValue("key", 1), /execution ended/);
    h.deliver({ protocol: 2, type: "notification", executionId: h.executionId, deliveryToken: h.executionId, event: "execution-ended", data: {} });
    await pending;
    await assert.rejects(h.api.setValue("key", 2), /execution ended/);
    assert.throws(() => h.single.get("key"), /execution ended/);
    assert.equal(h.values.key, undefined);
    assert.deepEqual(events, [["key", undefined, 1, false]]);
});

test("native document cleanup ignores another document and revokes the captured document", async () => {
    const h = await install();
    globalThis.__MonkeySharpRuntime.endDocument("another-document");
    await h.api.setValue("key", 1);
    globalThis.__MonkeySharpRuntime.endDocument(h.executionId);
    await assert.rejects(h.api.getValue("key"), /execution ended/);
    await assert.rejects(h.api.setValue("key", 2), /execution ended/);
    assert.equal(h.values.key, "n1");
});
