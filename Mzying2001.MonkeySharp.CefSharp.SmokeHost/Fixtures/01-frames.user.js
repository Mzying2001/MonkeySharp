// ==UserScript==
// @name Compatibility frames
// @match http://127.0.0.1:__PORT__/compat/frames*
// @match http://127.0.0.1:__OTHER_PORT__/compat/frames*
// @grant GM.setValue
// @grant GM.getValue
// @grant GM_getValue
// @grant GM_setValue
// @grant GM.deleteValue
// @grant GM.listValues
// @grant GM.addValueChangeListener
// @grant GM.removeValueChangeListener
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
        const storageChecks = [];
        const undefinedKey = prefix + 'undefined';
        const changes = [];
        const registration = GM.addValueChangeListener(undefinedKey, (key, oldValue, newValue, remote) => {
            changes.push({ oldUndefined: oldValue === undefined, newUndefined: newValue === undefined, remote });
        });
        storageChecks.push(typeof registration.then === 'function');
        const listenerId = await registration;
        const firstWrite = GM.setValue(undefinedKey, undefined);
        storageChecks.push(changes.length === 1 && changes[0].oldUndefined && changes[0].newUndefined && !changes[0].remote);
        await firstWrite;
        await GM.setValue(undefinedKey, undefined);
        storageChecks.push(await GM.getValue(undefinedKey, 'fallback') === undefined &&
            GM_getValue(undefinedKey, 'fallback') === undefined && (await GM.listValues()).includes(undefinedKey));
        const batch = await GM.getValues({ [undefinedKey]: 42, [missing]: undefined });
        storageChecks.push(Object.prototype.hasOwnProperty.call(batch, missing) && batch[missing] === undefined && batch[undefinedKey] === undefined);
        storageChecks.push(await GM.deleteValue(undefinedKey) === undefined);
        for (let attempt = 0; changes.length < 2 && attempt < 100; attempt++) await new Promise(resolve => setTimeout(resolve, 10));
        storageChecks.push(changes.length === 2 && changes.every(change => change.oldUndefined && change.newUndefined && !change.remote));
        storageChecks.push(await GM.removeValueChangeListener(listenerId) === undefined);
        GM_setValue(undefinedKey, { absent: undefined, items: [undefined, NaN] });
        const nested = GM_getValue(undefinedKey);
        storageChecks.push(!Object.prototype.hasOwnProperty.call(nested, 'absent') && nested.items.every(value => value === null));
        await GM.deleteValue(undefinedKey);
        result.storageCompatibility = storageChecks.every(Boolean);
        if (!result.storageCompatibility) throw new Error('Storage compatibility checks failed: ' + JSON.stringify(storageChecks));
        const sharedKey = 'compat-shared-undefined';
        result.persistedUndefined = Object.prototype.hasOwnProperty.call(await GM.getValues([sharedKey]), sharedKey) &&
            await GM.getValue(sharedKey, 'fallback') === undefined;
        result.sharedEvents = [];
        await GM.addValueChangeListener(sharedKey, (key, oldValue, newValue, remote) => {
            result.sharedEvents.push({ oldUndefined: oldValue === undefined, newUndefined: newValue === undefined, remote });
        });
        globalThis.__compatPublishUndefined = () => GM.setValue(sharedKey, undefined);
        result.noFrames = globalThis.__compatNoFrames || 0;
        result.done = true;
    } catch (error) { result.error = String(error); }
})();
