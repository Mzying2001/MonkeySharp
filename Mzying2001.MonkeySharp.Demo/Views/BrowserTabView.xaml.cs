using CefSharp;
using CefSharp.Handler;
using CefSharp.Wpf;
using Mzying2001.MonkeySharp.CefSharp;
using Mzying2001.MonkeySharp.Demo.Runtime;
using Mzying2001.MonkeySharp.Demo.ViewModels;
using System;
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
        private void AddressChanged(object sender, DependencyPropertyChangedEventArgs args) => Post(() => _tab.Address = args.NewValue as string);
        private void TitleChanged(object sender, DependencyPropertyChangedEventArgs args) => Post(() =>
            _tab.Title = string.IsNullOrWhiteSpace(args.NewValue as string) ? _tab.Address : (string)args.NewValue);
        private void LoadingChanged(object sender, LoadingStateChangedEventArgs args) => Post(() =>
        { _tab.IsLoading = args.IsLoading; _tab.CanGoBack = args.CanGoBack; _tab.CanGoForward = args.CanGoForward; });
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

        private sealed class NavigationHandler : RequestHandler
        {
            private readonly Action<string, bool> _open;
            public NavigationHandler(Action<string, bool> open) { _open = open; }
            protected override bool OnOpenUrlFromTab(IWebBrowser control, IBrowser browser, IFrame frame,
                string targetUrl, WindowOpenDisposition disposition, bool userGesture)
            { _open(targetUrl, disposition != WindowOpenDisposition.NewBackgroundTab); return true; }
        }
        private sealed class PopupHandler : LifeSpanHandler
        {
            private readonly Action<string, bool> _open;
            public PopupHandler(Action<string, bool> open) { _open = open; }
            protected override bool OnBeforePopup(IWebBrowser control, IBrowser browser, IFrame frame,
                string targetUrl, string targetFrameName, WindowOpenDisposition disposition, bool userGesture,
                IPopupFeatures popupFeatures, IWindowInfo windowInfo, IBrowserSettings settings,
                ref bool noJavascriptAccess, out IWebBrowser newBrowser)
            { newBrowser = null; _open(targetUrl, disposition != WindowOpenDisposition.NewBackgroundTab); return true; }
        }
        private sealed class BrowserDownloadHandler : DownloadHandler
        {
            private readonly string _directory;
            private readonly Action<string> _report;
            public BrowserDownloadHandler(string directory, Action<string> report) { _directory = directory; _report = report; }
#if CEF_SHARP_BOOLEAN_DOWNLOAD
            protected override bool OnBeforeDownload(IWebBrowser control, IBrowser browser, DownloadItem item, IBeforeDownloadCallback callback)
#else
            protected override void OnBeforeDownload(IWebBrowser control, IBrowser browser, DownloadItem item, IBeforeDownloadCallback callback)
#endif
            {
                using (callback) callback.Continue(System.IO.Path.Combine(_directory, Services.DownloadService.SafeFileName(item.SuggestedFileName)), true);
#if CEF_SHARP_BOOLEAN_DOWNLOAD
                return true;
#endif
            }
            protected override void OnDownloadUpdated(IWebBrowser control, IBrowser browser, DownloadItem item, IDownloadItemCallback callback)
            { if (item.IsComplete) _report("浏览器下载完成：" + item.FullPath); }
        }
    }
}
