using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Compatibility;
using Mzying2001.MonkeySharp.Core.Matching;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Mzying2001.MonkeySharp.Core.Parsing
{
    /// <summary>
    /// Parses and validates metadata from userscript source text.
    /// </summary>
    public interface IUserScriptMetadataParser
    {
        /// <summary>Parses the userscript metadata block at the beginning of a source file.</summary>
        /// <param name="source">The complete userscript source text.</param>
        /// <returns>The normalized metadata and any parsing or validation diagnostics.</returns>
        MetadataParseResult Parse(string source);
    }

    /// <summary>
    /// Parses MonkeySharp-supported userscript metadata and preserves unknown entries.
    /// </summary>
    public sealed class UserScriptMetadataParser : IUserScriptMetadataParser
    {
        private readonly UserScriptMetadataParserOptions _options;

        private static readonly HashSet<string> CollectionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "match", "include", "exclude", "exclude-match", "grant", "connect", "require", "resource",
            "antifeature", "webrequest"
        };

        private static readonly HashSet<string> SingletonKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "name", "namespace", "version", "description", "author", "license", "copyright", "icon", "iconurl",
            "icon64", "icon64url", "downloadurl", "updateurl", "homepage", "homepageurl", "website", "source",
            "supporturl", "noframes", "run-at", "run-in", "inject-into", "sandbox", "unwrap"
        };

        /// <summary>Initializes a parser with the default legacy-compatible rules.</summary>
        public UserScriptMetadataParser()
            : this(null)
        {
        }

        /// <summary>Initializes a parser with explicit compatibility rules.</summary>
        /// <param name="options">The parser options, or <see langword="null"/> for defaults.</param>
        public UserScriptMetadataParser(UserScriptMetadataParserOptions options)
        {
            _options = options ?? new UserScriptMetadataParserOptions();
            _options.Validate();
        }

        /// <inheritdoc />
        public MetadataParseResult Parse(string source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (source.Length > 0 && source[0] == '\uFEFF')
                source = source.Substring(1);

            var diagnostics = new List<MetadataDiagnostic>();
            var entries = ReadEntries(source, diagnostics, _options, out var foundHeader, out var foundFooter);
            if (!foundHeader || !foundFooter)
                return new MetadataParseResult(null, diagnostics);

            var values = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (!values.TryGetValue(entry.Key, out var list))
                {
                    list = new List<Entry>();
                    values.Add(entry.Key, list);
                }

                if (CollectionKeys.Contains(entry.Key) || !SingletonKeys.Contains(BaseKey(entry.Key)))
                {
                    if (!list.Any(item => string.Equals(item.Value, entry.Value, StringComparison.Ordinal)))
                        list.Add(entry);
                }
                else if (list.Count == 0)
                {
                    list.Add(entry);
                }
                else
                {
                    diagnostics.Add(new MetadataDiagnostic(
                        "MSM010_DUPLICATE_SINGLETON",
                        DiagnosticSeverity.Warning,
                        "Only the first @" + entry.Key + " value is used.",
                        entry.Line));
                }
            }

            var localizedNames = ReadLocalized(values, "name", diagnostics);
            var localizedDescriptions = ReadLocalized(values, "description", diagnostics);
            var antifeatures = ParseAntifeatures(values, diagnostics);
            var matches = ReadCollection(values, "match");
            var excludeMatches = ReadCollection(values, "exclude-match");
            ValidateMatchPatterns(matches, "match", diagnostics, values);
            ValidateMatchPatterns(excludeMatches, "exclude-match", diagnostics, values);

            var declaredGrants = ReadCollection(values, "grant").ToList();
            var grantDeclarationState = declaredGrants.Count == 0
                ? GrantDeclarationState.Missing
                : declaredGrants.Count == 1 && string.Equals(declaredGrants[0], "none", StringComparison.Ordinal)
                    ? GrantDeclarationState.ExplicitNone
                    : GrantDeclarationState.ExplicitList;
            if (declaredGrants.Contains("none") && declaredGrants.Count > 1)
            {
                diagnostics.Add(new MetadataDiagnostic(
                    "MSM030_GRANT_NONE_CONFLICT",
                    DiagnosticSeverity.Error,
                    "@grant none cannot be combined with another grant."));
            }
            var grants = new List<string>();
            foreach (var declaredGrant in declaredGrants)
            {
                if (UserScriptGrantCatalog.TryNormalize(
                    declaredGrant, _options.AcceptLegacyGrantAliases, out var canonicalGrant))
                {
                    if (!grants.Contains(canonicalGrant, StringComparer.Ordinal))
                        grants.Add(canonicalGrant);
                }
                else
                {
                    diagnostics.Add(new MetadataDiagnostic(
                        "MSM031_UNKNOWN_GRANT",
                        DiagnosticSeverity.Warning,
                        "Unknown grant '" + declaredGrant + "' will not be exposed."));
                }
            }
            if (grants.Count == 0 && grantDeclarationState != GrantDeclarationState.Missing)
                grants.Add("none");

            var runAt = ParseRunAt(First(values, "run-at"), diagnostics);
            var resources = ParseResources(values, diagnostics);
            var webRequest = ParseWebRequest(values, diagnostics);
            ValidateConnects(ReadCollection(values, "connect"), diagnostics, values);
            var knownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "name", "namespace", "version", "description", "author", "license", "copyright", "icon", "iconurl",
                "icon64", "icon64url", "downloadurl", "updateurl", "homepage", "homepageurl", "website", "source",
                "supporturl", "match", "include", "exclude", "exclude-match", "noframes", "run-at", "run-in",
                "inject-into", "sandbox", "unwrap", "grant", "connect", "require", "resource", "antifeature", "webrequest"
            };
            var additional = values
                .Where(pair => !knownKeys.Contains(BaseKey(pair.Key)))
                .ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<string>)pair.Value.Select(item => item.Value).ToList().AsReadOnly(),
                    StringComparer.OrdinalIgnoreCase);

            var metadata = new UserScriptMetadata(
                First(values, "name"),
                First(values, "namespace"),
                First(values, "version"),
                First(values, "description"),
                First(values, "author"),
                First(values, "license"),
                First(values, "copyright"),
                First(values, "iconurl") ?? First(values, "icon"),
                First(values, "icon64url") ?? First(values, "icon64"),
                First(values, "downloadurl"),
                First(values, "updateurl"),
                First(values, "homepageurl") ?? First(values, "homepage") ?? First(values, "website") ?? First(values, "source"),
                First(values, "website"),
                First(values, "source"),
                First(values, "supporturl"),
                runAt,
                grantDeclarationState,
                values.ContainsKey("noframes"),
                First(values, "run-in"),
                First(values, "inject-into"),
                First(values, "sandbox"),
                values.ContainsKey("unwrap"),
                localizedNames,
                localizedDescriptions,
                matches,
                ReadCollection(values, "include"),
                ReadCollection(values, "exclude"),
                excludeMatches,
                declaredGrants,
                grants,
                ReadCollection(values, "connect"),
                ReadCollection(values, "require"),
                resources,
                antifeatures,
                webRequest,
                additional);

            return new MetadataParseResult(metadata, diagnostics);
        }

        private static List<Entry> ReadEntries(
            string source,
            ICollection<MetadataDiagnostic> diagnostics,
            UserScriptMetadataParserOptions options,
            out bool foundHeader,
            out bool foundFooter)
        {
            var result = new List<Entry>();
            var lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foundHeader = false;
            foundFooter = false;

            var scannedCharacters = 0;
            for (var index = 0; index < lines.Length; index++)
            {
                var trimmed = lines[index].Trim();
                if (!foundHeader)
                {
                    if (index >= options.MaxHeaderScanLines ||
                        scannedCharacters + lines[index].Length > options.MaxHeaderScanCharacters)
                    {
                        diagnostics.Add(new MetadataDiagnostic(
                            "MSM000_HEADER_MISSING",
                            DiagnosticSeverity.Error,
                            "The userscript metadata header was not found within the configured scan limit.",
                            index + 1));
                        return result;
                    }
                    if (trimmed.Length == 0)
                    {
                        scannedCharacters += lines[index].Length + 1;
                        continue;
                    }
                    if (trimmed != "// ==UserScript==")
                    {
                        var preamble = options.AllowHeaderPreamble &&
                            index < options.MaxHeaderScanLines &&
                            scannedCharacters + lines[index].Length <= options.MaxHeaderScanCharacters &&
                            (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                             trimmed.StartsWith("/*", StringComparison.Ordinal) ||
                             trimmed.StartsWith("*", StringComparison.Ordinal) ||
                             trimmed.StartsWith("#", StringComparison.Ordinal));
                        if (preamble)
                        {
                            scannedCharacters += lines[index].Length + 1;
                            continue;
                        }
                        diagnostics.Add(new MetadataDiagnostic(
                            "MSM000_HEADER_MISSING",
                            DiagnosticSeverity.Error,
                            "The first non-empty line must be // ==UserScript==.",
                            index + 1));
                        return result;
                    }
                    foundHeader = true;
                    continue;
                }

                if (trimmed == "// ==UserScript==")
                {
                    diagnostics.Add(new MetadataDiagnostic(
                        "MSM002_HEADER_NESTED",
                        DiagnosticSeverity.Error,
                        "A userscript metadata block cannot be nested.",
                        index + 1));
                    continue;
                }
                if (trimmed == "// ==/UserScript==")
                {
                    foundFooter = true;
                    break;
                }

                var content = lines[index].TrimStart();
                if (!content.StartsWith("//", StringComparison.Ordinal))
                    continue;
                content = content.Substring(2).TrimStart();
                if (!content.StartsWith("@", StringComparison.Ordinal))
                    continue;

                content = content.Substring(1);
                var separator = 0;
                while (separator < content.Length && !char.IsWhiteSpace(content[separator]))
                    separator++;
                var key = content.Substring(0, separator).ToLowerInvariant();
                var value = separator < content.Length ? content.Substring(separator).Trim() : string.Empty;
                if (key.Length != 0)
                    result.Add(new Entry(key, value, index + 1));
            }

            if (foundHeader && !foundFooter)
            {
                diagnostics.Add(new MetadataDiagnostic(
                    "MSM001_HEADER_UNCLOSED",
                    DiagnosticSeverity.Error,
                    "The userscript metadata block is not closed."));
            }

            return result;
        }

        private static IDictionary<string, string> ReadLocalized(
            IDictionary<string, List<Entry>> values,
            string key,
            ICollection<MetadataDiagnostic> diagnostics)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in values.Where(item => item.Key.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase)))
            {
                var locale = pair.Key.Substring(key.Length + 1);
                try
                {
                    locale = CultureInfo.GetCultureInfo(locale).Name;
                    result[locale] = pair.Value[0].Value;
                }
                catch (CultureNotFoundException)
                {
                    diagnostics.Add(new MetadataDiagnostic(
                        "MSM011_INVALID_LOCALE",
                        DiagnosticSeverity.Warning,
                        "The locale '" + locale + "' is invalid.",
                        pair.Value[0].Line));
                }
            }
            return result;
        }

        private static UserScriptRunAt ParseRunAt(string value, ICollection<MetadataDiagnostic> diagnostics)
        {
            switch (value ?? "document-idle")
            {
                case "document-start": return UserScriptRunAt.DocumentStart;
                case "document-body": return UserScriptRunAt.DocumentBody;
                case "document-end": return UserScriptRunAt.DocumentEnd;
                case "document-idle": return UserScriptRunAt.DocumentIdle;
                default:
                    diagnostics.Add(new MetadataDiagnostic(
                        "MSM040_INVALID_RUN_AT",
                        DiagnosticSeverity.Error,
                        "Unknown @run-at value '" + value + "'."));
                    return UserScriptRunAt.DocumentIdle;
            }
        }

        private static IReadOnlyList<ResourceDeclaration> ParseResources(
            IDictionary<string, List<Entry>> values,
            ICollection<MetadataDiagnostic> diagnostics)
        {
            var result = new List<ResourceDeclaration>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (!values.TryGetValue("resource", out var resources))
                return result.AsReadOnly();

            foreach (var entry in resources)
            {
                var separator = entry.Value.IndexOfAny(new[] { ' ', '\t' });
                if (separator <= 0 || separator == entry.Value.Length - 1)
                {
                    diagnostics.Add(new MetadataDiagnostic(
                        "MSM050_INVALID_RESOURCE",
                        DiagnosticSeverity.Error,
                        "@resource requires a name and URL.",
                        entry.Line));
                    continue;
                }
                var name = entry.Value.Substring(0, separator);
                if (!names.Add(name))
                {
                    diagnostics.Add(new MetadataDiagnostic(
                        "MSM051_DUPLICATE_RESOURCE",
                        DiagnosticSeverity.Error,
                        "A resource named '" + name + "' is already declared.",
                        entry.Line));
                    continue;
                }
                result.Add(new ResourceDeclaration(name, entry.Value.Substring(separator).Trim()));
            }
            return result.AsReadOnly();
        }

        private static IReadOnlyList<AntifeatureDeclaration> ParseAntifeatures(
            IDictionary<string, List<Entry>> values,
            ICollection<MetadataDiagnostic> diagnostics)
        {
            var result = new List<AntifeatureDeclaration>();
            foreach (var pair in values.Where(item => item.Key == "antifeature" ||
                item.Key.StartsWith("antifeature:", StringComparison.OrdinalIgnoreCase)))
            {
                var locale = pair.Key.Length == "antifeature".Length
                    ? null
                    : pair.Key.Substring("antifeature:".Length);
                if (!string.IsNullOrEmpty(locale))
                {
                    try { locale = CultureInfo.GetCultureInfo(locale).Name; }
                    catch (CultureNotFoundException)
                    {
                        diagnostics.Add(new MetadataDiagnostic(
                            "MSM060_INVALID_ANTIFEATURE_LOCALE",
                            DiagnosticSeverity.Warning,
                            "The antifeature locale '" + locale + "' is invalid.",
                            pair.Value[0].Line));
                        continue;
                    }
                }
                foreach (var entry in pair.Value)
                {
                    var separator = entry.Value.IndexOfAny(new[] { ' ', '\t' });
                    if (separator <= 0 || separator == entry.Value.Length - 1)
                    {
                        diagnostics.Add(new MetadataDiagnostic(
                            "MSM061_INVALID_ANTIFEATURE",
                            DiagnosticSeverity.Error,
                            "@antifeature requires a type and description.",
                            entry.Line));
                        continue;
                    }
                    result.Add(new AntifeatureDeclaration(
                        entry.Value.Substring(0, separator),
                        entry.Value.Substring(separator).Trim(),
                        locale));
                }
            }
            return result.AsReadOnly();
        }

        private static IReadOnlyList<UserScriptWebRequestRule> ParseWebRequest(
            IDictionary<string, List<Entry>> values,
            ICollection<MetadataDiagnostic> diagnostics)
        {
            var result = new List<UserScriptWebRequestRule>();
            if (!values.TryGetValue("webrequest", out var entries))
                return result.AsReadOnly();
            foreach (var entry in entries)
            {
                try
                {
                    using (var document = JsonDocument.Parse(entry.Value))
                    {
                        var root = document.RootElement;
                        if (root.ValueKind != JsonValueKind.Object ||
                            !root.TryGetProperty("selector", out var selector) ||
                            !root.TryGetProperty("action", out var action) ||
                            !IsValidWebRequestSelector(selector) ||
                            !IsValidWebRequestAction(action))
                            throw new FormatException("A webRequest rule requires a valid selector and action.");
                        result.Add(new UserScriptWebRequestRule(selector, action));
                    }
                }
                catch (JsonException exception)
                {
                    diagnostics.Add(new MetadataDiagnostic(
                        "MSM062_INVALID_WEBREQUEST",
                        DiagnosticSeverity.Error,
                        "@webRequest is not valid JSON: " + exception.Message,
                        entry.Line));
                }
                catch (FormatException exception)
                {
                    diagnostics.Add(new MetadataDiagnostic(
                        "MSM062_INVALID_WEBREQUEST",
                        DiagnosticSeverity.Error,
                        exception.Message,
                        entry.Line));
                }
            }
            return result.AsReadOnly();
        }

        private static bool IsValidWebRequestSelector(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.String)
                return !string.IsNullOrWhiteSpace(value.GetString());
            if (value.ValueKind != JsonValueKind.Object)
                return false;
            var found = false;
            foreach (var name in new[] { "include", "match", "exclude" })
            {
                if (!value.TryGetProperty(name, out var item))
                    continue;
                found = true;
                if (item.ValueKind == JsonValueKind.String)
                {
                    if (string.IsNullOrWhiteSpace(item.GetString())) return false;
                }
                else if (item.ValueKind == JsonValueKind.Array)
                {
                    if (!item.EnumerateArray().Any(element => element.ValueKind != JsonValueKind.String ||
                        string.IsNullOrWhiteSpace(element.GetString()))) return false;
                }
                else return false;
            }
            return found;
        }

        private static bool IsValidWebRequestAction(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.String)
                return string.Equals(value.GetString(), "cancel", StringComparison.OrdinalIgnoreCase) ||
                    IsHttpUrl(value.GetString());
            if (value.ValueKind != JsonValueKind.Object)
                return false;
            if (value.TryGetProperty("cancel", out var cancel) &&
                cancel.ValueKind == JsonValueKind.True)
                return true;
            if (!value.TryGetProperty("redirect", out var redirect))
                return false;
            if (redirect.ValueKind == JsonValueKind.String)
                return IsHttpUrl(redirect.GetString()) || !string.IsNullOrWhiteSpace(redirect.GetString());
            return redirect.ValueKind == JsonValueKind.Object &&
                redirect.TryGetProperty("from", out var from) && from.ValueKind == JsonValueKind.String &&
                redirect.TryGetProperty("to", out var to) && to.ValueKind == JsonValueKind.String;
        }

        private static bool IsHttpUrl(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        private static void ValidateConnects(
            IReadOnlyList<string> connects,
            ICollection<MetadataDiagnostic> diagnostics,
            IDictionary<string, List<Entry>> values)
        {
            foreach (var connect in connects)
            {
                var valid = connect == "*" || connect == "self";
                if (!valid)
                {
                    var host = connect.StartsWith("*.", StringComparison.Ordinal) ? connect.Substring(2) : connect;
                    if (Uri.TryCreate(connect, UriKind.Absolute, out var uri))
                        valid = uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
                    else
                        valid = Uri.CheckHostName(host) != UriHostNameType.Unknown && !host.Contains("*");
                }
                if (!valid)
                {
                    diagnostics.Add(new MetadataDiagnostic(
                        "MSM052_INVALID_CONNECT",
                        DiagnosticSeverity.Error,
                        "Invalid @connect value '" + connect + "'.",
                        values["connect"].First(item => item.Value == connect).Line));
                }
            }
        }

        private static void ValidateMatchPatterns(
            IEnumerable<string> patterns,
            string key,
            ICollection<MetadataDiagnostic> diagnostics,
            IDictionary<string, List<Entry>> values)
        {
            foreach (var pattern in patterns)
            {
                if (!MatchPatternCompiler.TryCompile(pattern, out _, out var error))
                {
                    var line = values[key].First(item => item.Value == pattern).Line;
                    diagnostics.Add(new MetadataDiagnostic(
                        "MSM020_INVALID_MATCH",
                        DiagnosticSeverity.Error,
                        "Invalid @" + key + " pattern: " + error,
                        line));
                }
            }
        }

        private static string First(IDictionary<string, List<Entry>> values, string key)
        {
            return values.TryGetValue(key, out var entries) && entries.Count != 0 ? entries[0].Value : null;
        }

        private static IReadOnlyList<string> ReadCollection(IDictionary<string, List<Entry>> values, string key)
        {
            return values.TryGetValue(key, out var entries)
                ? entries.Select(item => item.Value).ToList().AsReadOnly()
                : new List<string>().AsReadOnly();
        }

        private static string BaseKey(string key)
        {
            var colon = key.IndexOf(':');
            return colon < 0 ? key : key.Substring(0, colon);
        }

        private sealed class Entry
        {
            public Entry(string key, string value, int line)
            {
                Key = key;
                Value = value;
                Line = line;
            }

            public string Key { get; }
            public string Value { get; }
            public int Line { get; }
        }
    }
}
