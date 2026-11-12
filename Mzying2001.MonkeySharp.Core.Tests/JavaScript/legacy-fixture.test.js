const completePlan = require("./test-plan");
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const template = require("./bootstrap-template");
const fixture = fs.readFileSync(path.resolve(__dirname, "fixtures/legacy-userscript.user.js"), "utf8");

function waitFor(predicate) {
    return new Promise((resolve, reject) => {
        const deadline = Date.now() + 2000;
        const timer = setInterval(() => {
            if (predicate()) {
                clearInterval(timer);
                resolve();
            } else if (Date.now() > deadline) {
                clearInterval(timer);
                reject(new Error("Timed out waiting for the legacy userscript fixture."));
            }
        }, 5);
    });
}

test("the shared legacy userscript fixture exercises callback and modern facade contracts", async () => {
    const requests = [];
    const downloadStarts = [];
    const notificationRequests = [];
    const webRequestRegistrations = [];
    let abortCount = 0;
    globalThis.__MonkeySharpBridge = {
        dispatch: async requestJson => {
            const request = JSON.parse(requestJson);
            if (request.type === "hello") return JSON.stringify({
                type: "hello-result", protocol: 2, ok: true, limits: {},
                apis: ["GM.cookie", "GM.download", "GM.getResourceText", "GM.getResourceUrl",
                    "GM.notification", "GM.webRequest", "window.close", "window.focus"],
                compatibility: { resources: { complete: true, values: {
                    "fixture-template": { text: "fixture resource", url: "data:text/plain;base64,ZA==" }
                } } }
            });
            requests.push(request);
            let result = { $monkeySharpType: "undefined" };
            if (request.method === "GM.cookie") result = [];
            else if (request.method === "GM.download" && request.params.operation === "start") {
                downloadStarts.push(request.params);
                result = { id: "download-" + downloadStarts.length, clientId: request.params.clientId };
            } else if (request.method === "GM.download" && request.params.operation === "abort") {
                abortCount += 1;
                result = { accepted: true };
            } else if (request.method === "GM.notification") {
                notificationRequests.push(request.params);
                result = { id: "notification-" + notificationRequests.length };
            } else if (request.method === "GM.webRequest" && request.params.operation === "register") {
                webRequestRegistrations.push(request.params);
                result = { id: "webrequest-1", listenerId: request.params.listenerId };
            } else if (request.method === "GM.webRequest") result = true;
            else if (request.method === "window.close" || request.method === "window.focus") result = true;
            return JSON.stringify({ type: "response", protocol: 2, requestId: request.requestId, ok: true, result });
        }
    };

    const payload = {
        protocol: 2, documentId: "legacy-fixture-document", frameId: "main", runAt: "DocumentEnd",
        invocations: [{
            executionId: "legacy-fixture-execution", scriptKey: "88888888-8888-8888-8888-888888888888",
            source: fixture.slice(fixture.indexOf("// ==/UserScript==") + "// ==/UserScript==".length),
            declaredGrants: ["GM_cookie", "GM_download", "GM_getResourceText", "GM_getResourceURL",
                "GM_notification", "GM_webRequest", "window.close", "window.focus", "window.onurlchange"],
            grants: ["GM.cookie", "GM.download", "GM.getResourceText", "GM.getResourceUrl", "GM.notification",
                "GM.webRequest", "window.close", "window.focus", "window.onurlchange"],
            grantDeclarationState: "ExplicitList", info: {}, capability: "legacy-fixture-capability",
            deliveryToken: "legacy-fixture-delivery",
        }]
    };
    const encoded = Buffer.from(JSON.stringify(completePlan(payload)), "utf8").toString("base64");
    vm.runInThisContext(template.replace("__MONKEYSHARP_PAYLOAD_BASE64__", encoded));
    await waitFor(() => globalThis.__legacyFixtureState && globalThis.__legacyFixtureState.cookie &&
        downloadStarts.length === 3 && notificationRequests.length === 2 && webRequestRegistrations.length === 1);
    await globalThis.__legacyFixtureState.windowActions;

    const state = globalThis.__legacyFixtureState;
    assert.deepEqual(state.cookie, { cookies: [], error: null });
    assert.equal(requests.find(item => item.method === "GM.cookie").params.details.url, undefined);
    assert.equal(state.resourceText, "fixture resource");
    assert.equal(state.resourceUrl, "data:text/plain;base64,ZA==");
    assert.deepEqual(webRequestRegistrations[0].rules[0].selector, {
        include: "https://example.com/*", exclude: ["https://example.com/blocked/*"]
    });
    assert.deepEqual(webRequestRegistrations[0].rules[0].action, { redirect: { from: "/old", to: "/new" } });
    assert.deepEqual(notificationRequests[0], {
        clientId: notificationRequests[0].clientId, tag: null, url: null,
        title: "Legacy notification title", text: "Legacy notification text",
        imageUrl: "https://cdn.example.com/notification.png", highlight: false, silent: false
    });
    assert.deepEqual(notificationRequests[1], {
        clientId: notificationRequests[1].clientId, tag: null, url: null,
        title: "Modern notification title", text: "Modern notification text",
        imageUrl: "https://cdn.example.com/modern-notification.png", highlight: true, silent: true, timeout: 250
    });

    const receive = (event, data) => globalThis.__MonkeySharpRuntime.receive({
        type: "notification", protocol: 2, executionId: "legacy-fixture-execution",
        deliveryToken: "legacy-fixture-delivery", event, data
    });
    assert.equal(receive("download-progress", {
        clientId: downloadStarts[0].clientId, loaded: 3, total: 8
    }), true);
    assert.equal(receive("download-complete", {
        clientId: downloadStarts[0].clientId, result: { name: "legacy.bin", loaded: 8 }
    }), true);
    assert.equal(receive("download-timeout", {
        clientId: downloadStarts[1].clientId, error: "timeout"
    }), true);
    await state.abortDownload.abort();
    assert.equal(receive("download-aborted", {
        clientId: downloadStarts[2].clientId, error: "not_succeeded"
    }), true);
    assert.equal(abortCount, 1);
    assert.deepEqual(state.downloadProgress, { loaded: 3, total: 8 });
    assert.deepEqual(state.downloadComplete, { name: "legacy.bin", loaded: 8 });
    assert.equal(state.downloadTimeout.error, "timeout");
    assert.equal(state.downloadAbortError.error, "not_succeeded");

    assert.equal(receive("notification-click", { clientId: notificationRequests[0].clientId }), true);
    assert.equal(receive("notification-click", { clientId: notificationRequests[1].clientId }), true);
    assert.equal(receive("notification-done", { clientId: notificationRequests[1].clientId }), true);
    assert.equal(state.notificationClicked, true);
    assert.equal(state.modernNotificationClicked, true);
    assert.equal(state.modernNotificationDone, true);
    assert.equal(receive("webrequest-result", {
        listenerId: webRequestRegistrations[0].listenerId,
        info: "redirect", message: "ok", details: { url: "https://example.com/old", redirect_url: "/new" }
    }), true);
    assert.equal(state.webRequestEvent.info, "redirect");
    assert.equal(state.webRequestEvent.message, "ok");
    assert.equal(state.webRequestEvent.details.redirect_url, "/new");

    globalThis.__MonkeySharpRuntime.urlChanged({ url: "https://example.com/next", oldURL: "https://example.com/" });
    assert.equal(state.urlChange.url, "https://example.com/next");
    assert.deepEqual(requests.filter(item => item.method === "window.close" || item.method === "window.focus")
        .map(item => item.method).sort(), ["window.close", "window.focus"]);

    delete globalThis.__legacyFixtureState;
    delete globalThis.__MonkeySharpBridge;
});
