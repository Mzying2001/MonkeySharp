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
        Removed,

        /// <summary>A reload changed the loaded or prepared state.</summary>
        Reloaded
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

        /// <summary>Reads external source changes and publishes the resulting ready or unavailable installation.</summary>
        Task<UserScriptInstallation> ReloadAsync(ScriptKey scriptKey, CancellationToken cancellationToken);

        /// <summary>Refreshes all declared assets without rewriting source bytes.</summary>
        Task<UserScriptInstallation> RefreshAssetsAsync(ScriptKey scriptKey, Guid expectedRevisionId, CancellationToken cancellationToken);

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
