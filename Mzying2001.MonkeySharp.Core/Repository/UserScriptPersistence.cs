using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Repository
{
    /// <summary>Stores complete installations in stable order, atomically and with source version checks.</summary>
    public interface IUserScriptPersistence
    {
        /// <summary>Loads known installations, retaining local failures as diagnostics.</summary>
        Task<IReadOnlyList<UserScriptLoadResult>> LoadAllAsync(CancellationToken cancellationToken);
        /// <summary>Loads one installation; an unknown key throws KeyNotFoundException.</summary>
        Task<UserScriptLoadResult> LoadAsync(ScriptKey scriptKey, CancellationToken cancellationToken);
        /// <summary>Commits complete source and assets and returns the committed source version.</summary>
        Task<string> SaveAsync(UserScriptSaveRequest request, CancellationToken cancellationToken);
        /// <summary>Changes only enabled intent and its timestamp.</summary>
        Task SetEnabledAsync(ScriptKey scriptKey, bool enabled, DateTimeOffset updatedAt, CancellationToken cancellationToken);
        /// <summary>Deletes the installation and its associated host state; unknown keys are ignored.</summary>
        Task DeleteAsync(ScriptKey scriptKey, CancellationToken cancellationToken);
    }

    /// <summary>Identifies an asset declaration family.</summary>
    public enum UserScriptAssetKind
    {
        /// <summary>An ordered JavaScript dependency.</summary>
        Require,
        /// <summary>A named resource.</summary>
        Resource
    }

    /// <summary>Determines whether saving may replace source bytes.</summary>
    public enum SourceWriteMode
    {
        /// <summary>Conditionally replaces the source.</summary>
        Replace,
        /// <summary>Checks but never rewrites the existing source.</summary>
        Preserve
    }

    /// <summary>Describes one persisted asset; null content denotes an unavailable cache entry.</summary>
    public sealed class UserScriptAsset
    {
        /// <summary>Creates an asset descriptor using the parser's URL without integrity fragments.</summary>
        public UserScriptAsset(UserScriptAssetKind kind, int ordinal, string name, string url, ResourceContent content)
        {
            if (kind != UserScriptAssetKind.Require && kind != UserScriptAssetKind.Resource) throw new ArgumentOutOfRangeException(nameof(kind));
            if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
            if (kind == UserScriptAssetKind.Require ? name != null : string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Require names must be null; resources require a name.", nameof(name));
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("An asset URL is required.", nameof(url));
            Kind = kind; Ordinal = ordinal; Name = name; Url = url; Content = content;
        }
        /// <summary>Gets the declaration family.</summary>
        public UserScriptAssetKind Kind { get; }
        /// <summary>Gets the zero-based position within that family.</summary>
        public int Ordinal { get; }
        /// <summary>Gets the resource name, or null for dependencies.</summary>
        public string Name { get; }
        /// <summary>Gets the declaration URL.</summary>
        public string Url { get; }
        /// <summary>Gets immutable content, or null when unavailable during loading.</summary>
        public ResourceContent Content { get; }
    }

    /// <summary>Exchanges source and asset content without prescribing a durable representation.</summary>
    public sealed class UserScriptPersistenceRecord
    {
        /// <summary>Creates an immutable record; source may be null when loading failed.</summary>
        public UserScriptPersistenceRecord(ScriptKey scriptKey, string source, string sourceOrigin,
            bool isEnabled, DateTimeOffset installedAt, DateTimeOffset updatedAt, IEnumerable<UserScriptAsset> assets)
        {
            if (!ScriptKey.TryParse(scriptKey.ToString(), out _)) throw new ArgumentException("A nonempty script key is required.", nameof(scriptKey));
            ScriptKey = scriptKey; Source = source; SourceOrigin = sourceOrigin; IsEnabled = isEnabled;
            InstalledAt = installedAt; UpdatedAt = updatedAt;
            var copy = (assets ?? throw new ArgumentNullException(nameof(assets))).ToArray();
            if (copy.Any(asset => asset == null)) throw new ArgumentException("Assets cannot contain null entries.", nameof(assets));
            foreach (var group in copy.GroupBy(asset => asset.Kind))
                if (!group.Select(asset => asset.Ordinal).OrderBy(value => value).SequenceEqual(Enumerable.Range(0, group.Count())))
                    throw new ArgumentException("Asset ordinals must be continuous and unique within each kind.", nameof(assets));
            if (copy.Where(asset => asset.Kind == UserScriptAssetKind.Resource).Select(asset => asset.Name).Distinct(StringComparer.Ordinal).Count() != copy.Count(asset => asset.Kind == UserScriptAssetKind.Resource))
                throw new ArgumentException("Resource names must be unique.", nameof(assets));
            Assets = Array.AsReadOnly(copy);
        }
        /// <summary>Gets the stable installation identity.</summary>
        public ScriptKey ScriptKey { get; }
        /// <summary>Gets source text, or null if unavailable.</summary>
        public string Source { get; }
        /// <summary>Gets the installation origin.</summary>
        public string SourceOrigin { get; }
        /// <summary>Gets persisted enabled intent.</summary>
        public bool IsEnabled { get; }
        /// <summary>Gets the original installation time.</summary>
        public DateTimeOffset InstalledAt { get; }
        /// <summary>Gets the last committed update time.</summary>
        public DateTimeOffset UpdatedAt { get; }
        /// <summary>Gets the last successfully saved asset associations.</summary>
        public IReadOnlyList<UserScriptAsset> Assets { get; }
    }

    /// <summary>Contains one actual source read and local persistence diagnostics.</summary>
    public sealed class UserScriptLoadResult
    {
        /// <summary>Creates a load result; null version means a reliable conditional save is impossible.</summary>
        public UserScriptLoadResult(UserScriptPersistenceRecord record, string sourceVersion,
            bool sourceChangedSinceSave, IEnumerable<UserScriptDiagnostic> diagnostics)
        {
            Record = record ?? throw new ArgumentNullException(nameof(record));
            if (sourceVersion != null && sourceVersion.Length == 0) throw new ArgumentException("Source version cannot be empty.", nameof(sourceVersion));
            SourceVersion = sourceVersion; SourceChangedSinceSave = sourceChangedSinceSave;
            var copy = (diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))).ToArray();
            if (copy.Any(item => item == null || item.ScriptKey != record.ScriptKey || item.DocumentId != null || item.FrameId != null))
                throw new ArgumentException("Persistence diagnostics must belong to this installation without page or frame context.", nameof(diagnostics));
            Diagnostics = Array.AsReadOnly(copy);
        }
        /// <summary>Gets the loaded installation record.</summary>
        public UserScriptPersistenceRecord Record { get; }
        /// <summary>Gets the opaque version of the actual source read.</summary>
        public string SourceVersion { get; }
        /// <summary>Gets whether file bytes differ from the last successful save.</summary>
        public bool SourceChangedSinceSave { get; }
        /// <summary>Gets local loading diagnostics.</summary>
        public IReadOnlyList<UserScriptDiagnostic> Diagnostics { get; }
    }

    /// <summary>Requests an atomic, conditional save of a complete installation.</summary>
    public sealed class UserScriptSaveRequest
    {
        /// <summary>Creates a save request. Only insertion uses a null expected version.</summary>
        public UserScriptSaveRequest(UserScriptPersistenceRecord record, string expectedSourceVersion, SourceWriteMode sourceWriteMode)
        {
            Record = record ?? throw new ArgumentNullException(nameof(record));
            if (record.Source == null || record.Assets.Any(asset => asset.Content == null)) throw new ArgumentException("Saving requires source and complete content.", nameof(record));
            if (expectedSourceVersion != null && expectedSourceVersion.Length == 0) throw new ArgumentException("Source version cannot be empty.", nameof(expectedSourceVersion));
            if (sourceWriteMode != SourceWriteMode.Replace && sourceWriteMode != SourceWriteMode.Preserve) throw new ArgumentOutOfRangeException(nameof(sourceWriteMode));
            if (sourceWriteMode == SourceWriteMode.Preserve && expectedSourceVersion == null) throw new ArgumentException("Preserve requires an existing source version.", nameof(expectedSourceVersion));
            ExpectedSourceVersion = expectedSourceVersion; SourceWriteMode = sourceWriteMode;
        }
        /// <summary>Gets the complete installation.</summary>
        public UserScriptPersistenceRecord Record { get; }
        /// <summary>Gets the previously observed opaque source version.</summary>
        public string ExpectedSourceVersion { get; }
        /// <summary>Gets the source writing policy.</summary>
        public SourceWriteMode SourceWriteMode { get; }
    }

    /// <summary>Reports an external source change since the repository observed it.</summary>
    public sealed class UserScriptSourceConflictException : InvalidOperationException
    {
        /// <summary>Creates a source conflict.</summary>
        public UserScriptSourceConflictException(ScriptKey scriptKey) : base("Source changed for script '" + scriptKey + "'; reload before saving.") { ScriptKey = scriptKey; }
        /// <summary>Gets the affected installation.</summary>
        public ScriptKey ScriptKey { get; }
    }

    /// <summary>Reports a local installation read, recovery or commit failure.</summary>
    public sealed class UserScriptPersistenceException : Exception
    {
        /// <summary>Creates a persistence failure retaining its original cause.</summary>
        public UserScriptPersistenceException(ScriptKey scriptKey, string message, Exception innerException) : base(message, innerException) { ScriptKey = scriptKey; }
        /// <summary>Gets the affected installation.</summary>
        public ScriptKey ScriptKey { get; }
    }
}
