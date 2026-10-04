using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;

namespace Mzying2001.MonkeySharp.Core.Domain
{
    /// <summary>
    /// Identifies a single installed userscript independently of its metadata.
    /// </summary>
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

        /// <summary>Parses a script key from its GUID string representation.</summary>
        /// <param name="value">The GUID string to parse.</param>
        /// <returns>The parsed script key.</returns>
        public static ScriptKey Parse(string value)
        {
            return new ScriptKey(Guid.Parse(value));
        }

        /// <summary>Attempts to parse a non-empty script key from a GUID string.</summary>
        /// <param name="value">The GUID string to parse.</param>
        /// <param name="scriptKey">Receives the parsed key when parsing succeeds.</param>
        /// <returns><see langword="true"/> when the value is a valid non-empty GUID; otherwise, <see langword="false"/>.</returns>
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

        /// <inheritdoc />
        public bool Equals(ScriptKey other) => _value.Equals(other._value);

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is ScriptKey other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => _value.GetHashCode();

        /// <summary>Returns the key as a canonical GUID string.</summary>
        /// <returns>The key formatted with hyphens.</returns>
        public override string ToString() => _value.ToString("D");

        /// <summary>Determines whether two script keys are equal.</summary>
        /// <param name="left">The first key to compare.</param>
        /// <param name="right">The second key to compare.</param>
        /// <returns><see langword="true"/> when the keys are equal.</returns>
        public static bool operator ==(ScriptKey left, ScriptKey right) => left.Equals(right);

