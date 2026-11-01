// ==UserScript==
// @name Compatibility real downloads
// @match http://127.0.0.1:__PORT__/compat/download
// @grant GM.download
// @run-at document-end
// ==/UserScript==
(async function () {
    const result = globalThis.__compatCase = { checks: {}, terminals: {} };
    const start = function (name, path, timeout) {
        const events = result.terminals[name] = [];
        let progress = 0;
        let handle;
        const completion = new Promise(function (resolve) {
            handle = GM_download({
                url: location.origin + path, name: name + '.bin', timeout: timeout,
                onprogress: function (event) {
                    if (event.loaded > 0) progress++;
                    if (name === 'cancel') handle.abort();
                },
                onload: function () { events.push('load'); resolve('load'); },
                onerror: function (error) { events.push(error.error); resolve(error.error); },
                ontimeout: function (error) { events.push(error.error); resolve(error.error); }
            });
        });
        return { completion: completion, progressed: function () { return progress > 0; }, handle: function () { return handle; } };
    };
    try {
        const promise = GM.download({ url: location.origin + '/compat/files/bytes', name: 'modern.bin' });
        result.checks.promise = typeof promise.then === 'function';
        const modern = await promise;
        result.checks.handle = !!modern.id && typeof modern.abort === 'function';
        const success = start('legacy', '/compat/files/bytes');
        result.checks.completed = await success.completion === 'load' && success.progressed();
        const canceled = start('cancel', '/compat/files/slow');
        result.checks.canceled = await canceled.completion === 'not_succeeded' && canceled.progressed();
        await canceled.handle().abort();
        const timed = start('timeout', '/compat/files/stalled', 200);
        result.checks.timeout = await timed.completion === 'timeout';
        const failed = start('failure', '/compat/files/missing');
        result.checks.failure = await failed.completion === 'not_succeeded';
        result.checks.singleTerminal = Object.keys(result.terminals).every(function (key) { return result.terminals[key].length === 1; });
        result.done = true;
    } catch (error) { result.error = String(error); }
})();
