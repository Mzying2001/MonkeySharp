const test = require("node:test");
const assert = require("node:assert/strict");
const { install, waitFor } = require("./storage-harness");

// Observable expectations checked against the published Chromium Tampermonkey 5.5.1
// storage facade on 2026-11-01. Host commit acknowledgement remains MonkeySharp-specific.
for (const options of [{ mirror: true }, { mirror: false }, { strict: true, mirror: false }]) {
    const mode = JSON.stringify(options);
    test(`stored undefined is distinct from absence and null (${mode})`, async () => {
        const { api, single } = await install(options);
        assert.equal(await api.getValue("missing", "fallback"), "fallback");
        assert.equal(await api.setValue("", undefined), undefined);
        assert.equal(await api.getValue("", "fallback"), undefined);
        assert.deepEqual(await api.listValues(), [""]);
        let result = await api.getValues(["", "missing"]);
        assert.equal(Object.hasOwn(result, ""), true);
        assert.equal(Object.hasOwn(result, "missing"), false);
        await api.setValues({ undefined: undefined, null: null });
        result = await api.getValues({ undefined: 42, null: 42, missing: undefined });
        assert.deepEqual({ ...result }, { undefined: undefined, null: null, missing: undefined });
        assert.equal(await api.deleteValue(""), undefined);
        assert.equal(await api.getValue("", "fallback"), "fallback");
        if (single.set) {
            single.set("legacy", undefined);
            assert.equal(single.get("legacy", "fallback"), undefined);
            assert.ok(single.list().includes("legacy"));
        }
    });

    test(`normalization preserves top-level numbers and follows nested JSON conversions (${mode})`, async () => {
        const { api } = await install(options);
        for (const value of [NaN, Infinity, -Infinity, -0, 1.5, "u", true, false, null]) {
            await api.setValue("value", value);
            const actual = await api.getValue("value");
            if (Number.isNaN(value)) assert.ok(Number.isNaN(actual));
            else assert.equal(actual, Object.is(value, -0) ? 0 : value);
        }
        await api.setValue("nested", { absent: undefined, array: [undefined, , NaN, Infinity], nested: { absent: undefined } });
        assert.deepEqual(await api.getValue("nested"), { array: [null, null, null, null], nested: {} });
        await api.setValue("date", new Date(0));
        assert.deepEqual(await api.getValue("date"), {});
        const input = { a: { b: 1 } };
        const write = api.setValue("copy", input);
        input.a.b = 9;
        await write;
        const copy = await api.getValue("copy");
        copy.a.b = 99;
        assert.deepEqual(await api.getValue("copy"), { a: { b: 1 } });
        const reserved = JSON.parse('{"$monkeySharpType":"undefined","__proto__":{"safe":true},"constructor":42}');
        await api.setValue("reserved", reserved);
        assert.deepEqual(await api.getValue("reserved"), reserved);
    });

    test(`missing defaults keep references and never enter the bridge (${mode})`, async () => {
        const { api, requests, single } = await install(options);
        const circular = {}; circular.self = circular;
        for (const fallback of [circular, () => 42, Symbol("fallback"), 1n, undefined]) {
            assert.equal(await api.getValue("missing", fallback), fallback);
            const bulk = await api.getValues({ missing: fallback });
            assert.ok(Object.hasOwn(bulk, "missing"));
            assert.equal(bulk.missing, fallback);
            if (single.get) assert.equal(single.get("missing", fallback), fallback);
        }
        assert.ok(requests.every(request => !Object.hasOwn(request.params, "defaultValue") && !Object.hasOwn(request.params, "defaults")));
    });

    test(`listeners use promises, report each committed change once, and stop after removal (${mode})`, async () => {
        const { api, single } = await install(options);
        const first = [], second = [];
        const registration = api.addValueChangeListener("key", (...args) => first.push(args));
        assert.ok(registration instanceof Promise);
        const id = await registration;
        await api.addValueChangeListener("key", (...args) => second.push(args));
        await api.setValue("key", undefined);
        await api.setValue("key", undefined);
        await api.setValues({ key: 1 });
        assert.equal(await api.removeValueChangeListener(id), undefined);
        await api.deleteValue("key");
        await api.deleteValue("key");
        assert.deepEqual(first, [["key", undefined, undefined, false], ["key", undefined, 1, false]]);
        assert.deepEqual(second, [...first, ["key", 1, undefined, false]]);
        if (single.remove) assert.equal(single.remove(id), undefined);
    });

    test(`key enumeration follows JavaScript order after update, delete, and reinsertion (${mode})`, async () => {
        const { api } = await install(options);
        await api.setValues({ z: 1, a: 2, "10": 10, "2": 2, "": undefined });
        await api.setValue("z", 3);
        assert.deepEqual(await api.listValues(), ["2", "10", "z", "a", ""]);
        await api.deleteValue("z");
        await api.setValue("z", 4);
        assert.deepEqual(await api.listValues(), ["2", "10", "a", "", "z"]);
    });
}

test("modern listener registration rejects host failure and removes the local callback", async () => {
    const { api, requests } = await install({ failRegistration: true });
    const changes = [];
    await assert.rejects(api.addValueChangeListener("key", (...args) => changes.push(args)), { code: "MSP999_INTERNAL" });
    await api.setValue("key", 1);
    assert.deepEqual(changes, []);
    assert.ok(requests.some(request => request.method === "runtime.reportError"));
});

for (const mirror of [true, false]) {
    test(`late single-value events are deduplicated per listener (mirror=${mirror})`, async () => {
        const { api, notifications, deliver } = await install({ mirror, deferNotifications: true });
        const changes = [];
        await api.addValueChangeListener("key", (...args) => changes.push(args));
        await api.setValue("key", 1);
        assert.deepEqual(changes, []);
        notifications.forEach(deliver);
        notifications.forEach(deliver);
        assert.deepEqual(changes, [["key", undefined, 1, false]]);
    });
}

test("legacy listener aliases are synchronous even without a storage mirror", async () => {
    const { single, api, requests } = await install({ mirror: false });
    const changes = [];
    const id = single.add("key", (...args) => changes.push(args));
    assert.equal(typeof id, "number");
    await api.setValue("key", 1);
    assert.equal(single.remove(id), undefined);
    await api.setValue("key", 2);
    assert.deepEqual(changes, [["key", undefined, 1, false]]);
    await waitFor(() => requests.some(request => request.method === "GM.removeValueChangeListener"));
});

for (const mirror of [true, false]) {
    test(`bulk defaults are captured before an asynchronous lookup (mirror=${mirror})`, async () => {
        let release;
        const barrier = new Promise(resolve => { release = resolve; });
        const { api } = await install({ mirror, beforeRead: () => barrier });
        const original = {};
        const defaults = { primitive: "before", reference: original, missing: undefined };
        const reading = api.getValues(defaults);
        defaults.primitive = "after";
        defaults.reference = {};
        delete defaults.missing;
        release();
        const result = await reading;
        assert.equal(result.primitive, "before");
        assert.equal(result.reference, original);
        assert.ok(Object.hasOwn(result, "missing"));
    });
}
