using Mzying2001.MonkeySharp.Demo.ViewModels;
using Mzying2001.MonkeySharp.Demo.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mzying2001.MonkeySharp.Demo.Runtime
{
    internal static class DemoSmokeTest
    {
        internal static async Task RunAsync(DemoRuntime runtime, Dictionary<string, bool> checks)
        {
            using (var fixture = new LocalHttpFixture())
            {
                var source = Script.Replace("__BASE__", fixture.BaseUrl);
                var installation = await runtime.Repository.InstallAsync(source, "application://demo-smoke", true, CancellationToken.None);
                await runtime.Repository.InstallAsync("// ==UserScript==\n// @name frame-marker\n// @match " + fixture.BaseUrl +
                    "/frame\n// @grant none\n// ==/UserScript==\nwindow.__demoFrame = true;", "application://frame-smoke", true, CancellationToken.None);
                var primary = runtime.MainWindow.NewTab(fixture.BaseUrl + "/page?primary");
                await WaitAsync(async () => await EvaluateBoolean(primary, "!!(window.__demo && window.__demo.ready)"));
                checks["storageResourcesXhrCookieWebRequest"] = await EvaluateBoolean(primary,
                    "window.__demo.storage && window.__demo.resources && window.__demo.xhr && window.__demo.cookie && window.__demo.webRequest");
                checks["domAndInfo"] = await EvaluateBoolean(primary, "window.__demo.dom && window.__demo.info");
                checks["iframe"] = await EvaluateBoolean(primary, "document.querySelector('iframe').contentWindow.__demoFrame === true");
                await WaitAsync(() => Task.FromResult(primary.MenuCommands.Count == 1));
                primary.MenuCommands[0].InvokeCommand.Execute(null);
                await WaitAsync(async () => await EvaluateBoolean(primary, "window.__demo.menu === true"));
                checks["menu"] = true;
                var secondary = runtime.MainWindow.NewTab(fixture.BaseUrl + "/page?secondary", false, primary, true, true);
                await WaitAsync(async () => await EvaluateBoolean(secondary, "!!(window.__demo && window.__demo.ready)"));
                checks["backgroundTab"] = runtime.MainWindow.SelectedTab == primary;
                runtime.MainWindow.SelectedTab = secondary;
                runtime.MainWindow.SelectedTab = primary;
                checks["tabSwitchPreservesDocument"] = await EvaluateBoolean(primary, "window.__demo.runs === 1");
                await Evaluate(secondary, "window.__demoSetShared()");
                await WaitAsync(async () => await EvaluateBoolean(primary, "window.__demo.remote === true"));
                checks["crossTabValues"] = true;
                checks["sharedCookies"] = await EvaluateBoolean(secondary, "document.cookie.includes('demo-cookie=ready')");
                await WaitAsync(async () => await EvaluateBoolean(primary, "window.__demo.tabClosed === true"));
                checks["openInTab"] = true;
                await WaitAsync(async () => await EvaluateBoolean(primary, "window.__demo.download === true"));
                checks["download"] = Directory.GetFiles(runtime.Paths.DownloadsDirectory, "demo-smoke*.txt").Length == 1;
                await WaitAsync(() => Task.FromResult(Application.Current.Windows.Cast<Window>().Any(window => window.Title == "Demo smoke notification")));
                var notification = Application.Current.Windows.Cast<Window>().First(window => window.Title == "Demo smoke notification");
                var button = ((StackPanel)notification.Content).Children.OfType<Button>().First();
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitAsync(async () => await EvaluateBoolean(primary, "window.__demo.notificationClick && window.__demo.notificationDone"));
                checks["notification"] = true;
                checks["tabState"] = await EvaluateBoolean(primary, "window.__demo.tabState");
                var mainWindow = Application.Current.MainWindow;
                var selector = (ListBox)mainWindow.FindName("BrowserTabs");
                mainWindow.UpdateLayout();
                ((ListBoxItem)selector.ItemContainerGenerator.ContainerFromItem(primary)).Focus();
                checks["selectedTabColors"] = TabColorsMatch(selector, primary, true) && TabColorsMatch(selector, secondary, false);
                ((TextBox)mainWindow.FindName("AddressBar")).Focus();
                checks["inactiveSelectedTabColors"] = TabColorsMatch(selector, primary, true);
                Capture(Application.Current.MainWindow, Path.Combine(runtime.Paths.BaseDirectory, "browser.png"));
                var manager = new ScriptManagerWindow(runtime) { Owner = Application.Current.MainWindow };
                manager.Show();
                await Task.Delay(200);
                var managerModel = (ScriptManagerViewModel)manager.DataContext;
                managerModel.SelectedScript = managerModel.Scripts.First();
                manager.UpdateLayout();
                Capture(manager, Path.Combine(runtime.Paths.BaseDirectory, "manager.png"));
                manager.Close();
                primary.Browser.Load(fixture.BaseUrl + "/page?navigated");
                await WaitAsync(async () => await EvaluateBoolean(primary, "window.__demo && window.__demo.ready && location.search === '?navigated'"));
                checks["navigationCleanup"] = primary.MenuCommands.Count == 0;
                await runtime.Repository.SetEnabledAsync(installation.ScriptKey, false, CancellationToken.None);
                await WaitAsync(async () => await EvaluateBoolean(primary, "location.search === '?navigated' && !window.__demo"));
                checks["disableAndReload"] = true;
                await runtime.Repository.SetEnabledAsync(installation.ScriptKey, true, CancellationToken.None);
                await WaitAsync(async () => await EvaluateBoolean(primary, "!!(window.__demo && window.__demo.ready)"));
                checks["enableAndReload"] = true;
                selector.SelectedItem = secondary;
                runtime.MainWindow.CloseTab(secondary);
                checks["closeTab"] = secondary.IsClosed;
                checks["closeTabSelectsPrevious"] = runtime.MainWindow.SelectedTab == primary && selector.SelectedItem == primary;
                for (var iteration = 0; iteration < 5; iteration++)
                {
                    var closing = runtime.MainWindow.NewTab(fixture.BaseUrl + "/child");
                    await WaitAsync(async () => await EvaluateBoolean(closing, "document.readyState === 'complete'"));
                    var view = (BrowserTabView)closing.Browser;
                    var requestsBefore = fixture.SlowRequests;
                    await Evaluate(closing, "for (let index = 0; index < 4; index++) fetch('/slow?' + index).then(response => response.text()).catch(() => {}); true;");
                    await WaitAsync(() => Task.FromResult(fixture.SlowRequests >= requestsBefore + 4));
                    runtime.MainWindow.CloseTab(closing);
                    await view.DisposeAsync();
                    if (runtime.MainWindow.SelectedTab != primary || selector.SelectedItem != primary)
                        throw new InvalidOperationException("Closing a loading tab did not select its previous neighbor.");
                }
                checks["closeTabsDuringRequests"] = true;
                if (checks.Any(pair => !pair.Value)) throw new InvalidOperationException("Failed checks: " + string.Join(", ", checks.Where(pair => !pair.Value).Select(pair => pair.Key)));
            }
        }

        private static bool TabColorsMatch(ListBox selector, BrowserTabViewModel tab, bool selected)
        {
            var item = (ListBoxItem)selector.ItemContainerGenerator.ContainerFromItem(tab);
            var border = VisualTreeHelper.GetChild(item, 0) as Border;
            var title = VisualDescendants(item).OfType<TextBlock>().FirstOrDefault(text => text.Text == tab.Title);
            var foreground = selected ? Color.FromRgb(0x17, 0x2B, 0x40) : Color.FromRgb(0xE2, 0xE8, 0xF0);
            var background = selected ? Color.FromRgb(0xE5, 0xF1, 0xFC) :
                (item.IsMouseOver ? Color.FromRgb(0x33, 0x4E, 0x68) : Color.FromRgb(0x24, 0x3B, 0x53));
            return item.IsSelected == selected && (title?.Foreground as SolidColorBrush)?.Color == foreground &&
                (border?.Background as SolidColorBrush)?.Color == background;
        }

        private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject parent)
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                yield return child;
                foreach (var descendant in VisualDescendants(child)) yield return descendant;
            }
        }

        private static async Task WaitAsync(Func<Task<bool>> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(25);
            while (DateTime.UtcNow < deadline)
            {
                if (await condition()) return;
                await Task.Delay(150);
            }
            throw new TimeoutException("A WPF/Chromium smoke assertion timed out.");
        }
        private static void Capture(Window window, string path)
        {
            var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            image.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var output = File.Create(path)) encoder.Save(output);
        }
        private static async Task<bool> EvaluateBoolean(BrowserTabViewModel tab, string script)
        {
            var result = await Evaluate(tab, "Boolean(" + script + ")");
            return result is bool value && value;
        }
        private static async Task<object> Evaluate(BrowserTabViewModel tab, string script)
        {
            if (!(tab.Browser is BrowserTabView view) || !view.IsJavaScriptReady) return null;
            var result = await view.EvaluateAsync(script);
            return result.Success ? result.Result : null;
        }

        private const string Script = @"// ==UserScript==
