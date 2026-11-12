using Mzying2001.MonkeySharp.Demo.Services;
using Mzying2001.MonkeySharp.Demo.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static Mzying2001.MonkeySharp.Demo.Runtime.SmokeBrowser;

namespace Mzying2001.MonkeySharp.Demo.Runtime
{
    internal static class NavigationMenuSmokeTest
    {
        internal static Task InstallAsync(DemoRuntime runtime, LocalHttpFixture fixture)
            => runtime.Repository.InstallAsync("// ==UserScript==\n// @name navigation-menu\n// @match " + fixture.BaseUrl +
                "/menu-page*\n// @match " + OtherSite(fixture) + "/menu-page*\n// @grant GM.registerMenuCommand\n// @run-at document-end\n// ==/UserScript==\n" +
                "(async function(){ window.__navigationMenuClicks = 0; await GM.registerMenuCommand('Navigation menu ' + location.host, function(){ window.__navigationMenuClicks++; }); window.__navigationMenuReady = true; })();",
                "application://navigation-menu-smoke", true, CancellationToken.None);

        internal static async Task RunAsync(DemoRuntime runtime, LocalHttpFixture fixture, Dictionary<string, bool> checks)
        {
            var main = runtime.MainWindow;
            var selected = main.SelectedTab;
            var firstUrl = fixture.BaseUrl + "/menu-page";
            var secondUrl = OtherSite(fixture) + "/menu-page";
            var tab = main.NewTab(firstUrl);
            var background = main.NewTab(firstUrl, false);
            try
            {
                var first = await CurrentMenuAsync(tab, firstUrl);
                var backgroundMenu = await CurrentMenuAsync(background, firstUrl);
                // Exercise the same command as typing a new URL into the address bar.
                main.Address = secondUrl;
                main.NavigateCommand.Execute(null);
                var second = await CurrentMenuAsync(tab, secondUrl, first);
                first.InvokeCommand.Execute(null);
                checks["crossSiteMenuCleanup"] = await EvaluateBoolean(tab, "window.__navigationMenuClicks === 0");
                second.InvokeCommand.Execute(null);
                await WaitAsync(async () => await EvaluateBoolean(tab, "window.__navigationMenuClicks === 1"));
                checks["crossSiteMenuCallback"] = true;

                await WaitAsync(() => Task.FromResult(tab.CanGoBack));
                main.BackCommand.Execute(null);
                var restoredFirst = await CurrentMenuAsync(tab, firstUrl, second);
                await WaitAsync(() => Task.FromResult(tab.CanGoForward));
                main.ForwardCommand.Execute(null);
                var restoredSecond = await CurrentMenuAsync(tab, secondUrl, restoredFirst);
                checks["historyMenuRefresh"] = true;

                main.ReloadCommand.Execute(null);
                var reloaded = await CurrentMenuAsync(tab, secondUrl, restoredSecond);
                reloaded.InvokeCommand.Execute(null);
                await WaitAsync(async () => await EvaluateBoolean(tab, "window.__navigationMenuClicks === 1"));
                checks["reloadMenuRefresh"] = true;

                await Evaluate(tab, "(location.hash = 'same-document', true)");
                await WaitAsync(() => Task.FromResult(tab.Address.EndsWith("#same-document", StringComparison.Ordinal)));
                await Evaluate(tab, "(history.pushState({}, '', '?same-document'), true)");
                await WaitAsync(() => Task.FromResult(tab.Address.EndsWith("?same-document", StringComparison.Ordinal)));
                checks["sameDocumentMenuPreserved"] = tab.MenuCommands.Count == 1 && ReferenceEquals(reloaded, tab.MenuCommands[0]);

                main.Address = OtherSite(fixture) + "/frame";
                main.NavigateCommand.Execute(null);
                await WaitAsync(async () => !tab.IsLoading && tab.MenuCommands.Count == 0 &&
                    await EvaluateBoolean(tab, "location.pathname === '/frame'"));
                checks["unmatchedPageMenuEmpty"] = true;
                backgroundMenu.InvokeCommand.Execute(null);
                await WaitAsync(async () => await EvaluateBoolean(background, "window.__navigationMenuClicks === 1"));
                checks["navigationMenuTabIsolation"] = background.MenuCommands.Count == 1 && ReferenceEquals(backgroundMenu, background.MenuCommands[0]);
            }
            finally
            {
                main.CloseTab(background);
                main.CloseTab(tab);
                main.SelectedTab = selected;
            }
        }

        private static string OtherSite(LocalHttpFixture fixture) => fixture.BaseUrl.Replace("127.0.0.1", "localhost");

        private static async Task<ScriptMenuCommand> CurrentMenuAsync(BrowserTabViewModel tab, string url, ScriptMenuCommand previous = null)
        {
            var label = "Navigation menu " + new Uri(url).Authority;
            await WaitAsync(async () => tab.MenuCommands.Count == 1 && !ReferenceEquals(previous, tab.MenuCommands[0]) &&
                tab.MenuCommands[0].Label.StartsWith(label, StringComparison.Ordinal) &&
                await EvaluateBoolean(tab, "window.__navigationMenuReady === true"));
            return tab.MenuCommands[0];
        }
    }
}
