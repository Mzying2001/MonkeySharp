// Inject.js

(function () {
    // current script id
    var __MonkeySharp_CurrentScriptId = "";

    // api functions
    function GM_log(message) {
        __MonkeySharp.sendMsg(["GM_log", __MonkeySharp_CurrentScriptId, JSON.stringify(message)]);
    }

    /*==========REPLACE_CODE_HERE==========*/
})();
