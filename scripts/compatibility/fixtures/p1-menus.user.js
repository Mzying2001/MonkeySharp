// ==UserScript==
// @name P1 frame menus
// @match http://127.0.0.1/*
// @grant GM.registerMenuCommand
// @run-at document-end
// ==/UserScript==
(async function () {
    const main = self === top;
    const receive = (name, event) => {
        const owner = top.document.documentElement;
        const events = JSON.parse(owner.dataset.menuEvents || '[]');
        events.push({ name, main, type: event.type, key: event.key, ctrlKey: event.ctrlKey, shiftKey: event.shiftKey,
            altKey: event.altKey, button: event.button, isTrusted: event.isTrusted });
        owner.dataset.menuEvents = JSON.stringify(events);
    };
    await GM.registerMenuCommand('P1 frame only', event => receive('frame', event), { clickedFrame: true });
    await GM.registerMenuCommand('P1 all frames', event => receive('all', event), { clickedFrame: false });
    await GM.registerMenuCommand('P1 mixed option', event => receive('mixed', event), { clickedFrame: main });
    document.body.style.cssText = 'font:24px sans-serif;padding:30px;background:' + (main ? '#e7f0ff' : '#ffe8c0');
    document.body.textContent = main ? 'Main frame menu fixture' : 'Child frame: right click here';
    if (main) {
        document.documentElement.dataset.menuEvents = '[]';
        const child = document.createElement('iframe'); child.src = '/p1-menu-child'; child.style.cssText = 'display:block;width:700px;height:260px;margin:30px 0'; document.body.appendChild(child);
        const button = document.createElement('button'); button.textContent = 'Save menu evidence';
        button.onclick = () => fetch('/evidence', { method: 'POST', body: JSON.stringify({ schemaVersion: 1, caseId: 'p1-menus',
            immediateReturn: { modern: 'promise' }, promise: {}, effects: {}, events: JSON.parse(document.documentElement.dataset.menuEvents || '[]') }) });
        document.body.appendChild(button);
    }
})();
