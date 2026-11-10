// ==UserScript==
// @name Page helpers browser
// @match http://127.0.0.1/compat/page-helpers
// @grant GM_addStyle
// @grant GM.addStyle
// @grant GM_addElement
// @grant GM.addElement
// @grant GM_log
// @grant GM.log
// @run-at document-end
// ==/UserScript==
(async function () {
    const r = globalThis.__pageHelpers = {};
    try {
        const cyclic = {}; cyclic.self = cyclic;
        const original = console.log;
        let logs = 0, argumentsPreserved = true;
        console.log = function (...args) { logs++; argumentsPreserved = argumentsPreserved && args[0] === cyclic && args[1] === 'second'; original.apply(console, args); };
        try {
            r.legacyLog = GM_log(cyclic, 'second') === undefined;
            r.modernLog = await GM.log(cyclic, 'second', 3) === undefined;
        } finally { console.log = original; }
        r.logObjects = logs === 2 && argumentsPreserved;
        const capabilities = GM_info.compatibility.capabilities;
        r.capabilityFacts = capabilities['execution.documentStart'] === 'best-effort' &&
            capabilities['execution.oopif'] === 'unsupported' && capabilities['execution.bfcache'] === 'fresh-navigation-required' &&
            capabilities['GM.xmlHttpRequest.cookie'] === 'unsupported:cef-urlrequest' &&
            capabilities['GM.xmlHttpRequest.cookiePartition'] === 'unsupported' &&
            capabilities['GM.registerMenuCommand.clickedFrame'] === 'unsupported';
        const style = GM_addStyle('body { --page-helper: 19; }');
        r.style = style.nodeName === 'STYLE' && style.parentNode === document.head && getComputedStyle(document.body).getPropertyValue('--page-helper').trim() === '19';
        const node = GM_addElement('span', { textContent: 'legacy' });
        r.defaultParent = node.parentNode === document.head && node.textContent === 'legacy';
        const promise = GM.addElement(document.body, 'input', { value: 'value', checked: true, 'data-helper': 'present' });
        const modern = await promise;
        r.modernNode = typeof promise.then === 'function' && modern.parentNode === document.body && modern.value === 'value' && modern.checked && modern.dataset.helper === 'present';
        r.invalidTag = GM_addElement('bad tag', {}) === null && await GM.addElement('bad tag', {}) === null;
        r.invalidAttribute = GM_addElement('span', { 'bad name': 'x' }) === null && await GM.addElement('span', { 'bad name': 'x' }) === null;
        r.invalidParent = GM_addElement(document.createTextNode('text'), 'span', {}) === null && await GM.addElement(document.createTextNode('text'), 'span', {}) === null;
        r.emptyTag = GM_addElement('', {}) === null && await GM.addElement('', {}) === null;
        const head = document.head, body = document.body;
        head.remove();
        r.bodyFallback = GM_addElement('span', {}).parentNode === body;
        body.remove();
        try {
            const fallback = GM_addElement('span', { textContent: 'fallback' });
            const fallbackStyle = await GM.addStyle('html { --fallback: 13; }');
            r.noHeadOrBody = fallback.parentNode === document.documentElement && fallbackStyle.parentNode === document.documentElement;
        } finally { document.documentElement.appendChild(head); document.documentElement.appendChild(body); }
        r.done = true;
    } catch (error) { r.error = String(error && error.stack || error); r.done = true; }
})();
