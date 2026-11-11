// ==UserScript==
// @name P1 stream request body
// @match http://127.0.0.1/*
// @grant GM.xmlHttpRequest
// @connect 127.0.0.1
// @run-at document-end
// ==/UserScript==
(async function () {
    const r = { schemaVersion: 1, caseId: 'p1-stream-body', immediateReturn: {}, promise: {}, events: [], effects: {} };
    try {
        const bytes = Uint8Array.from({ length: 150003 }, (_, index) => index % 251);
        const origin = 'https://127.0.0.1:__TLS_PORT__';
        r.effects.expectedHash = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes)), b => b.toString(16).padStart(2, '0')).join('');
        function make() {
            let sent = false;
            return new ReadableStream({ pull(controller) {
                r.events.push({ name: 'pull' });
                if (sent) controller.close(); else { sent = true; controller.enqueue(bytes); }
            } }, { highWaterMark: 0 });
        }
        const body = make();
        const operation = GM.xmlHttpRequest({ url: origin + '/p1-body', method: 'POST', data: body });
        r.immediateReturn.promise = typeof operation.then;
        r.events.push({ name: 'returned' });
        r.effects.upload = JSON.parse((await operation).responseText);
        r.effects.locked = body.locked;
        try { r.effects.redirect = JSON.parse((await GM.xmlHttpRequest({ url: origin + '/p1-body-redirect', method: 'POST', data: make() })).responseText); }
        catch (error) { r.effects.redirectError = { status: error.status, statusText: error.statusText }; }
        r.effects.empty = JSON.parse((await GM.xmlHttpRequest({ url: origin + '/p1-body', method: 'POST',
            data: new ReadableStream({ start(controller) { controller.close(); } }) })).responseText);
        for (const kind of ['locked', 'disturbed', 'producer']) {
            const stream = new ReadableStream({ pull(controller) {
                if (kind === 'producer') throw new Error('producer failed');
                controller.enqueue(new Uint8Array([1])); controller.close();
            } }, { highWaterMark: 0 });
            const reader = kind === 'producer' ? null : stream.getReader();
            if (kind === 'disturbed') { await reader.read(); reader.releaseLock(); }
            try { const value = await Promise.race([GM.xmlHttpRequest({ url: origin + '/p1-body', method: 'POST', data: stream }),
                new Promise(resolve => setTimeout(() => resolve('pending'), 1500))]); r.promise[kind] = { state: value === 'pending' ? 'pending' : 'fulfilled' }; }
            catch (error) { r.promise[kind] = { state: 'rejected', error: String(error) }; }
            if (kind === 'locked') reader.releaseLock();
        }
    } catch (error) { r.effects.error = { text: String(error), keys: Object.getOwnPropertyNames(error), message: error.message, error: error.error, details: error.details, status: error.status, statusText: error.statusText, responseText: error.responseText }; }
    await fetch('/evidence', { method: 'POST', body: JSON.stringify(r) });
})();
