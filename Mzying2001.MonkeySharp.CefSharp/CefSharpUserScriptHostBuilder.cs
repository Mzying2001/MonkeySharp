using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Compatibility;
using Mzying2001.MonkeySharp.Core.Permissions;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using Mzying2001.MonkeySharp.Core.Storage;
using System;
using System.Collections.Generic;

namespace Mzying2001.MonkeySharp.CefSharp
{
    /// <summary>
    /// Configures and creates a <see cref="CefSharpUserScriptHost"/> and its Core runtime services.
    /// </summary>
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

        /// <summary>Initializes a host builder.</summary>
        /// <param name="repository">The repository containing installed userscripts.</param>
        public CefSharpUserScriptHostBuilder(IUserScriptRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        /// <summary>Uses a custom persistent-value store instead of the default in-memory store.</summary>
        /// <param name="store">The value store to use.</param>
        /// <returns>This builder.</returns>
        public CefSharpUserScriptHostBuilder UseValueStore(IUserScriptValueStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            return this;
        }

        /// <summary>Uses a host policy to authorize API calls after metadata grant checks.</summary>
        /// <param name="permissionPolicy">The permission policy to use.</param>
        /// <returns>This builder.</returns>
        public CefSharpUserScriptHostBuilder UsePermissionPolicy(IUserScriptPermissionPolicy permissionPolicy)
        {
            _permissionPolicy = permissionPolicy ?? throw new ArgumentNullException(nameof(permissionPolicy));
            return this;
        }

        /// <summary>Adds a custom userscript API provider.</summary>
        /// <param name="provider">The API provider to add.</param>
        /// <returns>This builder.</returns>
        public CefSharpUserScriptHostBuilder AddApiProvider(IUserScriptApiProvider provider)
        {
            _providers.Add(provider ?? throw new ArgumentNullException(nameof(provider)));
            return this;
        }

        /// <summary>Enables resource GM APIs with a host resource provider.</summary>
        /// <param name="resources">The resource provider to use.</param>
        /// <returns>This builder.</returns>
        public CefSharpUserScriptHostBuilder UseResourceProvider(IResourceProvider resources)
        {
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            return this;
        }

        /// <summary>Enables <c>GM.xmlHttpRequest</c> with a host HTTP service.</summary>
        /// <param name="http">The HTTP request service to use.</param>
        /// <returns>This builder.</returns>
        public CefSharpUserScriptHostBuilder UseHttpRequestService(IHttpRequestService http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            return this;
        }

        /// <summary>Enables <c>@require</c> dependency loading.</summary>
        /// <param name="dependencies">The dependency provider to use.</param>
        /// <returns>This builder.</returns>
        public CefSharpUserScriptHostBuilder UseDependencyProvider(IUserScriptDependencyProvider dependencies)
        {
            _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            return this;
        }

        /// <summary>Enables menu command GM APIs with a host menu service.</summary>
        /// <param name="menu">The menu service to use.</param>
        /// <returns>This builder.</returns>
        public CefSharpUserScriptHostBuilder UseMenuService(IMenuService menu)
        {
            _menu = menu ?? throw new ArgumentNullException(nameof(menu));
            return this;
        }

        /// <summary>Enables <c>GM.notification</c> with a host notification service.</summary>
        /// <param name="notifications">The notification service to use.</param>
        /// <returns>This builder.</returns>
        public CefSharpUserScriptHostBuilder UseNotificationService(INotificationService notifications)
        {
            _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
            return this;
        }

        /// <summary>Enables <c>GM.setClipboard</c> with a host clipboard service.</summary>
        /// <param name="clipboard">The clipboard service to use.</param>
        /// <returns>This builder.</returns>
        public CefSharpUserScriptHostBuilder UseClipboardService(IClipboardService clipboard)
        {
            _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
            return this;
        }

        /// <summary>Enables <c>GM.openInTab</c> with a host tab service.</summary>
        /// <param name="tabs">The tab service to use.</param>
        /// <returns>This builder.</returns>
        public CefSharpUserScriptHostBuilder UseTabService(ITabService tabs)
        {
            _tabs = tabs ?? throw new ArgumentNullException(nameof(tabs));
            return this;
        }

        /// <summary>Enables <c>GM.download</c> with a host download service.</summary>
        /// <param name="downloads">The download service to use.</param>
        /// <returns>This builder.</returns>
        public CefSharpUserScriptHostBuilder UseDownloadService(IDownloadService downloads)
        {
            _downloads = downloads ?? throw new ArgumentNullException(nameof(downloads));
            return this;
        }

        /// <summary>Enables tab-state GM APIs with a host tab-state service.</summary>
        /// <param name="tabState">The tab-state service to use.</param>
        /// <returns>This builder.</returns>
        public CefSharpUserScriptHostBuilder UseTabStateService(ITabStateService tabState)
        {
            _tabState = tabState ?? throw new ArgumentNullException(nameof(tabState));
            return this;
        }

        /// <summary>Routes <c>GM.log</c> values to a host callback.</summary>
        /// <param name="log">The callback that receives log entries.</param>
        /// <returns>This builder.</returns>
        public CefSharpUserScriptHostBuilder LogTo(Action<UserScriptLogEntry> log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            return this;
        }

        /// <summary>Uses the supplied CefSharp host options.</summary>
        /// <param name="options">The host options to use.</param>
        /// <returns>This builder.</returns>
        public CefSharpUserScriptHostBuilder Configure(CefSharpHostOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (_options.Compatibility == null)
                _options.Compatibility = new UserScriptCompatibilityOptions();
            return this;
        }

        /// <summary>Builds the configured host.</summary>
        /// <returns>A host ready to attach to one CefSharp browser.</returns>
        /// <exception cref="InvalidOperationException">This builder has already built a host.</exception>
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
                    RequireVerifiedBridge = true,
                    Compatibility = _options.Compatibility
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
