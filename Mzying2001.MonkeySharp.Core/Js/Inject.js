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

    // logs a message to the console
    function GM_log(message) {
        __MonkeySharp.sendMsg("GM_log", new __MonkeySharp_ApiParam(message));
    }

    /*==========REPLACE_CODE_HERE==========*/
})();
