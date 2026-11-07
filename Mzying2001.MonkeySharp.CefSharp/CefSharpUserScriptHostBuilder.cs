using CefSharp;
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
        private IHttpRequestService _http;
        private IMenuService _menu;
        private INotificationService _notifications;
        private IClipboardService _clipboard;
        private ITabService _tabs;
        private IDownloadService _downloads;
        private Func<IHttpRequestService, IDownloadService> _downloadFactory;
        private ITabStateService _tabState;
        private IUserScriptWindowService _window;
        private ICookieService _cookies;
        private IWebRequestService _webRequests;
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

        /// <summary>Enables <c>GM.xmlHttpRequest</c> with a host HTTP service.</summary>
        /// <param name="http">The HTTP request service to use.</param>
        /// <returns>This builder.</returns>
        public CefSharpUserScriptHostBuilder UseHttpRequestService(IHttpRequestService http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
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
            _downloadFactory = null;
            return this;
        }

        /// <summary>Creates downloads using this host's actual shared HTTP service and request context.</summary>
        public CefSharpUserScriptHostBuilder UseDownloadServiceFactory(Func<IHttpRequestService, IDownloadService> factory)
        {
            _downloadFactory = factory ?? throw new ArgumentNullException(nameof(factory));
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

        /// <summary>Enables grant-gated <c>window.close</c> and <c>window.focus</c>.</summary>
        public CefSharpUserScriptHostBuilder UseWindowService(IUserScriptWindowService window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            return this;
        }

        /// <summary>Enables <c>GM.cookie</c> with a host cookie service.</summary>
        /// <param name="cookies">The cookie service to use.</param>
        /// <returns>This builder.</returns>
        public CefSharpUserScriptHostBuilder UseCookieService(ICookieService cookies)
        {
            _cookies = cookies ?? throw new ArgumentNullException(nameof(cookies));
            return this;
        }

        /// <summary>Enables <c>GM.webRequest</c> with a host web request service.</summary>
        public CefSharpUserScriptHostBuilder UseWebRequestService(IWebRequestService webRequests)
        {
            _webRequests = webRequests ?? throw new ArgumentNullException(nameof(webRequests));
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
            var contextAccessor = new CefSharpRequestContextAccessor();
            CefSharpUserScriptHost builtHost = null;
            var engine = new UserScriptEngine(
                _repository,
                options: new UserScriptEngineOptions
                {
                    RequireGuaranteedDocumentStart = _options.RequireGuaranteedDocumentStart,
                    RequireVerifiedBridge = true,
                    SupportsIsolatedWorlds = true,
                    EnvironmentProvider = frame => new UserScriptExecutionEnvironment(
                        _downloads?.DownloadMode ?? "disabled",
                        _options.IsIncognito?.Invoke(contextAccessor.Current),
                        _options.AutomaticallyUpdatesScripts,
                        unavailableCapabilities: _options.EnableDefaultNetworkServices ? null : new Dictionary<string, string>
                        {
                            ["GM.xmlHttpRequest"] = "policy-disabled", ["GM.cookie"] = "policy-disabled", ["GM.webRequest"] = "policy-disabled"
                        })
                });
            var ownedServices = new List<IDisposable>();
            try
            {
                var effectiveWebRequests = _webRequests;
                if (effectiveWebRequests == null && _options.EnableDefaultNetworkServices)
                {
                    effectiveWebRequests = new InMemoryWebRequestService();
                    ownedServices.Add((IDisposable)effectiveWebRequests);
                }

                var effectiveHttp = _http;
                if (effectiveHttp == null && _options.EnableDefaultNetworkServices)
                {
                    effectiveHttp = new CefSharpHttpRequestService(
                        () => contextAccessor.Current,
                        effectiveWebRequests);
                    ownedServices.Add((IDisposable)effectiveHttp);
                }

                var effectiveCookies = _cookies;
                if (_downloadFactory != null)
                {
                    if (effectiveHttp == null) throw new InvalidOperationException("The download factory requires an HTTP request service.");
                    _downloads = _downloadFactory(effectiveHttp) ?? throw new InvalidOperationException("The download factory returned no service.");
                    if (_downloads is IDisposable downloadOwner) ownedServices.Add(downloadOwner);
                }
                if (effectiveCookies == null && _options.EnableDefaultNetworkServices)
                {
                    effectiveCookies = new CefSharpCookieService(token => builtHost.GetCdpSessionAsync(token));
                    ownedServices.Add((IDisposable)effectiveCookies);
                }

                var providers = new List<IUserScriptApiProvider>(_providers);
                providers.Add(new ResourceAndNetworkApiProvider(http: effectiveHttp, options: _options.Bridge));
                if (_menu != null || _notifications != null || _clipboard != null || _tabs != null ||
                    _downloads != null || _tabState != null || _window != null)
                {
                    providers.Add(new HostInteractionApiProvider(
                        _menu,
                        _notifications,
                        _clipboard,
                        _tabs,
                        _downloads,
                        _tabState,
                        _window, _options.Bridge));
                }
                if (effectiveCookies != null)
                    providers.Add(new CookieApiProvider(effectiveCookies));
                if (effectiveWebRequests != null)
                    providers.Add(new WebRequestApiProvider(effectiveWebRequests, _repository));
                var gateway = new UserScriptBridgeGateway(
                    engine,
                    store,
                    _permissionPolicy,
                    providers,
                    _options.Bridge ?? throw new InvalidOperationException("Bridge options are required."),
                    _log);
                builtHost = new CefSharpUserScriptHost(
                    engine,
                    gateway,
                    _options,
                    ownsStore ? store as IDisposable : null,
                    effectiveWebRequests,
                    contextAccessor,
                    ownedServices, _repository, _permissionPolicy);
                return builtHost;
            }
            catch
            {
                engine.Dispose();
                if (ownsStore && store is IDisposable disposable)
                    disposable.Dispose();
                for (var index = ownedServices.Count - 1; index >= 0; index--)
                    ownedServices[index].Dispose();
                throw;
            }
        }
    }
}
