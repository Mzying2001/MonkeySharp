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
    /// <summary>Describes an installation independently of a particular persistence format.</summary>
    public sealed class UserScriptPersistenceRecord
    {
        /// <summary>Initializes a durable record. Null source represents an unavailable source file.</summary>
        public UserScriptPersistenceRecord(ScriptKey scriptKey, string source, string sourceOrigin,
            bool isEnabled, DateTimeOffset installedAt, DateTimeOffset updatedAt)
        {
            if (!ScriptKey.TryParse(scriptKey.ToString(), out var validatedKey))
                throw new ArgumentException("A nonempty script key is required.", nameof(scriptKey));
            ScriptKey = validatedKey;
            Source = source;
            SourceOrigin = sourceOrigin;
            IsEnabled = isEnabled;
            InstalledAt = installedAt;
            UpdatedAt = updatedAt;
        }

        /// <summary>Gets the stable installation identity.</summary>
        public ScriptKey ScriptKey { get; }

        /// <summary>Gets the source, or null when it cannot be recovered.</summary>
        public string Source { get; }

        /// <summary>Gets the original installation location.</summary>
        public string SourceOrigin { get; }

        /// <summary>Gets the persisted enabled state.</summary>
        public bool IsEnabled { get; }

        /// <summary>Gets the installation timestamp.</summary>
        public DateTimeOffset InstalledAt { get; }

        /// <summary>Gets the last update timestamp.</summary>
        public DateTimeOffset UpdatedAt { get; }
    }

    /// <summary>Stores records in stable repository order. Implementations must commit atomically.</summary>
    public interface IUserScriptRepositoryPersistence
    {
        /// <summary>Loads records in repository order, retaining records whose source is unavailable.</summary>
        Task<IReadOnlyList<UserScriptPersistenceRecord>> LoadAsync(CancellationToken cancellationToken);

        /// <summary>Inserts or replaces a record without changing its order. Cancellation after commit must succeed.</summary>
        Task SaveAsync(UserScriptPersistenceRecord record, CancellationToken cancellationToken);

        /// <summary>Removes a record. Failure before commit must leave the previous record intact.</summary>
        Task DeleteAsync(ScriptKey scriptKey, CancellationToken cancellationToken);
    }

    /// <summary>Serializes durable mutations and exposes immutable installation snapshots.</summary>
    public sealed class PersistentUserScriptRepository : IUserScriptRepository
    {
        private readonly object _sync = new object();
        private readonly SemaphoreSlim _mutations = new SemaphoreSlim(1, 1);
        private readonly IUserScriptRepositoryPersistence _persistence;
        private readonly IUserScriptMetadataParser _parser;
        private readonly Dictionary<ScriptKey, UserScriptInstallation> _installations =
            new Dictionary<ScriptKey, UserScriptInstallation>();
        private readonly List<ScriptKey> _order = new List<ScriptKey>();
        private bool _initialized;

        /// <summary>Creates a repository that must be initialized before it is attached to a runtime.</summary>
        public PersistentUserScriptRepository(IUserScriptRepositoryPersistence persistence,
            IUserScriptMetadataParser parser = null)
        {
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _parser = parser ?? new UserScriptMetadataParser();
        }

        /// <inheritdoc />
        public event EventHandler<UserScriptRepositoryChangedEventArgs> Changed;

        /// <summary>Restores stable identities and timestamps; invalid or unavailable scripts remain visible but disabled.</summary>
        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (_sync)
                    if (_initialized) throw new InvalidOperationException("The repository is already initialized.");
                var records = await _persistence.LoadAsync(cancellationToken).ConfigureAwait(false);
                var restored = new Dictionary<ScriptKey, UserScriptInstallation>();
                var order = new List<ScriptKey>();
                foreach (var record in records)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var source = record.Source ?? string.Empty;
                    var parsed = _parser.Parse(source);
                    restored.Add(record.ScriptKey, new UserScriptInstallation(record.ScriptKey,
                        new UserScriptDefinition(source, parsed), record.SourceOrigin,
                        record.IsEnabled && parsed.CanEnable, record.InstalledAt, record.UpdatedAt));
                    order.Add(record.ScriptKey);
                }
                lock (_sync)
                {
                    foreach (var pair in restored) _installations.Add(pair.Key, pair.Value);
                    _order.AddRange(order);
                    _initialized = true;
                }
            }
            finally { _mutations.Release(); }
        }

        /// <inheritdoc />
        public Task<UserScriptInstallation> InstallAsync(string source, string sourceOrigin, bool enabled,
            CancellationToken cancellationToken)
        {
            return SaveAsync(() =>
            {
                var now = DateTimeOffset.UtcNow;
                return new UserScriptInstallation(ScriptKey.Create(), Parse(source), sourceOrigin, enabled, now, now);
            }, RepositoryChangeKind.Installed, cancellationToken);
        }

        /// <inheritdoc />
        public Task<UserScriptInstallation> UpdateAsync(ScriptKey scriptKey, string source, string sourceOrigin,
            CancellationToken cancellationToken)
        {
            return SaveAsync(() => GetRequired(scriptKey).WithDefinition(Parse(source), sourceOrigin, DateTimeOffset.UtcNow),
                RepositoryChangeKind.Updated, cancellationToken);
        }

        /// <inheritdoc />
        public Task<UserScriptInstallation> UpdateIfUnchangedAsync(
            ScriptKey scriptKey,
            Guid expectedRevisionId,
            string source,
            string sourceOrigin,
            CancellationToken cancellationToken)
        {
            return SaveAsync(() =>
            {
                var current = GetRequired(scriptKey);
                if (current.RevisionId != expectedRevisionId)
                    throw new RepositoryRevisionMismatchException(scriptKey);
                return current.WithDefinition(Parse(source), sourceOrigin, DateTimeOffset.UtcNow);
            }, RepositoryChangeKind.Updated, cancellationToken);
        }

        /// <inheritdoc />
        public Task<UserScriptInstallation> SetEnabledAsync(ScriptKey scriptKey, bool enabled, CancellationToken cancellationToken)
        {
            return SaveAsync(() =>
            {
                var current = GetRequired(scriptKey);
                if (!current.Definition.ParseResult.CanEnable)
                    throw new MetadataValidationException(current.Definition.ParseResult);
                return current.WithEnabled(enabled, DateTimeOffset.UtcNow);
            }, enabled ? RepositoryChangeKind.Enabled : RepositoryChangeKind.Disabled, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<bool> RemoveAsync(ScriptKey scriptKey, CancellationToken cancellationToken)
        {
            await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                UserScriptInstallation installation;
                lock (_sync)
                {
                    EnsureInitialized();
                    if (!_installations.TryGetValue(scriptKey, out installation)) return false;
                }
                await _persistence.DeleteAsync(scriptKey, cancellationToken).ConfigureAwait(false);
                lock (_sync)
                {
                    _installations.Remove(scriptKey);
                    _order.Remove(scriptKey);
                }
                Publish(RepositoryChangeKind.Removed, installation);
                return true;
            }
            finally { _mutations.Release(); }
        }

        /// <inheritdoc />
        public Task<UserScriptInstallation> GetAsync(ScriptKey scriptKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                EnsureInitialized();
                return Task.FromResult(GetRequired(scriptKey));
            }
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<UserScriptInstallation>> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                EnsureInitialized();
                return Task.FromResult<IReadOnlyList<UserScriptInstallation>>(
                    new ReadOnlyCollection<UserScriptInstallation>(_order.Select(key => _installations[key]).ToList()));
            }
        }

        private async Task<UserScriptInstallation> SaveAsync(Func<UserScriptInstallation> create,
            RepositoryChangeKind kind, CancellationToken cancellationToken)
        {
            await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                UserScriptInstallation installation;
                lock (_sync)
                {
                    EnsureInitialized();
                    cancellationToken.ThrowIfCancellationRequested();
                    installation = create();
                }
                await _persistence.SaveAsync(new UserScriptPersistenceRecord(installation.ScriptKey,
                    installation.Definition.Source, installation.SourceOrigin, installation.IsEnabled,
                    installation.InstalledAt, installation.UpdatedAt), cancellationToken).ConfigureAwait(false);
                lock (_sync)
                {
                    if (!_installations.ContainsKey(installation.ScriptKey)) _order.Add(installation.ScriptKey);
                    _installations[installation.ScriptKey] = installation;
                }
                Publish(kind, installation);
                return installation;
            }
            finally { _mutations.Release(); }
        }

        private UserScriptDefinition Parse(string source)
        {
            var parsed = _parser.Parse(source);
            if (!parsed.CanEnable) throw new MetadataValidationException(parsed);
            return new UserScriptDefinition(source, parsed);
        }

        private UserScriptInstallation GetRequired(ScriptKey key)
        {
            if (!_installations.TryGetValue(key, out var value)) throw new KeyNotFoundException("Script not found: " + key);
            return value;
        }

        private void Publish(RepositoryChangeKind kind, UserScriptInstallation installation)
        {
            var handlers = Changed;
            if (handlers == null) return;
            foreach (EventHandler<UserScriptRepositoryChangedEventArgs> handler in handlers.GetInvocationList())
            {
                try { handler(this, new UserScriptRepositoryChangedEventArgs(kind, installation)); }
                catch (Exception exception) { System.Diagnostics.Trace.TraceError(exception.ToString()); }
            }
        }

        private void EnsureInitialized()
        {
            if (!_initialized) throw new InvalidOperationException("Initialize the repository before use.");
        }
    }
}
