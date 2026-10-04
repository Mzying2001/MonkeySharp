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
        public UserScriptCookieQuery(Uri url, string name = null, string domain = null, string path = null)
        {
            Url = ValidateUrl(url);
            Name = name;
            Domain = domain;
            Path = path;
        }

        /// <summary>Gets the URL whose cookie scope is queried.</summary>
        public Uri Url { get; }
        /// <summary>Gets the optional exact cookie name.</summary>
        public string Name { get; }
        /// <summary>Gets the optional case-insensitive cookie domain.</summary>
        public string Domain { get; }
        /// <summary>Gets the optional exact cookie path.</summary>
        public string Path { get; }

        /// <summary>Returns whether a cookie belongs to the query URL and optional constraints.</summary>
        public bool Matches(UserScriptCookie cookie)
        {
            if (Name != null && !string.Equals(Name, cookie.Name, StringComparison.Ordinal)) return false;
            if (Domain != null && !string.Equals(Domain, cookie.Domain, StringComparison.OrdinalIgnoreCase)) return false;
            if (Path != null && !string.Equals(Path, cookie.Path, StringComparison.Ordinal)) return false;
            if (!DomainMatches(Url.Host, cookie.Domain)) return false;
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
            return string.Equals(host, domain, StringComparison.OrdinalIgnoreCase) ||
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
            string firstPartyDomain = null, bool hostOnly = false, bool? session = null)
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
            Uri url, string name, string value = null, string domain = null, string path = "/",
            DateTime? expirationDate = null, bool secure = false, bool httpOnly = false, string sameSite = null,
            string firstPartyDomain = null, string originExecutionId = null)
        {
            Url = UserScriptCookieQuery.ValidateUrl(url);
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Cookie name is required.", nameof(name));
            Name = name;
            Value = value ?? string.Empty;
            HostOnly = string.IsNullOrEmpty(domain);
            Domain = HostOnly ? url.Host : domain.TrimStart('.');
            Path = string.IsNullOrEmpty(path) ? "/" : path;
            ExpirationDate = expirationDate;
            Secure = secure;
            HttpOnly = httpOnly;
            SameSite = sameSite;
            FirstPartyDomain = firstPartyDomain;
            OriginExecutionId = originExecutionId;
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
        /// <summary>Gets whether the mutation targets an exact host.</summary>
        public bool HostOnly { get; }
        /// <summary>Gets the execution that initiated the change, when known.</summary>
        public string OriginExecutionId { get; }
    }

    /// <summary>Describes a cookie change delivered to registered listeners.</summary>
    public sealed class UserScriptCookieChangedEventArgs : EventArgs
    {
        /// <summary>Initializes cookie change event data.</summary>
        public UserScriptCookieChangedEventArgs(UserScriptCookie cookie, string cause, bool removed,
            string originExecutionId, long sequence)
        {
            Cookie = cookie;
            Cause = cause;
            Removed = removed;
            OriginExecutionId = originExecutionId;
            Sequence = sequence;
        }

        /// <summary>Gets the cookie that changed.</summary>
        public UserScriptCookie Cookie { get; }
        /// <summary>Gets the service-defined change cause.</summary>
        public string Cause { get; }
        /// <summary>Gets whether the cookie was removed.</summary>
        public bool Removed { get; }
        /// <summary>Gets the originating execution, when known.</summary>
        public string OriginExecutionId { get; }
        /// <summary>Gets the monotonically increasing service change sequence.</summary>
        public long Sequence { get; }
    }

    /// <summary>Represents a registered cookie listener.</summary>
    public interface ICookieListenerRegistration : IDisposable
    {
    }

    /// <summary>Provides cookie CRUD and change-listener operations for one browser session.</summary>
    public interface ICookieService
    {
        /// <summary>Lists cookies visible to a query.</summary>
        Task<IReadOnlyList<UserScriptCookie>> ListAsync(UserScriptCookieQuery query, CancellationToken cancellationToken);
        /// <summary>Creates or overwrites a cookie.</summary>
        Task<UserScriptCookie> SetAsync(UserScriptCookieMutation mutation, CancellationToken cancellationToken);
        /// <summary>Deletes a cookie and returns whether one was removed.</summary>
        Task<bool> DeleteAsync(UserScriptCookieMutation mutation, CancellationToken cancellationToken);
        /// <summary>
        /// Registers a change listener. Browser adapters may only report mutations performed
        /// through this service when their underlying cookie API has no global observer.
        /// </summary>
        ICookieListenerRegistration AddListener(
            UserScriptCookieQuery query,
            EventHandler<UserScriptCookieChangedEventArgs> changed);
    }

    /// <summary>Adapts an <see cref="ICookieService"/> to modern and legacy GM cookie APIs.</summary>
    public sealed class CookieApiProvider : IUserScriptApiProvider, IUserScriptNotificationSource,
        IUserScriptExecutionObserver, IDisposable
    {
        private readonly ICookieService _cookies;
        private readonly object _sync = new object();
        private readonly Dictionary<string, ICookieListenerRegistration> _listeners =
            new Dictionary<string, ICookieListenerRegistration>(StringComparer.Ordinal);
        private readonly HashSet<string> _pendingListeners = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _endedPendingListeners = new HashSet<string>(StringComparer.Ordinal);
        private bool _disposed;

        /// <summary>Initializes a provider over an application-owned cookie service.</summary>
        public CookieApiProvider(ICookieService cookies)
        {
            _cookies = cookies ?? throw new ArgumentNullException(nameof(cookies));
        }

        /// <inheritdoc />
        public event EventHandler<ApiNotificationEventArgs> Notification;
        /// <inheritdoc />
        public IReadOnlyCollection<string> Methods { get; } = new ReadOnlyCollection<string>(new[]
        {
            "GM.cookie"
        });

        /// <inheritdoc />
        public async Task<ApiResult> InvokeAsync(ApiInvocationContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync) ThrowIfDisposed();
            switch (context.Method)
            {
                case "GM.cookie":
                    return await InvokeCookieAsync(context, cancellationToken).ConfigureAwait(false);
                default: throw new UnsupportedApiException("The API '" + context.Method + "' is not supported.");
            }
        }

        /// <inheritdoc />
        public void OnExecutionEnded(string executionId)
        {
            ICookieListenerRegistration[] registrations;
            var prefix = executionId + ":";
            lock (_sync)
            {
                var keys = _listeners.Keys
                    .Where(item => item.StartsWith(prefix, StringComparison.Ordinal))
                    .ToArray();
                registrations = keys.Select(item => _listeners[item]).ToArray();
                foreach (var key in keys) _listeners.Remove(key);
                foreach (var key in _pendingListeners.Where(item =>
                    item.StartsWith(prefix, StringComparison.Ordinal)))
                    _endedPendingListeners.Add(key);
            }
            foreach (var registration in registrations) registration.Dispose();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            ICookieListenerRegistration[] registrations;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                registrations = _listeners.Values.ToArray();
                _listeners.Clear();
                _endedPendingListeners.Clear();
            }
            foreach (var registration in registrations) registration.Dispose();
        }

        private async Task<ApiResult> InvokeCookieAsync(ApiInvocationContext context, CancellationToken cancellationToken)
        {
            ProviderParameters.RequireObject(context.Parameters);
            var operation = ProviderParameters.OptionalString(context.Parameters, "operation") ?? "list";
            var details = context.Parameters.TryGetProperty("details", out var detailsValue)
                ? detailsValue : context.Parameters;
            if (operation == "list")
            {
                var query = ReadQuery(details, context.Frame.Url);
                EnsureAccessible(context, query.Url);
                var cookies = await _cookies.ListAsync(query, cancellationToken).ConfigureAwait(false);
                return ApiResult.FromValue(cookies.Select(ToJson).ToArray());
            }
            if (operation == "set")
            {
                var mutation = ReadMutation(details, context.Frame.Url, context.ExecutionId);
                EnsureAccessible(context, mutation.Url);
                var cookie = await _cookies.SetAsync(mutation, cancellationToken).ConfigureAwait(false);
                return ApiResult.FromValue(ToJson(cookie));
            }
            if (operation == "delete")
            {
                var mutation = ReadMutation(details, context.Frame.Url, context.ExecutionId);
                EnsureAccessible(context, mutation.Url);
                return ApiResult.FromValue(await _cookies.DeleteAsync(mutation, cancellationToken).ConfigureAwait(false));
            }
            if (operation == "addListener")
            {
                var query = ReadQuery(details, context.Frame.Url);
                EnsureAccessible(context, query.Url);
                var listenerId = ProviderParameters.RequiredInt32(context.Parameters, "listenerId");
                var key = context.ExecutionId + ":" + listenerId;
                RegisterListener(key, () => _cookies.AddListener(query, (_, change) => Notification?.Invoke(this,
                    new ApiNotificationEventArgs(context.Installation.ScriptKey, context.ExecutionId, "cookie-change",
                        JsonSerializer.Serialize(new
                        {
                            listenerId,
                            cookie = ToJson(change.Cookie),
                            cause = change.Cause,
                            removed = change.Removed,
                            originExecutionId = change.OriginExecutionId,
                            sequence = change.Sequence
                        })))));
                return ApiResult.FromValue(listenerId);
            }
            if (operation == "removeListener")
            {
                var listenerId = ProviderParameters.RequiredInt32(context.Parameters, "listenerId");
                var key = context.ExecutionId + ":" + listenerId;
                ICookieListenerRegistration registration;
                lock (_sync)
                {
                    ThrowIfDisposed();
                    if (!_listeners.TryGetValue(key, out registration)) return ApiResult.FromValue(false);
                    _listeners.Remove(key);
                }
                registration.Dispose();
                return ApiResult.FromValue(true);
            }
            throw ProviderParameters.Invalid("Unknown cookie operation.");
        }

        private void RegisterListener(string key, Func<ICookieListenerRegistration> factory)
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                if (_listeners.ContainsKey(key) || !_pendingListeners.Add(key))
                    throw ProviderParameters.Invalid("listenerId is already registered.");
            }
            ICookieListenerRegistration registration = null;
            try
            {
                registration = factory();
                if (registration == null)
                    throw new InvalidOperationException("The cookie service returned no listener registration.");
                bool disposed;
                bool executionEnded;
                lock (_sync)
                {
                    _pendingListeners.Remove(key);
                    disposed = _disposed;
                    executionEnded = _endedPendingListeners.Remove(key);
                    if (!disposed && !executionEnded)
                        _listeners.Add(key, registration);
                }
                if (disposed || executionEnded)
                {
                    registration.Dispose();
                    registration = null;
                    if (disposed) throw new ObjectDisposedException(nameof(CookieApiProvider));
                    throw new OperationCanceledException(
                        "The script execution ended while registering a cookie listener.");
                }
            }
            catch
            {
                lock (_sync)
                {
                    _pendingListeners.Remove(key);
                    _endedPendingListeners.Remove(key);
                }
                registration?.Dispose();
                throw;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CookieApiProvider));
        }

        private static UserScriptCookieQuery ReadQuery(JsonElement details, Uri defaultUrl)
        {
            ProviderParameters.RequireObject(details);
            var urlText = ProviderParameters.OptionalString(details, "url") ?? defaultUrl?.AbsoluteUri;
            if (!Uri.TryCreate(urlText, UriKind.Absolute, out var url) ||
                (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
                throw ProviderParameters.Invalid("url must be an absolute HTTP or HTTPS URL.");
            try
            {
                return new UserScriptCookieQuery(url,
                    ProviderParameters.OptionalString(details, "name"),
                    ProviderParameters.OptionalString(details, "domain"),
                    ProviderParameters.OptionalString(details, "path"));
            }
            catch (ArgumentException exception)
            {
                throw ProviderParameters.Invalid(exception.Message);
            }
        }

        private static UserScriptCookieMutation ReadMutation(JsonElement details, Uri defaultUrl, string originExecutionId)
        {
            var query = ReadQuery(details, defaultUrl);
            var value = ProviderParameters.OptionalString(details, "value");
            var expiration = ReadExpiration(details);
            return new UserScriptCookieMutation(query.Url, query.Name,
                value, OptionalDomain(details), query.Path, expiration,
                ProviderParameters.OptionalBoolean(details, "secure"),
                ProviderParameters.OptionalBoolean(details, "httpOnly"),
                ProviderParameters.OptionalString(details, "sameSite"),
                ProviderParameters.OptionalString(details, "firstPartyDomain"), originExecutionId);
        }

        private static string OptionalDomain(JsonElement details)
        {
            return details.TryGetProperty("domain", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static DateTime? ReadExpiration(JsonElement details)
        {
            if (!details.TryGetProperty("expirationDate", out var value))
                return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var seconds))
            {
                if (double.IsNaN(seconds) || double.IsInfinity(seconds) ||
                    seconds != Math.Truncate(seconds) ||
                    seconds < DateTimeOffset.MinValue.ToUnixTimeSeconds() ||
                    seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
                    throw ProviderParameters.Invalid("expirationDate must be an integral Unix timestamp in range.");
                return DateTimeOffset.FromUnixTimeSeconds((long)seconds).UtcDateTime;
            }
            if (value.ValueKind == JsonValueKind.String && DateTime.TryParse(value.GetString(), out var date))
                return date.ToUniversalTime();
            throw ProviderParameters.Invalid("expirationDate must be Unix seconds or an ISO date.");
        }

        private static void EnsureAccessible(ApiInvocationContext context, Uri url)
        {
            if (!new UserScriptMatcher().IsMatch(context.Installation.Definition.Metadata, url))
                throw new BridgeProtocolException(BridgeErrorCodes.PermissionDenied,
                    "The cookie URL is not covered by the script's @match or @include rules.");
        }

        private static object ToJson(UserScriptCookie cookie)
        {
            return cookie == null ? null : new
            {
                name = cookie.Name, value = cookie.Value, domain = cookie.Domain, path = cookie.Path,
                expirationDate = cookie.ExpirationDate.HasValue ? new DateTimeOffset(cookie.ExpirationDate.Value).ToUnixTimeSeconds() : (long?)null,
                firstPartyDomain = cookie.FirstPartyDomain, hostOnly = cookie.HostOnly, session = cookie.Session,
                secure = cookie.Secure, httpOnly = cookie.HttpOnly, sameSite = cookie.SameSite
            };
        }
    }

    /// <summary>Thread-safe in-memory cookie service suitable for tests and hosts without a browser adapter.</summary>
    public sealed class InMemoryCookieService : ICookieService, IDisposable
    {
        private readonly object _sync = new object();
        private readonly List<UserScriptCookie> _cookies = new List<UserScriptCookie>();
        private readonly Dictionary<Guid, Listener> _listeners = new Dictionary<Guid, Listener>();
        private long _sequence;
        private bool _disposed;

        /// <inheritdoc />
        public Task<IReadOnlyList<UserScriptCookie>> ListAsync(UserScriptCookieQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                ThrowIfDisposed();
                return Task.FromResult<IReadOnlyList<UserScriptCookie>>(
                    new ReadOnlyCollection<UserScriptCookie>(_cookies.Where(query.Matches).ToList()));
            }
        }

        /// <inheritdoc />
        public Task<UserScriptCookie> SetAsync(UserScriptCookieMutation mutation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UserScriptCookie cookie;
            UserScriptCookie old;
            lock (_sync)
            {
                ThrowIfDisposed();
                old = _cookies.FirstOrDefault(item => item.Name == mutation.Name && item.Domain == mutation.Domain && item.Path == mutation.Path);
                if (old != null) _cookies.Remove(old);
                cookie = new UserScriptCookie(mutation.Name, mutation.Value, mutation.Domain, mutation.Path,
                    mutation.ExpirationDate, mutation.Secure, mutation.HttpOnly, mutation.SameSite,
                    mutation.FirstPartyDomain, mutation.HostOnly);
                _cookies.Add(cookie);
            }
            Publish(new UserScriptCookieChangedEventArgs(cookie, old == null ? "explicit" : "overwrite", false,
                mutation.OriginExecutionId, Interlocked.Increment(ref _sequence)));
            return Task.FromResult(cookie);
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(UserScriptCookieMutation mutation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UserScriptCookie old = null;
            lock (_sync)
            {
                ThrowIfDisposed();
                old = _cookies.FirstOrDefault(item => item.Name == mutation.Name && item.Domain == mutation.Domain && item.Path == mutation.Path);
                if (old != null) _cookies.Remove(old);
            }
            if (old != null) Publish(new UserScriptCookieChangedEventArgs(old, "explicit", true,
                mutation.OriginExecutionId, Interlocked.Increment(ref _sequence)));
            return Task.FromResult(old != null);
        }

        /// <inheritdoc />
        public ICookieListenerRegistration AddListener(UserScriptCookieQuery query, EventHandler<UserScriptCookieChangedEventArgs> changed)
        {
            if (changed == null) throw new ArgumentNullException(nameof(changed));
            lock (_sync)
            {
                ThrowIfDisposed();
                var id = Guid.NewGuid();
                _listeners.Add(id, new Listener(id, query, changed, this));
                return _listeners[id];
            }
        }

        private void Publish(UserScriptCookieChangedEventArgs change)
        {
            Listener[] listeners;
            lock (_sync) listeners = _listeners.Values.Where(item => item.Query.Matches(change.Cookie)).ToArray();
            foreach (var listener in listeners) listener.Callback(this, change);
        }

        private void Remove(Guid id) { lock (_sync) _listeners.Remove(id); }
        private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(InMemoryCookieService)); }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (_sync) { _listeners.Clear(); _cookies.Clear(); _disposed = true; }
        }

        private sealed class Listener : ICookieListenerRegistration
        {
            private readonly InMemoryCookieService _owner;
            public Listener(Guid id, UserScriptCookieQuery query, EventHandler<UserScriptCookieChangedEventArgs> callback, InMemoryCookieService owner)
            { Id = id; Query = query; Callback = callback; _owner = owner; }
            public Guid Id { get; }
            public UserScriptCookieQuery Query { get; }
            public EventHandler<UserScriptCookieChangedEventArgs> Callback { get; }
            public void Dispose() { _owner.Remove(Id); }
        }
    }
}
