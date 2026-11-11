using Mzying2001.MonkeySharp.Demo.ViewModels;
using Mzying2001.MonkeySharp.Demo.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Demo.Runtime
{
    internal static class PrivateContextSmokeTest
    {
        internal static async Task RunAsync(DemoRuntime runtime, LocalHttpFixture fixture, Dictionary<string, bool> checks, Func<int> prompts)
        {
            var script = await runtime.Repository.InstallAsync(Source.Replace("__BASE__", fixture.BaseUrl), "application://private-context", true, CancellationToken.None);
            foreach (var mode in new[] { "normal", "incognito" })
                await runtime.Repository.InstallAsync("// ==UserScript==\n// @name privacy-" + mode + "\n// @match " + fixture.BaseUrl +
                    "/private*\n// @run-in " + mode + "-tabs\n// @grant none\n// ==/UserScript==\ndocument.documentElement.dataset." + mode + "Context='ran';",
                    "application://privacy-condition", true, CancellationToken.None);
            var normal = runtime.MainWindow.NewTab(fixture.BaseUrl + "/private-normal", false);
            await Ready(normal);
            var normalContext = runtime.GetTabContext(normal);
            var decisions = (await runtime.PermissionDecisions.ListAsync(CancellationToken.None)).Count(item => item.ScriptKey == script.ScriptKey.ToString());
            var beforePrompts = prompts();
            await DemoSmokeTest.Evaluate(normal, "(window.__openPrivate(), true)");
            await DemoSmokeTest.WaitAsync(() => Task.FromResult(runtime.MainWindow.Tabs.Any(tab => tab.IsIncognito)));
            var first = runtime.MainWindow.Tabs.Single(tab => tab.IsIncognito);
            await Ready(first);
            var privateContext = runtime.GetTabContext(first);
            checks["privateNativeContext"] = privateContext.IsIncognito && !privateContext.RequestContext.IsGlobal &&
                privateContext.RequestContext.CachePath == string.Empty && privateContext.RequestContext != normalContext.RequestContext;
            checks["privateInfoRunIn"] = await DemoSmokeTest.EvaluateBoolean(first,
                "__privateCase.incognito === true && document.documentElement.dataset.incognitoContext === 'ran' && !document.documentElement.dataset.normalContext");
            checks["normalInfoRunIn"] = await DemoSmokeTest.EvaluateBoolean(normal,
                "__privateCase.incognito === false && document.documentElement.dataset.normalContext === 'ran' && !document.documentElement.dataset.incognitoContext");
            checks["privateCookieNetworkIsolation"] = await DemoSmokeTest.EvaluateBoolean(first,
                "__privateCase.cookieBefore === null && __privateCase.sentPrivate && __privateCase.baseline === 'normal'");
            checks["privatePermissionIsolation"] = prompts() > beforePrompts &&
                (await runtime.PermissionDecisions.ListAsync(CancellationToken.None)).Count(item => item.ScriptKey == script.ScriptKey.ToString()) == decisions;
            checks["privateCacheIsolation"] = fixture.RequestCount("/private-cache.js") == 2;
            var second = runtime.MainWindow.NewTab(fixture.BaseUrl + "/private-second", false, first);
            await Ready(second);
            checks["privateSessionShared"] = second.IsIncognito && runtime.GetTabContext(second).RequestContext == privateContext.RequestContext &&
                await DemoSmokeTest.EvaluateBoolean(second, "__privateCase.baseline === 'private' && __privateCase.previous === 'private-only-proof' && __privateCase.cookieBefore === 'private'");
            checks["privateCacheShared"] = fixture.RequestCount("/private-cache.js") == 2;
            checks["privateTabDataIsolation"] = (await privateContext.TabState.GetAllAsync(script.ScriptKey, CancellationToken.None)).Values.All(value => value.Contains("privateState")) &&
                (await normalContext.TabState.GetAllAsync(script.ScriptKey, CancellationToken.None)).Values.All(value => !value.Contains("privateState"));
            checks["privateWritesNotPersistent"] = !(await normalContext.Values.GetAsync(script.ScriptKey.ToString(), "private-write", CancellationToken.None)).Exists &&
                (await normalContext.Values.GetAsync(script.ScriptKey.ToString(), "baseline", CancellationToken.None)).Value.EncodedValue == "snormal";
            var firstView = (BrowserTabView)first.Browser;
            await DemoSmokeTest.Evaluate(normal, "(window.__closePrivate(), true)");
            await DemoSmokeTest.WaitAsync(() => Task.FromResult(first.IsClosed));
            await firstView.DisposeAsync();
            checks["privateLivesUntilLastTab"] = runtime.HasPrivateContext;
            await Close(runtime, second);
            checks["privateContextReleased"] = !runtime.HasPrivateContext;
            beforePrompts = prompts();
            await DemoSmokeTest.Evaluate(normal, "(window.__openPrivate(), true)");
            await DemoSmokeTest.WaitAsync(() => Task.FromResult(runtime.MainWindow.Tabs.Any(tab => tab.IsIncognito)));
            var fresh = runtime.MainWindow.Tabs.Single(tab => tab.IsIncognito);
            await Ready(fresh);
            checks["privateFreshSession"] = runtime.GetTabContext(fresh).RequestContext != privateContext.RequestContext &&
                await DemoSmokeTest.EvaluateBoolean(fresh, "__privateCase.baseline === 'normal' && __privateCase.previous === null && __privateCase.cookieBefore === null");
            checks["privatePermissionCleared"] = prompts() > beforePrompts;
            checks["privateCacheCleared"] = fixture.RequestCount("/private-cache.js") == 3;
            await Close(runtime, fresh);
            checks["privateLogsNotPersisted"] = Directory.GetFiles(runtime.Paths.LogsDirectory).All(path => !ReadSharedLog(path).Contains("PRIVATE_LOG_PROOF_NOT_PERSISTED"));
            await Close(runtime, normal);
        }
        private static Task Ready(BrowserTabViewModel tab) => DemoSmokeTest.WaitAsync(async () => await DemoSmokeTest.EvaluateBoolean(tab, "window.__privateCase && window.__privateCase.ready"));
        private static string ReadSharedLog(string path)
        {
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(input)) return reader.ReadToEnd();
        }
        private static async Task Close(DemoRuntime runtime, BrowserTabViewModel tab)
        { var view = (BrowserTabView)tab.Browser; runtime.MainWindow.CloseTab(tab); await view.DisposeAsync(); }

        private const string Source = @"// ==UserScript==
// @name Private context smoke
// @match __BASE__/private*
// @grant GM_getValue
// @grant GM_setValue
// @grant GM_cookie
// @grant GM_xmlhttpRequest
// @grant GM_openInTab
// @grant GM_getTab
// @grant GM_saveTab
// @grant GM_getTabs
// @grant GM_log
// @connect 127.0.0.1
// @run-at document-end
// ==/UserScript==
(async function () {
    const result = window.__privateCase = {incognito:GM.info.isIncognito};
    const cookie = {url:'__BASE__/private-cookie',name:'context-cookie'};
    if (!result.incognito) {
        await GM.setValue('baseline','normal');
        await GM.cookie.set({...cookie,value:'normal',path:'/'});
        await GM.xmlHttpRequest({url:'__BASE__/headers'});
        await GM.saveTab({normalState:true});
        window.__openPrivate = async () => {window.__privateHandle = await GM.openInTab('__BASE__/private-main',{incognito:true,active:false});};
        window.__closePrivate = () => window.__privateHandle.close();
    } else {
        result.baseline = GM_getValue('baseline',null);
        result.previous = GM_getValue('private-write',null);
        result.cookieBefore = (await GM.cookie.list(cookie))[0]?.value || null;
        await GM.setValue('baseline','private'); await GM.setValue('private-write','private-only-proof');
        await GM.cookie.set({...cookie,value:'private',path:'/'});
        const headers = (await GM.xmlHttpRequest({url:'__BASE__/headers'})).responseText;
        result.sentPrivate = headers.includes('context-cookie=private') && !headers.includes('context-cookie=normal') && !headers.includes('demo-cookie=ready');
        await GM.saveTab({privateState:true});
        GM_log('PRIVATE_LOG_PROOF_NOT_PERSISTED');
    }
    result.ready = true;
})().catch(error => { window.__privateCase.error = String(error); });";
    }
}
