// Init.js

var __MonkeySharp =
{
    sendMsg: function (msg) {
        // __MonkeySharp_Messenger: injected by MonkeySharp
        if (typeof __MonkeySharp_Messenger != "undefined") {
            return __MonkeySharp_Messenger.sendMessage(msg);
        } else {
            this.consoleLog("Messenger is not found.");
            return null;
        }
    },

    sendMsgAsync: async function (msg) {
        // __MonkeySharp_AsyncMessenger: injected by MonkeySharp
        if (typeof __MonkeySharp_AsyncMessenger != "undefined") {
            //return await __MonkeySharp_AsyncMessenger.sendMessageAsync(msg);
            //Task<T> is not supported by default in JavaScript, so we use sendMessage rather than sendMessageAsync.
            return await __MonkeySharp_AsyncMessenger.sendMessage(msg);
        } else {
            this.consoleLog("AsyncMessenger is not found.");
            return null;
        }
    },

    consoleLog: function (msg) {
        console.log("MonkeySharp: " + msg);
    },

    onDocumentStart: function () {
        this.sendMsg(["document-start", window.location.href]);
    },

    onDocumentBody: function () {
        this.sendMsg(["document-body", window.location.href]);
    },

    onDocumentEnd: function () {
        this.sendMsg(["document-end", window.location.href]);
    },

    onDocumentIdle: function () {
        this.sendMsg(["document-idle", window.location.href]);
    },

    onContextMenu: function () {
        this.sendMsg(["context-menu", window.location.href]);
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
