// ==UserScript==
// @name Compatibility frames
// @match http://127.0.0.1:__PORT__/compat/frames*
// @match http://127.0.0.1:__OTHER_PORT__/compat/frames*
// @grant GM.setValue
// @grant GM.getValue
// @run-at document-end
// ==/UserScript==
(async function () {
    const result = globalThis.__compatFrame = { runs: (globalThis.__compatFrame || {}).runs + 1 || 1 };
    try {
        await GM.setValue(location.pathname, location.href);
        result.value = await GM.getValue(location.pathname);
        result.noFrames = globalThis.__compatNoFrames || 0;
        result.done = true;
    } catch (error) { result.error = String(error); }
})();
