// ==UserScript==
// @name Compatibility frames
// @match http://127.0.0.1:__PORT__/compat/frames*
// @match http://127.0.0.1:__OTHER_PORT__/compat/frames*
// @grant GM.setValue
// @grant GM.getValue
// @grant GM_getValues
// @grant GM_setValues
// @grant GM_deleteValues
// @run-at document-end
// ==/UserScript==
(async function () {
    const result = globalThis.__compatFrame = { runs: (globalThis.__compatFrame || {}).runs + 1 || 1 };
    try {
        await GM.setValue(location.pathname, location.href);
        result.value = await GM.getValue(location.pathname);
        const prefix = location.pathname + ':bulk:';
        const modern = prefix + 'modern', legacy = prefix + 'legacy', temporary = prefix + 'temporary';
        const missing = prefix + 'missing', barrier = prefix + 'barrier';
        const checks = [];
        checks.push(await GM.setValues({ [modern]: { url: location.href }, [temporary]: null }) === undefined);
        const selected = await GM.getValues([modern, temporary, missing]);
        checks.push(selected[modern].url === location.href && selected[temporary] === null &&
            !Object.prototype.hasOwnProperty.call(selected, missing));
        checks.push(GM_setValues({ [legacy]: 42 }) === undefined && GM_getValues([legacy])[legacy] === 42);
        const defaults = GM_getValues({ [legacy]: 0, [missing]: 'fallback' });
        checks.push(defaults[legacy] === 42 && defaults[missing] === 'fallback');
        await GM.setValue(barrier, true);
        checks.push(GM_deleteValues([legacy, legacy, missing]) === undefined &&
            !Object.prototype.hasOwnProperty.call(GM_getValues(), legacy));
        checks.push(await GM.deleteValues([modern, temporary, barrier]) === undefined);
        const remaining = await GM.getValues();
        checks.push(!Object.keys(remaining).some(key => key.indexOf(prefix) === 0));
        result.bulkStorage = checks.every(Boolean);
        if (!result.bulkStorage) throw new Error('Bulk storage checks failed: ' + JSON.stringify(checks));
        result.noFrames = globalThis.__compatNoFrames || 0;
        result.done = true;
    } catch (error) { result.error = String(error); }
})();