// @name Demo all-services smoke
// @match __BASE__/page*
// @connect 127.0.0.1
// @noframes
// @require __BASE__/dependency.js
// @resource text __BASE__/resource
// @grant GM_getValue
// @grant GM_setValue
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
// @grant GM_info
// @grant GM_log
// @run-at document-end
// ==/UserScript==
window.__demo = { runs: Number(sessionStorage.getItem('runs') || 0) + 1 };
sessionStorage.setItem('runs', String(window.__demo.runs));
(async function () {
    const result = window.__demo;
    await GM.setValue('demo', 42);
    result.storage = (await GM.getValue('demo')) === 42 && GM_getValue('demo') === 42;
    result.resources = window.__demoDependency && GM_getResourceText('text') === 'demo-resource' && (await GM.getResourceURL('text')).startsWith('data:');
    await GM.addStyle('body { color: rgb(20, 40, 60); }');
    await GM.addElement('div', { id: 'demo-marker', textContent: 'ready' });
    result.dom = !!document.getElementById('demo-marker');
    result.info = !!GM.info && typeof GM.setClipboard === 'function';
    await GM.cookie.set({ url: location.href, name: 'demo-cookie', value: 'ready', path: '/' });
    result.cookie = (await GM.cookie.list({ url: location.href })).some(cookie => cookie.name === 'demo-cookie');
    result.xhr = (await GM.xmlHttpRequest({ url: '__BASE__/resource' })).responseText === 'demo-resource';
    const webRule = GM.webRequest([{ selector: '__BASE__/__monkeysharp_block__', action: 'cancel' }], function (info, message, details) { result.webRequest = message === 'cancel'; });
    result.webRequest = !!webRule && typeof webRule.remove === 'function';
    await GM.saveTab({ marker: location.search });
    result.tabState = (await GM.getTab()).marker === location.search && Object.keys(await GM.getTabs()).length >= 1;
    GM.addValueChangeListener('shared', function () { result.remote = true; });
    window.__demoSetShared = function () { GM.setValue('shared', 'changed'); };
    if (location.search === '?primary') {
        await GM.registerMenuCommand('Demo smoke menu', function () { result.menu = true; });
        await GM.notification({ title: 'Demo smoke notification', text: 'Click to complete lifecycle test', onclick: function () { result.notificationClick = true; }, ondone: function () { result.notificationDone = true; } });
        const child = GM_openInTab('__BASE__/child', { active: false, insert: true, setParent: true });
        setTimeout(function () { child.close(); setTimeout(function () { result.tabClosed = child.closed; }, 500); }, 500);
        GM_download({ url: '__BASE__/download', name: 'demo-smoke.txt', onload: function () { result.download = true; }, onerror: function (error) { result.error = String(error); } });
    }
    result.ready = true;
    GM.log('Demo smoke ready');
})().catch(function (error) { window.__demo.error = String(error && error.stack || error); console.error(window.__demo.error); });";
    }
}
