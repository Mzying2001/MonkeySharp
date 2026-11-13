// ==UserScript==
// @name Compatibility partition cookies
// @match http://127.0.0.1:__PORT__/compat/partitions
// @match https://127.0.0.1/*
// @grant GM.cookie
// @grant GM_cookie
// @grant GM.xmlHttpRequest
// @connect 127.0.0.1
// @run-at document-end
// ==/UserScript==
globalThis.__runPartitionProbe = async function (origin, supported, ancestors) {
    const result = globalThis.__partitionCase = {checks:{}};
    const base = {url:origin+'/',name:'partition-proof',path:'/',secure:true,sameSite:'no_restriction'};
    const a = {topLevelSite:'https://top-a.test'};
    const b = {topLevelSite:'https://top-b.test'};
    const list = key => GM.cookie.list({...base,...(key === null ? {} : {partitionKey:key})});
    try {
        if (!supported) {
            for (const operation of ['list','set','delete']) {
                let rejected = false;
                try { await GM.cookie[operation]({...base,value:'forbidden',partitionKey:a}); }
                catch (error) { rejected = error.code === 'MSP006_NOT_SUPPORTED'; }
                result.checks[operation+'Rejected'] = rejected;
            }
        } else {
            result.checks.capability = GM.info.compatibility.capabilities['GM.cookie.partitionKey'] ===
                (ancestors ? 'available:serializable-keys' : 'available:without-ancestor-dimension');
            await GM.cookie.set({...base,value:'plain'});
            await GM.cookie.set({...base,value:'a',partitionKey:a});
            await GM.cookie.set({...base,value:'b',partitionKey:b});
            result.checks.defaults = (await list(null)).length === 1 && (await list(null))[0].value === 'plain';
            result.checks.partitionA = (await list(a))[0].value === 'a';
            result.checks.partitionB = (await list(b))[0].value === 'b';
            const site = {topLevelSite:'https://a.b.example.co.uk:9443/path'};
            await GM.cookie.set({...base,value:'normalized',partitionKey:site});
            const normalized = await list(site);
            result.checks.siteNormalization = normalized.length === 1 && normalized[0].partitionKey.topLevelSite === 'https://example.co.uk';
            await GM.cookie.delete({...base,partitionKey:site});
            result.checks.all = (await list({})).length === 3;
            await GM.cookie.set({...base,value:'a2',partitionKey:a});
            result.checks.updated = (await list(a))[0].value === 'a2';
            const legacy = await new Promise((resolve,reject) => GM_cookie.list({...base,partitionKey:a}, (cookies,error) => error ? reject(error) : resolve(cookies)));
            result.checks.legacy = legacy[0].value === 'a2';
            await GM.cookie.delete(base);
            result.checks.normalDeletePreservesPartitions = (await list(null)).length === 0 && (await list({})).length === 2;
            await GM.cookie.set({...base,value:'plain'});
            await GM.cookie.delete({...base,partitionKey:a});
            const left = await list({});
            result.checks.partitionDeleteIsExact = left.length === 2 && left.some(cookie=>cookie.value==='plain') && left.some(cookie=>cookie.value==='b');
            const self = {topLevelSite:'https://127.0.0.1'};
            if (ancestors) {
                await GM.cookie.set({...base,value:'same-site',partitionKey:self});
                await GM.cookie.set({...base,value:'ancestor',partitionKey:{...self,hasCrossSiteAncestor:true}});
                result.checks.ancestorIdentities = (await list(self)).length === 2;
                await GM.cookie.delete({...base,partitionKey:self});
                result.checks.defaultDeleteAncestor = (await list(self)).length === 1 && (await list(self))[0].value === 'same-site';
                await GM.cookie.delete({...base,partitionKey:{...self,hasCrossSiteAncestor:false}});
            } else {
                let rejected = false;
                try { await GM.cookie.set({...base,value:'invalid-ancestor',partitionKey:{...self,hasCrossSiteAncestor:true}}); }
                catch (error) { rejected = error.code === 'MSP006_NOT_SUPPORTED'; }
                result.checks.ancestorUnsupported = rejected;
            }
            await GM.cookie.delete({...base,partitionKey:b});
            await GM.cookie.delete(base);
            result.checks.cleaned = (await list({})).length === 0;
        }
        let requestRejected = false;
        try { await GM.xmlHttpRequest({url:origin+'/partition-receive',cookiePartition:a}); }
        catch (error) { requestRejected = error.code === 'MSP006_NOT_SUPPORTED'; }
        result.checks.requestSelectorRejected = requestRejected;
        result.done = true;
    } catch (error) { result.error = String(error && error.stack || error); result.done = true; }
};
