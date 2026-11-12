// ==UserScript==
// @name Compatibility cookie CRUD
// @match http://127.0.0.1:__PORT__/compat/cookie
// @match https://127.0.0.1/*
// @match https://localhost/*
// @match https://*.monkeysharp.test/*
// @grant GM.cookie
// @grant GM.xmlHttpRequest
// @connect 127.0.0.1
// @connect localhost
// @connect cookie.monkeysharp.test
// @run-at document-end
// @grant GM_cookie
// ==/UserScript==
(async function () {
    const result = globalThis.__compatCase = { checks: {} };
    const legacy = (operation, details) => new Promise((resolve, reject) => GM_cookie[operation](details, (value, error) => {
        if (operation === 'list') { if (error) reject(new Error(error)); else resolve(value); }
        else if (value) reject(new Error(value)); else resolve();
    }));
    try {
        const details = { name: 'compat-cookie', value: 'first', path: '/compat/', sameSite: 'lax', httpOnly: true };
        result.checks.setUndefined = await GM.cookie.set(details) === undefined;
        const first = (await GM.cookie.list({ name: details.name }))[0];
        result.checks.setList = first.value === 'first' && first.sameSite === 'lax' && first.hostOnly && first.session && first.httpOnly;
        await legacy('set', Object.assign({}, details, { value: 'changed' }));
        result.checks.legacy = (await legacy('list', { name: details.name }))[0].value === 'changed';
        const echo = await GM.xmlHttpRequest({ url: location.origin + '/compat/cookie-echo' });
        const anonymous = await GM.xmlHttpRequest({ url: location.origin + '/compat/cookie-echo', anonymous: true });
        result.checks.context = echo.responseText.includes('compat-cookie=changed') && !anonymous.responseText.includes('compat-cookie=');
        result.checks.noListeners = typeof GM.cookie.addListener === 'undefined' && typeof GM_cookie.removeListener === 'undefined';
        await GM.cookie.set({ name: 'same-path', value: 'root', path: '/' });
        await GM.cookie.set({ name: 'same-path', value: 'deep', path: '/compat/' });
        await GM.cookie.delete({ name: 'same-path' });
        const remaining = await GM.cookie.list({ name: 'same-path' });
        result.checks.preciseDelete = remaining.length === 1 && remaining[0].value === 'root';
        await GM.cookie.delete({ name: 'same-path' });
        await legacy('delete', details);
        result.checks.deleted = !(await GM.cookie.list({ name: details.name })).length;
        document.cookie = 'p0Same=jar; Path=/'; document.cookie = 'p0Jar=jar; Path=/';
        result.explicitCookies = {};
        for (const [name, options] of Object.entries({
            jar: {}, header: { headers: { Cookie: 'p0Same=header; p0Header=h' } },
            details: { cookie: 'p0Same=details; p0Details=d' },
            both: { headers: { Cookie: 'p0Same=header; p0Header=h' }, cookie: 'p0Same=details; p0Details=d' },
            values: { cookie: 'p0Same=first; p0Same=last; p0Empty=; p0Equals=a=b=c' },
            anonymous: { anonymous: true, headers: { Cookie: 'p0Same=header' }, cookie: 'p0Same=details; p0Explicit=e' }
        })) {
            try { result.explicitCookies[name] = (await GM.xmlHttpRequest({ url: location.origin + '/compat/cookie-echo', ...options })).responseText; }
            catch (error) { result.explicitCookies[name] = error.code; }
        }
        result.checks.explicitCookieRejected = ['header', 'details', 'both', 'values', 'anonymous'].every(name => result.explicitCookies[name] === 'MSP006_NOT_SUPPORTED');
        result.checks.actualJarRetained = result.explicitCookies.jar.includes('p0Same=jar') && result.explicitCookies.jar.includes('p0Jar=jar');
        try { await GM.cookie.list({firstPartyDomain:'example.com'}); result.checks.unsupportedFirstParty = false; }
        catch (error) { result.checks.unsupportedFirstParty = error.code === 'MSP006_NOT_SUPPORTED'; }
        globalThis.__httpsCookieProbe = async function (base) {
            try {
                // A test hostname avoids Chromium's version-dependent HTTP-loopback Secure exception.
                base = base.replace('127.0.0.1', 'cookie.monkeysharp.test');
                const secure = {url:base+'/cookie',name:'secure-cookie',value:'https-only',secure:true,httpOnly:true,sameSite:'no_restriction'};
                await GM.cookie.set(secure);
                const listed = (await GM.cookie.list({url:secure.url,name:secure.name}))[0];
                result.checks.httpsState = listed.secure && listed.httpOnly && listed.sameSite === 'no_restriction';
                const tls = await GM.xmlHttpRequest({url:secure.url});
                const plain = await GM.xmlHttpRequest({url:location.origin.replace('127.0.0.1','cookie.monkeysharp.test')+'/cookie'});
                result.observations = { tlsStatus:tls.status, tlsCookies:tls.responseText, plainCookies:plain.responseText };
                result.checks.httpsSend = tls.responseText.includes('secure-cookie=https-only') && !plain.responseText.includes('secure-cookie=');
                await GM.cookie.set({url:secure.url,name:'persistent',value:'strict',secure:true,sameSite:'strict',expirationDate:Math.floor(Date.now()/1000)+3600});
                const persistent = (await GM.cookie.list({url:secure.url,name:'persistent'}))[0];
                result.checks.expirySameSite = !persistent.session && persistent.expirationDate > Date.now()/1000 && persistent.sameSite === 'strict';
                const domainUrl = base.replace('127.0.0.1', 'cookie.monkeysharp.test') + '/cookie';
                await GM.cookie.set({url:domainUrl,name:'domain-cookie',value:'domain',domain:'.monkeysharp.test',secure:true,sameSite:'lax'});
                const domain = (await GM.cookie.list({url:domainUrl,name:'domain-cookie'}))[0];
                result.checks.domainCookie = !domain.hostOnly && domain.domain === '.monkeysharp.test';
                await GM.cookie.delete({url:domainUrl,name:'domain-cookie',domain:'.monkeysharp.test',path:'/'});
                await GM.cookie.delete(secure);
                await GM.cookie.delete({url:secure.url,name:'persistent'});
                result.checks.httpsDeleted = !(await GM.cookie.list({url:secure.url,name:secure.name})).length;
                try { await GM.cookie.set({url:secure.url,name:'__Host-invalid',value:'no',path:'/wrong',secure:true}); result.checks.engineRejected = false; }
                catch (error) { result.checks.engineRejected = true; }
                result.httpsDone = true;
            } catch (error) { result.error = String(error); result.httpsDone = true; }
        };
        result.done = true;
    } catch (error) { result.error = String(error); }
})();
