using Mzying2001.MonkeySharp.Core.Domain;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Mzying2001.MonkeySharp.Core.Matching
{
    public sealed class GlobPattern
    {
        private readonly string _pattern;
        private readonly bool _questionMarkIsWildcard;

        private GlobPattern(string pattern, bool questionMarkIsWildcard)
        {
            _pattern = pattern;
            _questionMarkIsWildcard = questionMarkIsWildcard;
        }

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

    public sealed class MatchPattern
    {
        private readonly bool _allUrls;
        private readonly string _scheme;
        private readonly string _host;
        private readonly bool _anyHost;
        private readonly bool _subdomainsOnly;
        private readonly int? _port;
        private readonly GlobPattern _path;

        internal MatchPattern(
            bool allUrls,
            string scheme,
            string host,
            bool anyHost,
            bool subdomainsOnly,
            int? port,
            GlobPattern path)
        {
            _allUrls = allUrls;
            _scheme = scheme;
            _host = host;
            _anyHost = anyHost;
            _subdomainsOnly = subdomainsOnly;
            _port = port;
            _path = path;
        }

        public bool IsMatch(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri)
                return false;
            var scheme = uri.Scheme.ToLowerInvariant();
            if (_allUrls)
                return scheme == "http" || scheme == "https" || scheme == "file";
            if (_scheme == "*" && scheme != "http" && scheme != "https")
                return false;
            if (_scheme != "*" && !string.Equals(_scheme, scheme, StringComparison.Ordinal))
                return false;

            if (scheme != "file")
            {
                var host = NormalizeHost(uri.IdnHost);
                if (!_anyHost)
                {
                    if (_subdomainsOnly)
                    {
                        var suffix = "." + _host;
                        if (host.Length <= suffix.Length || !host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                            return false;
                    }
                    else if (!string.Equals(_host, host, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
                if (_port.HasValue && uri.Port != _port.Value)
                    return false;
            }

            return _path.IsMatch(uri.AbsolutePath + uri.Query);
        }

        private static string NormalizeHost(string host)
        {
            return new IdnMapping().GetAscii(host).ToLowerInvariant();
        }
    }

    public static class MatchPatternCompiler
    {
        public static MatchPattern Compile(string pattern)
        {
            if (!TryCompile(pattern, out var result, out var error))
                throw new FormatException(error);
            return result;
        }

        public static bool TryCompile(string pattern, out MatchPattern result, out string error)
        {
            result = null;
            error = null;
            if (string.IsNullOrWhiteSpace(pattern))
            {
                error = "The pattern is empty.";
                return false;
            }
            if (pattern == "<all_urls>")
            {
                result = new MatchPattern(true, null, null, true, false, null, GlobPattern.CompileMatchPath("*"));
                return true;
            }

            var schemeEnd = pattern.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd <= 0)
            {
                error = "The pattern must contain ://.";
                return false;
            }
            var scheme = pattern.Substring(0, schemeEnd).ToLowerInvariant();
            if (scheme != "http" && scheme != "https" && scheme != "file" && scheme != "*")
            {
                error = "The scheme must be http, https, file, or *.";
                return false;
            }

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
                result = new MatchPattern(false, scheme, string.Empty, true, false, null, GlobPattern.CompileMatchPath(path));
                return true;
            }
            if (authority.Length == 0)
            {
                error = "The host is required.";
                return false;
            }

            int? port = null;
            var hostPart = authority;
            var colon = authority.LastIndexOf(':');
            if (colon >= 0)
            {
                hostPart = authority.Substring(0, colon);
                if (!int.TryParse(authority.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPort) ||
                    parsedPort < 1 || parsedPort > 65535)
                {
                    error = "The port is invalid.";
                    return false;
                }
                port = parsedPort;
            }

            var anyHost = hostPart == "*";
            var subdomainsOnly = hostPart.StartsWith("*.", StringComparison.Ordinal);
            if (!anyHost && hostPart.Contains("*") && !subdomainsOnly)
            {
                error = "A host wildcard is only valid as * or *.domain.";
                return false;
            }
            if (subdomainsOnly)
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

            try
            {
                if (!anyHost)
                    hostPart = new IdnMapping().GetAscii(hostPart).ToLowerInvariant();
            }
            catch (ArgumentException)
            {
                error = "The host is not a valid IDN name.";
                return false;
            }

            result = new MatchPattern(false, scheme, hostPart, anyHost, subdomainsOnly, port, GlobPattern.CompileMatchPath(path));
            return true;
        }
    }

    public interface IUserScriptMatcher
    {
        bool IsMatch(UserScriptMetadata metadata, Uri url);
    }

    public sealed class UserScriptMatcher : IUserScriptMatcher
    {
        public bool IsMatch(UserScriptMetadata metadata, Uri url)
        {
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));
            if (url == null || !url.IsAbsoluteUri)
                return false;

            var normalizedUrl = NormalizeUrl(url);
            if (metadata.Excludes.Any(pattern => GlobPattern.Compile(pattern).IsMatch(normalizedUrl)))
                return false;
            if (metadata.ExcludeMatches.Any(pattern => CompileAndMatch(pattern, url)))
                return false;

            return metadata.Matches.Any(pattern => CompileAndMatch(pattern, url)) ||
                   metadata.Includes.Any(pattern => GlobPattern.Compile(pattern).IsMatch(normalizedUrl));
        }

        private static bool CompileAndMatch(string pattern, Uri url)
        {
            return MatchPatternCompiler.TryCompile(pattern, out var compiled, out _) && compiled.IsMatch(url);
        }

        internal static string NormalizeUrl(Uri uri)
        {
            var builder = new StringBuilder();
            builder.Append(uri.Scheme.ToLowerInvariant()).Append("://");
            if (uri.Scheme != "file")
            {
                builder.Append(new IdnMapping().GetAscii(uri.IdnHost).ToLowerInvariant());
                if (!uri.IsDefaultPort)
                    builder.Append(':').Append(uri.Port.ToString(CultureInfo.InvariantCulture));
            }
            builder.Append(uri.AbsolutePath).Append(uri.Query);
            return builder.ToString();
        }
    }
}
