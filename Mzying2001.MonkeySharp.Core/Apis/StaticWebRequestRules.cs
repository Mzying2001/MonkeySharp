using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Permissions;
using Mzying2001.MonkeySharp.Core.Repository;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Apis
{
    /// <summary>Activates prepared metadata rules before navigation and revokes changed installations.</summary>
    public sealed class StaticWebRequestRules : IDisposable
    {
        private readonly object _sync = new object();
        private readonly IUserScriptRepository _repository;
        private readonly IWebRequestService _service;
        private readonly IUserScriptPermissionPolicy _policy;
        private readonly string _browserId;
        private readonly Func<bool?> _isIncognito;
        private readonly Dictionary<ScriptKey, IWebRequestRegistration> _registrations = new Dictionary<ScriptKey, IWebRequestRegistration>();
        private bool _disposed;
        private long _generation;
        /// <summary>Creates a browser-scoped set. Await InitializeAsync before the first business navigation.</summary>
        public StaticWebRequestRules(IUserScriptRepository repository, IWebRequestService service,
            IUserScriptPermissionPolicy policy, string browserId, Func<bool?> isIncognito)
        {
            _repository = repository; _service = service; _policy = policy; _browserId = browserId; _isIncognito = isIncognito;
            repository.Changed += Changed;
        }
        /// <summary>Loads an atomic snapshot without overwriting concurrent repository changes.</summary>
        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                long generation;
                lock (_sync) { if (_disposed) return; generation = _generation; }
                var scripts = await _repository.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
                lock (_sync)
                {
                    if (_disposed) return;
                    if (_generation != generation) continue;
                    foreach (var script in scripts) Replace(script, false);
                    return;
                }
            }
        }
        private void Changed(object sender, UserScriptRepositoryChangedEventArgs args)
        {
            lock (_sync)
            {
                if (_disposed) return;
                _generation++;
                Replace(args.Installation, args.Kind == RepositoryChangeKind.Removed);
            }
        }
        private void Replace(UserScriptInstallation installation, bool removed)
        {
            if (_registrations.TryGetValue(installation.ScriptKey, out var old)) { old.Dispose(); _registrations.Remove(installation.ScriptKey); }
            if (removed || !installation.IsEnabled || installation.PreparedRevision.WebRequestRules.Count == 0) return;
            var metadata = installation.Definition.Metadata;
            _registrations[installation.ScriptKey] = _service.SetRules(_browserId + ":static:" + installation.ScriptKey,
                installation.PreparedRevision.WebRequestRules,
                request => request.Frame != null && request.Frame.BrowserSessionId == _browserId &&
                    (!metadata.NoFrames || request.Frame.IsMainFrame) &&
                    installation.PreparedRevision.UrlRules.AllowsEnvironment(_isIncognito(), out _) &&
                    installation.PreparedRevision.UrlRules.IsMatch(request.Frame.Url),
                authorize: async (request, token) => await _policy.AuthorizeAsync(new ApiAuthorizationRequest(installation,
                    request.Frame, "GM.webRequest", request.Url, "static @webRequest", new[] { "GM.webRequest" }, canPrompt: false), token)
                    .ConfigureAwait(false) == PermissionDecision.Allow,
                installationId: _browserId + ":" + installation.ScriptKey, isStatic: true);
        }
        /// <inheritdoc />
        public void Dispose()
        {
            _repository.Changed -= Changed;
            lock (_sync) { _disposed = true; foreach (var registration in _registrations.Values) registration.Dispose(); _registrations.Clear(); }
        }
    }
}
