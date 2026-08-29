using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Mzying2001.MonkeySharp.Core.Domain
{
    public readonly struct ScriptKey : IEquatable<ScriptKey>
    {
        private readonly Guid _value;

        private ScriptKey(Guid value)
        {
            if (value == Guid.Empty)
                throw new ArgumentException("A script key cannot be empty.", nameof(value));
            _value = value;
        }

        internal static ScriptKey Create()
        {
            return new ScriptKey(Guid.NewGuid());
        }

        public static ScriptKey Parse(string value)
        {
            return new ScriptKey(Guid.Parse(value));
        }

        public static bool TryParse(string value, out ScriptKey scriptKey)
        {
            if (Guid.TryParse(value, out var guid) && guid != Guid.Empty)
            {
                scriptKey = new ScriptKey(guid);
                return true;
            }

            scriptKey = default;
            return false;
        }

        public bool Equals(ScriptKey other) => _value.Equals(other._value);
        public override bool Equals(object obj) => obj is ScriptKey other && Equals(other);
        public override int GetHashCode() => _value.GetHashCode();
        public override string ToString() => _value.ToString("D");
        public static bool operator ==(ScriptKey left, ScriptKey right) => left.Equals(right);
        public static bool operator !=(ScriptKey left, ScriptKey right) => !left.Equals(right);
    }

    public enum UserScriptRunAt
    {
        DocumentStart,
        DocumentBody,
        DocumentEnd,
        DocumentIdle
    }

    public sealed class ResourceDeclaration
    {
        public ResourceDeclaration(string name, string url)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Url = url ?? throw new ArgumentNullException(nameof(url));
        }

        public string Name { get; }
        public string Url { get; }
    }

    public sealed class UserScriptMetadata
    {
        internal UserScriptMetadata(
            string name,
            string scriptNamespace,
            string version,
            string description,
            string author,
            string license,
            string iconUrl,
            string downloadUrl,
            string updateUrl,
            string homepageUrl,
            string supportUrl,
            UserScriptRunAt runAt,
            bool noFrames,
            string runIn,
            string injectInto,
            IDictionary<string, string> localizedNames,
            IDictionary<string, string> localizedDescriptions,
            IEnumerable<string> matches,
            IEnumerable<string> includes,
            IEnumerable<string> excludes,
            IEnumerable<string> excludeMatches,
            IEnumerable<string> grants,
            IEnumerable<string> connects,
            IEnumerable<string> requires,
            IEnumerable<ResourceDeclaration> resources,
            IDictionary<string, IReadOnlyList<string>> additionalEntries)
        {
            Name = name;
            Namespace = scriptNamespace;
            Version = version;
            Description = description;
            Author = author;
            License = license;
            IconUrl = iconUrl;
            DownloadUrl = downloadUrl;
            UpdateUrl = updateUrl;
            HomepageUrl = homepageUrl;
            SupportUrl = supportUrl;
            RunAt = runAt;
            NoFrames = noFrames;
            RunIn = runIn;
            InjectInto = injectInto;
            LocalizedNames = ReadOnlyDictionary(localizedNames);
            LocalizedDescriptions = ReadOnlyDictionary(localizedDescriptions);
            Matches = ReadOnlyList(matches);
            Includes = ReadOnlyList(includes);
            Excludes = ReadOnlyList(excludes);
            ExcludeMatches = ReadOnlyList(excludeMatches);
            Grants = ReadOnlyList(grants);
            Connects = ReadOnlyList(connects);
            Requires = ReadOnlyList(requires);
            Resources = new ReadOnlyCollection<ResourceDeclaration>(resources.ToList());
            AdditionalEntries = new ReadOnlyDictionary<string, IReadOnlyList<string>>(
                new Dictionary<string, IReadOnlyList<string>>(additionalEntries, StringComparer.OrdinalIgnoreCase));
        }

        public string Name { get; }
        public string Namespace { get; }
        public string Version { get; }
        public string Description { get; }
        public string Author { get; }
        public string License { get; }
        public string IconUrl { get; }
        public string DownloadUrl { get; }
        public string UpdateUrl { get; }
        public string HomepageUrl { get; }
        public string SupportUrl { get; }
        public UserScriptRunAt RunAt { get; }
        public bool NoFrames { get; }
        public string RunIn { get; }
        public string InjectInto { get; }
        public IReadOnlyDictionary<string, string> LocalizedNames { get; }
        public IReadOnlyDictionary<string, string> LocalizedDescriptions { get; }
        public IReadOnlyList<string> Matches { get; }
        public IReadOnlyList<string> Includes { get; }
        public IReadOnlyList<string> Excludes { get; }
        public IReadOnlyList<string> ExcludeMatches { get; }
        public IReadOnlyList<string> Grants { get; }
        public IReadOnlyList<string> Connects { get; }
        public IReadOnlyList<string> Requires { get; }
        public IReadOnlyList<ResourceDeclaration> Resources { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<string>> AdditionalEntries { get; }

        private static IReadOnlyList<string> ReadOnlyList(IEnumerable<string> values)
        {
            return new ReadOnlyCollection<string>(values.ToList());
        }

        private static IReadOnlyDictionary<string, string> ReadOnlyDictionary(IDictionary<string, string> values)
        {
            return new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase));
        }
    }

    public sealed class MetadataParseResult
    {
        internal MetadataParseResult(UserScriptMetadata metadata, IEnumerable<MetadataDiagnostic> diagnostics)
        {
            Metadata = metadata;
            Diagnostics = new ReadOnlyCollection<MetadataDiagnostic>(diagnostics.ToList());
        }

        public UserScriptMetadata Metadata { get; }
        public IReadOnlyList<MetadataDiagnostic> Diagnostics { get; }
        public bool CanEnable => Metadata != null && !Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error);
    }

    public sealed class UserScriptDefinition
    {
        public UserScriptDefinition(string source, MetadataParseResult parseResult)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            ParseResult = parseResult ?? throw new ArgumentNullException(nameof(parseResult));
        }

        public string Source { get; }
        public MetadataParseResult ParseResult { get; }
        public UserScriptMetadata Metadata => ParseResult.Metadata;
    }

    public sealed class UserScriptInstallation
    {
        internal UserScriptInstallation(
            ScriptKey scriptKey,
            UserScriptDefinition definition,
            string sourceOrigin,
            bool isEnabled,
            DateTimeOffset installedAt,
            DateTimeOffset updatedAt)
        {
            ScriptKey = scriptKey;
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            SourceOrigin = sourceOrigin;
            IsEnabled = isEnabled;
            InstalledAt = installedAt;
            UpdatedAt = updatedAt;
        }

        public ScriptKey ScriptKey { get; }
        public UserScriptDefinition Definition { get; }
        public string SourceOrigin { get; }
        public bool IsEnabled { get; }
        public DateTimeOffset InstalledAt { get; }
        public DateTimeOffset UpdatedAt { get; }

        internal UserScriptInstallation WithDefinition(UserScriptDefinition definition, string sourceOrigin, DateTimeOffset updatedAt)
        {
            return new UserScriptInstallation(ScriptKey, definition, sourceOrigin, IsEnabled, InstalledAt, updatedAt);
        }

        internal UserScriptInstallation WithEnabled(bool enabled, DateTimeOffset updatedAt)
        {
            return new UserScriptInstallation(ScriptKey, Definition, SourceOrigin, enabled, InstalledAt, updatedAt);
        }
    }
}
