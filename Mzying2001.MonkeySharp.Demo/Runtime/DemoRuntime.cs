using Mzying2001.MonkeySharp.Demo.Localization;
using CefSharp;
using Mzying2001.MonkeySharp.CefSharp;
using Mzying2001.MonkeySharp.Core.Permissions;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Demo.Persistence;
using Mzying2001.MonkeySharp.Demo.Services;
using Mzying2001.MonkeySharp.Demo.ViewModels;
using Mzying2001.MonkeySharp.Core.Updates;
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
        public static string TrustWarning => Strings.Get("TrustWarning");
        private readonly Dispatcher _dispatcher;
        private readonly List<TabRuntime> _tabs = new List<TabRuntime>();
        private FileStream _profileLock;
        private SqliteDatabase _database;
        private SqliteUserScriptValueStore _values;
        private SqliteTabStateService _tabStates;
        private InteractiveUserScriptPermissionPolicy _permissions;
        private TabContextManager _tabContexts;
        private bool _cefInitialized;
        private bool _disposed;
        private Task _shutdown;

        private DemoRuntime(AppDataPaths paths, Dispatcher dispatcher)
        {
            Paths = paths;
            _dispatcher = dispatcher;
        }

        public AppDataPaths Paths { get; }
        public PersistentUserScriptRepository Repository { get; private set; }
        public HttpContentService Content { get; private set; }
        public UserScriptUpdateService Updates { get; private set; }
        public MainWindowViewModel MainWindow { get; private set; }
        public IRequestContext RequestContext { get; private set; }
        public SqlitePermissionDecisionStore PermissionDecisions { get; private set; }
        public WpfPermissionPrompt PermissionPrompt { get; private set; }
        internal bool HasPrivateContext => _tabContexts?.HasPrivateContext == true;

        public static async Task<DemoRuntime> CreateAsync(Dispatcher dispatcher, string baseDirectory = null)
        {
            var runtime = new DemoRuntime(new AppDataPaths(baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory), dispatcher);
            try
            {
                await runtime.InitializeAsync();
                return runtime;
            }
            catch
            {
                await runtime.ShutdownAsync();
                throw;
            }
        }

        private async Task InitializeAsync()
        {
            Paths.EnsureCreated();
            _profileLock = new FileStream(Path.Combine(Paths.DataDirectory, ".profile.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _database = new SqliteDatabase(Paths.DatabasePath);
            await _database.InitializeAsync(CancellationToken.None);
            var diagnostics = new DiagnosticsViewModel(_dispatcher, Paths.LogsDirectory);
            Content = new HttpContentService(Paths);
            Repository = new PersistentUserScriptRepository(new SqliteUserScriptRepositoryPersistence(_database, Paths, diagnostics.Report),
                preparer: new UserScriptPreparer(Content, Content));
            await Repository.InitializeAsync(CancellationToken.None);
            _values = new SqliteUserScriptValueStore(_database);
            _tabStates = new SqliteTabStateService(_database);
            Updates = new UserScriptUpdateService(Repository, Content);
            PermissionDecisions = new SqlitePermissionDecisionStore(_database);
            PermissionPrompt = new WpfPermissionPrompt(_dispatcher);
            _permissions = new InteractiveUserScriptPermissionPolicy(new SensitiveUserScriptPermissionsPolicy(), PermissionDecisions, PermissionPrompt);
            await _dispatcher.InvokeAsync(() => InitializeBrowser(diagnostics));
            Repository.Changed += RepositoryChanged;
            diagnostics.Report(TrustWarning);
        }

        private void InitializeBrowser(DiagnosticsViewModel diagnostics)
        {
            CefSharpSettings.WcfEnabled = false;
            CefSharpSettings.ConcurrentTaskExecution = true;
            CefSharpSettings.ShutdownOnExit = false;
            var cache = Path.Combine(Paths.BrowserCacheDirectory, "Default");
            using (var settings = new global::CefSharp.Wpf.CefSettings
            {
                RootCachePath = Paths.BrowserCacheDirectory,
                CachePath = cache,
                PersistSessionCookies = true,
                Locale = UiLanguage.CurrentCulture.Name == "zh-CN" ? "zh-CN" : "en-US",
                LogFile = Path.Combine(Paths.LogsDirectory, "cef.log"),
                LogSeverity = LogSeverity.Disable,
                UncaughtExceptionStackSize = 20
            })
            {
                if (!Cef.Initialize(settings)) throw new InvalidOperationException("CefSharp initialization failed.");
            }
            _cefInitialized = true;
            RequestContext = new RequestContext(new RequestContextSettings { CachePath = cache, PersistSessionCookies = true });
            MainWindow = new MainWindowViewModel(diagnostics);
            _tabContexts = new TabContextManager(_dispatcher, RequestContext, _values,
                _tabStates, _permissions, PermissionPrompt);
        }

        public TabContext GetTabContext(BrowserTabViewModel tab)
        {
            _dispatcher.VerifyAccess();
            if (_disposed) throw new ObjectDisposedException(nameof(DemoRuntime));
            return _tabContexts.GetOrCreate(tab);
        }

        public TabRuntime AttachTab(BrowserTabViewModel tab, global::CefSharp.Wpf.ChromiumWebBrowser browser)
        {
            _dispatcher.VerifyAccess();
            if (_disposed) throw new ObjectDisposedException(nameof(DemoRuntime));
            var context = GetTabContext(tab);
            DownloadService downloads = null;
            var host = new CefSharpUserScriptHostBuilder(Repository)
                .UseValueStore(context.Values)
                .UsePermissionPolicy(context.Permissions)
                .UseMenuService(new WpfMenuService(tab, _dispatcher, Repository))
                .UseNotificationService(new WpfNotificationService(_dispatcher, Content, MainWindow, tab))
                .UseClipboardService(new WpfClipboardService(_dispatcher))
                .UseTabService(new WpfTabService(MainWindow, tab, _dispatcher))
                .UseWindowService(new WpfWindowService(MainWindow, tab, _dispatcher))
                .UseDownloadServiceFactory(http => downloads = new DownloadService(Paths.DownloadsDirectory, _dispatcher, MainWindow, http, isPrivate: context.IsIncognito))
                .UseTabStateService(context.TabState)
                .Configure(new CefSharpHostOptions
                {
                    TrustedPageWorld = true,
                    IsIncognito = native => native == context.RequestContext ? (bool?)context.IsIncognito : null,
                    AutomaticallyUpdatesScripts = false
                })
                .LogTo(entry =>
                {
                    if (!context.IsIncognito)
                        MainWindow.Diagnostics.Report("[" + tab.TabId.Substring(0, 8) + "/" + entry.ScriptKey + "] GM.log " + entry.JsonValue);
                })
                .Build();
            host.Diagnostic += (sender, diagnostic) =>
            {
                if (!context.IsIncognito)
                {
                    SmokeTrace.Write("diagnostic", new { tab = tab.TabId, diagnostic.Code, diagnostic.Message, diagnostic.DocumentId, diagnostic.FrameId, diagnostic.RequestId, exception = diagnostic.Exception?.ToString() });
                    MainWindow.Diagnostics.Report("[" + tab.TabId.Substring(0, 8) + "] " + diagnostic.Code + " " + diagnostic.Message);
                }
            };
            host.DocumentLifecycle += (sender, lifecycle) =>
            {
                if (!context.IsIncognito) SmokeTrace.Write("lifecycle", new { tab = tab.TabId, kind = lifecycle.Kind.ToString(), lifecycle.Frame.DocumentId, lifecycle.Frame.FrameId, url = lifecycle.Frame.Url.AbsoluteUri });
            };
            host.Gateway.Notification += (sender, notification) =>
            {
                if (!context.IsIncognito) SmokeTrace.Write("notification", new { tab = tab.TabId, notification.EventName, notification.ExecutionId, notification.Frame.DocumentId, notification.Frame.FrameId });
            };
            try { host.Attach(browser); }
            catch
            {
                host.Dispose();
                downloads?.Dispose();
                throw;
            }
            TabRuntime scope = null;
            scope = new TabRuntime(browser, host, downloads, context, () =>
            {
                _tabContexts.Release(tab);
                _tabs.Remove(scope);
            });
            _tabs.Add(scope);
            return scope;
        }

        private void RepositoryChanged(object sender, UserScriptRepositoryChangedEventArgs args)
        {
            if (_disposed || _dispatcher.HasShutdownStarted) return;
            _dispatcher.BeginInvoke(new Action(() =>
            {
                ReloadTabs();
                MainWindow.Diagnostics.Report(Strings.Format("ScriptsChanged", args.Kind));
            }));
        }

        public async Task RevokePermissionsAsync(string scriptKey = null)
        {
            var revocation = await _dispatcher.InvokeAsync(() => _tabContexts.CancelPendingAndRevokePrivatePermissionsAsync(scriptKey));
            await revocation;
            await PermissionDecisions.RevokeAsync(scriptKey, CancellationToken.None);
            await _dispatcher.InvokeAsync(ReloadTabs);
        }

        private void ReloadTabs()
        {
            foreach (var tab in MainWindow.Tabs.ToArray()) tab.Browser?.Reload();
        }

        public Task ShutdownAsync() => _shutdown ?? (_shutdown = ShutdownCoreAsync());

        private async Task ShutdownCoreAsync()
        {
            if (_disposed) return;
            _disposed = true;
            if (Repository != null) Repository.Changed -= RepositoryChanged;
            if (MainWindow != null) await _dispatcher.InvokeAsync(MainWindow.CloseAll);
            foreach (var scope in _tabs.ToArray()) await scope.DisposeAsync();
            await _dispatcher.InvokeAsync(() => _tabContexts?.Dispose());
            _permissions?.Dispose();
            Content?.Dispose();
            _values?.Dispose();
            _database?.Dispose();
            if (_cefInitialized)
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    RequestContext?.Dispose();
                    Cef.Shutdown();
                });
                _cefInitialized = false;
            }
            _profileLock?.Dispose();
        }
    }
}
