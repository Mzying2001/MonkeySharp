// ==UserScript==
// @name Page helper reference
// @match http://127.0.0.1/*
// @grant GM_addElement
// @grant GM.addElement
// @grant GM_addStyle
// @grant GM.addStyle
// @grant GM_log
// @grant GM.log
// @grant GM_openInTab
// @grant GM.openInTab
// @run-at document-end
// ==/UserScript==
(async function () {
    const r = { schemaVersion: 1, caseId: 'page-helpers', immediateReturn: {}, promise: {}, events: [], effects: {} };
    try {
        const circular = {}; circular.self = circular;
        try { GM_log(circular, 'second', 3); r.effects.circularLog = true; } catch (_) { r.effects.circularLog = false; }
        r.immediateReturn.legacyLog = typeof GM_log({ first: 1 }, 'second', 3);
        const logged = GM.log({ first: 1 }, 'second', 3);
        r.immediateReturn.modernLog = typeof logged.then;
        r.promise.logValue = typeof await logged;
        for (const tag of ['span', 'style', 'script', 'link', 'meta']) {
            const element = GM_addElement(tag, { id: 'helper-' + tag, textContent: tag === 'style' ? 'body { --helper: 7; }' : '' });
            r.effects[tag + 'Parent'] = element.parentNode.nodeName;
        }
        const style = GM_addStyle('body { --helper-style: 9; }');
        r.effects.styleNode = style.nodeName;
        r.effects.styleEffect = getComputedStyle(document.body).getPropertyValue('--helper-style').trim();
        const modern = GM.addElement('span', { textContent: 'modern' });
        r.immediateReturn.modernElement = typeof modern.then;
        r.effects.modernElement = (await modern).textContent;
        const legacy = GM_openInTab('/tab-helper-target', true);
        r.immediateReturn.legacyClose = typeof legacy.close();
        const tab = await GM.openInTab('/tab-helper-target', false);
        r.immediateReturn.modernClose = typeof tab.close();
        await new Promise(resolve => setTimeout(resolve, 800));
        r.effects.earlyClosed = legacy.closed && tab.closed;
        legacy.close(); tab.close();
        await new Promise(resolve => setTimeout(resolve, 800));
        r.effects.closed = legacy.closed && tab.closed;
    } catch (e) { r.effects.error = String(e && e.stack || e); }
    fetch('/evidence', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(r) });
})();
