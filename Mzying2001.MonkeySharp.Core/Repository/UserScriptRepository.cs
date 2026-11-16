using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Parsing;
using Mzying2001.MonkeySharp.Core.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Repository
{
    /// <summary>Serializes preparation and durable mutations while exposing immutable runtime snapshots.</summary>
    public sealed class UserScriptRepository : IUserScriptRepository
    {
        private sealed class Entry
        {
            internal Entry(UserScriptInstallation installation, string version, IReadOnlyList<UserScriptAsset> assets, bool sourceAvailable)
            { Installation = installation; Version = version; Assets = assets; SourceAvailable = sourceAvailable; }
            internal UserScriptInstallation Installation { get; }
            internal string Version { get; }
            internal IReadOnlyList<UserScriptAsset> Assets { get; }
            internal bool SourceAvailable { get; }
        }

        private readonly object _sync = new object();
        private readonly SemaphoreSlim _mutations = new SemaphoreSlim(1, 1);
        private readonly IUserScriptPersistence _persistence;
        private readonly IUserScriptMetadataParser _parser;
        private readonly UserScriptPreparer _preparer;
        private Dictionary<ScriptKey, Entry> _entries = new Dictionary<ScriptKey, Entry>();
        private List<ScriptKey> _order = new List<ScriptKey>();
        private bool _initialized;

        /// <summary>Creates a repository; initialize it before use with any storage adapter.</summary>
        public UserScriptRepository(IUserScriptPersistence persistence, IUserScriptMetadataParser parser = null, UserScriptPreparer preparer = null)
        {
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _parser = parser ?? new UserScriptMetadataParser();
            _preparer = preparer ?? new UserScriptPreparer();
        }

        /// <inheritdoc />
        public event EventHandler<UserScriptRepositoryChangedEventArgs> Changed;

        /// <summary>Loads the stable installation directory and opens the repository only after all entries are processed.</summary>
        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (_sync) if (_initialized) throw new InvalidOperationException("The repository is already initialized.");
                var records = await _persistence.LoadAllAsync(cancellationToken).ConfigureAwait(false);
                var entries = new Dictionary<ScriptKey, Entry>();
                var order = new List<ScriptKey>();
                foreach (var loaded in records)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = await RestoreAsync(loaded, loaded.Record.IsEnabled ? PreparationMode.Reuse : PreparationMode.CachedOnly, false, cancellationToken).ConfigureAwait(false);
                    entries.Add(loaded.Record.ScriptKey, entry);
                    order.Add(loaded.Record.ScriptKey);
                }
                cancellationToken.ThrowIfCancellationRequested();
                lock (_sync) { _entries = entries; _order = order; _initialized = true; }
            }
            finally { _mutations.Release(); }
        }

        /// <inheritdoc />
        public Task<UserScriptInstallation> InstallAsync(string source, string sourceOrigin, bool enabled, CancellationToken cancellationToken) => MutateAsync(async () =>
        {
            var now = DateTimeOffset.UtcNow;
            var installation = new UserScriptInstallation(ScriptKey.Create(), Parse(source), sourceOrigin, enabled, now, now);
            return await PrepareAndSaveAsync(installation, null, Array.Empty<UserScriptAsset>(), PreparationMode.Reuse, SourceWriteMode.Replace, RepositoryChangeKind.Installed, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

        /// <inheritdoc />
        public Task<UserScriptInstallation> UpdateAsync(ScriptKey scriptKey, string source, string sourceOrigin, CancellationToken cancellationToken) =>
            UpdateAsync(scriptKey, null, source, sourceOrigin, cancellationToken);

        /// <inheritdoc />
        public Task<UserScriptInstallation> UpdateIfUnchangedAsync(ScriptKey scriptKey, Guid expectedRevisionId, string source, string sourceOrigin, CancellationToken cancellationToken) =>
            UpdateAsync(scriptKey, expectedRevisionId, source, sourceOrigin, cancellationToken);

        private Task<UserScriptInstallation> UpdateAsync(ScriptKey key, Guid? revision, string source, string origin, CancellationToken token) => MutateAsync(async () =>
        {
            var old = GetRequired(key);
            CheckRevision(old, revision);
            EnsureVersion(old);
            var installation = old.Installation.WithDefinition(Parse(source), origin, DateTimeOffset.UtcNow);
            return await PrepareAndSaveAsync(installation, old.Version, old.Assets, PreparationMode.Reuse, SourceWriteMode.Replace, RepositoryChangeKind.Updated, token).ConfigureAwait(false);
        }, token);

        /// <inheritdoc />
        public Task<UserScriptInstallation> RefreshAssetsAsync(ScriptKey scriptKey, Guid expectedRevisionId, CancellationToken cancellationToken) => MutateAsync(async () =>
        {
            var old = GetRequired(scriptKey);
            CheckRevision(old, expectedRevisionId);
            EnsureVersion(old);
            if (!old.SourceAvailable) throw new UserScriptPersistenceException(scriptKey, "The observed source is unavailable; reload or repair it first.", null);
            var installation = old.Installation.WithDefinition(old.Installation.Definition, old.Installation.SourceOrigin, DateTimeOffset.UtcNow);
            return await PrepareAndSaveAsync(installation, old.Version, old.Assets, PreparationMode.Refresh, SourceWriteMode.Preserve, RepositoryChangeKind.Updated, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

        /// <inheritdoc />
        public Task<UserScriptInstallation> ReloadAsync(ScriptKey scriptKey, CancellationToken cancellationToken) => MutateAsync(async () =>
        {
            var old = GetRequired(scriptKey);
            UserScriptLoadResult loaded = null;
            Entry next;
            try
            {
                loaded = await _persistence.LoadAsync(scriptKey, cancellationToken).ConfigureAwait(false);
                next = await RestoreAsync(loaded, PreparationMode.Reuse, false, cancellationToken).ConfigureAwait(false);
            }
            catch (UserScriptPersistenceException error)
            {
                next = Unavailable(old.Installation, null, old.Assets, false, new[] { Diagnostic(scriptKey, "MSR423_PERSISTENCE_FAILED", error) });
            }
            catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested)
            {
                next = loaded == null ? Unavailable(old.Installation, old.Version, old.Assets, old.SourceAvailable, new[] { Diagnostic(scriptKey, "MSR426_PREPARATION_CANCELLED", error) }) :
                    FromLoad(loaded, new[] { Diagnostic(scriptKey, "MSR426_PREPARATION_CANCELLED", error) });
                Replace(next, RepositoryChangeKind.Reloaded);
                throw;
            }
            if (Equivalent(old, next)) return old.Installation;
            Replace(next, RepositoryChangeKind.Reloaded);
            return next.Installation;
        }, cancellationToken);

        /// <inheritdoc />
        public Task<UserScriptInstallation> SetEnabledAsync(ScriptKey scriptKey, bool enabled, CancellationToken cancellationToken) => MutateAsync(async () =>
        {
            var old = GetRequired(scriptKey);
            var installation = old.Installation.WithEnabled(enabled, DateTimeOffset.UtcNow);
            await _persistence.SetEnabledAsync(scriptKey, enabled, installation.UpdatedAt, cancellationToken).ConfigureAwait(false);
            var next = new Entry(installation, old.Version, old.Assets, old.SourceAvailable);
            if (enabled && !installation.IsReady)
            {
                try
                {
                    var record = ToRecord(installation, old.SourceAvailable ? installation.Definition.Source : null, old.Assets);
                    next = await RestoreAsync(new UserScriptLoadResult(record, old.Version, false, installation.Diagnostics), PreparationMode.Reuse, true, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested)
                { next = Unavailable(installation, old.Version, old.Assets, old.SourceAvailable, new[] { Diagnostic(scriptKey, "MSR426_PREPARATION_CANCELLED", error) }); }
                catch
                {
                    Replace(next, RepositoryChangeKind.Enabled);
                    throw;
                }
            }
            Replace(next, enabled ? RepositoryChangeKind.Enabled : RepositoryChangeKind.Disabled);
            return next.Installation;
        }, cancellationToken);

        /// <inheritdoc />
        public Task<bool> RemoveAsync(ScriptKey scriptKey, CancellationToken cancellationToken) => MutateAsync(async () =>
        {
            Entry old;
            lock (_sync) if (!_entries.TryGetValue(scriptKey, out old)) return false;
            await _persistence.DeleteAsync(scriptKey, cancellationToken).ConfigureAwait(false);
            lock (_sync) { _entries.Remove(scriptKey); _order.Remove(scriptKey); }
            Publish(RepositoryChangeKind.Removed, old.Installation);
            return true;
        }, cancellationToken);

        /// <inheritdoc />
        public Task<UserScriptInstallation> GetAsync(ScriptKey scriptKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(GetRequired(scriptKey).Installation);
        }
        /// <inheritdoc />
        public Task<IReadOnlyList<UserScriptInstallation>> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                EnsureInitialized();
                return Task.FromResult<IReadOnlyList<UserScriptInstallation>>(Array.AsReadOnly(_order.Select(key => _entries[key].Installation).ToArray()));
            }
        }

        private async Task<T> MutateAsync<T>(Func<Task<T>> operation, CancellationToken token)
        {
            await _mutations.WaitAsync(token).ConfigureAwait(false);
            try
            {
                lock (_sync) EnsureInitialized();
                token.ThrowIfCancellationRequested();
                return await operation().ConfigureAwait(false);
            }
            finally { _mutations.Release(); }
        }

        private async Task<UserScriptInstallation> PrepareAndSaveAsync(UserScriptInstallation installation, string version,
            IReadOnlyList<UserScriptAsset> assets, PreparationMode mode, SourceWriteMode writeMode, RepositoryChangeKind kind, CancellationToken token)
        {
            var prepared = await _preparer.PrepareAsync(installation, assets, mode, token).ConfigureAwait(false);
            var committedVersion = await _persistence.SaveAsync(new UserScriptSaveRequest(ToRecord(installation, installation.Definition.Source, prepared.Assets), version, writeMode), token).ConfigureAwait(false);
            var next = new Entry(installation.WithPreparedRevision(prepared.Revision), committedVersion, prepared.Assets, true);
            Replace(next, kind);
            return next.Installation;
        }

        private async Task<Entry> RestoreAsync(UserScriptLoadResult loaded, PreparationMode mode, bool forceSave, CancellationToken token)
        {
            var entry = FromLoad(loaded, loaded.Diagnostics);
            var installation = entry.Installation;
            if (!entry.SourceAvailable || entry.Version == null || !installation.Definition.ParseResult.CanEnable) return entry;
            try
            {
                var prepared = await _preparer.PrepareAsync(installation, entry.Assets, mode, token).ConfigureAwait(false);
                var version = entry.Version;
                if (forceSave || loaded.SourceChangedSinceSave || prepared.Downloaded || !SameAssets(entry.Assets, prepared.Assets))
                {
                    installation = installation.WithDefinition(installation.Definition, installation.SourceOrigin, DateTimeOffset.UtcNow);
                    version = await _persistence.SaveAsync(new UserScriptSaveRequest(ToRecord(installation, installation.Definition.Source, prepared.Assets), version, SourceWriteMode.Preserve), token).ConfigureAwait(false);
                }
                else
                {
                    var verified = await _persistence.LoadAsync(installation.ScriptKey, token).ConfigureAwait(false);
                    if (verified.SourceVersion == null || verified.SourceVersion != version) throw new UserScriptSourceConflictException(installation.ScriptKey);
                    token.ThrowIfCancellationRequested();
                }
                return new Entry(new UserScriptInstallation(installation.ScriptKey, installation.Definition, installation.SourceOrigin, installation.IsEnabled,
                    installation.InstalledAt, installation.UpdatedAt, prepared.Revision, MetadataDiagnostics(installation)), version, prepared.Assets, true);
            }
            catch (Exception error) when (IsLocalFailure(error))
            { return Unavailable(installation, entry.Version, entry.Assets, entry.SourceAvailable, MetadataDiagnostics(installation).Concat(new[] { Diagnostic(installation.ScriptKey, Code(error), error) })); }
        }

        private Entry FromLoad(UserScriptLoadResult loaded, IEnumerable<UserScriptDiagnostic> diagnostics)
        {
            var record = loaded.Record;
            var definition = new UserScriptDefinition(record.Source ?? string.Empty, _parser.Parse(record.Source ?? string.Empty));
            var installation = new UserScriptInstallation(record.ScriptKey, definition, record.SourceOrigin, record.IsEnabled, record.InstalledAt, record.UpdatedAt);
            var all = diagnostics.Concat(MetadataDiagnostics(installation)).ToList();
            if (record.Source == null && !all.Any(item => item.Code == "MSR420_SOURCE_UNAVAILABLE"))
                all.Add(new UserScriptDiagnostic("MSR420_SOURCE_UNAVAILABLE", DiagnosticSeverity.Error, "Source is unavailable.", scriptKey: record.ScriptKey));
            if (loaded.SourceVersion == null && !all.Any(item => item.Severity == DiagnosticSeverity.Error))
                all.Add(new UserScriptDiagnostic("MSR423_PERSISTENCE_FAILED", DiagnosticSeverity.Error, "No reliable source version is available.", scriptKey: record.ScriptKey));
            return Unavailable(installation, loaded.SourceVersion, record.Assets, record.Source != null, all);
        }

        private static Entry Unavailable(UserScriptInstallation installation, string version, IReadOnlyList<UserScriptAsset> assets, bool sourceAvailable, IEnumerable<UserScriptDiagnostic> diagnostics) =>
            new Entry(new UserScriptInstallation(installation.ScriptKey, installation.Definition, installation.SourceOrigin, installation.IsEnabled,
                installation.InstalledAt, installation.UpdatedAt, diagnostics: diagnostics), version, assets, sourceAvailable);
        private static IEnumerable<UserScriptDiagnostic> MetadataDiagnostics(UserScriptInstallation installation) => installation.Definition.ParseResult.Diagnostics.Select(item =>
            new UserScriptDiagnostic(item.Code, item.Severity, item.Message, scriptKey: installation.ScriptKey));
        private static UserScriptPersistenceRecord ToRecord(UserScriptInstallation installation, string source, IEnumerable<UserScriptAsset> assets) =>
            new UserScriptPersistenceRecord(installation.ScriptKey, source, installation.SourceOrigin, installation.IsEnabled, installation.InstalledAt, installation.UpdatedAt, assets);
        private static UserScriptDiagnostic Diagnostic(ScriptKey key, string code, Exception error) => new UserScriptDiagnostic(code, DiagnosticSeverity.Error, error.Message, error, key);
        private static bool IsLocalFailure(Exception error) => error is PreparationException || error is ResourceIntegrityException || error is UserScriptSourceConflictException || error is UserScriptPersistenceException;
        private static string Code(Exception error) => error is PreparationException preparation ? preparation.Code : error is ResourceIntegrityException integrity ? integrity.Code :
            error is UserScriptSourceConflictException ? "MSR422_SOURCE_CHANGED" : "MSR423_PERSISTENCE_FAILED";

        private static bool SameAssets(IReadOnlyList<UserScriptAsset> left, IReadOnlyList<UserScriptAsset> right) => left.Count == right.Count &&
            left.OrderBy(asset => asset.Kind).ThenBy(asset => asset.Ordinal).Zip(right.OrderBy(asset => asset.Kind).ThenBy(asset => asset.Ordinal), (a, b) =>
                a.Kind == b.Kind && a.Ordinal == b.Ordinal && a.Name == b.Name && a.Url == b.Url && a.Content != null && b.Content != null &&
                a.Content.MediaType == b.Content.MediaType && a.Content.Charset == b.Content.Charset && a.Content.Bytes.SequenceEqual(b.Content.Bytes)).All(equal => equal);
        private static bool Equivalent(Entry left, Entry right)
        {
            var a = left.Installation; var b = right.Installation;
            return left.Version == right.Version && left.SourceAvailable == right.SourceAvailable && a.Definition.Source == b.Definition.Source &&
                a.SourceOrigin == b.SourceOrigin && a.IsEnabled == b.IsEnabled && a.IsReady == b.IsReady && a.InstalledAt == b.InstalledAt && a.UpdatedAt == b.UpdatedAt &&
                a.Diagnostics.Select(item => item.Code + ":" + item.Message).SequenceEqual(b.Diagnostics.Select(item => item.Code + ":" + item.Message)) &&
                SameAssetsIncludingMissing(left.Assets, right.Assets);
        }
        private static bool SameAssetsIncludingMissing(IReadOnlyList<UserScriptAsset> left, IReadOnlyList<UserScriptAsset> right) =>
            left.Count == right.Count && left.Zip(right, (a, b) => a.Content == null && b.Content == null ?
                a.Kind == b.Kind && a.Ordinal == b.Ordinal && a.Name == b.Name && a.Url == b.Url : SameAssets(new[] { a }, new[] { b })).All(equal => equal);
        private UserScriptDefinition Parse(string source)
        {
            var parsed = _parser.Parse(source);
            if (!parsed.CanEnable) throw new MetadataValidationException(parsed);
            return new UserScriptDefinition(source, parsed);
        }
        private static void CheckRevision(Entry entry, Guid? revision)
        { if (revision.HasValue && entry.Installation.RevisionId != revision.Value) throw new RepositoryRevisionMismatchException(entry.Installation.ScriptKey); }
        private static void EnsureVersion(Entry entry)
        { if (entry.Version == null) throw new UserScriptPersistenceException(entry.Installation.ScriptKey, "Reload a reliable source version before saving.", null); }
        private Entry GetRequired(ScriptKey key)
        {
            lock (_sync)
            {
                EnsureInitialized();
                return _entries.TryGetValue(key, out var entry) ? entry : throw new KeyNotFoundException("Script not found: " + key);
            }
        }
        private void Replace(Entry entry, RepositoryChangeKind kind)
        {
            lock (_sync)
            {
                if (!_entries.ContainsKey(entry.Installation.ScriptKey)) _order.Add(entry.Installation.ScriptKey);
                _entries[entry.Installation.ScriptKey] = entry;
            }
            Publish(kind, entry.Installation);
        }
        private void Publish(RepositoryChangeKind kind, UserScriptInstallation installation)
        {
            var handlers = Changed;
            if (handlers == null) return;
            foreach (EventHandler<UserScriptRepositoryChangedEventArgs> handler in handlers.GetInvocationList())
                try { handler(this, new UserScriptRepositoryChangedEventArgs(kind, installation)); }
                catch (Exception error) { System.Diagnostics.Trace.TraceError(error.ToString()); }
        }
        private void EnsureInitialized()
        { if (!_initialized) throw new InvalidOperationException("Initialize the repository before use."); }
    }
}
