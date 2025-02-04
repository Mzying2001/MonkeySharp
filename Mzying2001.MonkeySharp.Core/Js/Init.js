// Init.js

var __MonkeySharp =
{
    sendMsg: function (msg, param) {
        // __MonkeySharp_Messenger: injected by MonkeySharp
        if (typeof __MonkeySharp_Messenger !== "undefined") {
            var result = __MonkeySharp_Messenger.sendMessage([msg, this.serialize(param)]);
            return this.deserialize(result);
        } else {
            this.consoleLog("Messenger is not found.");
            return undefined;
        }
    },

    sendMsgAsync: async function (msg, param) {
        // __MonkeySharp_AsyncMessenger: injected by MonkeySharp
        if (typeof __MonkeySharp_AsyncMessenger !== "undefined") {
            //Task<T> is not supported by default in JavaScript, so we use sendMessage rather than sendMessageAsync.
            var result = await __MonkeySharp_AsyncMessenger.sendMessage([msg, this.serialize(param)]);
            return this.deserialize(result);
        } else {
            this.consoleLog("AsyncMessenger is not found.");
            return undefined;
        }
    },

    serialize: function (obj) {
        if (typeof obj === "undefined") {
            return null;
        } else {
            return JSON.stringify(obj);
        }
    },

    deserialize: function (str) {
        if (typeof str === "undefined" || str === null) {
            return undefined;
        } else {
            return JSON.parse(str);
        }
    },

    consoleLog: function (msg, isJson) {
        if (typeof isJson === "undefined" || isJson === false) {
            console.log("[MonkeySharp]", msg);
        } else {
            console.log("[MonkeySharp]", this.deserialize(msg));
        }
    },

    onDocumentStart: function () {
        this.sendMsg("document-start", window.location.href);
    },

    onDocumentBody: function () {
        this.sendMsg("document-body", window.location.href);
    },

    onDocumentEnd: function () {
        this.sendMsg("document-end", window.location.href);
    },

    onDocumentIdle: function () {
        this.sendMsg("document-idle", window.location.href);
    },

    onContextMenu: function () {
        this.sendMsg("context-menu", window.location.href);
    }
};

(function () {
    // avoid duplicate injection
    if (window.__MonkeySharp_Injected) {
        return;
    }

    // mark as injected
    window.__MonkeySharp_Injected = true;

    // raise document-start event
    __MonkeySharp.onDocumentStart();

    // register document-end event
    window.addEventListener("DOMContentLoaded", function () {
        __MonkeySharp.onDocumentEnd();
    });

    // register document-idle event
    window.addEventListener("load", function () {
        __MonkeySharp.onDocumentIdle();
    });

    // register context-menu event
    window.addEventListener("contextmenu", function () {
        __MonkeySharp.onContextMenu();
    });

    // register document-body event
    var intervalId = setInterval(function () {
        if (document.body) {
            clearInterval(intervalId);
            __MonkeySharp.onDocumentBody();
        }
    }, 50);
})();
