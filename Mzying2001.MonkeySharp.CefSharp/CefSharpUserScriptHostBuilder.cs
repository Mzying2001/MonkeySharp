using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Permissions;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using Mzying2001.MonkeySharp.Core.Storage;
using System;
using System.Collections.Generic;

namespace Mzying2001.MonkeySharp.CefSharp
{
    public sealed class CefSharpUserScriptHostBuilder
    {
        private readonly IUserScriptRepository _repository;
        private readonly List<IUserScriptApiProvider> _providers = new List<IUserScriptApiProvider>();
        private IUserScriptValueStore _store;
        private IUserScriptPermissionPolicy _permissionPolicy;
        private Action<UserScriptLogEntry> _log;
        private CefSharpHostOptions _options = new CefSharpHostOptions();
        private IResourceProvider _resources;
        private IHttpRequestService _http;
        private IMenuService _menu;
        private INotificationService _notifications;
        private IClipboardService _clipboard;
        private ITabService _tabs;
        private IDownloadService _downloads;
        private ITabStateService _tabState;
        private IUserScriptDependencyProvider _dependencies;
        private bool _built;

        public CefSharpUserScriptHostBuilder(IUserScriptRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public CefSharpUserScriptHostBuilder UseValueStore(IUserScriptValueStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            return this;
        }

        public CefSharpUserScriptHostBuilder UsePermissionPolicy(IUserScriptPermissionPolicy permissionPolicy)
        {
            _permissionPolicy = permissionPolicy ?? throw new ArgumentNullException(nameof(permissionPolicy));
            return this;
        }

        public CefSharpUserScriptHostBuilder AddApiProvider(IUserScriptApiProvider provider)
        {
            _providers.Add(provider ?? throw new ArgumentNullException(nameof(provider)));
            return this;
        }

        public CefSharpUserScriptHostBuilder UseResourceProvider(IResourceProvider resources)
        {
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            return this;
        }

        public CefSharpUserScriptHostBuilder UseHttpRequestService(IHttpRequestService http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            return this;
        }

        public CefSharpUserScriptHostBuilder UseDependencyProvider(IUserScriptDependencyProvider dependencies)
        {
            _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            return this;
        }

        public CefSharpUserScriptHostBuilder UseMenuService(IMenuService menu)
        {
            _menu = menu ?? throw new ArgumentNullException(nameof(menu));
            return this;
        }

        public CefSharpUserScriptHostBuilder UseNotificationService(INotificationService notifications)
        {
            _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
            return this;
        }

        public CefSharpUserScriptHostBuilder UseClipboardService(IClipboardService clipboard)
        {
            _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
            return this;
        }

        public CefSharpUserScriptHostBuilder UseTabService(ITabService tabs)
        {
            _tabs = tabs ?? throw new ArgumentNullException(nameof(tabs));
            return this;
        }

        public CefSharpUserScriptHostBuilder UseDownloadService(IDownloadService downloads)
        {
            _downloads = downloads ?? throw new ArgumentNullException(nameof(downloads));
            return this;
        }

        public CefSharpUserScriptHostBuilder UseTabStateService(ITabStateService tabState)
        {
            _tabState = tabState ?? throw new ArgumentNullException(nameof(tabState));
            return this;
        }

        public CefSharpUserScriptHostBuilder LogTo(Action<UserScriptLogEntry> log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            return this;
        }

        public CefSharpUserScriptHostBuilder Configure(CefSharpHostOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            return this;
        }

        public CefSharpUserScriptHost Build()
        {
            if (_built)
                throw new InvalidOperationException("A CefSharpUserScriptHostBuilder can only build one host.");
            _built = true;
            var ownsStore = _store == null;
            var store = _store ?? new InMemoryUserScriptValueStore();
            var engine = new UserScriptEngine(
                _repository,
                options: new UserScriptEngineOptions
                {
                    RequireGuaranteedDocumentStart = _options.RequireGuaranteedDocumentStart,
                    RequireVerifiedBridge = true
                },
                sourceResolver: _dependencies == null
                    ? null
                    : new ResourceScriptSourceResolver(
                        _dependencies,
                        _options.Bridge?.MaxResourceBytes ?? 10 * 1024 * 1024));
            try
            {
                var providers = new List<IUserScriptApiProvider>(_providers);
                if (_resources != null || _http != null)
                {
                    providers.Add(new ResourceAndNetworkApiProvider(
                        _resources,
                        _http,
                        _options.Bridge));
                }
                if (_menu != null || _notifications != null || _clipboard != null || _tabs != null ||
                    _downloads != null || _tabState != null)
                {
                    providers.Add(new HostInteractionApiProvider(
                        _menu,
                        _notifications,
                        _clipboard,
                        _tabs,
                        _downloads,
                        _tabState));
                }
                var gateway = new UserScriptBridgeGateway(
                    engine,
                    store,
                    _permissionPolicy,
                    providers,
                    _options.Bridge ?? throw new InvalidOperationException("Bridge options are required."),
                    _log);
                return new CefSharpUserScriptHost(
                    engine,
                    gateway,
                    _options,
                    ownsStore ? store as IDisposable : null);
            }
            catch
            {
                engine.Dispose();
                if (ownsStore && store is IDisposable disposable)
                    disposable.Dispose();
                throw;
            }
        }
    }
}
