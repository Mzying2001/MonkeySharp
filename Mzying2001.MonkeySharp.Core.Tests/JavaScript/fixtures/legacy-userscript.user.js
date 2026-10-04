/* Licensed compatibility fixture. */
// ==UserScript==
// @name Legacy userscript compatibility fixture
// @namespace https://example.com/monkeysharp/fixtures
// @version 1.0.0
// @match http*://example.com/*
// @include /^https?:\/\/example\.com\/legacy\/.*$/
// @exclude /\/blocked\//
// @grant GM_cookie
// @grant GM_download
// @grant GM_getResourceText
// @grant GM_getResourceURL
// @grant GM_notification
// @grant GM_webRequest
// @grant window.close
// @grant window.focus
// @grant window.onurlchange
// @require https://cdn.example.com/legacy-dependency.js
// @resource fixture-template https://cdn.example.com/legacy-template.txt
// @connect downloads.example.com
// @webRequest {"selector":{"include":"https://example.com/*","exclude":"https://example.com/blocked/*"},"action":{"redirect":{"from":"/old","to":"/new"}}}
// @run-at document-end
// ==/UserScript==
globalThis.__legacyFixtureState = {
    cookie: null,
    downloadProgress: null,
    downloadComplete: null,
    downloadTimeout: null,
    downloadError: null,
    downloadAbortError: null,
    notificationClicked: false,
    notificationDone: false,
    modernNotificationClicked: false,
    modernNotificationDone: false,
    webRequest: null,
    urlChange: null,
    resourceText: GM_getResourceText("fixture-template"),
    resourceUrl: GM_getResourceURL("fixture-template")
};

GM_cookie.list(function (cookies, error) {
    globalThis.__legacyFixtureState.cookie = { cookies: cookies, error: error };
});

globalThis.__legacyFixtureState.download = GM_download({
    url: "https://downloads.example.com/legacy.bin",
    timeout: 1000,
    onprogress: function (progress) { globalThis.__legacyFixtureState.downloadProgress = progress; },
    onload: function (result) { globalThis.__legacyFixtureState.downloadComplete = result; },
    onerror: function (error) { globalThis.__legacyFixtureState.downloadError = error; }
});
globalThis.__legacyFixtureState.timeoutDownload = GM_download({
    url: "https://downloads.example.com/timeout.bin",
    timeout: 1,
    ontimeout: function (error) { globalThis.__legacyFixtureState.downloadTimeout = error; }
});
globalThis.__legacyFixtureState.abortDownload = GM_download({
    url: "https://downloads.example.com/abort.bin",
    onerror: function (error) { globalThis.__legacyFixtureState.downloadAbortError = error; }
});

GM_notification(
    "Legacy notification text",
    "Legacy notification title",
    "https://cdn.example.com/notification.png",
    function () { globalThis.__legacyFixtureState.notificationClicked = true; }
);
GM_notification({
    text: "Modern notification text",
    title: "Modern notification title",
    image: "https://cdn.example.com/modern-notification.png",
    highlight: true,
    silent: true,
    timeout: 250,
    onclick: function () { globalThis.__legacyFixtureState.modernNotificationClicked = true; },
    ondone: function () { globalThis.__legacyFixtureState.modernNotificationDone = true; }
});

globalThis.__legacyFixtureState.webRequest = GM_webRequest({
    selector: {
        include: "https://example.com/*",
        exclude: ["https://example.com/blocked/*"]
    },
    action: { redirect: { from: "/old", to: "/new" } }
}, function (info, message, details) {
    globalThis.__legacyFixtureState.webRequestEvent = { info: info, message: message, details: details };
});

window.onurlchange = function (info) { globalThis.__legacyFixtureState.urlChange = info; };
globalThis.__legacyFixtureState.windowActions = Promise.all([window.close(), window.focus()]);
