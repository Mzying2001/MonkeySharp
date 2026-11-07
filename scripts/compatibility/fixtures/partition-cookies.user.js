// ==UserScript==
// @name Partition cookie reference
// @match http://127.0.0.1/*
// @match https://127.0.0.1/*
// @grant GM_cookie
// @grant GM.cookie
// @grant GM_xmlhttpRequest
// @connect 127.0.0.1
// @run-at document-end
// ==/UserScript==
(async function () {
    const r = {schemaVersion:1,caseId:'partition-cookies',immediateReturn:{},promise:{},events:[],effects:{}};
    const origin = 'https://127.0.0.1:__TLS_PORT__';
    const base = {url:origin+'/',name:'partition-proof',path:'/',secure:true,sameSite:'no_restriction'};
    const a = {topLevelSite:'https://top-a.test'};
    const b = {topLevelSite:'https://top-b.test'};
    const self = {topLevelSite:'https://127.0.0.1'};
    const list = key => GM.cookie.list({...base,...(key === null ? {} : {partitionKey:key})});
    try {
        await GM.cookie.set({...base,value:'plain'});
        await GM.cookie.set({...base,value:'a',partitionKey:a});
        await GM.cookie.set({...base,value:'b',partitionKey:b});
        r.effects.defaults = await list(null);
        r.effects.a = await list(a);
        r.effects.b = await list(b);
        r.effects.all = await list({});
        await GM.cookie.delete({...base,partitionKey:a});
        r.effects.afterDeleteA = await list({});
        await GM.cookie.delete(base);
        r.effects.afterDeletePlain = await list({});
        await GM.cookie.set({...base,value:'a',partitionKey:a});
        await GM.cookie.set({...base,value:'plain'});
        const response = await GM.xmlHttpRequest({url:origin+'/partition-echo',cookiePartition:a});
        r.effects.requestA = response.responseText;
        await GM.xmlHttpRequest({url:origin+'/partition-receive',cookiePartition:a});
        r.effects.receivedA = await GM.cookie.list({url:origin+'/',name:'partition-received',partitionKey:a});
        r.effects.afterRequest = await list({});
        await GM.cookie.set({...base,value:'same-site',partitionKey:self});
        await GM.cookie.set({...base,value:'cross-ancestor',partitionKey:{...self,hasCrossSiteAncestor:true}});
        r.effects.selfBefore = await list(self);
        await GM.cookie.delete({...base,partitionKey:self});
        r.effects.selfAfter = await list(self);
        r.immediateReturn.list = 'Promise';
    } catch (error) { r.effects.error = String(error && error.message || error); }
    fetch('/evidence',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(r)});
})();
