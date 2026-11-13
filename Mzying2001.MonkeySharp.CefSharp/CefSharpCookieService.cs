using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.CefSharp
{
    /// <summary>Cookie CRUD through the attached browser's shared CDP session and actual RequestContext.</summary>
    public sealed class CefSharpCookieService : IPartitionedCookieService, IDisposable
    {
        private readonly Func<CancellationToken, Task<CefSharpCdpSession>> _session;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly SemaphoreSlim _mutations = new SemaphoreSlim(1, 1);
        private int _disposed;
        private readonly object _protocolLock = new object();
        private Task<CookieProtocolProfile> _protocol;

        /// <inheritdoc />
        public async Task<UserScriptCookiePartitionKey> NormalizePartitionKeyAsync(UserScriptCookiePartitionKey partitionKey,
            Uri cookieUrl, CookiePartitionOperation operation, CancellationToken cancellationToken)
        {
            if (partitionKey == null) return null;
            var protocol = await ProtocolAsync(cancellationToken).ConfigureAwait(false);
            var major = protocol.Major;
            if (!protocol.SupportsPartitions) throw new UnsupportedApiException("Partitioned Cookie CRUD requires Chromium 114 or later; this engine is " + major + ".");
            if (!protocol.SupportsAncestors && partitionKey.HasCrossSiteAncestor.HasValue) throw new UnsupportedApiException("hasCrossSiteAncestor requires Chromium 128 or later; this engine is " + major + ".");
            if (string.IsNullOrEmpty(partitionKey.TopLevelSite)) return operation == CookiePartitionOperation.List ? partitionKey : null;
            var site = SchemefulSite.FromUrl(new Uri(partitionKey.TopLevelSite), major);
            var ancestor = partitionKey.HasCrossSiteAncestor;
            if (protocol.SupportsAncestors && operation == CookiePartitionOperation.Set)
            {
                var crossSite = SchemefulSite.FromUrl(cookieUrl, major) != site;
                if (ancestor == false && crossSite) throw new ArgumentException("hasCrossSiteAncestor cannot be false for a cross-site cookie URL.");
                ancestor = ancestor ?? crossSite;
            }
            else if (protocol.SupportsAncestors && operation == CookiePartitionOperation.Delete) ancestor = ancestor ?? true;
            return new UserScriptCookiePartitionKey(site, ancestor);
        }
        private async Task<CookieProtocolProfile> ProtocolAsync(CancellationToken token)
        {
            Task<CookieProtocolProfile> protocol;
            lock (_protocolLock)
            {
                if (_protocol == null || _protocol.IsCanceled || _protocol.IsFaulted) _protocol = ReadProtocolAsync();
                protocol = _protocol;
            }
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => canceled.TrySetCanceled())) await await Task.WhenAny(protocol, canceled.Task).ConfigureAwait(false);
            return await protocol.ConfigureAwait(false);
        }
        private async Task<CookieProtocolProfile> ReadProtocolAsync()
        {
            var session = await _session(_lifetime.Token).ConfigureAwait(false);
            var version = await session.ExecuteAsync("Browser.getVersion", cancellationToken: _lifetime.Token).ConfigureAwait(false);
            var product = version.GetProperty("product").GetString();
            var number = product.Substring(product.LastIndexOf('/') + 1).Split('.')[0];
            if (!int.TryParse(number, out var major) || major <= 0) throw new InvalidOperationException("The native engine did not report a usable version.");
            return new CookieProtocolProfile(major);
        }

        /// <summary>Uses the host's attached browser session. This service never owns/disposes the shared session.</summary>
        public CefSharpCookieService(CefSharpUserScriptHost host)
            : this(host == null ? throw new ArgumentNullException(nameof(host)) : new Func<CancellationToken, Task<CefSharpCdpSession>>(host.GetCdpSessionAsync)) { }

        internal CefSharpCookieService(Func<CancellationToken, Task<CefSharpCdpSession>> session)
        { _session = session ?? throw new ArgumentNullException(nameof(session)); }

        /// <inheritdoc />
        public async Task<IReadOnlyList<UserScriptCookie>> ListAsync(UserScriptCookieQuery query, CancellationToken cancellationToken)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            using (var linked = Link(cancellationToken))
            {
                var key = await NormalizePartitionKeyAsync(query.PartitionKey, query.Url, CookiePartitionOperation.List, linked.Token).ConfigureAwait(false);
                var normalized = new UserScriptCookieQuery(query.Url, query.Name, query.Domain, query.Path, key);
                var session = await _session(linked.Token).ConfigureAwait(false);
                var result = query.PartitionKey == null
                    ? await session.ExecuteAsync("Network.getCookies", new { urls = new[] { query.Url.AbsoluteUri } }, linked.Token).ConfigureAwait(false)
                    : await session.ExecuteAsync("Network.getAllCookies", cancellationToken: linked.Token).ConfigureAwait(false);
                var protocol = query.PartitionKey == null ? null : await ProtocolAsync(linked.Token).ConfigureAwait(false);
                return Cookies(result, protocol).Where(normalized.Matches).ToArray();
            }
        }

        /// <inheritdoc />
        public async Task<UserScriptCookie> SetAsync(UserScriptCookieMutation mutation, CancellationToken cancellationToken)
        {
            Validate(mutation);
            using (var linked = Link(cancellationToken))
            {
                mutation = WithPartition(mutation, await NormalizePartitionKeyAsync(mutation.PartitionKey, mutation.Url, CookiePartitionOperation.Set, linked.Token).ConfigureAwait(false));
                var protocol = mutation.PartitionKey == null ? null : await ProtocolAsync(linked.Token).ConfigureAwait(false);
                if (mutation.PartitionKey != null && !mutation.Secure)
                    throw new ArgumentException("Partitioned cookies require secure:true.");
                await _mutations.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    var session = await _session(linked.Token).ConfigureAwait(false);
                    var parameters = await ParametersAsync(mutation, linked.Token).ConfigureAwait(false);
                    var sameSite = ToProtocolSameSite(mutation.SameSite);
                    var result = await session.ExecuteAsync("Network.setCookie", parameters, linked.Token).ConfigureAwait(false);
                    if (result.TryGetProperty("success", out var accepted) && !accepted.GetBoolean())
                        throw new InvalidOperationException("The browser rejected the cookie.");
                    // Protocol success is insufficient: Chromium may silently reject policy-invalid cookies.
                    var actual = (await AllAsync(session, linked.Token, protocol).ConfigureAwait(false)).SingleOrDefault(cookie => Identity(cookie, mutation));
                    if (mutation.ExpirationDate <= DateTime.UtcNow)
                    {
                        if (actual != null) throw new InvalidOperationException("The browser retained an expired cookie.");
                        return null;
                    }
                    if (actual == null || actual.Value != mutation.Value || actual.Secure != mutation.Secure || actual.HttpOnly != mutation.HttpOnly ||
                        actual.Session != !mutation.ExpirationDate.HasValue ||
                        (mutation.HostOnly && !actual.HostOnly) ||
                        (!IPAddress.TryParse(mutation.Url.Host, out _) && !mutation.HostOnly && actual.HostOnly) ||
                        (sameSite != null && actual.SameSite != FromProtocolSameSite(sameSite)) ||
                        (mutation.ExpirationDate.HasValue && Math.Abs((actual.ExpirationDate.Value - mutation.ExpirationDate.Value.ToUniversalTime()).TotalSeconds) > 1))
                        throw new InvalidOperationException("The browser did not retain the requested cookie state.");
                    return actual;
                }
                finally { _mutations.Release(); }
            }
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(UserScriptCookieMutation mutation, CancellationToken cancellationToken)
        {
            Validate(mutation);
            using (var linked = Link(cancellationToken))
            {
                mutation = WithPartition(mutation, await NormalizePartitionKeyAsync(mutation.PartitionKey, mutation.Url, CookiePartitionOperation.Delete, linked.Token).ConfigureAwait(false));
                var protocol = mutation.PartitionKey == null ? null : await ProtocolAsync(linked.Token).ConfigureAwait(false);
                await _mutations.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    var session = await _session(linked.Token).ConfigureAwait(false);
                    var query = new UserScriptCookieQuery(mutation.Url, mutation.Name, mutation.HostOnly ? null : mutation.Domain,
                        mutation.HasExplicitPath ? mutation.Path : null, mutation.PartitionKey);
                    var selected = (await AllAsync(session, linked.Token, protocol).ConfigureAwait(false)).Where(query.Matches)
                        .OrderByDescending(cookie => cookie.Path.Length).ThenByDescending(cookie => cookie.HostOnly).FirstOrDefault();
                    if (selected == null) return false;
                    // Older deleteCookies commands cannot select a partition; expiration targets one
                    // complete canonical identity on every supported engine without deleting siblings.
                    var expired = new UserScriptCookieMutation(mutation.Url, selected.Name, string.Empty,
                        selected.HostOnly ? null : selected.Domain, selected.Path, new DateTime(1970, 1, 1, 0, 0, 1, DateTimeKind.Utc),
                        selected.Secure, selected.HttpOnly, selected.SameSite, partitionKey: selected.PartitionKey);
                    var result = await session.ExecuteAsync("Network.setCookie", await ParametersAsync(expired, linked.Token).ConfigureAwait(false), linked.Token).ConfigureAwait(false);
                    if (result.TryGetProperty("success", out var accepted) && !accepted.GetBoolean()) throw new InvalidOperationException("The browser rejected the selected cookie expiration.");
                    if ((await AllAsync(session, linked.Token, protocol).ConfigureAwait(false)).Any(cookie =>
                        cookie.Name == selected.Name && cookie.Domain == selected.Domain && cookie.Path == selected.Path &&
                        UserScriptCookiePartitionKey.SameIdentity(cookie.PartitionKey, selected.PartitionKey)))
                        throw new InvalidOperationException("The browser did not remove the selected cookie.");
                    return true;
                }
                finally { _mutations.Release(); }
            }
        }

        private static bool Identity(UserScriptCookie cookie, UserScriptCookieMutation mutation) => cookie.Name == mutation.Name &&
            cookie.Path == mutation.Path && string.Equals(cookie.Domain.TrimStart('.'), mutation.Domain.TrimStart('.'), StringComparison.OrdinalIgnoreCase) &&
            UserScriptCookiePartitionKey.SameIdentity(cookie.PartitionKey, mutation.PartitionKey) &&
            (mutation.HostOnly ? cookie.HostOnly : IPAddress.TryParse(mutation.Url.Host, out _) || !cookie.HostOnly);

        private static UserScriptCookieMutation WithPartition(UserScriptCookieMutation mutation, UserScriptCookiePartitionKey key) =>
            new UserScriptCookieMutation(mutation.Url, mutation.Name, mutation.Value, mutation.HostOnly ? null : mutation.Domain,
                mutation.HasExplicitPath ? mutation.Path : null, mutation.ExpirationDate, mutation.Secure, mutation.HttpOnly,
                mutation.SameSite, mutation.FirstPartyDomain, key);

        private async Task<Dictionary<string, object>> ParametersAsync(UserScriptCookieMutation mutation, CancellationToken token)
        {
            var parameters = new Dictionary<string, object> { ["url"] = mutation.Url.AbsoluteUri,
                ["name"] = mutation.Name, ["value"] = mutation.Value, ["path"] = mutation.Path,
                ["secure"] = mutation.Secure, ["httpOnly"] = mutation.HttpOnly };
            if (!mutation.HostOnly) parameters["domain"] = mutation.Domain;
            if (mutation.ExpirationDate.HasValue) parameters["expires"] = new DateTimeOffset(mutation.ExpirationDate.Value.ToUniversalTime()).ToUnixTimeSeconds();
            var sameSite = ToProtocolSameSite(mutation.SameSite);
            if (sameSite != null) parameters["sameSite"] = sameSite;
            if (mutation.PartitionKey != null)
            {
                var protocol = await ProtocolAsync(token).ConfigureAwait(false);
                parameters["partitionKey"] = !protocol.UsesObjectKey ? (object)mutation.PartitionKey.TopLevelSite :
                    new { topLevelSite = mutation.PartitionKey.TopLevelSite, hasCrossSiteAncestor = mutation.PartitionKey.HasCrossSiteAncestor ?? true };
            }
            return parameters;
        }

        private static async Task<IReadOnlyList<UserScriptCookie>> AllAsync(CefSharpCdpSession session, CancellationToken token, CookieProtocolProfile protocol)
            => Cookies(await session.ExecuteAsync("Network.getAllCookies", cancellationToken: token).ConfigureAwait(false), protocol).ToArray();

        private static IEnumerable<UserScriptCookie> Cookies(JsonElement result, CookieProtocolProfile protocol)
        {
            foreach (var item in result.GetProperty("cookies").EnumerateArray())
            {
                if (item.TryGetProperty("partitionKeyOpaque", out var opaque) && opaque.GetBoolean()) continue;
                UserScriptCookiePartitionKey partitionKey = null;
                if (item.TryGetProperty("partitionKey", out var key))
                {
                    if (key.ValueKind == JsonValueKind.String) partitionKey = new UserScriptCookiePartitionKey(key.GetString());
                    else if (key.ValueKind == JsonValueKind.Object)
                    {
                        var ancestor = key.GetProperty("hasCrossSiteAncestor").GetBoolean();
                        partitionKey = new UserScriptCookiePartitionKey(key.GetProperty("topLevelSite").GetString(),
                            protocol != null && !protocol.SupportsAncestors ? (bool?)null : ancestor);
                    }
                    else throw new InvalidOperationException("The browser returned an unsupported cookie partition key shape.");
                }
                var domain = item.GetProperty("domain").GetString();
                var session = item.GetProperty("session").GetBoolean();
                DateTime? expiration = session ? (DateTime?)null : DateTimeOffset.FromUnixTimeSeconds((long)item.GetProperty("expires").GetDouble()).UtcDateTime;
                yield return new UserScriptCookie(item.GetProperty("name").GetString(), item.GetProperty("value").GetString(), domain,
                    item.GetProperty("path").GetString(), expiration, item.GetProperty("secure").GetBoolean(), item.GetProperty("httpOnly").GetBoolean(),
                    item.TryGetProperty("sameSite", out var sameSite) ? FromProtocolSameSite(sameSite.GetString()) : "unspecified",
                    hostOnly: !domain.StartsWith(".", StringComparison.Ordinal), session: session, partitionKey: partitionKey);
            }
        }
        private static string ToProtocolSameSite(string value)
        {
            switch (value)
            {
                case null: case "unspecified": return null;
                case "no_restriction": return "None";
                case "lax": return "Lax";
                case "strict": return "Strict";
                default: throw new ArgumentException("Unknown SameSite mode.");
            }
        }
        private static string FromProtocolSameSite(string value)
        {
            switch (value) { case "None": return "no_restriction"; case "Lax": return "lax"; case "Strict": return "strict"; default: return "unspecified"; }
        }
        private static void Validate(UserScriptCookieMutation mutation)
        {
            if (mutation == null) throw new ArgumentNullException(nameof(mutation));
            if (mutation.FirstPartyDomain != null) throw new UnsupportedApiException("firstPartyDomain is not supported by this CEF backend.");
            ToProtocolSameSite(mutation.SameSite);
        }
        private CancellationTokenSource Link(CancellationToken token)
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(CefSharpCookieService));
            return CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        }
        /// <summary>Cancels this service's calls without detaching the shared protocol observer.</summary>
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) _lifetime.Cancel(); }
    }
}
