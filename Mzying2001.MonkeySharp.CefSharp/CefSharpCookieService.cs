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
    public sealed class CefSharpCookieService : ICookieService, IDisposable
    {
        private readonly Func<CancellationToken, Task<CefSharpCdpSession>> _session;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly SemaphoreSlim _mutations = new SemaphoreSlim(1, 1);
        private int _disposed;

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
                var session = await _session(linked.Token).ConfigureAwait(false);
                var result = await session.ExecuteAsync("Network.getCookies", new { urls = new[] { query.Url.AbsoluteUri } }, linked.Token).ConfigureAwait(false);
                return Cookies(result).Where(query.Matches).ToArray();
            }
        }

        /// <inheritdoc />
        public async Task<UserScriptCookie> SetAsync(UserScriptCookieMutation mutation, CancellationToken cancellationToken)
        {
            Validate(mutation);
            using (var linked = Link(cancellationToken))
            {
                await _mutations.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    var session = await _session(linked.Token).ConfigureAwait(false);
                    var parameters = new Dictionary<string, object> { ["url"] = mutation.Url.AbsoluteUri,
                        ["name"] = mutation.Name, ["value"] = mutation.Value, ["path"] = mutation.Path,
                        ["secure"] = mutation.Secure, ["httpOnly"] = mutation.HttpOnly };
                    if (!mutation.HostOnly) parameters["domain"] = mutation.Domain;
                    if (mutation.ExpirationDate.HasValue) parameters["expires"] = new DateTimeOffset(mutation.ExpirationDate.Value.ToUniversalTime()).ToUnixTimeSeconds();
                    var sameSite = ToProtocolSameSite(mutation.SameSite);
                    if (sameSite != null) parameters["sameSite"] = sameSite;
                    var result = await session.ExecuteAsync("Network.setCookie", parameters, linked.Token).ConfigureAwait(false);
                    if (result.TryGetProperty("success", out var accepted) && !accepted.GetBoolean())
                        throw new InvalidOperationException("The browser rejected the cookie.");
                    // Protocol success is insufficient: Chromium may silently reject policy-invalid cookies.
                    var actual = (await AllAsync(session, linked.Token).ConfigureAwait(false)).SingleOrDefault(cookie => Identity(cookie, mutation));
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
                await _mutations.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    var session = await _session(linked.Token).ConfigureAwait(false);
                    var query = new UserScriptCookieQuery(mutation.Url, mutation.Name, mutation.HostOnly ? null : mutation.Domain,
                        mutation.HasExplicitPath ? mutation.Path : null);
                    var selected = (await AllAsync(session, linked.Token).ConfigureAwait(false)).Where(query.Matches)
                        .OrderByDescending(cookie => cookie.Path.Length).ThenByDescending(cookie => cookie.HostOnly).FirstOrDefault();
                    if (selected == null) return false;
                    await session.ExecuteAsync("Network.deleteCookies", new { name = selected.Name, domain = selected.Domain, path = selected.Path }, linked.Token).ConfigureAwait(false);
                    if ((await AllAsync(session, linked.Token).ConfigureAwait(false)).Any(cookie =>
                        cookie.Name == selected.Name && cookie.Domain == selected.Domain && cookie.Path == selected.Path))
                        throw new InvalidOperationException("The browser did not remove the selected cookie.");
                    return true;
                }
                finally { _mutations.Release(); }
            }
        }

        private static bool Identity(UserScriptCookie cookie, UserScriptCookieMutation mutation) => cookie.Name == mutation.Name &&
            cookie.Path == mutation.Path && string.Equals(cookie.Domain.TrimStart('.'), mutation.Domain.TrimStart('.'), StringComparison.OrdinalIgnoreCase) &&
            (mutation.HostOnly ? cookie.HostOnly : IPAddress.TryParse(mutation.Url.Host, out _) || !cookie.HostOnly);

        private static async Task<IReadOnlyList<UserScriptCookie>> AllAsync(CefSharpCdpSession session, CancellationToken token)
            => Cookies(await session.ExecuteAsync("Network.getAllCookies", cancellationToken: token).ConfigureAwait(false)).ToArray();

        private static IEnumerable<UserScriptCookie> Cookies(JsonElement result)
        {
            foreach (var item in result.GetProperty("cookies").EnumerateArray())
            {
                // Partitioned CRUD is a separate version-gated enhancement. Never mutate one accidentally.
                if (item.TryGetProperty("partitionKey", out _) ||
                    (item.TryGetProperty("partitionKeyOpaque", out var opaque) && opaque.GetBoolean())) continue;
                var domain = item.GetProperty("domain").GetString();
                var session = item.GetProperty("session").GetBoolean();
                DateTime? expiration = session ? (DateTime?)null : DateTimeOffset.FromUnixTimeSeconds((long)item.GetProperty("expires").GetDouble()).UtcDateTime;
                yield return new UserScriptCookie(item.GetProperty("name").GetString(), item.GetProperty("value").GetString(), domain,
                    item.GetProperty("path").GetString(), expiration, item.GetProperty("secure").GetBoolean(), item.GetProperty("httpOnly").GetBoolean(),
                    item.TryGetProperty("sameSite", out var sameSite) ? FromProtocolSameSite(sameSite.GetString()) : "unspecified",
                    hostOnly: !domain.StartsWith(".", StringComparison.Ordinal), session: session);
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
