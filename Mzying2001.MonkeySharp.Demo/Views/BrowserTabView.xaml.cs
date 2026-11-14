using CefSharp;
using CefSharp.Wpf;
using Mzying2001.MonkeySharp.CefSharp;
using Mzying2001.MonkeySharp.Demo.Browser;
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
        private readonly BrowserFavicon _favicon;
        private bool _disposed;

        public BrowserTabView(DemoRuntime runtime, BrowserTabViewModel tab)
        {
            InitializeComponent();
            _runtime = runtime;
            _tab = tab;
            DataContext = tab;
            _browser = new ChromiumWebBrowser(tab.Address)
            {
                RequestContext = runtime.GetTabContext(tab).RequestContext,
                Background = System.Windows.Media.Brushes.White
            };
            var requestHandlers = new CefSharpWebRequestHandlerMultiplexer();
            requestHandlers.Add(new NavigationHandler(OpenTab));
            _browser.RequestHandler = requestHandlers;
            _favicon = new BrowserFavicon(_browser, tab, Post);
            _browser.DisplayHandler = new FaviconDisplayHandler(FaviconChanged);
            _browser.LifeSpanHandler = new PopupHandler(OpenTab);
            _browser.DownloadHandler = new BrowserDownloadHandler(runtime.Paths.DownloadsDirectory, Report);
            // Attach the host before adding the browser to the visual tree (which creates Chromium).
            _scope = runtime.AttachTab(tab, _browser);
            _browser.AddressChanged += AddressChanged;
            _browser.TitleChanged += TitleChanged;
            _browser.LoadingStateChanged += LoadingChanged;
            _browser.LoadError += LoadError;
            _browser.ConsoleMessage += ConsoleMessage;
            _tab.Browser = this;
            BrowserContainer.Children.Add(_browser);
        }

        private void Report(string message)
        {
            if (!_tab.IsIncognito) _runtime.MainWindow.Diagnostics.Report(message);
        }

        private void Post(Action action)
        {
            if (Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_tab.IsClosed && !_disposed) action();
            }));
        }

        private void AddressChanged(object sender, DependencyPropertyChangedEventArgs args) => Post(() =>
        {
            _tab.Address = args.NewValue as string;
            _tab.IconUrl = null;
        });
        private void TitleChanged(object sender, DependencyPropertyChangedEventArgs args) => Post(() =>
            _tab.Title = string.IsNullOrWhiteSpace(args.NewValue as string) ? _tab.Address : (string)args.NewValue);
        private void FaviconChanged(string iconUrl) => Post(() => _tab.IconUrl = _tab.IsIncognito ? null : iconUrl);
        private void LoadingChanged(object sender, LoadingStateChangedEventArgs args) => Post(() =>
        {
            _tab.IsLoading = args.IsLoading;
            _tab.CanGoBack = args.CanGoBack;
            _tab.CanGoForward = args.CanGoForward;
            if (!args.IsLoading) _ = _favicon.RefreshAsync();
        });
        private void LoadError(object sender, LoadErrorEventArgs args)
        {
            if (args.ErrorCode == CefErrorCode.Aborted) return;
            Post(() =>
            {
                _tab.Error = args.ErrorCode + ": " + args.ErrorText;
                Report(_tab.Error);
            });
        }
        private void ConsoleMessage(object sender, ConsoleMessageEventArgs args)
        {
            Runtime.SmokeTrace.Write("console", new { tab = _tab.TabId, level = args.Level.ToString(), args.Message, args.Source, args.Line });
            Report("Console " + args.Level + ": " + args.Message + " (" + args.Source + ":" + args.Line + ")");
        }
        private void OpenTab(string url, bool active) => Post(() =>
        {
            try { _runtime.MainWindow.NewTab(MainWindowViewModel.NormalizeAddress(url), active, _tab, true, true); }
            catch (ArgumentException exception) { Report(exception.Message); }
        });

        public void Load(string address)
        {
            _tab.Error = null;
            _browser.Load(address);
        }

        public void Back() { if (_browser.CanGoBack) _browser.Back(); }
        public void Forward() { if (_browser.CanGoForward) _browser.Forward(); }
        public void Reload() { if (_browser.IsBrowserInitialized) _browser.Reload(); }
        public void Stop() { if (_browser.IsBrowserInitialized) _browser.Stop(); }
        public void ShowDevTools() { if (_browser.IsBrowserInitialized) _browser.ShowDevTools(); }
        internal bool IsJavaScriptReady => _browser.IsBrowserInitialized && _browser.CanExecuteJavascriptInMainFrame;
        internal Task<JavascriptResponse> EvaluateAsync(string script) => _browser.EvaluateScriptAsync(script, timeout: TimeSpan.FromSeconds(5));

        public Task DisposeAsync()
        {
            if (_disposed) return _scope.DisposeAsync();
            _disposed = true;
            _tab.Browser = null;
            _favicon.Dispose();
            _browser.AddressChanged -= AddressChanged;
            _browser.TitleChanged -= TitleChanged;
            _browser.LoadingStateChanged -= LoadingChanged;
            _browser.LoadError -= LoadError;
            _browser.ConsoleMessage -= ConsoleMessage;
            return _scope.DisposeAsync();
        }

    }
}
