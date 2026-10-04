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
    /// <summary>
    /// Specifies the kind of change made to a userscript repository.
    /// </summary>
    public enum RepositoryChangeKind
    {
        /// <summary>A new script was installed.</summary>
        Installed,

        /// <summary>An installed script's source or origin was updated.</summary>
        Updated,

        /// <summary>An installed script was enabled.</summary>
        Enabled,

        /// <summary>An installed script was disabled.</summary>
        Disabled,

        /// <summary>An installed script was removed.</summary>
        Removed
    }

    /// <summary>
    /// Provides data for a userscript repository change.
    /// </summary>
    public sealed class UserScriptRepositoryChangedEventArgs : EventArgs
    {
        /// <summary>Initializes repository change event data.</summary>
        /// <param name="kind">The kind of repository change.</param>
        /// <param name="installation">The installation affected by the change.</param>
        public UserScriptRepositoryChangedEventArgs(RepositoryChangeKind kind, UserScriptInstallation installation)
        {
            Kind = kind;
            Installation = installation;
        }

        /// <summary>Gets the kind of repository change.</summary>
        public RepositoryChangeKind Kind { get; }

        /// <summary>Gets the installation affected by the change.</summary>
        public UserScriptInstallation Installation { get; }
    }

    /// <summary>
    /// Stores installed userscripts and manages their enabled state.
    /// </summary>
    public interface IUserScriptRepository
    {
        /// <summary>Occurs after an installation is added, updated, enabled, disabled, or removed.</summary>
        event EventHandler<UserScriptRepositoryChangedEventArgs> Changed;

        /// <summary>Parses and installs a userscript.</summary>
        /// <param name="source">The complete userscript source.</param>
        /// <param name="sourceOrigin">The origin from which the script was obtained.</param>
        /// <param name="enabled">Whether the new installation is enabled.</param>
        /// <param name="cancellationToken">A token that cancels the operation before it commits.</param>
        /// <returns>The newly created installation.</returns>
        Task<UserScriptInstallation> InstallAsync(string source, string sourceOrigin, bool enabled, CancellationToken cancellationToken);

        /// <summary>Replaces the source and origin of an installed userscript.</summary>
        /// <param name="scriptKey">The installation to update.</param>
        /// <param name="source">The replacement userscript source.</param>
        /// <param name="sourceOrigin">The replacement source origin.</param>
        /// <param name="cancellationToken">A token that cancels the operation before it commits.</param>
        /// <returns>The updated installation.</returns>
        Task<UserScriptInstallation> UpdateAsync(ScriptKey scriptKey, string source, string sourceOrigin, CancellationToken cancellationToken);

        /// <summary>Replaces a script only when its immutable revision identity still matches.</summary>
        Task<UserScriptInstallation> UpdateIfUnchangedAsync(
            ScriptKey scriptKey,
            Guid expectedRevisionId,
            string source,
            string sourceOrigin,
            CancellationToken cancellationToken);

        /// <summary>Changes whether an installed userscript is enabled.</summary>
        /// <param name="scriptKey">The installation to change.</param>
        /// <param name="enabled">The new enabled state.</param>
        /// <param name="cancellationToken">A token that cancels the operation before it commits.</param>
        /// <returns>The updated installation.</returns>
        Task<UserScriptInstallation> SetEnabledAsync(ScriptKey scriptKey, bool enabled, CancellationToken cancellationToken);

        /// <summary>Removes an installed userscript.</summary>
        /// <param name="scriptKey">The installation to remove.</param>
        /// <param name="cancellationToken">A token that cancels the operation before it commits.</param>
        /// <returns><see langword="true"/> when an installation was removed.</returns>
        Task<bool> RemoveAsync(ScriptKey scriptKey, CancellationToken cancellationToken);

        /// <summary>Gets an installed userscript by key.</summary>
        /// <param name="scriptKey">The installation key.</param>
        /// <param name="cancellationToken">A token that cancels the lookup.</param>
        /// <returns>The requested installation.</returns>
        Task<UserScriptInstallation> GetAsync(ScriptKey scriptKey, CancellationToken cancellationToken);

        /// <summary>Gets an immutable snapshot of installations in repository order.</summary>
        /// <param name="cancellationToken">A token that cancels the lookup.</param>
        /// <returns>The current installation snapshot.</returns>
        Task<IReadOnlyList<UserScriptInstallation>> GetSnapshotAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Provides a thread-safe, process-local userscript repository.
    /// </summary>
    public sealed class InMemoryUserScriptRepository : IUserScriptRepository
    {
        private readonly object _sync = new object();
        private readonly IUserScriptMetadataParser _parser;
        private readonly Dictionary<ScriptKey, UserScriptInstallation> _installations =
            new Dictionary<ScriptKey, UserScriptInstallation>();
        private readonly List<ScriptKey> _order = new List<ScriptKey>();

        /// <summary>Initializes an empty in-memory repository.</summary>
        /// <param name="parser">The metadata parser, or <see langword="null"/> to use the default parser.</param>
        public InMemoryUserScriptRepository(IUserScriptMetadataParser parser = null)
        {
            _parser = parser ?? new UserScriptMetadataParser();
        }

        /// <inheritdoc />
        public event EventHandler<UserScriptRepositoryChangedEventArgs> Changed;

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
        public Task<UserScriptInstallation> UpdateIfUnchangedAsync(
            ScriptKey scriptKey,
            Guid expectedRevisionId,
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
                var current = GetRequired(scriptKey);
                if (current.RevisionId != expectedRevisionId)
                    throw new RepositoryRevisionMismatchException(scriptKey);
                installation = current.WithDefinition(definition, sourceOrigin, DateTimeOffset.UtcNow);
                _installations[scriptKey] = installation;
            }
            Changed?.Invoke(this, new UserScriptRepositoryChangedEventArgs(RepositoryChangeKind.Updated, installation));
            return Task.FromResult(installation);
        }

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
        public Task<UserScriptInstallation> GetAsync(ScriptKey scriptKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
                return Task.FromResult(GetRequired(scriptKey));
        }

        /// <inheritdoc />
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

    /// <summary>Indicates that an installation changed after it was inspected.</summary>
    public sealed class RepositoryRevisionMismatchException : InvalidOperationException
    {
        /// <summary>Initializes a revision mismatch exception.</summary>
        public RepositoryRevisionMismatchException(ScriptKey scriptKey)
            : base("Script installation '" + scriptKey + "' changed after it was inspected.")
        {
            ScriptKey = scriptKey;
        }

        /// <summary>Gets the installation whose revision no longer matches.</summary>
        public ScriptKey ScriptKey { get; }
    }
}
