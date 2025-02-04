// Inject.js

(function () {
    // current script id
    var __MonkeySharp_CurrentScriptId = "";

    // the parameter send to C#
    class __MonkeySharp_ApiParam {
        constructor(scriptId, param) {
            this.scriptId = scriptId;
            this.jsonData = JSON.stringify(param);
        }
    }

    // logs a message to the console
    function GM_log(message) {
        __MonkeySharp.sendMsg("GM_log", new __MonkeySharp_ApiParam(__MonkeySharp_CurrentScriptId, message));
    }

    /*==========REPLACE_CODE_HERE==========*/
})();
