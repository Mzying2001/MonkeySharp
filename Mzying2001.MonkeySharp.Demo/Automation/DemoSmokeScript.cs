namespace Mzying2001.MonkeySharp.Demo.Runtime
{
    internal static class DemoSmokeScript
    {
        internal const string Source = @"// ==UserScript==
// @name Demo all-services smoke
// @match __BASE__/page*
// @match https://127.0.0.1/*
// @connect 127.0.0.1
// @noframes
// @require __BASE__/dependency.js
// @resource text __BASE__/resource
// @grant GM_getValue
// @grant GM_setValue
// @grant GM_getValues
// @grant GM_setValues
// @grant GM_deleteValues
// @grant GM_listValues
// @grant GM_addValueChangeListener
// @grant GM_addStyle
// @grant GM_addElement
// @grant GM_getResourceText
// @grant GM_getResourceURL
// @grant GM_xmlhttpRequest
// @grant GM_registerMenuCommand
// @grant GM_notification
// @grant GM_setClipboard
// @grant GM_openInTab
// @grant GM_download
// @grant GM_getTab
// @grant GM_saveTab
// @grant GM_getTabs
// @grant GM_cookie
// @grant GM_webRequest
// @grant GM_audio
// @grant GM_info
// @grant GM_log
// @grant window.close
// @grant window.focus
// @run-at document-end
// ==/UserScript==
window.__demo = { runs: Number(sessionStorage.getItem('runs') || 0) + 1 };
sessionStorage.setItem('runs', String(window.__demo.runs));
(async function () {
    const result = window.__demo;
    result.restoredUndefined = Object.prototype.hasOwnProperty.call(GM_getValues(), 'demo-undefined') && GM_getValue('demo-undefined', 'fallback') === undefined;
    await GM.setValues({ 'demo-undefined': undefined, 'demo-nested': { absent: undefined, items: [undefined, NaN] } });
    await GM.setValue('demo', 42);
    result.storage = (await GM.getValue('demo')) === 42 && GM_getValue('demo') === 42 &&
        GM_getValue('demo-undefined', 'fallback') === undefined && (await GM.listValues()).includes('demo-undefined') &&
        !Object.prototype.hasOwnProperty.call(GM_getValue('demo-nested'), 'absent') && GM_getValue('demo-nested').items.every(value => value === null);
    result.resources = window.__demoDependency && GM_getResourceText('text') === 'demo-resource' && (await GM.getResourceUrl('text')).startsWith('data:');
    await GM.addStyle('body { color: rgb(20, 40, 60); }');
    await GM.addElement('div', { id: 'demo-marker', textContent: 'ready' });
    result.dom = !!document.getElementById('demo-marker');
    result.info = !!GM.info && GM.info.scriptWillUpdate === false && GM.info.isIncognito === false && GM.info.downloadMode === 'browser' && typeof GM.setClipboard === 'function';
    await GM.cookie.set({ url: location.href, name: 'demo-cookie', value: 'ready', path: '/' });
    result.cookie = (await GM.cookie.list({ url: location.href })).some(cookie => cookie.name === 'demo-cookie');
    result.xhr = (await GM.xmlHttpRequest({ url: '__BASE__/resource' })).responseText === 'demo-resource';
    await GM.audio.setMute({ isMuted: true });
    result.audio = (await GM.audio.getState()).isMuted === true;
    await new Promise((resolve, reject) => GM_audio.setMute({ isMuted: false }, error => error ? reject(error) : resolve()));
    const audioState = await GM.audio.getState();
    result.audio = result.audio && audioState.isMuted === false && !('isAudible' in audioState) && GM.info.compatibility.capabilities['GM.audio'] === 'partial:mute-only;audible-unavailable';
    const webRequestReturnsVoid = GM.webRequest([{ selector: '__BASE__/__monkeysharp_block__', action: { cancel: true } }]) === undefined;
    await new Promise(resolve => setTimeout(resolve, 200));
    try { await fetch('__BASE__/__monkeysharp_block__'); result.webRequest = false; } catch (_) { result.webRequest = webRequestReturnsVoid; }
    GM.webRequest([]);
    window.__testTabOptions = async function () {
        const tab = await GM.openInTab('/child', { active: false, insert: 0, setParent: true });
        result.optionsClosed = 0;
        tab.onclose = function () { result.optionsClosed++; };
        window.__closeOptionsTab = function () { tab.close(); tab.close(); };
    };
    window.__testWindowFocus = function () { return window.focus(); };
    window.__testWindowClose = function () { return window.close(); };
    window.__testCookiePartitions = async function () {
        result.partitionCapability = GM.info.compatibility.capabilities['GM.cookie.partitionKey'];
        const details = {url:'__BASE__'.replace(/^http:/,'https:')+'/',name:'wpf-partition-proof',value:'ready',path:'/',secure:true,sameSite:'no_restriction',partitionKey:{topLevelSite:'https://top-a.test'}};
        try {
            await GM.cookie.set(details);
            result.partitionCrud = (await GM.cookie.list(details))[0].value === 'ready';
            await GM.cookie.delete(details);
            result.partitionCrud = result.partitionCrud && (await GM.cookie.list(details)).length === 0;
        } catch (error) { result.partitionUnsupported = error.code === 'MSP006_NOT_SUPPORTED'; }
        result.partitionDone = true;
    };
    result.previousTabMarker = (await GM.getTab()).marker;
    await GM.saveTab({ marker: location.search });
    result.tabState = (await GM.getTab()).marker === location.search && Object.keys(await GM.getTabs()).length >= 1;
    await GM.addValueChangeListener('shared', function (key, oldValue, newValue, remote) { result.remote = remote && newValue === undefined; });
    window.__demoSetShared = function () { GM.setValue('shared', undefined); };
    if (location.search === '?primary') {
        await GM.registerMenuCommand('Demo smoke menu', function (event) { result.menu = true; result.menuInput = {type:event.type,key:event.key,altKey:event.altKey,isTrusted:event.isTrusted}; });
        const notification = GM.notification({ silent: true, title: 'Demo smoke notification', text: 'Click to complete lifecycle test', onclick: function () { result.notificationClick = true; }, ondone: function () { result.notificationDone = true; } });
        notification.then(function (clicked) { result.notificationResolved = clicked; });
        const child = GM_openInTab('__BASE__/child', { active: false, insert: true, setParent: true });
        setTimeout(function () { child.close(); setTimeout(function () { result.tabClosed = child.closed; }, 500); }, 500);
        const download = GM.download({ url: '__BASE__/download', name: 'demo-smoke.txt' });
        const completed = await download;
        result.download = typeof download.abort === 'function' && completed.loaded === 256 * 1024;
        window.__testSaveCancel = function () {
            GM.download({ url: '__BASE__/save-cancel', name: 'canceled.txt', saveAs: true })
                .then(function () { result.saveCanceled = false; }, function (error) { result.saveCanceled = error.error === 'not_succeeded'; });
        };
        window.__testTaggedNotification = function () {
            GM.notification({text:'old',title:'Tagged original',tag:'replace',silent:true}).then(value => result.taggedOld = value);
            GM.notification({text:'new',title:'Tagged replacement',tag:'replace',silent:true,url:'__BASE__/child',onclick:event => event.preventDefault()}).then(value => result.tagged = value);
        };
        window.__testClipboard = function () {
            GM.setClipboard('<b>Clipboard \u4f60\u597d</b>', {type:'html',mimetype:'text/html'}, function () { result.clipboardWritten = true; });
        };
        window.__testUrlNotification = function () {
            GM.notification({text:'url',title:'URL notification',silent:true,url:'__BASE__/child'}).then(value => result.urlNotification = value);
        };
        window.__testHighlightNotification = function () {
            GM.notification({highlight:true,silent:true}).then(value => result.highlightNotification = value);
        };
        window.__testTextClipboard = function () {
            GM_setClipboard('plain clipboard', 'text', function () { result.clipboardText = true; });
        };
    }
    result.ready = true;
    GM.log('Demo smoke ready');
})().catch(function (error) { window.__demo.error = String(error && error.stack || error); console.error(window.__demo.error); });";
    }
}
