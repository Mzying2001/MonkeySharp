using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Parsing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Repository
{
    public enum RepositoryChangeKind
    {
        Installed,
        Updated,
        Enabled,
        Disabled,
        Removed
    }

    public sealed class UserScriptRepositoryChangedEventArgs : EventArgs
    {
        public UserScriptRepositoryChangedEventArgs(RepositoryChangeKind kind, UserScriptInstallation installation)
        {
            Kind = kind;
            Installation = installation;
        }

        public RepositoryChangeKind Kind { get; }
        public UserScriptInstallation Installation { get; }
    }

    public interface IUserScriptRepository
    {
        event EventHandler<UserScriptRepositoryChangedEventArgs> Changed;
        Task<UserScriptInstallation> InstallAsync(string source, string sourceOrigin, bool enabled, CancellationToken cancellationToken);
        Task<UserScriptInstallation> UpdateAsync(ScriptKey scriptKey, string source, string sourceOrigin, CancellationToken cancellationToken);
        Task<UserScriptInstallation> SetEnabledAsync(ScriptKey scriptKey, bool enabled, CancellationToken cancellationToken);
        Task<bool> RemoveAsync(ScriptKey scriptKey, CancellationToken cancellationToken);
        Task<UserScriptInstallation> GetAsync(ScriptKey scriptKey, CancellationToken cancellationToken);
        Task<IReadOnlyList<UserScriptInstallation>> GetSnapshotAsync(CancellationToken cancellationToken);
    }

    public sealed class InMemoryUserScriptRepository : IUserScriptRepository
    {
        private readonly object _sync = new object();
        private readonly IUserScriptMetadataParser _parser;
        private readonly Dictionary<ScriptKey, UserScriptInstallation> _installations =
            new Dictionary<ScriptKey, UserScriptInstallation>();
        private readonly List<ScriptKey> _order = new List<ScriptKey>();

        public InMemoryUserScriptRepository(IUserScriptMetadataParser parser = null)
        {
            _parser = parser ?? new UserScriptMetadataParser();
        }

        public event EventHandler<UserScriptRepositoryChangedEventArgs> Changed;

        public Task<UserScriptInstallation> InstallAsync(
            string source,
            string sourceOrigin,
            bool enabled,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var definition = ParseDefinition(source);
            var now = DateTimeOffset.UtcNow;
            var installation = new UserScriptInstallation(
                ScriptKey.Create(), definition, sourceOrigin, enabled, now, now);
            lock (_sync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _installations.Add(installation.ScriptKey, installation);
                _order.Add(installation.ScriptKey);
            }
            Changed?.Invoke(this, new UserScriptRepositoryChangedEventArgs(RepositoryChangeKind.Installed, installation));
            return Task.FromResult(installation);
        }

        public Task<UserScriptInstallation> UpdateAsync(
            ScriptKey scriptKey,
            string source,
            string sourceOrigin,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var definition = ParseDefinition(source);
            UserScriptInstallation installation;
            lock (_sync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                installation = GetRequired(scriptKey).WithDefinition(definition, sourceOrigin, DateTimeOffset.UtcNow);
                _installations[scriptKey] = installation;
            }
            Changed?.Invoke(this, new UserScriptRepositoryChangedEventArgs(RepositoryChangeKind.Updated, installation));
            return Task.FromResult(installation);
        }

        public Task<UserScriptInstallation> SetEnabledAsync(
            ScriptKey scriptKey,
            bool enabled,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UserScriptInstallation installation;
            lock (_sync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = GetRequired(scriptKey);
                if (enabled && !current.Definition.ParseResult.CanEnable)
                    throw new MetadataValidationException(current.Definition.ParseResult);
                installation = current.WithEnabled(enabled, DateTimeOffset.UtcNow);
                _installations[scriptKey] = installation;
            }
            Changed?.Invoke(this, new UserScriptRepositoryChangedEventArgs(
                enabled ? RepositoryChangeKind.Enabled : RepositoryChangeKind.Disabled,
                installation));
            return Task.FromResult(installation);
        }

        public Task<bool> RemoveAsync(ScriptKey scriptKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UserScriptInstallation installation;
            lock (_sync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_installations.TryGetValue(scriptKey, out installation))
                    return Task.FromResult(false);
                _installations.Remove(scriptKey);
                _order.Remove(scriptKey);
            }
            Changed?.Invoke(this, new UserScriptRepositoryChangedEventArgs(RepositoryChangeKind.Removed, installation));
            return Task.FromResult(true);
        }

        public Task<UserScriptInstallation> GetAsync(ScriptKey scriptKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
                return Task.FromResult(GetRequired(scriptKey));
        }

        public Task<IReadOnlyList<UserScriptInstallation>> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                IReadOnlyList<UserScriptInstallation> result = new ReadOnlyCollection<UserScriptInstallation>(
                    _order.Select(key => _installations[key]).ToList());
                return Task.FromResult(result);
            }
        }

        private UserScriptDefinition ParseDefinition(string source)
        {
            var parseResult = _parser.Parse(source);
            if (!parseResult.CanEnable)
                throw new MetadataValidationException(parseResult);
            return new UserScriptDefinition(source, parseResult);
        }

        private UserScriptInstallation GetRequired(ScriptKey scriptKey)
        {
            if (!_installations.TryGetValue(scriptKey, out var installation))
                throw new KeyNotFoundException("Script installation '" + scriptKey + "' was not found.");
            return installation;
        }
    }
}
