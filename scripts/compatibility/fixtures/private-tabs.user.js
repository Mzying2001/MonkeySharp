// ==UserScript==
// @name Private tab context reference
// @match http://127.0.0.1/*
// @grant GM_getValue
// @grant GM_setValue
// @grant GM_openInTab
// @grant GM.openInTab
// @run-at document-end
// @run-in normal-tabs
// @run-in incognito-tabs
// ==/UserScript==
(async function () {
    const r = {schemaVersion:1,caseId:'private-tabs',immediateReturn:{},promise:{},events:[],effects:{}};
    try {
        if (location.pathname === '/private-check') {
            fetch('/reload-evidence',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({isIncognito:GM.info.isIncognito,fromPrivate:GM_getValue('private-context-child',null)})});
            return;
        }
        if (location.pathname === '/private-child') {
            const normal = GM_getValue('private-context-normal', null);
            GM_setValue('private-context-child', 'shared-from-private');
            fetch('/private-evidence',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({isIncognito:GM.info.isIncognito,normal,readback:GM_getValue('private-context-child')})});
            return;
        }
        GM_setValue('private-context-normal', 'shared-from-normal');
        const opened = GM.openInTab(location.origin + '/private-child', {incognito:true,active:false});
        r.immediateReturn.open = typeof opened?.then;
        const handle = await opened;
        r.effects.normalIncognito = GM.info.isIncognito;
        const deadline = Date.now() + 10000;
        while (GM_getValue('private-context-child', null) !== 'shared-from-private' && Date.now() < deadline) await new Promise(resolve => setTimeout(resolve,50));
        r.effects.fromPrivate = GM_getValue('private-context-child', null);
        r.effects.modernFromPrivate = await GM.getValue('private-context-child', null);
        handle.close();
    } catch (error) { r.effects.error = String(error); }
    fetch('/evidence',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(r)});
})();
