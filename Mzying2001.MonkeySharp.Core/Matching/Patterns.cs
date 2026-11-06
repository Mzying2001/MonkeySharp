using Mzying2001.MonkeySharp.Core.Domain;
using System;
using System.Runtime.CompilerServices;
using System.Net;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Mzying2001.MonkeySharp.Core.Matching
{
    /// <summary>
    /// Represents a compiled case-sensitive glob supporting <c>*</c> and <c>?</c> wildcards.
    /// </summary>
    public sealed class GlobPattern
    {
        private readonly string _pattern;
        private readonly bool _questionMarkIsWildcard;

        private GlobPattern(string pattern, bool questionMarkIsWildcard)
        {
            _pattern = pattern;
            _questionMarkIsWildcard = questionMarkIsWildcard;
        }

        /// <summary>Compiles a glob pattern.</summary>
        /// <param name="pattern">The pattern to compile.</param>
        /// <returns>A reusable compiled glob.</returns>
        public static GlobPattern Compile(string pattern)
        {
            if (pattern == null)
                throw new ArgumentNullException(nameof(pattern));
            return new GlobPattern(pattern, true);
        }

        internal static GlobPattern CompileMatchPath(string pattern)
        {
            return new GlobPattern(pattern, false);
        }

        /// <summary>Determines whether a value matches the compiled glob.</summary>
        /// <param name="value">The value to test.</param>
        /// <returns><see langword="true"/> when the complete value matches the pattern.</returns>
        public bool IsMatch(string value)
        {
            if (value == null)
                return false;

            var patternIndex = 0;
            var valueIndex = 0;
            var starIndex = -1;
            var retryIndex = -1;
            while (valueIndex < value.Length)
            {
                if (patternIndex < _pattern.Length &&
                    ((_questionMarkIsWildcard && _pattern[patternIndex] == '?') ||
                     _pattern[patternIndex] == value[valueIndex]))
                {
                    patternIndex++;
                    valueIndex++;
                }
                else if (patternIndex < _pattern.Length && _pattern[patternIndex] == '*')
                {
                    starIndex = patternIndex++;
                    retryIndex = valueIndex;
                }
                else if (starIndex >= 0)
                {
                    patternIndex = starIndex + 1;
                    valueIndex = ++retryIndex;
                }
                else
                {
                    return false;
                }
            }

            while (patternIndex < _pattern.Length && _pattern[patternIndex] == '*')
                patternIndex++;
            return patternIndex == _pattern.Length;
        }
    }

    /// <summary>
    /// Represents a compiled WebExtension-style URL match pattern.
    /// </summary>
    public sealed class MatchPattern
    {
        private readonly bool _aboutBlank;
        private readonly string _scheme;
        private readonly string _host;
        private readonly bool _anyHost;
        private readonly bool _includeSubdomains;
        private readonly int? _port;
        private readonly GlobPattern _path;

        internal MatchPattern(
            string scheme,
            string host,
            bool anyHost,
            bool includeSubdomains,
            int? port,
            GlobPattern path)
        {
            _aboutBlank = false;
            _scheme = scheme;
            _host = host;
            _anyHost = anyHost;
            _includeSubdomains = includeSubdomains;
            _port = port;
            _path = path;
        }

        private MatchPattern(bool aboutBlank)
        {
            _aboutBlank = aboutBlank;
            _scheme = null;
            _host = null;
            _anyHost = false;
            _includeSubdomains = false;
            _port = null;
            _path = null;
        }

        internal static MatchPattern AboutBlank()
        {
            return new MatchPattern(true);
        }

        /// <summary>Determines whether an absolute URL matches the pattern.</summary>
        /// <param name="uri">The URL to test.</param>
        /// <returns><see langword="true"/> when the URL matches the scheme, host, port, and path constraints.</returns>
        public bool IsMatch(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri)
                return false;
            var scheme = uri.Scheme.ToLowerInvariant();
            if (_aboutBlank)
            {
                return scheme == "about" &&
                       string.Equals(uri.AbsolutePath, "blank", StringComparison.Ordinal) &&
                       string.IsNullOrEmpty(uri.Query);
            }
            if (_scheme == "*" && scheme != "http" && scheme != "https")
                return false;
            if (_scheme != "*" && !string.Equals(_scheme, scheme, StringComparison.Ordinal))
                return false;

            if (scheme != "file")
            {
                var host = NormalizeHost(uri.IdnHost);
                if (!_anyHost &&
                    !string.Equals(_host, host, StringComparison.OrdinalIgnoreCase) &&
                    (!_includeSubdomains || !host.EndsWith("." + _host, StringComparison.OrdinalIgnoreCase)))
                    return false;
                if (_port.HasValue && uri.Port != _port.Value)
                    return false;
            }

            return _path.IsMatch(UserScriptMatcher.PathAndQuery(uri));
        }

        private static string NormalizeHost(string host)
        {
            return MatchPatternCompiler.NormalizeHost(host);
        }
    }

    /// <summary>
    /// Compiles WebExtension-style URL match patterns used by userscript metadata.
    /// </summary>
    public static class MatchPatternCompiler
    {
        internal static string NormalizeHost(string host)
        {
            host = host.Trim('[', ']');
            if (IPAddress.TryParse(host, out var address)) return address.ToString().ToLowerInvariant();
            return new IdnMapping().GetAscii(host).ToLowerInvariant();
        }

        /// <summary>Compiles a URL match pattern or throws when it is invalid.</summary>
        /// <param name="pattern">The match pattern text.</param>
        /// <returns>The compiled match pattern.</returns>
        /// <exception cref="FormatException">The pattern is invalid.</exception>
        public static MatchPattern Compile(string pattern)
        {
            if (!TryCompile(pattern, out var result, out var error))
                throw new FormatException(error);
            return result;
        }

        /// <summary>Attempts to compile a URL match pattern.</summary>
        /// <param name="pattern">The match pattern text.</param>
        /// <param name="result">Receives the compiled pattern when successful.</param>
        /// <param name="error">Receives a validation message when compilation fails.</param>
        /// <returns><see langword="true"/> when the pattern was compiled successfully.</returns>
        public static bool TryCompile(string pattern, out MatchPattern result, out string error)
        {
            result = null;
            error = null;
            if (string.IsNullOrWhiteSpace(pattern))
            {
                error = "The pattern is empty.";
                return false;
            }
            if (string.Equals(pattern, "about:blank", StringComparison.OrdinalIgnoreCase))
            {
                result = MatchPattern.AboutBlank();
                return true;
            }

            var schemeEnd = pattern.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd <= 0)
            {
                error = "The pattern must contain ://.";
                return false;
            }
            var scheme = pattern.Substring(0, schemeEnd).ToLowerInvariant();
            if (scheme != "http" && scheme != "https" && scheme != "file" && scheme != "*" && scheme != "http*")
            {
                error = "The scheme must be http, https, http*, file, or *.";
                return false;
            }
            if (scheme == "http*")
                scheme = "*";

            var authorityStart = schemeEnd + 3;
            var pathStart = pattern.IndexOf('/', authorityStart);
            if (pathStart < 0)
            {
                error = "The pattern must include a path beginning with /.";
                return false;
            }
            var authority = pattern.Substring(authorityStart, pathStart - authorityStart);
            var path = pattern.Substring(pathStart);
            if (scheme == "file")
            {
                if (authority.Length != 0 && authority != "*")
                {
                    error = "A file pattern cannot specify a host.";
                    return false;
                }
                result = new MatchPattern(scheme, string.Empty, true, false, null, GlobPattern.CompileMatchPath(path));
                return true;
            }
            if (authority.Length == 0)
            {
                error = "The host is required.";
                return false;
            }

            int? port = null;
            var hostPart = authority;
            string portText = null;
            if (authority.StartsWith("[", StringComparison.Ordinal))
            {
                var end = authority.IndexOf(']');
                if (end < 0 || Uri.CheckHostName(authority.Substring(1, end - 1)) != UriHostNameType.IPv6 ||
                    end + 1 < authority.Length && authority[end + 1] != ':')
                { error = "Invalid bracketed IPv6 host."; return false; }
                hostPart = authority.Substring(1, end - 1);
                if (end + 1 < authority.Length) portText = authority.Substring(end + 2);
            }
            else
            {
                var colon = authority.LastIndexOf(':');
                if (colon >= 0) { hostPart = authority.Substring(0, colon); portText = authority.Substring(colon + 1); }
                if (hostPart.Contains(":")) { error = "IPv6 hosts require brackets."; return false; }
            }
            if (portText != null && portText != "*")
            {
                if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPort) || parsedPort < 0 || parsedPort > 65535)
                { error = "The port is invalid."; return false; }
                port = parsedPort;
            }

            var anyHost = hostPart == "*";
            var includeSubdomains = hostPart.StartsWith("*.", StringComparison.Ordinal);
            if (!anyHost && hostPart.Contains("*") && !includeSubdomains)
            {
                error = "A host wildcard is only valid as * or *.domain.";
                return false;
            }
            if (includeSubdomains)
                hostPart = hostPart.Substring(2);
            if (!anyHost && hostPart.Contains("*"))
            {
                error = "A subdomain wildcard must be followed by a concrete domain.";
                return false;
            }
            if (!anyHost && hostPart.Length == 0)
            {
                error = "The host is empty.";
                return false;
            }

            if (!anyHost && Uri.CheckHostName(hostPart.Trim('[', ']')) == UriHostNameType.Unknown)
            { error = "Invalid host name."; return false; }
            try
            {
                if (!anyHost)
                    hostPart = hostPart.Any(value => value > 127) ? hostPart.ToLowerInvariant() : NormalizeHost(hostPart);
            }
            catch (ArgumentException)
            {
                error = "The host is not a valid IDN name.";
                return false;
            }

            result = new MatchPattern(scheme, hostPart, anyHost, includeSubdomains, port, GlobPattern.CompileMatchPath(path));
            return true;
        }
    }

    internal static class UserScriptUrlPattern
    {
        private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
        public static bool IsRegex(string pattern) => pattern != null && pattern.StartsWith("/", StringComparison.Ordinal);

        public static bool TryValidate(string pattern, out string error)
        {
            try { Compile(pattern); error = null; return true; }
            catch (ArgumentException exception) { error = exception.Message; return false; }
        }

        public static Regex Compile(string pattern)
        {
            if (pattern == null) throw new ArgumentNullException(nameof(pattern));
            string expression;
            if (IsRegex(pattern))
            {
                if (pattern.Length < 2 || !pattern.EndsWith("/", StringComparison.Ordinal))
                    throw new ArgumentException("Use /expression/ without literal flags; URL expressions are case-insensitive.");
                expression = pattern.Substring(1, pattern.Length - 2);
                // Translate only the ECMAScript subset shared with the bounded .NET evaluator.
                if (Regex.IsMatch(expression, @"\(\?(?![:=!])|\\(?:[pPAZzG]|u\{|k|[1-9])|\*\+|\+\+|\?\+|\}\+|-\["))
                    throw new ArgumentException("This JavaScript expression uses constructs outside the supported URL-regex subset.");
            }
            else
            {
                var result = new StringBuilder("^");
                var schemeEnd = pattern.IndexOf("://", StringComparison.Ordinal);
                var hostStart = schemeEnd < 0 ? -1 : schemeEnd + 3;
                var hostEnd = hostStart < 0 ? -1 : pattern.IndexOf('/', hostStart);
                if (hostStart >= 0 && hostEnd < 0) hostEnd = pattern.Length;
                for (var index = 0; index < pattern.Length; index++)
                {
                    if (pattern[index] != '*') result.Append(Regex.Escape(pattern[index].ToString()));
                    else result.Append(schemeEnd >= 0 && index < schemeEnd ? "[^:]*" : hostStart >= 0 && index >= hostStart && index < hostEnd ? "[^/]*" : ".*");
                }
                expression = result.Append('$').ToString();
            }
            return new Regex(expression, RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
        }
    }

    /// <summary>Contains reusable, immutable URL rules for one metadata revision.</summary>
    public sealed class UserScriptUrlRules
    {
        private static readonly ConditionalWeakTable<UserScriptMetadata, UserScriptUrlRules> Cache = new ConditionalWeakTable<UserScriptMetadata, UserScriptUrlRules>();
        private readonly MatchPattern[] _matches;
        private readonly MatchPattern[] _excludeMatches;
        private readonly Regex[] _includes;
        private readonly Regex[] _excludes;
        private readonly HashSet<string> _runIn;
        private UserScriptUrlRules(UserScriptMetadata metadata)
        {
            _runIn = new HashSet<string>(metadata.RunIn, StringComparer.Ordinal);
            _matches = metadata.Matches.Select(MatchPatternCompiler.Compile).ToArray();
            _excludeMatches = metadata.ExcludeMatches.Select(MatchPatternCompiler.Compile).ToArray();
            _includes = metadata.Includes.Select(UserScriptUrlPattern.Compile).ToArray();
            _excludes = metadata.Excludes.Select(UserScriptUrlPattern.Compile).ToArray();
        }
        /// <summary>Compiles once per immutable metadata object; entries expire with that object's lifetime.</summary>
        public static UserScriptUrlRules Compile(UserScriptMetadata metadata) => Cache.GetValue(metadata, value => new UserScriptUrlRules(value));
        /// <summary>Evaluates declared privacy contexts against the actual host context; unknown contexts do not satisfy a restriction.</summary>
        public bool AllowsEnvironment(bool? isIncognito, out string reason)
        {
            reason = null;
            if (_runIn.Count == 0 || _runIn.Contains("normal-tabs") && _runIn.Contains("incognito-tabs")) return true;
            if (!isIncognito.HasValue) { reason = "The host cannot identify the privacy context required by @run-in."; return false; }
            if (_runIn.Contains(isIncognito.Value ? "incognito-tabs" : "normal-tabs")) return true;
            reason = _runIn.Any(value => value.StartsWith("container-id-", StringComparison.Ordinal))
                ? "The requested container context is unavailable in this Chromium host." : "The current privacy context does not match @run-in.";
            return false;
        }
        /// <summary>Evaluates rules, ignoring include/exclude for an explicit context-menu trigger.</summary>
        /// <exception cref="RegexMatchTimeoutException">A rule exceeded its bounded evaluation time; callers must reject execution.</exception>
        public bool IsMatch(Uri url, bool contextMenu = false)
        {
            if (url == null || !url.IsAbsoluteUri) return false;
            if (_excludeMatches.Any(pattern => pattern.IsMatch(url))) return false;
            if (contextMenu) return _matches.Length == 0 || _matches.Any(pattern => pattern.IsMatch(url));
            var normalized = UserScriptMatcher.NormalizeUrl(url);
            if (_excludes.Any(pattern => pattern.IsMatch(normalized))) return false;
            return _matches.Any(pattern => pattern.IsMatch(url)) || _includes.Any(pattern => pattern.IsMatch(normalized));
        }
    }

    /// <summary>
    /// Determines whether userscript metadata permits execution on a URL.
    /// </summary>
    public interface IUserScriptMatcher
    {
        /// <summary>Evaluates the include and exclusion rules in userscript metadata.</summary>
        /// <param name="metadata">The metadata containing URL rules.</param>
        /// <param name="url">The absolute URL to evaluate.</param>
        /// <returns><see langword="true"/> when the script should run on the URL.</returns>
        bool IsMatch(UserScriptMetadata metadata, Uri url);
    }

    /// <summary>
    /// Implements userscript URL matching with exclusion rules taking precedence.
    /// </summary>
    public sealed class UserScriptMatcher : IUserScriptMatcher
    {
        /// <inheritdoc />
        public bool IsMatch(UserScriptMetadata metadata, Uri url)
        {
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));
            if (url == null || !url.IsAbsoluteUri)
                return false;

            try { return UserScriptUrlRules.Compile(metadata).IsMatch(url); }
            catch (RegexMatchTimeoutException) { return false; }
            catch (FormatException) { return false; }
            catch (ArgumentException) { return false; }
        }

        internal static string NormalizeUrl(Uri uri)
        {
            var scheme = uri.Scheme.ToLowerInvariant();
            if (scheme == "about")
                return scheme + ":" + uri.AbsolutePath + uri.Query;

            var builder = new StringBuilder();
            builder.Append(scheme).Append("://");
            if (uri.Scheme != "file")
            {
                var host = MatchPatternCompiler.NormalizeHost(uri.IdnHost);
                builder.Append(host.Contains(":") ? "[" + host + "]" : host);
                if (!uri.IsDefaultPort)
                    builder.Append(':').Append(uri.Port.ToString(CultureInfo.InvariantCulture));
            }
            builder.Append(PathAndQuery(uri));
            return builder.ToString();
        }

        internal static string PathAndQuery(Uri uri)
        {
            // Browser hrefs preserve escaped unreserved characters that System.Uri unescapes.
            var original = uri.OriginalString;
            var authority = original.IndexOf("://", StringComparison.Ordinal);
            if (authority < 0 || !uri.IsWellFormedOriginalString()) return uri.AbsolutePath + uri.Query;
            var start = original.IndexOfAny(new[] { '/', '?', '#' }, authority + 3);
            var suffix = start < 0 ? "/" : original.Substring(start).Split('#')[0];
            if (suffix.Length == 0 || suffix[0] != '/') suffix = "/" + suffix;
            if (suffix.Any(value => value > 127)) return uri.AbsolutePath + uri.Query;
            var query = suffix.IndexOf('?');
            var path = query < 0 ? suffix : suffix.Substring(0, query);
            var parts = path.Split('/');
            var normalized = new List<string>();
            for (var index = 1; index < parts.Length; index++)
            {
                var dot = parts[index].Replace("%2e", ".").Replace("%2E", ".");
                if (dot == "..") { if (normalized.Count != 0) normalized.RemoveAt(normalized.Count - 1); }
                else if (dot != ".") normalized.Add(parts[index]);
                if (index == parts.Length - 1 && (dot == "." || dot == "..")) normalized.Add(string.Empty);
            }
            return "/" + string.Join("/", normalized) + (query < 0 ? string.Empty : suffix.Substring(query));
        }
    }
}
