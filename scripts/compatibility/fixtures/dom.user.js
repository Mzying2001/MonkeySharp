// ==UserScript==
// @name DOM failure contract
// @match http://127.0.0.1/*
// @grant GM_addElement
// @grant GM.addElement
// @run-at document-end
// ==/UserScript==
(async function () {
    const r = { schemaVersion: 1, caseId: 'dom', immediateReturn: {}, promise: {}, events: [], effects: {} };
    const describe = value => value === null ? 'null' : value && value.nodeType ? value.nodeName : typeof value;
    const parent = document.createElement('div'); document.body.appendChild(parent);
    const cases = {
        valid: () => ['span', { textContent: 'live' }],
        invalidTag: () => ['bad tag', {}],
        invalidAttribute: () => ['span', { 'bad name': 'value' }],
        explicitParent: () => [parent, 'span', { textContent: 'child' }],
        invalidParent: () => [document.createTextNode('text'), 'span', {}],
        emptyTag: () => ['', {}],
        invalidType: () => [42, {}]
    };
    for (const [name, args] of Object.entries(cases)) {
        try { r.immediateReturn[name] = describe(GM_addElement(...args())); }
        catch (e) { r.immediateReturn[name] = 'throws:' + e.name; }
        try {
            const result = GM.addElement(...args());
            r.events.push({ name, then: typeof result?.then });
            r.promise[name] = { state: 'fulfilled', value: describe(await result) };
        } catch (e) { r.promise[name] = { state: 'rejected', error: e.name }; }
    }
    const head = document.head; head.remove();
    r.effects.withoutHead = describe(GM_addElement('span', { id: 'without-head' }));
    r.effects.withoutHeadParent = document.getElementById('without-head')?.parentNode.nodeName;
    const body = document.body; body.remove();
    try { r.effects.withoutHeadBody = describe(GM_addElement('span', {})); }
    catch (e) { r.effects.withoutHeadBody = 'throws:' + e.name; }
    document.documentElement.append(head, body);
    r.effects.continued = true;
    fetch('/evidence', { method: 'POST', body: JSON.stringify(r) });
})();
