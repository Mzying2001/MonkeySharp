using CefSharp;
using CefSharp.Handler;
using CefSharp.Wpf;
using Mzying2001.MonkeySharp.CefSharp;
using Mzying2001.MonkeySharp.Demo.Runtime;
using Mzying2001.MonkeySharp.Demo.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Mzying2001.MonkeySharp.Demo.Views
{
    public partial class BrowserTabView : UserControl, IBrowserCommands
    {
        private readonly BrowserTabViewModel _tab;
        private readonly DemoRuntime _runtime;
        private readonly ChromiumWebBrowser _browser;
        private readonly TabRuntime _scope;
        public BrowserTabView(DemoRuntime runtime, BrowserTabViewModel tab)
        {
            InitializeComponent();
            _runtime = runtime; _tab = tab; DataContext = tab;
            _browser = new ChromiumWebBrowser(tab.Address) { RequestContext = runtime.RequestContext, Background = System.Windows.Media.Brushes.White };
            var requestHandlers = new CefSharpWebRequestHandlerMultiplexer();
            requestHandlers.Add(new NavigationHandler(OpenTab));
            _browser.RequestHandler = requestHandlers;
            _browser.DisplayHandler = new FaviconDisplayHandler(FaviconChanged);
            _browser.LifeSpanHandler = new PopupHandler(OpenTab);
            _browser.DownloadHandler = new BrowserDownloadHandler(runtime.Paths.DownloadsDirectory, runtime.MainWindow.Diagnostics.Report);
            _scope = runtime.AttachTab(tab, _browser);
            _browser.AddressChanged += AddressChanged;
            _browser.TitleChanged += TitleChanged;
            _browser.LoadingStateChanged += LoadingChanged;
            _browser.LoadError += LoadError;
            _browser.ConsoleMessage += ConsoleMessage;
            _tab.Browser = this;
            BrowserContainer.Children.Add(_browser);
        }

        private void Post(Action action) { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(() => { if (!_tab.IsClosed) action(); })); }
        private void AddressChanged(object sender, DependencyPropertyChangedEventArgs args) => Post(() =>
        {
            _tab.Address = args.NewValue as string;
            _tab.IconUrl = null;
        });
        private void TitleChanged(object sender, DependencyPropertyChangedEventArgs args) => Post(() =>
            _tab.Title = string.IsNullOrWhiteSpace(args.NewValue as string) ? _tab.Address : (string)args.NewValue);
        private void FaviconChanged(string iconUrl) => Post(() => _tab.IconUrl = iconUrl);
        private void LoadingChanged(object sender, LoadingStateChangedEventArgs args) => Post(() =>
        {
            _tab.IsLoading = args.IsLoading; _tab.CanGoBack = args.CanGoBack; _tab.CanGoForward = args.CanGoForward;
            if (!args.IsLoading)
            {
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    await Task.Delay(100);
                    ReadDeclaredFavicon();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        });
        private void LoadError(object sender, LoadErrorEventArgs args)
        {
            if (args.ErrorCode == CefErrorCode.Aborted) return;
            Post(() => { _tab.Error = args.ErrorCode + ": " + args.ErrorText; _runtime.MainWindow.Diagnostics.Report(_tab.Error); });
        }
        private void ConsoleMessage(object sender, ConsoleMessageEventArgs args)
            => _runtime.MainWindow.Diagnostics.Report("Console " + args.Level + ": " + args.Message);
        private void OpenTab(string url, bool active) => Post(() =>
        {
            try { _runtime.MainWindow.NewTab(MainWindowViewModel.NormalizeAddress(url), active, _tab, true, true); }
            catch (ArgumentException exception) { _runtime.MainWindow.Diagnostics.Report(exception.Message); }
        });

        public void Load(string address) { _tab.Error = null; _browser.Load(address); }
        public void Back() { if (_browser.CanGoBack) _browser.Back(); }
        public void Forward() { if (_browser.CanGoForward) _browser.Forward(); }
        public void Reload() { if (_browser.IsBrowserInitialized) _browser.Reload(); }
        public void Stop() { if (_browser.IsBrowserInitialized) _browser.Stop(); }
        public void ShowDevTools() { if (_browser.IsBrowserInitialized) _browser.ShowDevTools(); }
        internal bool IsJavaScriptReady => _browser.IsBrowserInitialized && _browser.CanExecuteJavascriptInMainFrame;
        internal Task<JavascriptResponse> EvaluateAsync(string script) => _browser.EvaluateScriptAsync(script, timeout: TimeSpan.FromSeconds(5));

        private static string SelectFaviconUrl(IList<string> urls)
        {
            if (urls == null) return null;
            foreach (var url in urls)
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                    !string.IsNullOrEmpty(uri.UserInfo)) continue;
                return uri.AbsoluteUri;
            }
            return null;
        }

        private static string DefaultFaviconUrl(string address)
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(uri.UserInfo)) return null;
            var builder = new UriBuilder(uri) { Path = "/favicon.ico", Query = string.Empty, Fragment = string.Empty };
            return builder.Uri.AbsoluteUri;
        }

        private async void ReadDeclaredFavicon()
        {
            var address = _tab.Address;
            try
            {
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    if (!_browser.IsBrowserInitialized || !_browser.CanExecuteJavascriptInMainFrame)
                    {
                        await Task.Delay(200).ConfigureAwait(false);
                        continue;
                    }
                    var response = await _browser.EvaluateScriptAsync(
                        "(function(){var icon=document.querySelector('link[rel~=" +
                        "\"icon\"],link[rel=\"shortcut icon\"]);return icon ? icon.href : null;})()",
                        timeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    if (!response.Success)
                    {
                        await Task.Delay(200).ConfigureAwait(false);
                        continue;
                    }
                    var iconUrl = SelectFaviconUrl(new[] { response.Result as string }) ?? DefaultFaviconUrl(address);
                    Post(() =>
                    {
                        if (string.Equals(_tab.Address, address, StringComparison.OrdinalIgnoreCase)) _tab.IconUrl = iconUrl;
                    });
                    return;
                }
                Post(() =>
                {
                    if (string.Equals(_tab.Address, address, StringComparison.OrdinalIgnoreCase))
                        _tab.IconUrl = DefaultFaviconUrl(address);
                });
            }
            catch (Exception) { }
        }

        public Task DisposeAsync()
        {
            _tab.Browser = null;
            _browser.AddressChanged -= AddressChanged;
            _browser.TitleChanged -= TitleChanged;
            _browser.LoadingStateChanged -= LoadingChanged;
            _browser.LoadError -= LoadError;
            _browser.ConsoleMessage -= ConsoleMessage;
            return _scope.DisposeAsync();
        }

        private sealed class FaviconDisplayHandler : DisplayHandler
        {
            private readonly Action<string> _changed;
            public FaviconDisplayHandler(Action<string> changed) { _changed = changed; }
            protected override void OnFaviconUrlChange(IWebBrowser chromiumWebBrowser, IBrowser browser, IList<string> urls)
                => _changed(SelectFaviconUrl(urls));
        }

        private sealed class NavigationHandler : RequestHandler
        {
            private readonly Action<string, bool> _open;
            public NavigationHandler(Action<string, bool> open) { _open = open; }
            protected override bool OnOpenUrlFromTab(IWebBrowser control, IBrowser browser, IFrame frame,
                string targetUrl, WindowOpenDisposition disposition, bool userGesture)
            { _open(targetUrl, disposition != WindowOpenDisposition.NewBackgroundTab); return true; }
        }
        private sealed class PopupHandler : ILifeSpanHandler
        {
            private readonly Action<string, bool> _open;
            public PopupHandler(Action<string, bool> open) { _open = open; }
            public bool OnBeforePopup(IWebBrowser control, IBrowser browser, IFrame frame,
                string targetUrl, string targetFrameName, WindowOpenDisposition disposition, bool userGesture,
                IPopupFeatures popupFeatures, IWindowInfo windowInfo, IBrowserSettings settings,
                ref bool noJavascriptAccess, out IWebBrowser newBrowser)
            { newBrowser = null; _open(targetUrl, disposition != WindowOpenDisposition.NewBackgroundTab); return true; }
            public void OnAfterCreated(IWebBrowser control, IBrowser browser) { }
            public bool DoClose(IWebBrowser control, IBrowser browser) => !browser.IsPopup;
            public void OnBeforeClose(IWebBrowser control, IBrowser browser) { }
        }
        private sealed class BrowserDownloadHandler : IDownloadHandler
        {
            private readonly string _directory;
            private readonly Action<string> _report;
            public BrowserDownloadHandler(string directory, Action<string> report) { _directory = directory; _report = report; }
#if CEF_SHARP_HAS_DOWNLOAD_CAN_DOWNLOAD
            public bool CanDownload(IWebBrowser control, IBrowser browser, string url, string requestMethod) => true;
#endif
#if CEF_SHARP_BOOLEAN_DOWNLOAD
            public bool OnBeforeDownload(IWebBrowser control, IBrowser browser, DownloadItem item, IBeforeDownloadCallback callback)
#else
            public void OnBeforeDownload(IWebBrowser control, IBrowser browser, DownloadItem item, IBeforeDownloadCallback callback)
#endif
            {
                using (callback) callback.Continue(System.IO.Path.Combine(_directory, Services.DownloadService.SafeFileName(item.SuggestedFileName)), true);
#if CEF_SHARP_BOOLEAN_DOWNLOAD
                return true;
#endif
            }
            public void OnDownloadUpdated(IWebBrowser control, IBrowser browser, DownloadItem item, IDownloadItemCallback callback)
            { if (item.IsComplete) _report("浏览器下载完成：" + item.FullPath); }
        }
    }
}
