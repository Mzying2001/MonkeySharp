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
                "/menu-page*\n// @match " + OtherSite(fixture) + "/menu-page*\n// @grant GM.registerMenuCommand\n// @grant GM.unregisterMenuCommand\n// @run-at document-end\n// ==/UserScript==\n" +
                "(async function(){ window.__navigationMenuClicks = 0; const id = await GM.registerMenuCommand('Navigation menu ' + location.host, function(){ window.__navigationMenuClicks++; }); window.__removeNavigationMenu = () => GM.unregisterMenuCommand(id); window.__navigationMenuReady = true; })();",
                "application://navigation-menu-smoke", true, CancellationToken.None);

        internal static async Task RunAsync(DemoRuntime runtime, LocalHttpFixture fixture, Dictionary<string, bool> checks)
        {
            var main = runtime.MainWindow;
            var selected = main.SelectedTab;
            var firstUrl = fixture.BaseUrl + "/menu-page";
            var secondUrl = OtherSite(fixture) + "/menu-page";
            var tab = main.NewTab();
            var background = main.NewTab(firstUrl, false);
            try
            {
                SmokeTrace.Step("navigation-menu: initial registration");
                await WaitAsync(async () => await EvaluateBoolean(tab, "location.href === 'about:blank'"));
                await ScriptMenuSmokeTest.WaitVisibilityAsync(false);
                checks["emptyScriptMenuHidden"] = true;
                main.Address = firstUrl;
                main.NavigateCommand.Execute(null);
                var first = await CurrentMenuAsync(tab, firstUrl);
                await ScriptMenuSmokeTest.WaitVisibilityAsync(true);
                checks["registeredScriptMenuVisible"] = true;
                var backgroundMenu = await CurrentMenuAsync(background, firstUrl);
                SmokeTrace.Step("navigation-menu: cross-site registration");
                // Exercise the same command as typing a new URL into the address bar.
                main.Address = secondUrl;
                main.NavigateCommand.Execute(null);
                var second = await CurrentMenuAsync(tab, secondUrl, first);
                first.InvokeCommand.Execute(null);
                checks["crossSiteMenuCleanup"] = await EvaluateBoolean(tab, "window.__navigationMenuClicks === 0");
                SmokeTrace.Step("navigation-menu: cross-site callback exactly once");
                second.InvokeCommand.Execute(null);
                await WaitAsync(async () => await EvaluateBoolean(tab, "window.__navigationMenuClicks === 1"));
                checks["crossSiteMenuCallback"] = true;
                SmokeTrace.Step("navigation-menu: history");

                await WaitAsync(() => Task.FromResult(tab.CanGoBack));
                main.BackCommand.Execute(null);
                var restoredFirst = await CurrentMenuAsync(tab, firstUrl, second);
                await WaitAsync(() => Task.FromResult(tab.CanGoForward));
                main.ForwardCommand.Execute(null);
                var restoredSecond = await CurrentMenuAsync(tab, secondUrl, restoredFirst);
                checks["historyMenuRefresh"] = true;
                SmokeTrace.Step("navigation-menu: reload callback exactly once");

                main.ReloadCommand.Execute(null);
                var reloaded = await CurrentMenuAsync(tab, secondUrl, restoredSecond);
                reloaded.InvokeCommand.Execute(null);
                await WaitAsync(async () => await EvaluateBoolean(tab, "window.__navigationMenuClicks === 1"));
                checks["reloadMenuRefresh"] = true;
                SmokeTrace.Step("navigation-menu: same-document preservation");

                await Evaluate(tab, "(location.hash = 'same-document', true)");
                await WaitAsync(() => Task.FromResult(tab.Address.EndsWith("#same-document", StringComparison.Ordinal)));
                await Evaluate(tab, "(history.pushState({}, '', '?same-document'), true)");
                await WaitAsync(() => Task.FromResult(tab.Address.EndsWith("?same-document", StringComparison.Ordinal)));
                checks["sameDocumentMenuPreserved"] = tab.MenuCommands.Count == 1 && ReferenceEquals(reloaded, tab.MenuCommands[0]);
                SmokeTrace.Step("navigation-menu: unmatched page and tab isolation");

                main.Address = OtherSite(fixture) + "/frame";
                main.NavigateCommand.Execute(null);
                await WaitAsync(async () => !tab.IsLoading && tab.MenuCommands.Count == 0 &&
                    await EvaluateBoolean(tab, "location.pathname === '/frame'"));
                checks["unmatchedPageMenuEmpty"] = true;
                await ScriptMenuSmokeTest.WaitVisibilityAsync(false);
                checks["navigationHidesEmptyScriptMenu"] = true;
                backgroundMenu.InvokeCommand.Execute(null);
                await WaitAsync(async () => await EvaluateBoolean(background, "window.__navigationMenuClicks === 1"));
                checks["navigationMenuTabIsolation"] = background.MenuCommands.Count == 1 && ReferenceEquals(backgroundMenu, background.MenuCommands[0]);
                main.SelectedTab = background;
                await ScriptMenuSmokeTest.WaitVisibilityAsync(true);
                main.SelectedTab = tab;
                await ScriptMenuSmokeTest.WaitVisibilityAsync(false);
                checks["scriptMenuVisibilityFollowsTab"] = true;
                main.SelectedTab = background;
                await ScriptMenuSmokeTest.WaitVisibilityAsync(true);
                await Evaluate(background, "window.__removeNavigationMenu()");
                await WaitAsync(() => Task.FromResult(background.MenuCommands.Count == 0));
                await ScriptMenuSmokeTest.WaitVisibilityAsync(false);
                checks["lastCommandHidesScriptMenu"] = true;
            }
            finally
            {
                main.CloseTab(background);
                main.CloseTab(tab);
                main.SelectedTab = selected;
            }
        }

        private static string OtherSite(LocalHttpFixture fixture) => fixture.BaseUrl.Replace("127.0.0.1", "localhost");

        private static Task<ScriptMenuCommand> CurrentMenuAsync(BrowserTabViewModel tab, string url, ScriptMenuCommand previous = null)
            => CurrentMenuAsync(tab, url, previous, () => EvaluateBoolean(tab, "window.__navigationMenuReady === true"));

        internal static async Task<ScriptMenuCommand> CurrentMenuAsync(BrowserTabViewModel tab, string url, ScriptMenuCommand previous,
            Func<Task<bool>> isReady)
        {
            var label = "Navigation menu " + new Uri(url).Authority;
            ScriptMenuCommand current = null;
            try
            {
                await WaitAsync(async () =>
                {
                    // Navigation can replace the collection while the renderer evaluation is pending.
                    if (!await isReady() || tab.MenuCommands.Count != 1) return false;
                    var command = tab.MenuCommands[0];
                    if (ReferenceEquals(previous, command) || !command.Label.StartsWith(label, StringComparison.Ordinal)) return false;
                    current = command;
                    SmokeTrace.Write("menu-ready", new { tab = tab.TabId, url, tab.Address, command = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(command), subscribers = command.Subscribers.Count });
                    return true;
                });
            }
            catch (TimeoutException error)
            {
                throw new TimeoutException("Navigation menu did not become ready. Expected=" + url + "; address=" + tab.Address + "; menuCount=" + tab.MenuCommands.Count +
                    " js=" + await Evaluate(tab, "JSON.stringify({url:location.href,ready:window.__navigationMenuReady,clicks:window.__navigationMenuClicks})"), error);
            }
            return current;
        }
    }
}
