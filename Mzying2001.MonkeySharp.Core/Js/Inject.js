// Inject.js

(function () {
    // current script id
    var __MonkeySharp_CurrentScriptId = "";

    // the parameter send to C#
    class __MonkeySharp_ApiParam {
        constructor(param) {
            var type = typeof param;
            if (type === "undefined") {
                throw new Error("param is required");
            }
            if (type === "string") {
                this.scriptId = __MonkeySharp_CurrentScriptId;
                this.isJson = false;
                this.data = param;
            }
            else {
                this.scriptId = __MonkeySharp_CurrentScriptId;
                this.isJson = true;
                this.data = JSON.stringify(param);
            }
        }
    }

    // represents a js object
    class __MonkeySharp_JsObject {
        constructor(obj) {
            this.type = typeof obj;
            this.json = __MonkeySharp.serialize(obj);
        }
    }

    // request the api
    function __MonkeySharp_SendApiRequest(api, param = null) {
        return __MonkeySharp.sendMsg(api, new __MonkeySharp_ApiParam(param));
    }

    // throws an error if the value is not a string
    function __MonkeySharp_ThrowIfNotString(value, name) {
        if (typeof value !== "string") {
            throw new Error(name + " must be a string");
        }
    }

    // gets the real window object
    function __MonkeySharp_GetUnsafeWindow() {
        return __MonkeySharp_SendApiRequest("unsafeWindow") ? window : undefined;
    }

    // logs a message to the console
    function GM_log(message) {
        __MonkeySharp_SendApiRequest("GM_log", new __MonkeySharp_JsObject(message));
    }

    // set the value of a specific key in the userscript's storage
    function GM_setValue(key, value) {
        __MonkeySharp_ThrowIfNotString(key, "key");
        __MonkeySharp_SendApiRequest("GM_setValue", { key: key, value: new __MonkeySharp_JsObject(value) });
    }

    // retrieve the value of a specific key in the userscript's storage
    function GM_getValue(key, defaultValue) {
        __MonkeySharp_ThrowIfNotString(key, "key");
        var result = __MonkeySharp_SendApiRequest("GM_getValue", key);
        return typeof result === "undefined" ? defaultValue : result;
    }

    // delete a key from the userscript's storage
    function GM_deleteValue(key) {
        __MonkeySharp_ThrowIfNotString(key, "key");
        __MonkeySharp_SendApiRequest("GM_deleteValue", key);
    }

    // retrieve the list of keys in the userscript's storage
    function GM_listValues() {
        return __MonkeySharp_SendApiRequest("GM_listValues");
    }

    // the async version of api functions
    const GM = {
        log: async function (message) {
            GM_log(message);
        },
        setValue: async function (key, value) {
            GM_setValue(key, value);
        },
        getValue: async function (key, defaultValue) {
            return GM_getValue(key, defaultValue);
        },
        deleteValue: async function (key) {
            GM_deleteValue(key);
        },
        listValues: async function () {
            return GM_listValues();
        }
    };

    // the sandbox object
    const __MonkeySharp_Sandbox =
    {
        // window.focus api
        focus: function () {
            if (__MonkeySharp_SendApiRequest("window.focus")) window.focus();
        },

        // window.close api
        close: function () {
            if (__MonkeySharp_SendApiRequest("window.close")) window.close();
        },

        // the main function
        __main: function () {
            const window = this;
            /*==========REPLACE_CODE_HERE==========*/
        }
    };

    // executes script in the sandbox
    __MonkeySharp_Sandbox.__main();
})();
