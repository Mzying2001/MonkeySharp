using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CS1591

namespace Mzying2001.MonkeySharp.Core.Apis
{
    /// <summary>Describes the scope used to query userscript cookies.</summary>
    public sealed class UserScriptCookieQuery
    {
        public UserScriptCookieQuery(Uri url, string name = null, string domain = null, string path = null)
        {
            Url = ValidateUrl(url);
            Name = name;
            Domain = domain;
            Path = path;
        }

        public Uri Url { get; }
        public string Name { get; }
        public string Domain { get; }
        public string Path { get; }

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
        public UserScriptCookie(
            string name, string value, string domain, string path,
            DateTime? expiration = null, bool secure = false, bool httpOnly = false, string sameSite = null)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Cookie name is required.", nameof(name));
            Name = name;
            Value = value ?? string.Empty;
            Domain = string.IsNullOrEmpty(domain) ? throw new ArgumentException("Cookie domain is required.", nameof(domain)) : domain;
            Path = string.IsNullOrEmpty(path) ? "/" : path;
            Expiration = expiration;
            Secure = secure;
            HttpOnly = httpOnly;
            SameSite = sameSite;
        }

        public string Name { get; }
        public string Value { get; }
        public string Domain { get; }
        public string Path { get; }
        public DateTime? Expiration { get; }
        public bool Secure { get; }
        public bool HttpOnly { get; }
        public string SameSite { get; }
    }

    /// <summary>Describes a cookie mutation requested by a userscript.</summary>
    public sealed class UserScriptCookieMutation
    {
        public UserScriptCookieMutation(
            Uri url, string name, string value = null, string domain = null, string path = "/",
            DateTime? expiration = null, bool secure = false, string sameSite = null, string originExecutionId = null)
        {
            Url = UserScriptCookieQuery.ValidateUrl(url);
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Cookie name is required.", nameof(name));
            Name = name;
            Value = value ?? string.Empty;
            Domain = string.IsNullOrEmpty(domain) ? url.Host : domain.TrimStart('.');
            Path = string.IsNullOrEmpty(path) ? "/" : path;
            Expiration = expiration;
            Secure = secure;
            SameSite = sameSite;
            OriginExecutionId = originExecutionId;
        }

        public Uri Url { get; }
        public string Name { get; }
        public string Value { get; }
        public string Domain { get; }
        public string Path { get; }
        public DateTime? Expiration { get; }
        public bool Secure { get; }
        public string SameSite { get; }
        public string OriginExecutionId { get; }
    }

    /// <summary>Describes a cookie change delivered to registered listeners.</summary>
    public sealed class UserScriptCookieChangedEventArgs : EventArgs
    {
        public UserScriptCookieChangedEventArgs(UserScriptCookie cookie, string cause, bool removed,
            string originExecutionId, long sequence)
        {
            Cookie = cookie;
            Cause = cause;
            Removed = removed;
            OriginExecutionId = originExecutionId;
            Sequence = sequence;
        }

        public UserScriptCookie Cookie { get; }
        public string Cause { get; }
        public bool Removed { get; }
        public string OriginExecutionId { get; }
        public long Sequence { get; }
    }

    /// <summary>Represents a registered cookie listener.</summary>
    public interface ICookieListenerRegistration : IDisposable
    {
    }

    /// <summary>Provides cookie CRUD and change-listener operations for one browser session.</summary>
    public interface ICookieService
    {
        Task<IReadOnlyList<UserScriptCookie>> ListAsync(UserScriptCookieQuery query, CancellationToken cancellationToken);
        Task<UserScriptCookie> SetAsync(UserScriptCookieMutation mutation, CancellationToken cancellationToken);
        Task<bool> DeleteAsync(UserScriptCookieMutation mutation, CancellationToken cancellationToken);
        ICookieListenerRegistration AddListener(
            UserScriptCookieQuery query,
            EventHandler<UserScriptCookieChangedEventArgs> changed);
    }

    /// <summary>Adapts an <see cref="ICookieService"/> to modern and legacy GM cookie APIs.</summary>
    public sealed class CookieApiProvider : IUserScriptApiProvider, IUserScriptNotificationSource,
        IUserScriptExecutionObserver, IDisposable
    {
        private readonly ICookieService _cookies;
        private readonly Dictionary<string, ICookieListenerRegistration> _listeners =
            new Dictionary<string, ICookieListenerRegistration>(StringComparer.Ordinal);
        private bool _disposed;

        public CookieApiProvider(ICookieService cookies)
        {
            _cookies = cookies ?? throw new ArgumentNullException(nameof(cookies));
        }

        public event EventHandler<ApiNotificationEventArgs> Notification;
        public IReadOnlyCollection<string> Methods { get; } = new ReadOnlyCollection<string>(new[]
        {
            "GM.cookie"
        });

        public async Task<ApiResult> InvokeAsync(ApiInvocationContext context, CancellationToken cancellationToken)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CookieApiProvider));
            switch (context.Method)
            {
                case "GM.cookie":
                    return await InvokeCookieAsync(context, cancellationToken).ConfigureAwait(false);
                default: throw new UnsupportedApiException("The API '" + context.Method + "' is not supported.");
            }
        }

        public void OnExecutionEnded(string executionId)
        {
            foreach (var key in _listeners.Keys.Where(item => item.StartsWith(executionId + ":", StringComparison.Ordinal)).ToArray())
            {
                if (_listeners.TryGetValue(key, out var registration)) registration.Dispose();
                _listeners.Remove(key);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var registration in _listeners.Values.ToArray()) registration.Dispose();
            _listeners.Clear();
        }

        private async Task<ApiResult> InvokeCookieAsync(ApiInvocationContext context, CancellationToken cancellationToken)
        {
            ProviderParameters.RequireObject(context.Parameters);
            var operation = ProviderParameters.OptionalString(context.Parameters, "operation") ?? "list";
            var details = context.Parameters.TryGetProperty("details", out var detailsValue)
                ? detailsValue : context.Parameters;
            if (operation == "list")
            {
                var query = ReadQuery(details);
                var cookies = await _cookies.ListAsync(query, cancellationToken).ConfigureAwait(false);
                return ApiResult.FromValue(cookies.Select(ToJson).ToArray());
            }
            if (operation == "set")
            {
                var mutation = ReadMutation(details, context.ExecutionId);
                var cookie = await _cookies.SetAsync(mutation, cancellationToken).ConfigureAwait(false);
                return ApiResult.FromValue(ToJson(cookie));
            }
            if (operation == "delete")
            {
                var mutation = ReadMutation(details, context.ExecutionId);
                return ApiResult.FromValue(await _cookies.DeleteAsync(mutation, cancellationToken).ConfigureAwait(false));
            }
            if (operation == "addListener")
            {
                var query = ReadQuery(details);
                var listenerId = ProviderParameters.RequiredInt32(context.Parameters, "listenerId");
                var key = context.ExecutionId + ":" + listenerId;
                if (_listeners.ContainsKey(key)) throw ProviderParameters.Invalid("listenerId is already registered.");
                var registration = _cookies.AddListener(query, (_, change) => Notification?.Invoke(this,
                    new ApiNotificationEventArgs(context.Installation.ScriptKey, context.ExecutionId, "cookie-change",
                        JsonSerializer.Serialize(new
                        {
                            listenerId,
                            cookie = ToJson(change.Cookie),
                            cause = change.Cause,
                            removed = change.Removed,
                            originExecutionId = change.OriginExecutionId,
                            sequence = change.Sequence
                        }))));
                if (registration == null) throw new InvalidOperationException("The cookie service returned no listener registration.");
                _listeners.Add(key, registration);
                return ApiResult.FromValue(listenerId);
            }
            if (operation == "removeListener")
            {
                var listenerId = ProviderParameters.RequiredInt32(context.Parameters, "listenerId");
                var key = context.ExecutionId + ":" + listenerId;
                if (!_listeners.TryGetValue(key, out var registration)) return ApiResult.FromValue(false);
                _listeners.Remove(key);
                registration.Dispose();
                return ApiResult.FromValue(true);
            }
            throw ProviderParameters.Invalid("Unknown cookie operation.");
        }

        private static UserScriptCookieQuery ReadQuery(JsonElement details)
        {
            ProviderParameters.RequireObject(details);
            var urlText = ProviderParameters.RequiredString(details, "url");
            if (!Uri.TryCreate(urlText, UriKind.Absolute, out var url))
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

        private static UserScriptCookieMutation ReadMutation(JsonElement details, string originExecutionId)
        {
            var query = ReadQuery(details);
            var value = ProviderParameters.OptionalString(details, "value");
            var expiration = ReadExpiration(details);
            return new UserScriptCookieMutation(query.Url, query.Name,
                value, query.Domain, query.Path, expiration,
                ProviderParameters.OptionalBoolean(details, "secure"),
                ProviderParameters.OptionalString(details, "sameSite"), originExecutionId);
        }

        private static DateTime? ReadExpiration(JsonElement details)
        {
            if (!details.TryGetProperty("expiration", out var value) && !details.TryGetProperty("expirationDate", out value))
                return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var seconds))
                return DateTimeOffset.FromUnixTimeSeconds((long)seconds).UtcDateTime;
            if (value.ValueKind == JsonValueKind.String && DateTime.TryParse(value.GetString(), out var date))
                return date.ToUniversalTime();
            throw ProviderParameters.Invalid("expiration must be Unix seconds or an ISO date.");
        }

        private static object ToJson(UserScriptCookie cookie)
        {
            return cookie == null ? null : new
            {
                name = cookie.Name, value = cookie.Value, domain = cookie.Domain, path = cookie.Path,
                expiration = cookie.Expiration.HasValue ? new DateTimeOffset(cookie.Expiration.Value).ToUnixTimeSeconds() : (long?)null,
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
                    mutation.Expiration, mutation.Secure, false, mutation.SameSite);
                _cookies.Add(cookie);
            }
            Publish(new UserScriptCookieChangedEventArgs(cookie, old == null ? "explicit" : "overwrite", false,
                mutation.OriginExecutionId, Interlocked.Increment(ref _sequence)));
            return Task.FromResult(cookie);
        }

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
