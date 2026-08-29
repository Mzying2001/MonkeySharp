(function () {
    "use strict";
    const binary = atob("__MONKEYSHARP_PAYLOAD_BASE64__");
    const bytes = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
    const notification = JSON.parse(new TextDecoder().decode(bytes));
    const runtime = globalThis.__MonkeySharpRuntime;
    if (runtime && typeof runtime.receive === "function") runtime.receive(notification);
})();
