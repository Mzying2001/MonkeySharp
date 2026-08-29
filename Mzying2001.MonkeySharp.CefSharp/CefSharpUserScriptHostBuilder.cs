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
            var ownsStore = _store == null;
            var store = _store ?? new InMemoryUserScriptValueStore();
            var engine = new UserScriptEngine(
                _repository,
                options: new UserScriptEngineOptions
                {
                    RequireGuaranteedDocumentStart = _options.RequireGuaranteedDocumentStart,
                    RequireVerifiedBridge = true
                });
            try
            {
                var gateway = new UserScriptBridgeGateway(
                    engine,
                    store,
                    _permissionPolicy,
                    _providers,
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
