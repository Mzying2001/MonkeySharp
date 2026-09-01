using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Apis
{
    /// <summary>Identifies the Chromium request lifecycle phase being evaluated.</summary>
    public enum WebRequestPhase
    {
        /// <summary>Before URL dispatch.</summary>
        OnBeforeRequest,
        /// <summary>Before request headers are sent.</summary>
        OnBeforeSendHeaders,
        /// <summary>After response headers arrive.</summary>
        OnHeadersReceived,
        /// <summary>When credentials are requested.</summary>
        OnAuthRequired,
        /// <summary>When the response begins.</summary>
        OnResponseStarted,
        /// <summary>After a request completes successfully.</summary>
        OnCompleted,
        /// <summary>After a request fails.</summary>
        OnErrorOccurred
    }

    /// <summary>Identifies the action produced by a web request rule.</summary>
    public enum WebRequestActionKind
    {
        /// <summary>Allows the request without a terminal change.</summary>
        Allow,
        /// <summary>Blocks the request.</summary>
        Block,
        /// <summary>Redirects the request.</summary>
        Redirect,
        /// <summary>Changes request headers.</summary>
        ModifyRequestHeaders,
        /// <summary>Changes response headers.</summary>
        ModifyResponseHeaders,
        /// <summary>Supplies authentication credentials.</summary>
        AuthResponse
    }

    /// <summary>Matches requests by URL glob, resource type, and HTTP method.</summary>
    public sealed class WebRequestFilter
    {
        /// <summary>Initializes a request filter. Empty collections match every value.</summary>
        public WebRequestFilter(IEnumerable<string> urlPatterns = null, IEnumerable<string> resourceTypes = null, IEnumerable<string> methods = null)
        {
            UrlPatterns = new ReadOnlyCollection<string>((urlPatterns ?? Enumerable.Empty<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item)).ToArray());
            ResourceTypes = new ReadOnlyCollection<string>((resourceTypes ?? Enumerable.Empty<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item)).ToArray());
            Methods = new ReadOnlyCollection<string>((methods ?? Enumerable.Empty<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.ToUpperInvariant()).ToArray());
        }
        /// <summary>Gets case-insensitive URL globs.</summary>
        public IReadOnlyList<string> UrlPatterns { get; }
        /// <summary>Gets matched Chromium resource type names.</summary>
        public IReadOnlyList<string> ResourceTypes { get; }
        /// <summary>Gets matched uppercase HTTP methods.</summary>
        public IReadOnlyList<string> Methods { get; }
        /// <summary>Returns whether all configured constraints match a request.</summary>
        public bool Matches(WebRequestEvent request)
        {
            if (UrlPatterns.Count > 0 && !UrlPatterns.Any(pattern => GlobMatch(pattern, request.Url))) return false;
            if (ResourceTypes.Count > 0 && !ResourceTypes.Contains(request.ResourceType, StringComparer.OrdinalIgnoreCase)) return false;
            if (Methods.Count > 0 && !Methods.Contains(request.Method, StringComparer.OrdinalIgnoreCase)) return false;
            return true;
        }
        private static bool GlobMatch(string pattern, string value)
        {
            var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(value ?? string.Empty, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }

    /// <summary>Describes the effect of a web request rule.</summary>
    public sealed class WebRequestAction
    {
        /// <summary>Initializes a rule action and copies its header mutations.</summary>
        public WebRequestAction(WebRequestActionKind kind, string redirectUrl = null, IDictionary<string, string> headers = null, string username = null, string password = null)
        {
            Kind = kind;
            RedirectUrl = redirectUrl;
            Username = username;
            Password = password;
            Headers = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(
                headers ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase));
        }
        /// <summary>Gets the action kind.</summary>
        public WebRequestActionKind Kind { get; }
        /// <summary>Gets the redirect target for <see cref="WebRequestActionKind.Redirect"/>.</summary>
        public string RedirectUrl { get; }
        /// <summary>Gets request or response header replacements.</summary>
        public IReadOnlyDictionary<string, string> Headers { get; }
        /// <summary>Gets the authentication username.</summary>
        public string Username { get; }
        /// <summary>Gets the authentication password.</summary>
        public string Password { get; }
    }

    /// <summary>Defines a prioritized action for one request lifecycle phase.</summary>
    public sealed class WebRequestRule
    {
        /// <summary>Initializes a web request rule.</summary>
        public WebRequestRule(string id, WebRequestFilter filter, WebRequestPhase phase, int priority, WebRequestAction action)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Rule id is required.", nameof(id));
            Id = id;
            Filter = filter ?? throw new ArgumentNullException(nameof(filter));
            Action = action ?? throw new ArgumentNullException(nameof(action));
            Phase = phase;
            Priority = priority;
        }
        /// <summary>Gets the service-local rule identifier.</summary>
        public string Id { get; }
        /// <summary>Gets the request filter.</summary>
        public WebRequestFilter Filter { get; }
        /// <summary>Gets the evaluated lifecycle phase.</summary>
        public WebRequestPhase Phase { get; }
        /// <summary>Gets rule priority; higher values win terminal and conflicting header decisions.</summary>
        public int Priority { get; }
        /// <summary>Gets the rule action.</summary>
        public WebRequestAction Action { get; }
    }

    /// <summary>Contains an immutable request snapshot delivered to rules and listeners.</summary>
    public sealed class WebRequestEvent : EventArgs
    {
        /// <summary>Initializes a request lifecycle event.</summary>
        public WebRequestEvent(WebRequestPhase phase, ulong requestId, string url, string method = "GET", string resourceType = null,
            IDictionary<string, string> headers = null, int? statusCode = null, string error = null)
        {
            Phase = phase;
            RequestId = requestId;
            Url = url ?? throw new ArgumentNullException(nameof(url));
            Method = method ?? "GET";
            ResourceType = resourceType;
            Headers = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(
                headers ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase));
            StatusCode = statusCode;
            Error = error;
        }
        /// <summary>Gets the lifecycle phase.</summary>
        public WebRequestPhase Phase { get; }
        /// <summary>Gets the adapter-provided request identifier.</summary>
        public ulong RequestId { get; }
        /// <summary>Gets the absolute request URL.</summary>
        public string Url { get; }
        /// <summary>Gets the HTTP method.</summary>
        public string Method { get; }
        /// <summary>Gets the Chromium resource type name.</summary>
        public string ResourceType { get; }
        /// <summary>Gets request or response headers for the current phase.</summary>
        public IReadOnlyDictionary<string, string> Headers { get; }
        /// <summary>Gets the response status code when available.</summary>
        public int? StatusCode { get; }
        /// <summary>Gets the failure description for error events.</summary>
        public string Error { get; }
    }

    /// <summary>Contains the combined synchronous decision for a request event.</summary>
    public sealed class WebRequestDecision
    {
        /// <summary>Initializes a combined request decision.</summary>
        public WebRequestDecision(WebRequestActionKind kind = WebRequestActionKind.Allow, string redirectUrl = null, IDictionary<string, string> headers = null, string username = null, string password = null)
        {
            Kind = kind;
            RedirectUrl = redirectUrl;
            Headers = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(
                headers ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase));
            Username = username;
            Password = password;
        }
        /// <summary>Gets the terminal action kind.</summary>
        public WebRequestActionKind Kind { get; }
        /// <summary>Gets the redirect URL.</summary>
        public string RedirectUrl { get; }
        /// <summary>Gets merged header changes, with higher-priority and later rules winning conflicts.</summary>
        public IReadOnlyDictionary<string, string> Headers { get; }
        /// <summary>Gets the authentication username.</summary>
        public string Username { get; }
        /// <summary>Gets the authentication password.</summary>
        public string Password { get; }
    }

    /// <summary>Owns a web request rule or listener registration.</summary>
    public interface IWebRequestRegistration : IDisposable
    {
        /// <summary>Gets the service-local registration identifier.</summary>
        string Id { get; }
    }

    /// <summary>Evaluates request events synchronously and manages rules and listeners.</summary>
    public interface IWebRequestService
    {
        /// <summary>Adds a rule and returns its owned registration.</summary>
        IWebRequestRegistration AddRule(WebRequestRule rule);
        /// <summary>Removes a rule by service-local identifier.</summary>
        bool RemoveRule(string id);
        /// <summary>Lists rules in descending priority and then registration order.</summary>
        IReadOnlyList<WebRequestRule> ListRules();
        /// <summary>Adds a synchronous observer that must not block Chromium callbacks.</summary>
        IWebRequestRegistration AddListener(WebRequestFilter filter, EventHandler<WebRequestEvent> listener);
        /// <summary>Evaluates matching listeners and rules synchronously.</summary>
        WebRequestDecision Evaluate(WebRequestEvent request);
    }

    /// <summary>Adapts an execution-isolated <see cref="IWebRequestService"/> to <c>GM.webRequest</c>.</summary>
    public sealed class WebRequestApiProvider : IUserScriptApiProvider, IUserScriptNotificationSource, IUserScriptExecutionObserver, IDisposable
    {
        private readonly IWebRequestService _service;
        private readonly object _sync = new object();
        private readonly Dictionary<string, IWebRequestRegistration> _registrations = new Dictionary<string, IWebRequestRegistration>(StringComparer.Ordinal);
        private readonly HashSet<string> _pendingRegistrations = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _endedPendingRegistrations = new HashSet<string>(StringComparer.Ordinal);
        private bool _disposed;
        /// <summary>Initializes a provider over an application-owned request service.</summary>
        public WebRequestApiProvider(IWebRequestService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }
        /// <inheritdoc />
        public event EventHandler<ApiNotificationEventArgs> Notification;
        /// <inheritdoc />
        public IReadOnlyCollection<string> Methods { get; } = new ReadOnlyCollection<string>(new[] { "GM.webRequest" });
        /// <inheritdoc />
        public Task<ApiResult> InvokeAsync(ApiInvocationContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync) ThrowIfDisposed();
            ProviderParameters.RequireObject(context.Parameters);
            var operation = ProviderParameters.OptionalString(context.Parameters, "operation") ?? "listRules";
            if (operation == "addRule")
            {
                var rule = ReadRule(ProviderParameters.RequiredObject(context.Parameters, "rule"));
                var registrationKey = RuleKey(context.ExecutionId, rule.Id);
                var serviceRule = new WebRequestRule(
                    Guid.NewGuid().ToString("D"),
                    rule.Filter,
                    rule.Phase,
                    rule.Priority,
                    rule.Action);
                Register(registrationKey, () => _service.AddRule(serviceRule),
                    "The rule ID is already registered by this execution.");
                return Task.FromResult(ApiResult.FromValue(rule.Id));
            }
            if (operation == "removeRule")
            {
                var id = ProviderParameters.RequiredString(context.Parameters, "id");
                var key = RuleKey(context.ExecutionId, id);
                var registration = RemoveRegistration(key);
                registration?.Dispose();
                return Task.FromResult(ApiResult.FromValue(registration != null));
            }
            if (operation == "listRules")
            {
                var prefix = RulePrefix(context.ExecutionId);
                string[] rules;
                lock (_sync)
                {
                    ThrowIfDisposed();
                    rules = _registrations.Keys
                        .Where(item => item.StartsWith(prefix, StringComparison.Ordinal))
                        .Select(item => item.Substring(prefix.Length))
                        .OrderBy(item => item, StringComparer.Ordinal)
                        .ToArray();
                }
                return Task.FromResult(ApiResult.FromValue(rules));
            }
            if (operation == "addListener")
            {
                var listenerId = ProviderParameters.RequiredInt32(context.Parameters, "listenerId");
                var registrationKey = ListenerKey(context.ExecutionId, listenerId);
                var filter = ReadFilter(
                    ProviderParameters.RequiredObject(context.Parameters, "filter"),
                    "filter");
                Register(registrationKey, () => _service.AddListener(filter, (_, item) =>
                    Notification?.Invoke(this, new ApiNotificationEventArgs(
                        context.Installation.ScriptKey, context.ExecutionId, "webrequest-event", JsonSerializer.Serialize(new
                        {
                            listenerId, phase = item.Phase.ToString(), requestId = item.RequestId, url = item.Url,
                            method = item.Method, resourceType = item.ResourceType, headers = item.Headers,
                            statusCode = item.StatusCode, error = item.Error
                        })))), "listenerId is already registered.");
                return Task.FromResult(ApiResult.FromValue(listenerId));
            }
            if (operation == "removeListener")
            {
                var id = ProviderParameters.RequiredInt32(context.Parameters, "listenerId");
                var key = ListenerKey(context.ExecutionId, id);
                var registration = RemoveRegistration(key);
                registration?.Dispose();
                return Task.FromResult(ApiResult.FromValue(registration != null));
            }
            throw ProviderParameters.Invalid("Unknown webRequest operation.");
        }
        /// <inheritdoc />
        public void OnExecutionEnded(string executionId)
        {
            IWebRequestRegistration[] registrations;
            var prefix = executionId + ":";
            lock (_sync)
            {
                var keys = _registrations.Keys
                    .Where(item => item.StartsWith(prefix, StringComparison.Ordinal))
                    .ToArray();
                registrations = keys.Select(item => _registrations[item]).ToArray();
                foreach (var key in keys) _registrations.Remove(key);
                foreach (var key in _pendingRegistrations.Where(item =>
                    item.StartsWith(prefix, StringComparison.Ordinal)))
                    _endedPendingRegistrations.Add(key);
            }
            foreach (var registration in registrations) registration.Dispose();
        }
        /// <inheritdoc />
        public void Dispose()
        {
            IWebRequestRegistration[] registrations;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                registrations = _registrations.Values.ToArray();
                _registrations.Clear();
                _endedPendingRegistrations.Clear();
            }
            foreach (var registration in registrations) registration.Dispose();
        }
        private void Register(
            string key,
            Func<IWebRequestRegistration> factory,
            string duplicateMessage)
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                if (_registrations.ContainsKey(key) || !_pendingRegistrations.Add(key))
                    throw ProviderParameters.Invalid(duplicateMessage);
            }
            IWebRequestRegistration registration = null;
            try
            {
                registration = factory();
                if (registration == null || string.IsNullOrEmpty(registration.Id))
                    throw new InvalidOperationException("The webRequest service returned no registration.");
                bool disposed;
                bool executionEnded;
                lock (_sync)
                {
                    _pendingRegistrations.Remove(key);
                    disposed = _disposed;
                    executionEnded = _endedPendingRegistrations.Remove(key);
                    if (!disposed && !executionEnded)
                        _registrations.Add(key, registration);
                }
                if (disposed || executionEnded)
                {
                    registration.Dispose();
                    registration = null;
                    if (disposed) throw new ObjectDisposedException(nameof(WebRequestApiProvider));
                    throw new OperationCanceledException(
                        "The script execution ended while registering a webRequest operation.");
                }
            }
            catch
            {
                lock (_sync)
                {
                    _pendingRegistrations.Remove(key);
                    _endedPendingRegistrations.Remove(key);
                }
                registration?.Dispose();
                throw;
            }
        }
        private IWebRequestRegistration RemoveRegistration(string key)
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                if (!_registrations.TryGetValue(key, out var registration)) return null;
                _registrations.Remove(key);
                return registration;
            }
        }
        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WebRequestApiProvider));
        }
        private static string ListenerKey(string executionId, int listenerId)
        {
            return executionId + ":listener:" + listenerId;
        }
        private static string RuleKey(string executionId, string ruleId)
        {
            return RulePrefix(executionId) + ruleId;
        }
        private static string RulePrefix(string executionId)
        {
            return executionId + ":rule:";
        }
        private static WebRequestRule ReadRule(JsonElement value)
        {
            ProviderParameters.RequireObject(value);
            var id = ProviderParameters.RequiredString(value, "id", "rule.id");
            var phaseText = ProviderParameters.RequiredString(value, "phase", "rule.phase");
            if (!Enum.TryParse(phaseText, true, out WebRequestPhase phase))
                throw ProviderParameters.Invalid("rule.phase is invalid.");
            var filter = ReadFilter(
                ProviderParameters.RequiredObject(value, "filter", "rule.filter"),
                "rule.filter");
            var actionValue = ProviderParameters.RequiredObject(value, "action", "rule.action");
            var actionText = ProviderParameters.RequiredString(actionValue, "kind", "rule.action.kind");
            if (!Enum.TryParse(actionText, true, out WebRequestActionKind kind))
                throw ProviderParameters.Invalid("rule.action.kind is invalid.");
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (actionValue.TryGetProperty("headers", out var headerValues))
            {
                if (headerValues.ValueKind != JsonValueKind.Object)
                    throw ProviderParameters.Invalid("rule.action.headers must be an object.");
                foreach (var header in headerValues.EnumerateObject())
                {
                    if (header.Value.ValueKind != JsonValueKind.String)
                        throw ProviderParameters.Invalid(
                            "rule.action.headers." + header.Name + " must be a string.");
                    headers[header.Name] = header.Value.GetString();
                }
            }
            var priority = 0;
            if (value.TryGetProperty("priority", out var priorityValue) &&
                (priorityValue.ValueKind != JsonValueKind.Number || !priorityValue.TryGetInt32(out priority)))
                throw ProviderParameters.Invalid("rule.priority must be an integer.");
            return new WebRequestRule(id, filter, phase, priority,
                new WebRequestAction(kind,
                    ProviderParameters.OptionalString(actionValue, "redirectUrl", "rule.action.redirectUrl"),
                    headers,
                    ProviderParameters.OptionalString(actionValue, "username", "rule.action.username"),
                    ProviderParameters.OptionalString(actionValue, "password", "rule.action.password")));
        }
        private static WebRequestFilter ReadFilter(JsonElement value, string path)
        {
            ProviderParameters.RequireObject(value);
            return new WebRequestFilter(
                ReadStrings(value, "urlPatterns", path + ".urlPatterns"),
                ReadStrings(value, "resourceTypes", path + ".resourceTypes"),
                ReadStrings(value, "methods", path + ".methods"));
        }
        private static IEnumerable<string> ReadStrings(JsonElement value, string name, string path)
        {
            if (!value.TryGetProperty(name, out var items)) return Enumerable.Empty<string>();
            if (items.ValueKind != JsonValueKind.Array)
                throw ProviderParameters.Invalid(path + " must be an array.");
            var result = new List<string>();
            var index = 0;
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                    throw ProviderParameters.Invalid(path + "[" + index + "] must be a string.");
                result.Add(item.GetString());
                index++;
            }
            return result;
        }
    }

    /// <summary>
    /// Provides a thread-safe in-memory rule engine. Listener callbacks run synchronously and
    /// cannot change the decision. Higher-priority rules win terminal and conflicting header
    /// decisions; rules with equal priority use registration order, with later header edits winning.
    /// </summary>
    public sealed class InMemoryWebRequestService : IWebRequestService, IDisposable
    {
        private readonly object _sync = new object();
        private readonly List<Tuple<WebRequestRule, long, IWebRequestRegistration>> _rules = new List<Tuple<WebRequestRule, long, IWebRequestRegistration>>();
        private readonly List<Tuple<WebRequestFilter, EventHandler<WebRequestEvent>, IWebRequestRegistration>> _listeners = new List<Tuple<WebRequestFilter, EventHandler<WebRequestEvent>, IWebRequestRegistration>>();
        private long _sequence;
        private bool _disposed;
        /// <inheritdoc />
        public IWebRequestRegistration AddRule(WebRequestRule rule)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            lock (_sync)
            {
                ThrowIfDisposed();
                var r = new Registration(rule.Id, () => RemoveRule(rule.Id));
                _rules.Add(Tuple.Create(rule, ++_sequence, (IWebRequestRegistration)r));
                return r;
            }
        }
        /// <inheritdoc />
        public bool RemoveRule(string id)
        {
            lock (_sync)
            {
                var item = _rules.FirstOrDefault(x => x.Item1.Id == id);
                if (item == null) return false;
                _rules.Remove(item);
                item.Item3.Dispose();
                return true;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<WebRequestRule> ListRules()
        {
            lock (_sync)
            {
                return new ReadOnlyCollection<WebRequestRule>(_rules
                    .OrderByDescending(x => x.Item1.Priority)
                    .ThenBy(x => x.Item2)
                    .Select(x => x.Item1)
                    .ToList());
            }
        }
        /// <inheritdoc />
        public IWebRequestRegistration AddListener(WebRequestFilter filter, EventHandler<WebRequestEvent> listener)
        {
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            lock (_sync)
            {
                ThrowIfDisposed();
                Registration registration = null;
                registration = new Registration(Guid.NewGuid().ToString("D"), () =>
                {
                    lock (_sync) _listeners.RemoveAll(x => ReferenceEquals(x.Item3, registration));
                });
                _listeners.Add(Tuple.Create(filter, listener, (IWebRequestRegistration)registration));
                return registration;
            }
        }
        /// <inheritdoc />
        public WebRequestDecision Evaluate(WebRequestEvent request)
        {
            Tuple<WebRequestRule, long, IWebRequestRegistration>[] rules;
            Tuple<WebRequestFilter, EventHandler<WebRequestEvent>, IWebRequestRegistration>[] listeners;
            lock (_sync)
            {
                ThrowIfDisposed();
                rules = _rules
                    .Where(x => x.Item1.Phase == request.Phase && x.Item1.Filter.Matches(request))
                    .OrderByDescending(x => x.Item1.Priority)
                    .ThenBy(x => x.Item2)
                    .ToArray();
                listeners = _listeners.Where(x => x.Item1.Matches(request)).ToArray();
            }
            foreach (var listener in listeners)
            {
                // Observers must never be able to delay or change the host's
                // synchronous network decision.
                try { listener.Item2(this, request); }
                catch { }
            }
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var kind = WebRequestActionKind.Allow;
            string redirect = null;
            string user = null;
            string password = null;
            foreach (var item in rules)
            {
                var action = item.Item1.Action;
                if (action.Kind == WebRequestActionKind.Block || action.Kind == WebRequestActionKind.Redirect)
                {
                    kind = action.Kind;
                    redirect = action.RedirectUrl;
                    break;
                }
                if (action.Kind == WebRequestActionKind.AuthResponse)
                {
                    kind = action.Kind;
                    user = action.Username;
                    password = action.Password;
                    break;
                }
            }
            foreach (var item in rules
                .OrderBy(x => x.Item1.Priority)
                .ThenBy(x => x.Item2))
            {
                // Header mutations are phase-specific. Keeping this filtering in
                // the service prevents a request rule from accidentally changing
                // response headers (and vice versa) for every adapter.
                var isRequestPhase = request.Phase == WebRequestPhase.OnBeforeRequest ||
                    request.Phase == WebRequestPhase.OnBeforeSendHeaders;
                var isResponsePhase = request.Phase == WebRequestPhase.OnHeadersReceived;
                if ((isRequestPhase && item.Item1.Action.Kind == WebRequestActionKind.ModifyRequestHeaders) ||
                    (isResponsePhase && item.Item1.Action.Kind == WebRequestActionKind.ModifyResponseHeaders))
                    foreach (var h in item.Item1.Action.Headers) headers[h.Key] = h.Value;
            }
            return new WebRequestDecision(kind, redirect, headers, user, password);
        }
        /// <inheritdoc />
        public void Dispose()
        {
            IWebRequestRegistration[] rules;
            IWebRequestRegistration[] listeners;
            lock (_sync)
            {
                if (_disposed) return;
                rules = _rules.Select(item => item.Item3).ToArray();
                listeners = _listeners.Select(item => item.Item3).ToArray();
                _rules.Clear();
                _listeners.Clear();
                _disposed = true;
            }
            foreach (var registration in rules) registration.Dispose();
            foreach (var registration in listeners) registration.Dispose();
        }
        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(InMemoryWebRequestService));
        }
        private sealed class Registration : IWebRequestRegistration
        {
            private readonly Action _dispose;
            private int _disposed;

            public Registration(string id, Action dispose)
            {
                Id = id;
                _dispose = dispose;
            }

            public string Id { get; }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    _dispose();
            }
        }
    }
}
