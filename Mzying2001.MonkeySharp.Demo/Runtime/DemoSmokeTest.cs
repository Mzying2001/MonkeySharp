using Mzying2001.MonkeySharp.Demo.ViewModels;
using Mzying2001.MonkeySharp.Demo.Views;
using Mzying2001.MonkeySharp.Demo.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mzying2001.MonkeySharp.Demo.Runtime
{
    internal static class DemoSmokeTest
    {
        internal static async Task RunAsync(DemoRuntime runtime, Dictionary<string, bool> checks)
        {
            var permissionPrompts = 0;
            runtime.PermissionPrompt.Opened += dialog =>
            {
                permissionPrompts++;
                var panel = (StackPanel)dialog.Content;
                var buttons = panel.Children.OfType<WrapPanel>().Single();
                buttons.Children.OfType<Button>().Single(button => (string)button.Content == "记忆允许").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            using (var fixture = new LocalHttpFixture())
            {
                var source = Script.Replace("__BASE__", fixture.BaseUrl);
                var installation = await runtime.Repository.InstallAsync(source, "application://demo-smoke", true, CancellationToken.None);
                await runtime.Repository.InstallAsync("// ==UserScript==\n// @name frame-marker\n// @match " + fixture.BaseUrl +
                    "/frame\n// @grant none\n// ==/UserScript==\nwindow.__demoFrame = true;", "application://frame-smoke", true, CancellationToken.None);
                await runtime.Repository.InstallAsync("// ==UserScript==\n// @name merged-menu\n// @match " + fixture.BaseUrl +
                    "/page*\n// @match " + fixture.BaseUrl + "/frame\n// @grant GM_registerMenuCommand\n// @grant GM_unregisterMenuCommand\n// @run-at document-end\n// ==/UserScript==\n" +
                    "if (top.location.search === '?primary') { window.__mergedClicks = 0; const id = GM_registerMenuCommand('Merged command', () => window.__mergedClicks++, {id:'merged',title:'Merged tooltip',autoClose:false}); window.__removeMerged = () => GM_unregisterMenuCommand(id); window.__updateMerged = () => GM_registerMenuCommand('Updated command', () => window.__mergedClicks += 10, {id,title:'Updated tooltip',autoClose:true}); }",
                    "application://merged-menu-smoke", true, CancellationToken.None);
                await runtime.Repository.InstallAsync("// ==UserScript==\n// @name zz-tabstate-isolation\n// @match " + fixture.BaseUrl +
                    "/page*\n// @grant GM_getTab\n// @grant GM_saveTab\n// @grant GM_getTabs\n// @run-at document-end\n// ==/UserScript==\n" +
                    "(async function(){ const state=await GM.getTab(); await GM.saveTab({isolated:true}); const all=await GM.getTabs(); window.__tabIsolation=typeof state.marker==='undefined' && Object.values(all).every(value=>value.isolated===true && typeof value.marker==='undefined'); })();",
                    "application://tab-isolation-smoke", true, CancellationToken.None);
                var primary = runtime.MainWindow.NewTab(fixture.BaseUrl + "/page?primary");
                await WaitAsync(async () => await EvaluateBoolean(primary, "!!(window.__demo && window.__demo.ready)"));
                await WaitAsync(() => Task.FromResult(string.Equals(primary.IconUrl, fixture.BaseUrl + "/favicon.ico", StringComparison.OrdinalIgnoreCase)));
                checks["permissionPrompt"] = permissionPrompts > 0;
                checks["permissionRemembered"] = (await runtime.PermissionDecisions.ListAsync(CancellationToken.None)).Any(scope => scope.ScriptKey == installation.ScriptKey.ToString());
                checks["storageResourcesXhrCookieWebRequest"] = await EvaluateBoolean(primary,
                    "window.__demo.storage && window.__demo.resources && window.__demo.xhr && window.__demo.cookie && window.__demo.webRequest");
                await WaitAsync(async () => await EvaluateBoolean(primary, "window.__tabIsolation === true"));
                checks["tabStateScriptIsolation"] = true;
                checks["domAndInfo"] = await EvaluateBoolean(primary, "window.__demo.dom && window.__demo.info");
                await WaitAsync(async () => await EvaluateBoolean(primary,
                    "document.querySelector('iframe').contentWindow.__demoFrame === true"));
                checks["iframe"] = true;
                await WaitAsync(() => Task.FromResult(primary.MenuCommands.Count == 2));
                primary.MenuCommands.Single(item => item.Label.StartsWith("Demo smoke menu")).InvokeCommand.Execute(null);
                await WaitAsync(async () => await EvaluateBoolean(primary, "window.__demo.menu === true"));
                checks["menu"] = true;
                var merged = primary.MenuCommands.Single(item => item.Label.StartsWith("Merged command"));
                checks["menuOptions"] = merged.Title == "Merged tooltip" && merged.StaysOpenOnClick;
                merged.InvokeCommand.Execute(null);
                await WaitAsync(async () => await EvaluateBoolean(primary, "window.__mergedClicks === 1 && document.querySelector('iframe').contentWindow.__mergedClicks === 1"));
                await Evaluate(primary, "window.__updateMerged()");
                await WaitAsync(() => Task.FromResult(primary.MenuCommands.Count == 3));
                var updatedMenu = primary.MenuCommands.Single(item => item.Label.StartsWith("Updated command"));
                checks["menuUpdate"] = updatedMenu.Title == "Updated tooltip" && !updatedMenu.StaysOpenOnClick;
                updatedMenu.InvokeCommand.Execute(null);
                await WaitAsync(async () => await EvaluateBoolean(primary, "window.__mergedClicks === 11"));
                await Evaluate(primary, "window.__removeMerged()");
                await Task.Delay(100);
                merged.InvokeCommand.Execute(null);
                await WaitAsync(async () => await EvaluateBoolean(primary, "window.__mergedClicks === 11 && document.querySelector('iframe').contentWindow.__mergedClicks === 2"));
                await Evaluate(primary, "document.querySelector('iframe').src = '/child'");
                await WaitAsync(() => Task.FromResult(primary.MenuCommands.Count == 1));
                checks["multiFrameMenu"] = true;
                var secondary = runtime.MainWindow.NewTab(fixture.BaseUrl + "/page?secondary", false, primary, true, true);
                await WaitAsync(async () => await EvaluateBoolean(secondary, "!!(window.__demo && window.__demo.ready)"));
                checks["backgroundTab"] = runtime.MainWindow.SelectedTab == primary;
                runtime.MainWindow.SelectedTab = secondary;
                runtime.MainWindow.SelectedTab = primary;
                checks["tabSwitchPreservesDocument"] = await EvaluateBoolean(primary, "window.__demo.runs === 1");
                await Evaluate(secondary, "window.__demoSetShared()");
                await WaitAsync(async () => await EvaluateBoolean(primary, "window.__demo.remote === true"));
                checks["crossTabValues"] = true;
                checks["undefinedSnapshot"] = await EvaluateBoolean(secondary, "window.__demo.restoredUndefined === true");
                await Evaluate(secondary, "location.href = '/page?reloaded'");
                await WaitAsync(async () => await EvaluateBoolean(secondary,
                    "location.search === '?reloaded' && window.__demo && window.__demo.ready && window.__demo.runs === 2"));
                await Evaluate(primary, "window.__testWindowFocus()");
                await WaitAsync(() => Task.FromResult(runtime.MainWindow.SelectedTab == primary));
                checks["windowFocus"] = true;
                checks["tabStateNavigation"] = await EvaluateBoolean(secondary, "window.__demo.previousTabMarker === '?secondary'");
                checks["undefinedNavigation"] = await EvaluateBoolean(secondary, "window.__demo.restoredUndefined && window.__demo.storage");
                checks["sharedCookies"] = await EvaluateBoolean(secondary, "document.cookie.includes('demo-cookie=ready')");
                await WaitAsync(async () => await EvaluateBoolean(primary, "window.__demo.tabClosed === true"));
                checks["openInTab"] = true;
                var tabsBeforeOptions = runtime.MainWindow.Tabs.Count;
                await Evaluate(primary, "window.__testTabOptions()");
                await WaitAsync(() => Task.FromResult(runtime.MainWindow.Tabs.Count == tabsBeforeOptions + 1));
                var optionTab = runtime.MainWindow.Tabs[0];
                checks["tabRelativeUrlAndPosition"] = optionTab.Address == fixture.BaseUrl + "/child" && optionTab.ParentTabId == primary.TabId && runtime.MainWindow.SelectedTab == primary;
                await WaitAsync(async () => await EvaluateBoolean(primary, "typeof window.__closeOptionsTab === 'function'"));
                await Evaluate(primary, "window.__closeOptionsTab()");
                await WaitAsync(async () => await EvaluateBoolean(primary, "window.__demo.optionsClosed === 1"));
                checks["tabCloseOnce"] = optionTab.IsClosed;
                await WaitAsync(async () => await EvaluateBoolean(primary, "window.__demo.download === true"));
                var downloaded = Directory.GetFiles(runtime.Paths.DownloadsDirectory, "demo-smoke*.txt");
                checks["download"] = downloaded.Length == 1 && File.ReadAllText(downloaded[0]) == new string('d', 256 * 1024);
                await Evaluate(primary, "window.__testSaveCancel()");
                await WaitAsync(() => Task.FromResult(DownloadFilePicker.Close(new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow).Handle)));
                await WaitAsync(async () => await EvaluateBoolean(primary, "window.__demo.saveCanceled === true"));
                checks["downloadSaveCanceled"] = fixture.RequestCount("/save-cancel") == 0 && !File.Exists(Path.Combine(runtime.Paths.DownloadsDirectory, "canceled.txt"));
                using (var hash = System.Security.Cryptography.SHA256.Create())
                    Console.WriteLine(JsonSerializer.Serialize(new { type = "download-file-evidence", sha256 = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(downloaded[0]))).Replace("-", "").ToLowerInvariant(), bytes = new FileInfo(downloaded[0]).Length }));
                await WaitAsync(() => Task.FromResult(Application.Current.Windows.Cast<Window>().Any(window => window.Title == "Demo smoke notification")));
                var notification = Application.Current.Windows.Cast<Window>().First(window => window.Title == "Demo smoke notification");
                checks["notificationWaits"] = await EvaluateBoolean(primary, "typeof window.__demo.notificationResolved === 'undefined'");
                var button = ((StackPanel)notification.Content).Children.OfType<Button>().First();
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitAsync(async () => await EvaluateBoolean(primary, "window.__demo.notificationClick && window.__demo.notificationDone && window.__demo.notificationResolved === true"));
                checks["notification"] = true;
                var tabCount = runtime.MainWindow.Tabs.Count;
                await Evaluate(primary, "window.__testTaggedNotification()");
                await WaitAsync(() => Task.FromResult(Application.Current.Windows.Cast<Window>().Any(window => window.Title == "Tagged replacement")));
                checks["notificationTag"] = !Application.Current.Windows.Cast<Window>().Any(window => window.Title == "Tagged original");
                var tagged = Application.Current.Windows.Cast<Window>().Single(window => window.Title == "Tagged replacement");
                ((StackPanel)tagged.Content).Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitAsync(async () => await EvaluateBoolean(primary, "window.__demo.tagged === true && window.__demo.taggedOld === false"));
                checks["notificationPreventDefault"] = runtime.MainWindow.Tabs.Count == tabCount;
                await Evaluate(primary, "window.__testUrlNotification()");
                await WaitAsync(() => Task.FromResult(Application.Current.Windows.Cast<Window>().Any(window => window.Title == "URL notification")));
                var urlNotification = Application.Current.Windows.Cast<Window>().Single(window => window.Title == "URL notification");
                ((StackPanel)urlNotification.Content).Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitAsync(async () => await EvaluateBoolean(primary, "window.__demo.urlNotification === true"));
                checks["notificationUrl"] = runtime.MainWindow.Tabs.Count == tabCount + 1;
                runtime.MainWindow.CloseTab(runtime.MainWindow.Tabs.Last());
                runtime.MainWindow.SelectedTab = secondary;
                await Evaluate(primary, "window.__testHighlightNotification()");
                await WaitAsync(async () => await EvaluateBoolean(primary, "window.__demo.highlightNotification === false"));
                checks["notificationHighlight"] = runtime.MainWindow.SelectedTab == primary;
                var savedClipboard = Clipboard.GetDataObject();
                try
                {
                    await Evaluate(primary, "window.__testClipboard()");
                    await WaitAsync(async () => await EvaluateBoolean(primary, "window.__demo.clipboardWritten === true"));
                    var html = Clipboard.GetData(DataFormats.Html) as string;
                    var bytes = System.Text.Encoding.UTF8.GetBytes(html ?? "");
                    var start = int.Parse(System.Text.RegularExpressions.Regex.Match(html ?? "", "StartFragment:(\\d+)").Groups[1].Value);
                    var end = int.Parse(System.Text.RegularExpressions.Regex.Match(html ?? "", "EndFragment:(\\d+)").Groups[1].Value);
                    checks["clipboardHtml"] = System.Text.Encoding.UTF8.GetString(bytes, start, end - start) == "<b>Clipboard \u4f60\u597d</b>";
                    await Evaluate(primary, "window.__testTextClipboard()");
                    await WaitAsync(async () => await EvaluateBoolean(primary, "window.__demo.clipboardText === true"));
                    checks["clipboardText"] = Clipboard.GetText() == "plain clipboard";
                }
                finally { if (savedClipboard != null) Clipboard.SetDataObject(savedClipboard, true); else Clipboard.Clear(); }
                checks["tabState"] = await EvaluateBoolean(primary, "window.__demo.tabState");
                var mainWindow = Application.Current.MainWindow;
                var selector = (ListBox)mainWindow.FindName("BrowserTabs");
                mainWindow.UpdateLayout();
                await WaitAsync(() => Task.FromResult(TabIconTemplatePresent(selector, primary)));
                checks["tabFavicon"] = true;
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
                checks["scriptManagerDetails"] = managerModel.Details.Name == managerModel.SelectedScript.Name &&
                    managerModel.Details.SourceOrigin == managerModel.SelectedScript.Installation.SourceOrigin;
                checks["scriptManagerInlineToggle"] = VisualDescendants(manager).OfType<ToggleButton>().Any();
                checks["scriptManagerCtrlS"] = manager.InputBindings.OfType<KeyBinding>().Any(binding =>
                    binding.Key == Key.S && binding.Modifiers == ModifierKeys.Control &&
                    ReferenceEquals(binding.Command, managerModel.SaveCommand));
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
                await Evaluate(secondary, "window.__testWindowClose()");
                await WaitAsync(() => Task.FromResult(secondary.IsClosed));
                checks["windowClose"] = true;
                checks["closeTab"] = secondary.IsClosed;
                checks["closeTabSelectsPrevious"] = runtime.MainWindow.SelectedTab == primary && selector.SelectedItem == primary;
                await Evaluate(primary, "window.__testWindowClose()");
                await Task.Delay(150);
                checks["lastTabPolicy"] = !primary.IsClosed && runtime.MainWindow.Tabs.Count == 1;
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

        private static bool TabIconTemplatePresent(ListBox selector, BrowserTabViewModel tab)
        {
            var item = selector.ItemContainerGenerator.ContainerFromItem(tab) as ListBoxItem;
            return item != null && VisualDescendants(item).OfType<ScriptIcon>().Any();
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
    const webRule = GM.webRequest([{ selector: '__BASE__/__monkeysharp_block__', action: 'cancel' }], function (info, message, details) { result.webRequest = message === 'cancel'; });
    result.webRequest = !!webRule && typeof webRule.remove === 'function';
    window.__testTabOptions = async function () {
        const tab = await GM.openInTab('/child', { active: false, insert: 0, setParent: true });
        result.optionsClosed = 0;
        tab.onclose = function () { result.optionsClosed++; };
        window.__closeOptionsTab = function () { tab.close(); tab.close(); };
    };
    window.__testWindowFocus = function () { return window.focus(); };
    window.__testWindowClose = function () { return window.close(); };
    result.previousTabMarker = (await GM.getTab()).marker;
    await GM.saveTab({ marker: location.search });
    result.tabState = (await GM.getTab()).marker === location.search && Object.keys(await GM.getTabs()).length >= 1;
    await GM.addValueChangeListener('shared', function (key, oldValue, newValue, remote) { result.remote = remote && newValue === undefined; });
    window.__demoSetShared = function () { GM.setValue('shared', undefined); };
    if (location.search === '?primary') {
        await GM.registerMenuCommand('Demo smoke menu', function () { result.menu = true; });
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