        /// <summary>Determines whether two script keys are different.</summary>
        /// <param name="left">The first key to compare.</param>
        /// <param name="right">The second key to compare.</param>
        /// <returns><see langword="true"/> when the keys are different.</returns>
        public static bool operator !=(ScriptKey left, ScriptKey right) => !left.Equals(right);
    }

    /// <summary>
    /// Specifies the document lifecycle phase at which a userscript runs.
    /// </summary>
    public enum UserScriptRunAt
    {
        /// <summary>Runs at the earliest available document-start phase.</summary>
        DocumentStart,

        /// <summary>Runs after the document body becomes available.</summary>
        DocumentBody,

        /// <summary>Runs after DOM content has loaded.</summary>
        DocumentEnd,

        /// <summary>Runs after the document load phase when the host is idle.</summary>
        DocumentIdle
    }

    /// <summary>Describes how a script declared its GM grants.</summary>
    public enum GrantDeclarationState
    {
        /// <summary>No <c>@grant</c> entry was declared.</summary>
        Missing,

        /// <summary>The script explicitly declared <c>@grant none</c>.</summary>
        ExplicitNone,

        /// <summary>The script declared one or more explicit capabilities.</summary>
        ExplicitList
    }

    /// <summary>
    /// Describes a named external resource declared by a userscript.
    /// </summary>
    public sealed class ResourceDeclaration
    {
        /// <summary>Initializes a resource declaration.</summary>
        /// <param name="name">The name used to reference the resource.</param>
        /// <param name="url">The resource URL from the metadata block.</param>
        /// <param name="integrity">The optional subresource integrity declarations.</param>
        internal ResourceDeclaration(string name, string url, IEnumerable<ResourceIntegrityDeclaration> integrity)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Url = url ?? throw new ArgumentNullException(nameof(url));
            Integrity = new ReadOnlyCollection<ResourceIntegrityDeclaration>((integrity ?? Enumerable.Empty<ResourceIntegrityDeclaration>()).ToList());
        }

        /// <summary>Gets the resource name.</summary>
        public string Name { get; }

        /// <summary>Gets the resource URL.</summary>
        public string Url { get; }

        /// <summary>Gets the optional subresource integrity declarations.</summary>
        public IReadOnlyList<ResourceIntegrityDeclaration> Integrity { get; }
    }

    /// <summary>
    /// Describes one hash declaration attached to an external userscript resource.
    /// </summary>
    public sealed class ResourceIntegrityDeclaration
    {
        internal ResourceIntegrityDeclaration(string algorithm, string digest)
        {
            Algorithm = algorithm ?? throw new ArgumentNullException(nameof(algorithm));
            Digest = digest ?? throw new ArgumentNullException(nameof(digest));
        }

        /// <summary>Gets the normalized hash algorithm name.</summary>
        public string Algorithm { get; }

        /// <summary>Gets the hexadecimal or Base64 digest text.</summary>
        public string Digest { get; }

        /// <summary>Gets whether the runtime can verify this algorithm.</summary>
        public bool IsSupported => string.Equals(Algorithm, "md5", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Algorithm, "sha256", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Describes one JavaScript dependency declared with <c>@require</c>.
    /// </summary>
    public sealed class UserScriptDependencyDeclaration
    {
        internal UserScriptDependencyDeclaration(string url, IEnumerable<ResourceIntegrityDeclaration> integrity)
        {
            Url = url ?? throw new ArgumentNullException(nameof(url));
            Integrity = new ReadOnlyCollection<ResourceIntegrityDeclaration>((integrity ?? Enumerable.Empty<ResourceIntegrityDeclaration>()).ToList());
        }

        /// <summary>Gets the dependency URL without its integrity fragment.</summary>
        public string Url { get; }

        /// <summary>Gets the optional subresource integrity declarations.</summary>
        public IReadOnlyList<ResourceIntegrityDeclaration> Integrity { get; }
    }

    /// <summary>Describes one localized antifeature declaration.</summary>
    public sealed class AntifeatureDeclaration
    {
        internal AntifeatureDeclaration(string type, string description, string locale)
        {
            Type = type;
            Description = description;
            Locale = locale;
        }

        /// <summary>Gets the antifeature type.</summary>
        public string Type { get; }

        /// <summary>Gets the disclosure description.</summary>
        public string Description { get; }

        /// <summary>Gets the normalized locale, or <see langword="null"/> for the default locale.</summary>
        public string Locale { get; }
    }

    /// <summary>Contains one validated static <c>@webRequest</c> rule.</summary>
    public sealed class UserScriptWebRequestRule
    {
        internal UserScriptWebRequestRule(JsonElement selector, JsonElement action)
        {
            Selector = selector.Clone();
            Action = action.Clone();
        }

        /// <summary>Gets the selector JSON value, either a string or selector object.</summary>
        public JsonElement Selector { get; }

        /// <summary>Gets the action JSON value.</summary>
        public JsonElement Action { get; }
    }

    /// <summary>
    /// Contains normalized metadata parsed from a userscript header.
    /// </summary>
    public sealed class UserScriptMetadata
    {
        internal UserScriptMetadata(
            string name,
            string scriptNamespace,
            string version,
            string description,
            string author,
            string license,
            string copyright,
            string iconUrl,
            string icon64Url,
            string downloadUrl,
            string updateUrl,
            string homepageUrl,
            string websiteUrl,
            string sourceUrl,
            string supportUrl,
            UserScriptRunAt runAt,
            GrantDeclarationState grantDeclarationState,
            bool noFrames,
            string runIn,
            string injectInto,
            string sandbox,
            bool unwrap,
            IDictionary<string, string> localizedNames,
            IDictionary<string, string> localizedDescriptions,
            IEnumerable<string> matches,
            IEnumerable<string> includes,
            IEnumerable<string> excludes,
            IEnumerable<string> excludeMatches,
            IEnumerable<string> declaredGrants,
            IEnumerable<string> grants,
            IEnumerable<string> connects,
            IEnumerable<UserScriptDependencyDeclaration> requires,
            IEnumerable<ResourceDeclaration> resources,
            IEnumerable<AntifeatureDeclaration> antifeatures,
            IEnumerable<UserScriptWebRequestRule> webRequest,
            IDictionary<string, IReadOnlyList<string>> additionalEntries)
        {
            Name = name;
            Namespace = scriptNamespace;
            Version = version;
            Description = description;
            Author = author;
            License = license;
            Copyright = copyright;
            IconUrl = iconUrl;
            Icon64Url = icon64Url;
            DownloadUrl = downloadUrl;
            UpdateUrl = updateUrl;
            HomepageUrl = homepageUrl;
            WebsiteUrl = websiteUrl;
            SourceUrl = sourceUrl;
            SupportUrl = supportUrl;
            RunAt = runAt;
            GrantDeclarationState = grantDeclarationState;
            NoFrames = noFrames;
            RunIn = runIn;
            InjectInto = injectInto;
            Sandbox = sandbox;
            Unwrap = unwrap;
            LocalizedNames = ReadOnlyDictionary(localizedNames);
            LocalizedDescriptions = ReadOnlyDictionary(localizedDescriptions);
            Matches = ReadOnlyList(matches);
            Includes = ReadOnlyList(includes);
            Excludes = ReadOnlyList(excludes);
            ExcludeMatches = ReadOnlyList(excludeMatches);
            DeclaredGrants = ReadOnlyList(declaredGrants);
            Grants = ReadOnlyList(grants);
            Connects = ReadOnlyList(connects);
            Requires = new ReadOnlyCollection<UserScriptDependencyDeclaration>(requires.ToList());
            Resources = new ReadOnlyCollection<ResourceDeclaration>(resources.ToList());
            Antifeatures = new ReadOnlyCollection<AntifeatureDeclaration>(antifeatures.ToList());
            WebRequest = new ReadOnlyCollection<UserScriptWebRequestRule>(webRequest.ToList());
            AdditionalEntries = new ReadOnlyDictionary<string, IReadOnlyList<string>>(
                new Dictionary<string, IReadOnlyList<string>>(additionalEntries, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>Gets the default display name.</summary>
        public string Name { get; }

        /// <summary>Gets the namespace used with the name to identify the script authoring context.</summary>
        public string Namespace { get; }

        /// <summary>Gets the script version string.</summary>
        public string Version { get; }

        /// <summary>Gets the default script description.</summary>
        public string Description { get; }

        /// <summary>Gets the script author.</summary>
        public string Author { get; }

        /// <summary>Gets the script license declaration.</summary>
        public string License { get; }

        /// <summary>Gets the copyright declaration.</summary>
        public string Copyright { get; }

        /// <summary>Gets the icon URL.</summary>
        public string IconUrl { get; }

        /// <summary>Gets the 64px icon URL.</summary>
        public string Icon64Url { get; }

        /// <summary>Gets the script download URL.</summary>
        public string DownloadUrl { get; }

        /// <summary>Gets the update metadata URL.</summary>
        public string UpdateUrl { get; }

        /// <summary>Gets the script homepage URL.</summary>
        public string HomepageUrl { get; }

        /// <summary>Gets the optional website alias.</summary>
        public string WebsiteUrl { get; }

        /// <summary>Gets the optional source URL alias.</summary>
        public string SourceUrl { get; }

        /// <summary>Gets the support URL.</summary>
        public string SupportUrl { get; }

        /// <summary>Gets the requested document lifecycle phase.</summary>
        public UserScriptRunAt RunAt { get; }

        /// <summary>Gets whether grants were missing, explicit none, or an explicit list.</summary>
        public GrantDeclarationState GrantDeclarationState { get; }

        /// <summary>Gets whether the script is restricted to the main frame.</summary>
        public bool NoFrames { get; }

        /// <summary>Gets the declared execution environment.</summary>
        public string RunIn { get; }

        /// <summary>Gets the declared JavaScript world into which the script should be injected.</summary>
        public string InjectInto { get; }

        /// <summary>Gets the declared Tampermonkey sandbox mode.</summary>
        public string Sandbox { get; }

        /// <summary>Gets whether the script requested wrapper removal.</summary>
        public bool Unwrap { get; }

        /// <summary>Gets localized names keyed by normalized culture name.</summary>
        public IReadOnlyDictionary<string, string> LocalizedNames { get; }

        /// <summary>Gets localized descriptions keyed by normalized culture name.</summary>
        public IReadOnlyDictionary<string, string> LocalizedDescriptions { get; }

        /// <summary>Gets WebExtension-style match patterns.</summary>
        public IReadOnlyList<string> Matches { get; }

        /// <summary>Gets legacy include glob patterns.</summary>
        public IReadOnlyList<string> Includes { get; }

        /// <summary>Gets legacy exclusion glob patterns.</summary>
        public IReadOnlyList<string> Excludes { get; }

        /// <summary>Gets WebExtension-style exclusion match patterns.</summary>
        public IReadOnlyList<string> ExcludeMatches { get; }

        /// <summary>Gets the GM API grants declared by the script.</summary>
        public IReadOnlyList<string> DeclaredGrants { get; }

        /// <summary>Gets the normalized canonical capabilities granted to the script.</summary>
        public IReadOnlyList<string> Grants { get; }

        /// <summary>Gets the network targets declared with <c>@connect</c>.</summary>
        public IReadOnlyList<string> Connects { get; }

        /// <summary>Gets dependency URLs in declaration order.</summary>
        public IReadOnlyList<UserScriptDependencyDeclaration> Requires { get; }

        /// <summary>Gets named external resource declarations.</summary>
        public IReadOnlyList<ResourceDeclaration> Resources { get; }

        /// <summary>Gets structured antifeature disclosures.</summary>
        public IReadOnlyList<AntifeatureDeclaration> Antifeatures { get; }

        /// <summary>Gets parsed static webRequest rules.</summary>
        public IReadOnlyList<UserScriptWebRequestRule> WebRequest { get; }

        /// <summary>Gets unrecognized metadata entries, preserving their values in declaration order.</summary>
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

    /// <summary>
    /// Contains parsed userscript metadata together with validation diagnostics.
    /// </summary>
    public sealed class MetadataParseResult
    {
        internal MetadataParseResult(UserScriptMetadata metadata, IEnumerable<MetadataDiagnostic> diagnostics)
        {
            Metadata = metadata;
            Diagnostics = new ReadOnlyCollection<MetadataDiagnostic>(diagnostics.ToList());
        }

        /// <summary>Gets the parsed metadata, or <see langword="null"/> when no valid metadata block was found.</summary>
        public UserScriptMetadata Metadata { get; }

        /// <summary>Gets parsing and validation diagnostics.</summary>
        public IReadOnlyList<MetadataDiagnostic> Diagnostics { get; }

        /// <summary>Gets whether the parsed script can be enabled.</summary>
        public bool CanEnable => Metadata != null && !Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>
    /// Combines userscript source text with its parsed metadata.
    /// </summary>
    public sealed class UserScriptDefinition
    {
        /// <summary>Initializes a userscript definition.</summary>
        /// <param name="source">The complete userscript source text.</param>
        /// <param name="parseResult">The result of parsing the source metadata.</param>
        public UserScriptDefinition(string source, MetadataParseResult parseResult)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            ParseResult = parseResult ?? throw new ArgumentNullException(nameof(parseResult));
        }

        /// <summary>Gets the complete userscript source text.</summary>
        public string Source { get; }

        /// <summary>Gets the metadata parse result.</summary>
        public MetadataParseResult ParseResult { get; }

        /// <summary>Gets the parsed metadata, if available.</summary>
        public UserScriptMetadata Metadata => ParseResult.Metadata;
    }

    /// <summary>
    /// Represents an installed userscript and its repository state.
    /// </summary>
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
            RevisionId = Guid.NewGuid();
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            SourceOrigin = sourceOrigin;
            IsEnabled = isEnabled;
            InstalledAt = installedAt;
            UpdatedAt = updatedAt;
        }

        /// <summary>Gets the stable installation key.</summary>
        public ScriptKey ScriptKey { get; }

        /// <summary>Gets the immutable revision identity for optimistic concurrency.</summary>
        public Guid RevisionId { get; }

        /// <summary>Gets the installed script definition.</summary>
        public UserScriptDefinition Definition { get; }

        /// <summary>Gets the origin from which the script was installed, if known.</summary>
        public string SourceOrigin { get; }

        /// <summary>Gets whether the installation is enabled.</summary>
        public bool IsEnabled { get; }

        /// <summary>Gets when the script was installed.</summary>
        public DateTimeOffset InstalledAt { get; }

        /// <summary>Gets when the installation was last updated.</summary>
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
