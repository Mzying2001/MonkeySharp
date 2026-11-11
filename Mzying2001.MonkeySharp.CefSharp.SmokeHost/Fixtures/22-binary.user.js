// ==UserScript==
// @name Binary browser
// @match http://127.0.0.1/compat/binary
// @grant GM.xmlHttpRequest
// @grant GM.download
// @connect 127.0.0.1
// @run-at document-end
// ==/UserScript==
(async function () {
    const r = globalThis.__binary = {};
    try {
        const bytes = Uint8Array.from({ length: 350003 }, (_, i) => i % 251);
        const blob = new Blob([bytes], { type: 'application/octet-stream' });
        const file = new File([blob], 'file-bytes.bin');
        const url = URL.createObjectURL(blob);
        try {
            const context = { source: 'original' };
            const events = [];
            const local = await GM.xmlHttpRequest({ url: blob, responseType: 'arraybuffer', context,
                onload(response) { r.callbackThis = this === response; r.context = response.context === context; events.push('load'); },
                onloadend() { events.push('end'); } });
            r.blob = new Uint8Array(local.response).every((b, i) => b === bytes[i]);
            r.events = events.join(',') === 'load,end';
            const fromUrl = await GM.xmlHttpRequest({ url, responseType: 'blob' });
            r.url = fromUrl.response.size === bytes.length;
            const fromFile = await GM.xmlHttpRequest({ url: new File(['file text'], 'text.txt') });
            r.file = fromFile.responseText === 'file text';
            const echo = await GM.xmlHttpRequest({ url: new URL('/echo', location.href), method: 'POST', data: blob, responseType: 'arraybuffer' });
            r.echo = echo.response.byteLength === bytes.length && new Uint8Array(echo.response).every((b, i) => b === bytes[i]);
            if (!r.echo) throw new Error('echo length ' + echo.response.byteLength + ' expected ' + bytes.length);
            const form = new FormData(); form.append('text', 'form value'); form.append('binary', new File(['file value'], 'file.txt'));
            const multi = await GM.xmlHttpRequest({ url: '/echo', method: 'POST', data: form });
            r.multipart = multi.responseText.includes('name="text"') && multi.responseText.includes('filename="file.txt"') && multi.responseText.includes('file value');
            const params = await GM.xmlHttpRequest({ url: '/echo', method: 'POST', data: new URLSearchParams({ key: 'a b' }) });
            r.searchParams = params.responseText === 'key=a+b';
            const latin = await GM.xmlHttpRequest({ url: '/compat/latin' }); r.charset = latin.responseText === 'caf\u00e9';
            const prefixes = [];
            const progressive = await GM.xmlHttpRequest({ url: '/compat/progress', onprogress(value) {
                prefixes.push(value.responseText);
                r.progressState = value.readyState === 3 && value.response === value.responseText;
            } });
            r.progress = prefixes.includes('a') && progressive.responseText === 'a\u4f60\u597d' && !prefixes.some(text => text.includes('\ufffd'));
            r.progressGzip = (await GM.xmlHttpRequest({ url: '/compat/progress-gzip' })).responseText === 'a\u4f60\u597d';
            let partialAborts = 0, partialLoads = 0;
            const partial = GM.xmlHttpRequest({ url: '/compat/progress', onprogress() { partial.abort(); },
                onabort() { partialAborts++; }, onload() { partialLoads++; } });
            r.progressAbort = await partial.then(() => false, () => partialAborts === 1 && partialLoads === 0);
            let partialTimeouts = 0;
            r.progressTimeout = await GM.xmlHttpRequest({ url: '/compat/progress', timeout: 150,
                ontimeout() { partialTimeouts++; } }).then(() => false, () => partialTimeouts === 1);
            const html = await GM.xmlHttpRequest({ url: '/compat/html', responseType: 'document' }); r.html = html.response.title === 'parsed html';
            const boundary = 'x'.repeat(65535) + '\u4f60\u597d';
            const utf8 = await GM.xmlHttpRequest({ url: '/echo', method: 'POST', data: boundary }); r.decoderBoundary = utf8.responseText === boundary;
            let ended = false;
            const stream = await GM.xmlHttpRequest({ url: '/echo', method: 'POST', data: blob, responseType: 'stream', onloadend() { ended = true; } });
            await new Promise(resolve => setTimeout(resolve, 100));
            const reader = stream.response.getReader(); let offset = 0, good = true, max = 0;
            for (;;) { const part = await reader.read(); if (part.done) break; max = Math.max(max, part.value.length); for (const b of part.value) good = good && b === bytes[offset++]; }
            r.stream = good && offset === bytes.length && max <= 65536; r.streamCompletion = ended;
            const damaged = await GM.xmlHttpRequest({ url: '/compat/truncated', responseType: 'stream' });
            const damagedReader = damaged.response.getReader();
            r.streamFailure = false;
            try { while (!(await damagedReader.read()).done) {} } catch (_) { r.streamFailure = true; }
            await GM.download({ url: blob, name: 'blob-bytes.bin' });
            await GM.download({ url: file });
            await GM.download({ url, name: 'url-bytes.bin' });
            const canceled = GM.download({ url: blob, name: 'canceled-blob.bin' }); canceled.abort();
            r.abort = await canceled.then(() => false, () => true);
        } finally { URL.revokeObjectURL(url); }
        r.done = true;
    } catch (e) { r.error = String(e && e.stack || e); r.done = true; }
})();
