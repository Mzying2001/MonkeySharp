using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Matching;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Apis
{
    /// <summary>Describes the scope used to query userscript cookies.</summary>
    public sealed class UserScriptCookieQuery
    {
        /// <summary>Initializes a cookie query for an absolute HTTP or HTTPS URL.</summary>
        public UserScriptCookieQuery(Uri url, string name = null, string domain = null, string path = null, UserScriptCookiePartitionKey partitionKey = null)
        {
            Url = ValidateUrl(url);
            Name = name;
            Domain = domain;
            Path = path;
            PartitionKey = partitionKey;
        }

        /// <summary>Gets the URL whose cookie scope is queried.</summary>
        public Uri Url { get; }
        /// <summary>Gets the optional exact cookie name.</summary>
        public string Name { get; }
        /// <summary>Gets the optional case-insensitive cookie domain.</summary>
        public string Domain { get; }
        /// <summary>Gets the optional exact cookie path.</summary>
        public string Path { get; }
        /// <summary>Gets the partition filter, or null for unpartitioned cookies.</summary>
        public UserScriptCookiePartitionKey PartitionKey { get; }

        /// <summary>Returns whether a cookie belongs to the query URL and optional constraints.</summary>
        public bool Matches(UserScriptCookie cookie)
        {
            if (PartitionKey == null ? cookie.PartitionKey != null : !PartitionKey.Matches(cookie.PartitionKey)) return false;
            if (Name != null && !string.Equals(Name, cookie.Name, StringComparison.Ordinal)) return false;
            if (Domain != null && !string.Equals(Domain.TrimStart('.'), cookie.Domain.TrimStart('.'), StringComparison.OrdinalIgnoreCase)) return false;
            if (Path != null && !string.Equals(Path, cookie.Path, StringComparison.Ordinal)) return false;
            if (cookie.Secure && Url.Scheme != Uri.UriSchemeHttps) return false;
            if (cookie.HostOnly ? !string.Equals(Url.Host, cookie.Domain.TrimStart('.'), StringComparison.OrdinalIgnoreCase) : !DomainMatches(Url.Host, cookie.Domain)) return false;
            return PathMatches(Url.AbsolutePath, cookie.Path);
        }

        internal static Uri ValidateUrl(Uri url)
        {
            if (url == null || !url.IsAbsoluteUri ||
                (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("The cookie URL must be an absolute HTTP or HTTPS URL.", nameof(url));
            return url;
        }

        internal static bool DomainMatches(string host, string domain)
        {
            return string.Equals(host, domain.TrimStart('.'), StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + domain.TrimStart('.'), StringComparison.OrdinalIgnoreCase);
        }

        internal static bool PathMatches(string requestPath, string cookiePath)
        {
            if (string.IsNullOrEmpty(cookiePath) || cookiePath == "/") return true;
            return requestPath.StartsWith(cookiePath, StringComparison.Ordinal) &&
                (requestPath.Length == cookiePath.Length || cookiePath.EndsWith("/", StringComparison.Ordinal) ||
                 requestPath[cookiePath.Length] == '/');
        }
    }

    /// <summary>Represents a cookie exposed through the userscript cookie API.</summary>
    public sealed class UserScriptCookie
    {
        /// <summary>Initializes an immutable userscript cookie value.</summary>
        public UserScriptCookie(
            string name, string value, string domain, string path,
            DateTime? expirationDate = null, bool secure = false, bool httpOnly = false, string sameSite = null,
            string firstPartyDomain = null, bool hostOnly = false, bool? session = null, UserScriptCookiePartitionKey partitionKey = null)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Cookie name is required.", nameof(name));
            Name = name;
            Value = value ?? string.Empty;
            Domain = string.IsNullOrEmpty(domain) ? throw new ArgumentException("Cookie domain is required.", nameof(domain)) : domain;
            Path = string.IsNullOrEmpty(path) ? "/" : path;
            ExpirationDate = expirationDate;
            Secure = secure;
            HttpOnly = httpOnly;
            SameSite = sameSite;
            FirstPartyDomain = firstPartyDomain;
            PartitionKey = partitionKey;
            HostOnly = hostOnly;
            Session = session ?? !expirationDate.HasValue;
        }

        /// <summary>Gets the cookie name.</summary>
        public string Name { get; }
        /// <summary>Gets the cookie value.</summary>
        public string Value { get; }
        /// <summary>Gets the normalized cookie domain.</summary>
        public string Domain { get; }
        /// <summary>Gets the cookie path.</summary>
        public string Path { get; }
        /// <summary>Gets the UTC expiration time, or <see langword="null"/> for a session cookie.</summary>
        public DateTime? ExpirationDate { get; }
        /// <summary>Gets whether the cookie requires a secure transport.</summary>
        public bool Secure { get; }
        /// <summary>Gets whether scripts are prohibited from reading the cookie.</summary>
        public bool HttpOnly { get; }
        /// <summary>Gets the SameSite mode reported by the browser service.</summary>
        public string SameSite { get; }
        /// <summary>Gets the first-party domain associated with the cookie.</summary>
        public string FirstPartyDomain { get; }
        /// <summary>Gets the canonical partition identity, or null when unpartitioned.</summary>
        public UserScriptCookiePartitionKey PartitionKey { get; }
        /// <summary>Gets whether the cookie is restricted to the exact host.</summary>
        public bool HostOnly { get; }
        /// <summary>Gets whether the cookie is a session cookie.</summary>
        public bool Session { get; }
    }

    /// <summary>Describes a cookie mutation requested by a userscript.</summary>
    public sealed class UserScriptCookieMutation
    {
        /// <summary>Initializes a set or delete request and derives omitted domain/path defaults.</summary>
        public UserScriptCookieMutation(
            Uri url, string name, string value = null, string domain = null, string path = null,
            DateTime? expirationDate = null, bool secure = false, bool httpOnly = false, string sameSite = null,
            string firstPartyDomain = null, UserScriptCookiePartitionKey partitionKey = null)
        {
            Url = UserScriptCookieQuery.ValidateUrl(url);
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Cookie name is required.", nameof(name));
            Name = name;
            Value = value ?? string.Empty;
            HostOnly = string.IsNullOrEmpty(domain);
            Domain = HostOnly ? url.Host.ToLowerInvariant() : "." + domain.TrimStart('.').ToLowerInvariant();
            if (!UserScriptCookieQuery.DomainMatches(url.Host, Domain)) throw new ArgumentException("Cookie domain is outside the target URL.", nameof(domain));
            HasExplicitPath = path != null;
            if (path != null && !path.StartsWith("/", StringComparison.Ordinal)) throw new ArgumentException("Cookie path must start with /.", nameof(path));
            var lastSlash = url.AbsolutePath.LastIndexOf('/');
            Path = path ?? (lastSlash <= 0 ? "/" : url.AbsolutePath.Substring(0, lastSlash));
            ExpirationDate = expirationDate;
            Secure = secure;
            HttpOnly = httpOnly;
            SameSite = sameSite;
            FirstPartyDomain = firstPartyDomain;
            PartitionKey = partitionKey;
        }

        /// <summary>Gets the target URL.</summary>
        public Uri Url { get; }
        /// <summary>Gets the cookie name.</summary>
        public string Name { get; }
        /// <summary>Gets the value used by set operations.</summary>
        public string Value { get; }
        /// <summary>Gets the normalized cookie domain.</summary>
        public string Domain { get; }
        /// <summary>Gets the cookie path.</summary>
        public string Path { get; }
        /// <summary>Gets the expiration time.</summary>
        public DateTime? ExpirationDate { get; }
        /// <summary>Gets whether the cookie requires a secure transport.</summary>
        public bool Secure { get; }
        /// <summary>Gets whether the cookie is HTTP-only.</summary>
        public bool HttpOnly { get; }
        /// <summary>Gets the requested SameSite mode.</summary>
        public string SameSite { get; }
        /// <summary>Gets the first-party domain requested by the script.</summary>
        public string FirstPartyDomain { get; }
        /// <summary>Gets the canonical partition identity, or null when unpartitioned.</summary>
        public UserScriptCookiePartitionKey PartitionKey { get; }
        /// <summary>Gets whether the mutation targets an exact host.</summary>
        public bool HostOnly { get; }
        /// <summary>Gets whether a path was explicitly selected.</summary>
        public bool HasExplicitPath { get; }
    }

    /// <summary>Provides cookie CRUD for one browser context. Results must reflect engine state.</summary>
    public interface ICookieService
    {
        /// <summary>Lists cookies visible to the URL and exact optional filters.</summary>
        Task<IReadOnlyList<UserScriptCookie>> ListAsync(UserScriptCookieQuery query, CancellationToken cancellationToken);
        /// <summary>Sets and verifies a cookie, returning actual state (null for an expired deletion).</summary>
        Task<UserScriptCookie> SetAsync(UserScriptCookieMutation mutation, CancellationToken cancellationToken);
        /// <summary>Deletes at most one selected cookie identity and verifies its absence.</summary>
        Task<bool> DeleteAsync(UserScriptCookieMutation mutation, CancellationToken cancellationToken);
    }

    /// <summary>Authorizes and normalizes the modern/legacy cookie CRUD contract.</summary>
    public sealed class CookieApiProvider : IUserScriptApiProvider, IDisposable
    {
        private readonly ICookieService _cookies;
        private bool _disposed;
        /// <summary>Uses a host-owned cookie backend.</summary>
        public CookieApiProvider(ICookieService cookies) { _cookies = cookies ?? throw new ArgumentNullException(nameof(cookies)); }
        /// <inheritdoc />
        public IReadOnlyCollection<string> Methods { get; } = new ReadOnlyCollection<string>(new[] { "GM.cookie" });
        /// <summary>Releases this instance.</summary>
        public void Dispose() { _disposed = true; }
        /// <inheritdoc />
        public async Task<ApiResult> InvokeAsync(ApiInvocationContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed) throw new ObjectDisposedException(nameof(CookieApiProvider));
            if (context.Method != "GM.cookie") throw new UnsupportedApiException(context.Method);
            ProviderParameters.RequireObject(context.Parameters);
            var operation = ProviderParameters.OptionalString(context.Parameters, "operation") ?? "list";
            if (operation != "list" && operation != "set" && operation != "delete") throw ProviderParameters.Invalid("Unknown cookie operation.");
            var details = context.Parameters.TryGetProperty("details", out var item) ? item : context.Parameters;
            ProviderParameters.RequireObject(details);
            foreach (var unsupported in new[] { "cookiePartition", "firstPartyDomain" })
                if (details.TryGetProperty(unsupported, out _)) throw new UnsupportedApiException(unsupported + " is not supported by this cookie backend.");
            var text = ProviderParameters.OptionalString(details, "url") ?? context.Frame.Url?.AbsoluteUri;
            if (!Uri.TryCreate(text, UriKind.Absolute, out var url) || (url.Scheme != "http" && url.Scheme != "https"))
                throw ProviderParameters.Invalid("url must be an absolute HTTP(S) URL.");
            EnsureAccessible(context, url);
            UserScriptCookiePartitionKey partitionKey = null;
            if (details.TryGetProperty("partitionKey", out var key))
            {
                if (!(_cookies is IPartitionedCookieService partitioned)) throw new UnsupportedApiException("This cookie backend does not support partitionKey.");
                try { partitionKey = await partitioned.NormalizePartitionKeyAsync(ReadPartitionKey(key), url,
                    operation == "list" ? CookiePartitionOperation.List : operation == "set" ? CookiePartitionOperation.Set : CookiePartitionOperation.Delete, cancellationToken).ConfigureAwait(false); }
                catch (ArgumentException error) { throw ProviderParameters.Invalid(error.Message); }
            }
            var query = new UserScriptCookieQuery(url, ProviderParameters.OptionalString(details, "name"),
                ProviderParameters.OptionalString(details, "domain"), ProviderParameters.OptionalString(details, "path"), partitionKey);
            if (query.Domain != null && !UserScriptCookieQuery.DomainMatches(url.Host, query.Domain))
                throw ProviderParameters.Invalid("Cookie domain is outside the authorized URL.");
            if (operation == "list")
            {
                var values = await _cookies.ListAsync(query, cancellationToken).ConfigureAwait(false);
                return ApiResult.FromValue(values.Where(query.Matches).Select(ToJson).ToArray());
            }
            if (operation != "set" && operation != "delete") throw ProviderParameters.Invalid("Unknown cookie operation.");
            var name = ProviderParameters.RequiredString(details, "name");
            DateTime? expiration = null;
            if (details.TryGetProperty("expirationDate", out var expires))
            {
                if (expires.ValueKind != JsonValueKind.Number || !expires.TryGetDouble(out var seconds) || seconds != Math.Truncate(seconds) ||
                    seconds < DateTimeOffset.MinValue.ToUnixTimeSeconds() || seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
                    throw ProviderParameters.Invalid("expirationDate must be integral Unix seconds in range.");
                expiration = DateTimeOffset.FromUnixTimeSeconds((long)seconds).UtcDateTime;
            }
            var sameSite = NormalizeSameSite(ProviderParameters.OptionalString(details, "sameSite"));
            if (operation == "set" && partitionKey != null && !ProviderParameters.OptionalBoolean(details, "secure"))
                throw ProviderParameters.Invalid("Partitioned cookies require secure:true.");
            UserScriptCookieMutation mutation;
            try { mutation = new UserScriptCookieMutation(url, name, ProviderParameters.OptionalString(details, "value"), query.Domain,
                query.Path, expiration, ProviderParameters.OptionalBoolean(details, "secure"), ProviderParameters.OptionalBoolean(details, "httpOnly"), sameSite, partitionKey: partitionKey); }
            catch (ArgumentException error) { throw ProviderParameters.Invalid(error.Message); }
            if (operation == "set") await _cookies.SetAsync(mutation, cancellationToken).ConfigureAwait(false);
            else await _cookies.DeleteAsync(mutation, cancellationToken).ConfigureAwait(false);
            return ApiResult.Undefined;
        }
        private static UserScriptCookiePartitionKey ReadPartitionKey(JsonElement value)
        {
            ProviderParameters.RequireObject(value, "partitionKey");
            foreach (var property in value.EnumerateObject())
                if (property.Name != "topLevelSite" && property.Name != "hasCrossSiteAncestor") throw ProviderParameters.Invalid("Unknown partitionKey property: " + property.Name);
            string site = null;
            bool? ancestor = null;
            if (value.TryGetProperty("topLevelSite", out var top))
            {
                if (top.ValueKind != JsonValueKind.String) throw ProviderParameters.Invalid("partitionKey.topLevelSite must be a string.");
                site = top.GetString();
            }
            if (value.TryGetProperty("hasCrossSiteAncestor", out var bit))
            {
                if (bit.ValueKind != JsonValueKind.True && bit.ValueKind != JsonValueKind.False) throw ProviderParameters.Invalid("partitionKey.hasCrossSiteAncestor must be a boolean.");
                ancestor = bit.GetBoolean();
            }
            try { return new UserScriptCookiePartitionKey(site, ancestor); }
            catch (ArgumentException error) { throw ProviderParameters.Invalid(error.Message); }
        }
        internal static string NormalizeSameSite(string value)
        {
            if (value == null || value == "unspecified") return "unspecified";
            if (value == "no_restriction" || value == "lax" || value == "strict") return value;
            throw ProviderParameters.Invalid("sameSite must be unspecified, no_restriction, lax or strict.");
        }
        private static void EnsureAccessible(ApiInvocationContext context, Uri url)
        {
            if (!new UserScriptMatcher().IsMatch(context.Installation.Definition.Metadata, url))
                throw new BridgeProtocolException(BridgeErrorCodes.PermissionDenied, "The script does not match the target cookie URL.");
        }
        private static object ToJson(UserScriptCookie cookie)
        {
            var value = new Dictionary<string, object> { ["name"] = cookie.Name, ["value"] = cookie.Value, ["domain"] = cookie.Domain,
                ["path"] = cookie.Path, ["secure"] = cookie.Secure, ["httpOnly"] = cookie.HttpOnly,
                ["sameSite"] = cookie.SameSite ?? "unspecified", ["hostOnly"] = cookie.HostOnly, ["session"] = cookie.Session };
            if (cookie.PartitionKey != null)
            {
                var key = new Dictionary<string, object> { ["topLevelSite"] = cookie.PartitionKey.TopLevelSite };
                if (cookie.PartitionKey.HasCrossSiteAncestor.HasValue) key["hasCrossSiteAncestor"] = cookie.PartitionKey.HasCrossSiteAncestor.Value;
                value["partitionKey"] = key;
            }
            if (cookie.ExpirationDate.HasValue) value["expirationDate"] = new DateTimeOffset(cookie.ExpirationDate.Value.ToUniversalTime()).ToUnixTimeSeconds();
            return value;
        }
    }

    /// <summary>Deterministic in-memory CRUD for non-browser hosts and tests; not browser evidence.</summary>
    public sealed class InMemoryCookieService : ICookieService, IDisposable
    {
        private readonly List<UserScriptCookie> _cookies = new List<UserScriptCookie>();
        private readonly object _sync = new object();
        /// <inheritdoc />
        public Task<IReadOnlyList<UserScriptCookie>> ListAsync(UserScriptCookieQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (query.PartitionKey != null) throw new UnsupportedApiException("The in-memory cookie backend is unpartitioned only.");
            lock (_sync) return Task.FromResult<IReadOnlyList<UserScriptCookie>>(_cookies.Where(query.Matches).ToArray());
        }
        /// <inheritdoc />
        public Task<UserScriptCookie> SetAsync(UserScriptCookieMutation mutation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (mutation.PartitionKey != null) throw new UnsupportedApiException("The in-memory cookie backend is unpartitioned only.");
            lock (_sync)
            {
                _cookies.RemoveAll(cookie => cookie.Name == mutation.Name && cookie.Domain == mutation.Domain && cookie.Path == mutation.Path);
                if (mutation.ExpirationDate <= DateTime.UtcNow) return Task.FromResult<UserScriptCookie>(null);
                var value = new UserScriptCookie(mutation.Name, mutation.Value, mutation.Domain, mutation.Path, mutation.ExpirationDate,
                    mutation.Secure, mutation.HttpOnly, mutation.SameSite, hostOnly: mutation.HostOnly);
                _cookies.Add(value); return Task.FromResult(value);
            }
        }
        /// <inheritdoc />
        public Task<bool> DeleteAsync(UserScriptCookieMutation mutation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (mutation.PartitionKey != null) throw new UnsupportedApiException("The in-memory cookie backend is unpartitioned only.");
            lock (_sync)
            {
                var query = new UserScriptCookieQuery(mutation.Url, mutation.Name, mutation.HostOnly ? null : mutation.Domain, mutation.HasExplicitPath ? mutation.Path : null);
                var selected = _cookies.Where(query.Matches).OrderByDescending(cookie => cookie.Path.Length).ThenByDescending(cookie => cookie.HostOnly).FirstOrDefault();
                return Task.FromResult(selected != null && _cookies.Remove(selected));
            }
        }
        /// <summary>Releases this instance.</summary>
        public void Dispose() { lock (_sync) _cookies.Clear(); }
    }
}
