using CefSharp;
using Mzying2001.MonkeySharp.CefSharp;
using Mzying2001.MonkeySharp.Core.Permissions;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Demo.Persistence;
using Mzying2001.MonkeySharp.Demo.Services;
using Mzying2001.MonkeySharp.Demo.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Mzying2001.MonkeySharp.Demo.Runtime
{
    public sealed class DemoRuntime
    {
        public const string TrustWarning = "TrustedPageWorld 演示模式：网页可能观察并使用脚本已获授权的桥接能力。请勿运行不可信脚本或访问不可信网站；这不是安全沙箱。";
        private readonly Dispatcher _dispatcher;
        private readonly List<TabRuntime> _tabs = new List<TabRuntime>();
        private FileStream _profileLock;
        private SqliteDatabase _database;
        private SqliteUserScriptValueStore _values;
        private SqliteTabStateService _tabStates;
        private bool _cefInitialized;
        private bool _disposed;
        private DemoRuntime(AppDataPaths paths, Dispatcher dispatcher) { Paths = paths; _dispatcher = dispatcher; }
        public AppDataPaths Paths { get; }
        public PersistentUserScriptRepository Repository { get; private set; }
        public HttpContentService Content { get; private set; }
        public MainWindowViewModel MainWindow { get; private set; }
        public IRequestContext RequestContext { get; private set; }

        public static async Task<DemoRuntime> CreateAsync(Dispatcher dispatcher, string baseDirectory = null)
        {
            var runtime = new DemoRuntime(new AppDataPaths(baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory), dispatcher);
            try
            {
                runtime.Paths.EnsureCreated();
                runtime._profileLock = new FileStream(Path.Combine(runtime.Paths.DataDirectory, ".profile.lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                runtime._database = new SqliteDatabase(runtime.Paths.DatabasePath);
                await runtime._database.InitializeAsync(CancellationToken.None);
                var diagnostics = new DiagnosticsViewModel(dispatcher, runtime.Paths.LogsDirectory);
                runtime.Repository = new PersistentUserScriptRepository(new SqliteUserScriptRepositoryPersistence(runtime._database, runtime.Paths, diagnostics.Report));
                await runtime.Repository.InitializeAsync(CancellationToken.None);
                runtime._values = new SqliteUserScriptValueStore(runtime._database);
                runtime._tabStates = new SqliteTabStateService(runtime._database);
                runtime.Content = new HttpContentService(runtime.Paths);
                await dispatcher.InvokeAsync(() =>
                {
                    CefSharpSettings.WcfEnabled = false;
                    CefSharpSettings.ConcurrentTaskExecution = true;
                    CefSharpSettings.ShutdownOnExit = false;
                    var cache = Path.Combine(runtime.Paths.BrowserCacheDirectory, "Default");
                    using (var settings = new global::CefSharp.Wpf.CefSettings
                    {
                        RootCachePath = runtime.Paths.BrowserCacheDirectory, CachePath = cache,
                        PersistSessionCookies = true, LogFile = Path.Combine(runtime.Paths.LogsDirectory, "cef.log"),
                        LogSeverity = LogSeverity.Warning, UncaughtExceptionStackSize = 20
                    })
                    {
                        if (!Cef.Initialize(settings)) throw new InvalidOperationException("CefSharp initialization failed.");
                    }
                    runtime._cefInitialized = true;
                    runtime.RequestContext = new RequestContext(new RequestContextSettings { CachePath = cache, PersistSessionCookies = true });
                    runtime.MainWindow = new MainWindowViewModel(diagnostics);
                });
                runtime.Repository.Changed += runtime.RepositoryChanged;
                diagnostics.Report(TrustWarning);
                return runtime;
            }
            catch { await runtime.ShutdownAsync(); throw; }
        }

        public TabRuntime AttachTab(BrowserTabViewModel tab, global::CefSharp.Wpf.ChromiumWebBrowser browser)
        {
            _dispatcher.VerifyAccess();
            if (_disposed) throw new ObjectDisposedException(nameof(DemoRuntime));
            var downloads = new DownloadService(Paths.DownloadsDirectory, _dispatcher, MainWindow);
            var host = new CefSharpUserScriptHostBuilder(Repository)
                .UseValueStore(_values).UsePermissionPolicy(new AllowDeclaredPermissionsPolicy())
                .UseMenuService(new WpfMenuService(tab, _dispatcher))
                .UseNotificationService(new WpfNotificationService(_dispatcher, Content))
                .UseClipboardService(new WpfClipboardService(_dispatcher))
                .UseTabService(new WpfTabService(MainWindow, tab, _dispatcher))
                .UseDownloadService(downloads).UseTabStateService(_tabStates.ForTab(tab.TabId))
                .UseResourceProvider(Content).UseDependencyProvider(Content)
                .Configure(new CefSharpHostOptions { TrustedPageWorld = true })
                .LogTo(entry => MainWindow.Diagnostics.Report("[" + tab.TabId.Substring(0, 8) + "/" + entry.ScriptKey + "] GM.log " + entry.JsonValue))
                .Build();
            host.Diagnostic += (sender, diagnostic) => MainWindow.Diagnostics.Report(
                "[" + tab.TabId.Substring(0, 8) + "] " + diagnostic.Code + " " + diagnostic.Message);
            try { host.Attach(browser); }
            catch { host.Dispose(); downloads.Dispose(); throw; }
            TabRuntime scope = null;
            scope = new TabRuntime(browser, host, downloads, () => { _tabStates.CloseTab(tab.TabId); _tabs.Remove(scope); });
            _tabs.Add(scope);
            return scope;
        }

        private void RepositoryChanged(object sender, UserScriptRepositoryChangedEventArgs args)
        {
            if (_disposed || _dispatcher.HasShutdownStarted) return;
            _dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var tab in MainWindow.Tabs.ToArray()) tab.Browser?.Reload();
                MainWindow.Diagnostics.Report("脚本变更 " + args.Kind + "：已刷新打开的标签页。");
            }));
        }

        public async Task ShutdownAsync()
        {
            if (_disposed) return;
            _disposed = true;
            if (Repository != null) Repository.Changed -= RepositoryChanged;
            if (MainWindow != null) await _dispatcher.InvokeAsync(MainWindow.CloseAll);
            foreach (var scope in _tabs.ToArray()) await scope.DisposeAsync();
            Content?.Dispose();
            _values?.Dispose();
            _database?.Dispose();
            if (_cefInitialized)
            {
                await _dispatcher.InvokeAsync(() => { RequestContext?.Dispose(); Cef.Shutdown(); });
                _cefInitialized = false;
            }
            _profileLock?.Dispose();
        }
    }

    public sealed class TabRuntime
    {
        private readonly global::CefSharp.Wpf.ChromiumWebBrowser _browser;
        private readonly DownloadService _downloads;
        private readonly Action _closed;
        private Task _disposal;
        public TabRuntime(global::CefSharp.Wpf.ChromiumWebBrowser browser, CefSharpUserScriptHost host, DownloadService downloads, Action closed)
        { _browser = browser; Host = host; _downloads = downloads; _closed = closed; }
        public CefSharpUserScriptHost Host { get; }
        public Task DisposeAsync() => _disposal ?? (_disposal = DisposeCoreAsync());
        private async Task DisposeCoreAsync()
        {
            await _browser.Dispatcher.InvokeAsync(() =>
            {
                if (!_browser.IsDisposed && _browser.IsBrowserInitialized) _browser.Stop();
            });
            _downloads.Dispose();
            await Host.DetachAsync(CancellationToken.None);
            Host.Dispose();
            await _downloads.DrainAsync();
            await _browser.Dispatcher.InvokeAsync(() => { _browser.Dispose(); _closed(); });
        }
    }
}
