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

    // throws an error if the value is not a string
    function __MonkeySharp_ThrowIfNotString(value, name) {
        if (typeof value !== "string") {
            throw new Error(name + " must be a string");
        }
    }

    // logs a message to the console
    function GM_log(message) {
        __MonkeySharp.sendMsg("GM_log", new __MonkeySharp_ApiParam(message));
    }

    // set the value of a specific key in the userscript's storage
    function GM_setValue(key, value) {
        __MonkeySharp_ThrowIfNotString(key, "key");
        __MonkeySharp.sendMsg("GM_setValue",
            new __MonkeySharp_ApiParam({ key: key, value: new __MonkeySharp_JsObject(value) }));
    }

    // retrieve the value of a specific key in the userscript's storage
    function GM_getValue(key, defaultValue) {
        __MonkeySharp_ThrowIfNotString(key, "key");
        var result = __MonkeySharp.sendMsg("GM_getValue", new __MonkeySharp_ApiParam(key));
        return typeof result === "undefined" ? defaultValue : result;
    }

    // delete a key from the userscript's storage
    function GM_deleteValue(key) {
        __MonkeySharp_ThrowIfNotString(key, "key");
        __MonkeySharp.sendMsg("GM_deleteValue", new __MonkeySharp_ApiParam(key));
    }

    /*==========REPLACE_CODE_HERE==========*/
})();
