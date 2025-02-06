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

    // logs a message to the console
    function GM_log(message) {
        __MonkeySharp.sendMsg("GM_log", new __MonkeySharp_ApiParam(message));
    }

    // set the value of a specific key in the userscript's storage
    function GM_setValue(key, value) {
        if (typeof key !== "string") {
            throw new Error("key must be a string");
        }
        __MonkeySharp.sendMsg("GM_setValue",
            new __MonkeySharp_ApiParam({ key: key, value: new __MonkeySharp_JsObject(value) }));
    }

    // retrieve the value of a specific key in the userscript's storage
    function GM_getValue(key, defaultValue) {
        if (typeof key !== "string") {
            throw new Error("key must be a string");
        }
        return __MonkeySharp.sendMsg("GM_getValue", new __MonkeySharp_ApiParam(key));
    }

    // delete a key from the userscript's storage
    function GM_deleteValue(key) {
        if (typeof key !== "string") {
            throw new Error("key must be a string");
        }
        __MonkeySharp.sendMsg("GM_deleteValue", new __MonkeySharp_ApiParam(key));
    }

    /*==========REPLACE_CODE_HERE==========*/
})();
