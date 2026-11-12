using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Matching;
using Mzying2001.MonkeySharp.Core.Runtime;
using Mzying2001.MonkeySharp.Core.Repository;
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
        Redirect
    }

    /// <summary>Matches requests by URL glob, resource type, and HTTP method.</summary>
    public sealed class WebRequestFilter
    {
        private readonly Func<string, bool> _urlMatcher;
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

        internal WebRequestFilter(
            Func<string, bool> urlMatcher,
            IEnumerable<string> resourceTypes = null,
            IEnumerable<string> methods = null)
            : this((IEnumerable<string>)null, resourceTypes, methods)
        {
            _urlMatcher = urlMatcher;
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
            if (_urlMatcher != null && !_urlMatcher(request.Url)) return false;
            if (_urlMatcher == null && UrlPatterns.Count > 0 && !UrlPatterns.Any(pattern => GlobMatch(pattern, request.Url))) return false;
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

    /// <summary>One compiled Tampermonkey selector/action pair.</summary>
    public sealed class WebRequestRule
    {
        private readonly Func<string, bool> _matches;
        private readonly Func<string, string> _redirect;
        private readonly Func<Uri, bool> _redirectAllowed;
        internal WebRequestRule(JsonElement value, Func<string, bool> matches, bool cancel,
            Func<string, string> redirect, Func<Uri, bool> redirectAllowed)
        { Value = value.Clone(); _matches = matches; Cancel = cancel; _redirect = redirect; _redirectAllowed = redirectAllowed; }
        /// <summary>Gets the original, validated rule for listener details.</summary>
        public JsonElement Value { get; }
        internal bool Cancel { get; }
        internal bool Matches(string url) => _matches(url);
        internal WebRequestDecision Decide(string url)
        {
            if (Cancel) return new WebRequestDecision(WebRequestActionKind.Block);
            var target = _redirect(url);
            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") || !_redirectAllowed(uri))
                return new WebRequestDecision(WebRequestActionKind.Block, description: "Redirect target is outside the script URL permissions.");
            return new WebRequestDecision(WebRequestActionKind.Redirect, uri.AbsoluteUri);
        }
    }

    /// <summary>Contains an immutable request snapshot delivered to rules and listeners.</summary>
    public sealed class WebRequestEvent : EventArgs
    {
        /// <summary>Initializes a request lifecycle event.</summary>
        public WebRequestEvent(WebRequestPhase phase, ulong requestId, string url, string method = "GET", string resourceType = null,
            IDictionary<string, string> headers = null, int? statusCode = null, string error = null, DocumentFrame frame = null)
        {
            Frame = frame;
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
        /// <summary>Gets host-verified ownership; null requests cannot use script rules.</summary>
        public DocumentFrame Frame { get; }
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
        /// <summary>Initializes a synchronous cancel/redirect decision.</summary>
        public WebRequestDecision(WebRequestActionKind kind = WebRequestActionKind.Allow, string redirectUrl = null, string description = null)
        { Kind = kind; RedirectUrl = redirectUrl; Description = description; }
        /// <summary>Gets the action.</summary>
        public WebRequestActionKind Kind { get; }
        /// <summary>Gets the authorized redirect destination.</summary>
        public string RedirectUrl { get; }
        /// <summary>Gets an optional rule error.</summary>
        public string Description { get; }
    }

    /// <summary>Owns a host-side registration; this is never exposed to userscripts.</summary>
    public interface IWebRequestRegistration : IDisposable
    {
        /// <summary>Gets the host-local identity.</summary>
        string Id { get; }
    }

    /// <summary>Owns precompiled scoped rule sets and request observers.</summary>
    public interface IWebRequestService
    {
        /// <summary>Atomically replaces one owner's rules and listener. Empty rules revoke that owner.</summary>
        IWebRequestRegistration SetRules(string owner, IReadOnlyList<WebRequestRule> rules,
            Func<WebRequestEvent, bool> scope, Action<WebRequestRule, WebRequestEvent, WebRequestDecision> listener = null,
            Func<WebRequestEvent, CancellationToken, Task<bool>> authorize = null, string installationId = null, bool isStatic = false);
        /// <summary>Adds a host-only lifecycle observer.</summary>
        IWebRequestRegistration AddListener(WebRequestFilter filter, EventHandler<WebRequestEvent> listener);
        /// <summary>Evaluates rules whose authorization completes synchronously; never waits on a policy.</summary>
        WebRequestDecision Evaluate(WebRequestEvent request);
    }

    /// <summary>Allows Chromium to defer an intercepted request while host authorization completes.</summary>
    public interface IAsyncWebRequestService : IWebRequestService
    {
        /// <summary>Awaits policy decisions, then performs bounded synchronous rule evaluation.</summary>
        Task<WebRequestDecision> EvaluateAsync(WebRequestEvent request, CancellationToken cancellationToken);
    }

    /// <summary>Adapts an execution-isolated <see cref="IWebRequestService"/> to <c>GM.webRequest</c>.</summary>
    public sealed class WebRequestApiProvider : IUserScriptApiProvider, IUserScriptNotificationSource, IUserScriptExecutionObserver, IDisposable
    {
        private readonly IWebRequestService _service;
        private readonly IUserScriptRepository _repository;
        private readonly Dictionary<string, ScriptKey> _executionScripts = new Dictionary<string, ScriptKey>();
        private readonly object _sync = new object();
        private readonly Dictionary<string, IWebRequestRegistration> _registrations = new Dictionary<string, IWebRequestRegistration>(StringComparer.Ordinal);
        private bool _disposed;
        /// <summary>Initializes a provider over an application-owned request service.</summary>
        public WebRequestApiProvider(IWebRequestService service, IUserScriptRepository repository = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _repository = repository;
            if (repository != null) repository.Changed += RepositoryChanged;
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
            var operation = ProviderParameters.RequiredString(context.Parameters, "operation");
            if (operation == "register")
            {
                var rulesValue = ProviderParameters.RequiredProperty(context.Parameters, "rules");
                var parsed = CompileRules(rulesValue, context.Installation);
                var listenerId = 0;
                if (context.Parameters.TryGetProperty("listenerId", out var listenerValue) &&
                    (listenerValue.ValueKind != JsonValueKind.Number || !listenerValue.TryGetInt32(out listenerId) || listenerId <= 0))
                    throw ProviderParameters.Invalid("listenerId must be a positive integer.");
                var key = context.ExecutionId + ":registration";
                lock (_sync)
                {
                    ThrowIfDisposed();
                    if (_registrations.TryGetValue(key, out var old)) { _registrations.Remove(key); old.Dispose(); }
                    _executionScripts[context.ExecutionId] = context.Installation.ScriptKey;
                    _registrations[key] = _service.SetRules(key, parsed,
                        item => item.Frame != null && item.Frame.BrowserSessionId == context.Frame.BrowserSessionId &&
                            item.Frame.FrameId == context.Frame.FrameId && item.Frame.DocumentId == context.Frame.DocumentId,
                        listenerId == 0 ? (Action<WebRequestRule, WebRequestEvent, WebRequestDecision>)null : (rule, item, decision) =>
                        {
                            lock (_sync) if (_disposed || !_registrations.ContainsKey(key)) return;
                            Notification?.Invoke(this, new ApiNotificationEventArgs(context.Installation.ScriptKey, context.ExecutionId,
                                "webrequest-result", JsonSerializer.Serialize(new { listenerId,
                                    info = rule.Cancel ? "cancel" : "redirect", message = decision.Description == null ? "ok" : "error",
                                    details = new { rule = rule.Value, url = item.Url, redirect_url = decision.RedirectUrl, description = decision.Description } })));
                        }, authorize: context.AuthorizeTargetAsync == null ? (Func<WebRequestEvent, CancellationToken, Task<bool>>)null :
                            (request, token) => context.AuthorizeTargetAsync(request.Url, token),
                        installationId: context.Frame.BrowserSessionId + ":" + context.Installation.ScriptKey);
                }
                return Task.FromResult(ApiResult.Undefined);
            }
            throw ProviderParameters.Invalid("Unknown webRequest operation.");
        }
        private void RepositoryChanged(object sender, UserScriptRepositoryChangedEventArgs args)
        {
            string[] executions;
            lock (_sync) executions = _executionScripts.Where(item => item.Value.Equals(args.Installation.ScriptKey)).Select(item => item.Key).ToArray();
            foreach (var execution in executions) OnExecutionEnded(execution);
        }
        /// <inheritdoc />
        public void OnExecutionEnded(string executionId)
        {
            IWebRequestRegistration[] registrations;
            var prefix = executionId + ":";
            lock (_sync)
            {
                _executionScripts.Remove(executionId);
                var keys = _registrations.Keys
                    .Where(item => item.StartsWith(prefix, StringComparison.Ordinal))
                    .ToArray();
                registrations = keys.Select(item => _registrations[item]).ToArray();
                foreach (var key in keys) _registrations.Remove(key);
            }
            foreach (var registration in registrations) registration.Dispose();
        }
        /// <inheritdoc />
        public void Dispose()
        {
            if (_repository != null) _repository.Changed -= RepositoryChanged;
            IWebRequestRegistration[] registrations;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                registrations = _registrations.Values.ToArray();
                _registrations.Clear();
                _executionScripts.Clear();
            }
            foreach (var registration in registrations) registration.Dispose();
        }
        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WebRequestApiProvider));
        }
        private static WebRequestFilter ReadSelector(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                var matcher = CompileSelector(value.GetString(), "selector");
                return new WebRequestFilter(matcher);
            }
            if (value.ValueKind != JsonValueKind.Object)
                throw ProviderParameters.Invalid("selector must be a string or object.");
            var includes = ReadStringValues(value, "include", "selector.include");
            var matches = ReadStringValues(value, "match", "selector.match");
            var excludes = ReadStringValues(value, "exclude", "selector.exclude");
            if (includes.Count == 0 && matches.Count == 0 && excludes.Count == 0)
                throw ProviderParameters.Invalid("selector must contain include, match, or exclude.");
            var includeMatchers = includes.Select(item => CompileSelector(item, "selector.include")).ToArray();
            var matchers = matches.Select(item =>
            {
                if (!MatchPatternCompiler.TryCompile(item, out var pattern, out var error))
                    throw ProviderParameters.Invalid("selector.match contains an invalid match pattern: " + error);
                return pattern;
            }).ToArray();
            var excludeMatchers = excludes.Select(item => CompileSelector(item, "selector.exclude")).ToArray();
            return new WebRequestFilter(url =>
            {
                var excluded = excludeMatchers.Any(matcher => matcher(url));
                if (excluded) return false;
                if (includeMatchers.Length == 0 && matchers.Length == 0) return true;
                var uri = Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed : null;
                return includeMatchers.Any(matcher => matcher(url)) ||
                    (uri != null && matchers.Any(matcher => matcher.IsMatch(uri)));
            });
        }

        private static Func<string, bool> CompileSelector(string value, string path)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw ProviderParameters.Invalid(path + " cannot contain an empty pattern.");
            try { var expression = UserScriptUrlPattern.Compile(value); return url => expression.IsMatch(url ?? string.Empty); }
            catch (ArgumentException error) { throw ProviderParameters.Invalid(path + ": " + error.Message); }
        }

        private static List<string> ReadStringValues(JsonElement value, string name, string path)
        {
            if (!value.TryGetProperty(name, out var items) || items.ValueKind == JsonValueKind.Null)
                return new List<string>();
            if (items.ValueKind == JsonValueKind.String)
                return new List<string> { items.GetString() };
            if (items.ValueKind != JsonValueKind.Array)
                throw ProviderParameters.Invalid(path + " must be a string or array.");
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

        /// <summary>Validates and compiles the official selector/action array.</summary>
        public static IReadOnlyList<WebRequestRule> CompileRules(JsonElement rules, UserScriptInstallation installation)
        {
            if (rules.ValueKind != JsonValueKind.Array) throw ProviderParameters.Invalid("rules must be an array.");
            return rules.EnumerateArray().Select(value => CompileRule(value, installation.Definition.Metadata)).ToArray();
        }

        internal static WebRequestRule CompileRule(JsonElement value, UserScriptMetadata metadata)
        {
            ProviderParameters.RequireObject(value, "rule");
            var selector = ReadSelector(ProviderParameters.RequiredProperty(value, "selector"));
            var action = ProviderParameters.RequiredProperty(value, "action");
            var cancel = action.ValueKind == JsonValueKind.String && action.GetString() == "cancel";
            JsonElement redirect = default(JsonElement);
            if (!cancel)
            {
                if (action.ValueKind != JsonValueKind.Object) throw ProviderParameters.Invalid("action must be cancel or an action object.");
                cancel = action.TryGetProperty("cancel", out var flag) && flag.ValueKind == JsonValueKind.True;
                if (!cancel && !action.TryGetProperty("redirect", out redirect)) throw ProviderParameters.Invalid("action requires cancel:true or redirect.");
            }
            Func<string, string> resolve = null;
            if (!cancel)
            {
                if (redirect.ValueKind == JsonValueKind.String)
                {
                    var target = redirect.GetString();
                    if (metadata != null) target = RequireRedirectTarget(target, metadata, "action.redirect");
                    else if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                        throw ProviderParameters.Invalid("action.redirect requires an absolute HTTP(S) URL.");
                    resolve = _ => target;
                }
                else
                {
                    ProviderParameters.RequireObject(redirect, "action.redirect");
                    var from = ProviderParameters.RequiredString(redirect, "from");
                    var to = ProviderParameters.RequiredString(redirect, "to");
                    Regex expression;
                    try
                    {
                        var validated = UserScriptUrlPattern.Compile("/" + from + "/");
                        expression = new Regex(validated.ToString(), RegexOptions.ECMAScript | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                    }
                    catch (ArgumentException error) { throw ProviderParameters.Invalid(error.Message); }
                    resolve = url => ReplaceCapture(expression, url, to);
                }
            }
            return new WebRequestRule(value, url => selector.Matches(new WebRequestEvent(WebRequestPhase.OnBeforeRequest, 0, url)), cancel,
                resolve, uri => metadata == null || UserScriptUrlRules.Compile(metadata).IsMatch(uri));
        }

        // JavaScript replacement tokens differ from .NET ($0, $+, $_, ${name}).
        private static string ReplaceCapture(Regex expression, string url, string replacement)
        {
            var match = expression.Match(url);
            if (!match.Success) return url;
            var result = new System.Text.StringBuilder();
            for (var index = 0; index < replacement.Length; index++)
            {
                var ch = replacement[index];
                if (ch != '$' || index + 1 == replacement.Length) { result.Append(ch); continue; }
                var token = replacement[index + 1];
                if (token == '$') { result.Append('$'); index++; }
                else if (token == '&') { result.Append(match.Value); index++; }
                else if (token == '`') { result.Append(url.Substring(0, match.Index)); index++; }
                else if (token == '\'') { result.Append(url.Substring(match.Index + match.Length)); index++; }
                else if (token >= '1' && token <= '9' && token - '0' < match.Groups.Count)
                {
                    var group = token - '0'; index++;
                    if (index + 1 < replacement.Length && char.IsDigit(replacement[index + 1]) &&
                        group * 10 + replacement[index + 1] - '0' < match.Groups.Count) group = group * 10 + replacement[++index] - '0';
                    result.Append(match.Groups[group].Value);
                }
                else result.Append('$');
            }
            return url.Substring(0, match.Index) + result + url.Substring(match.Index + match.Length);
        }

        private static string RequireRedirectTarget(string value, UserScriptMetadata metadata, string path)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !new UserScriptMatcher().IsMatch(metadata, uri))
                throw new BridgeProtocolException(BridgeErrorCodes.PermissionDenied,
                    path + " redirect target is not allowed by the script URL rules.");
            return uri.AbsoluteUri;
        }


    }

    /// <summary>Evaluates immutable scoped rule sets; userscript listeners run outside network callbacks.</summary>
    public sealed class InMemoryWebRequestService : IAsyncWebRequestService, IDisposable
    {
        private readonly object _sync = new object();
        private readonly List<RuleSet> _sets = new List<RuleSet>();
        private readonly List<Tuple<WebRequestFilter, EventHandler<WebRequestEvent>>> _observers = new List<Tuple<WebRequestFilter, EventHandler<WebRequestEvent>>>();
        private bool _disposed;
        /// <inheritdoc />
        public IWebRequestRegistration SetRules(string owner, IReadOnlyList<WebRequestRule> rules, Func<WebRequestEvent, bool> scope,
            Action<WebRequestRule, WebRequestEvent, WebRequestDecision> listener = null,
            Func<WebRequestEvent, CancellationToken, Task<bool>> authorize = null, string installationId = null, bool isStatic = false)
        {
            if (string.IsNullOrEmpty(owner)) throw new ArgumentException("An owner is required.", nameof(owner));
            var set = new RuleSet { Owner = owner, Rules = rules.ToArray(), Scope = scope ?? throw new ArgumentNullException(nameof(scope)), Listener = listener, Authorize = authorize, InstallationId = installationId, IsStatic = isStatic };
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(InMemoryWebRequestService));
                foreach (var old in _sets.Where(item => item.Owner == owner)) old.Active = false;
                _sets.RemoveAll(item => item.Owner == owner);
                _sets.Add(set);
            }
            return new Registration(owner, () => { lock (_sync) { set.Active = false; _sets.Remove(set); } });
        }
        /// <inheritdoc />
        public IWebRequestRegistration AddListener(WebRequestFilter filter, EventHandler<WebRequestEvent> listener)
        {
            var observer = Tuple.Create(filter, listener);
            lock (_sync) _observers.Add(observer);
            return new Registration(Guid.NewGuid().ToString(), () => { lock (_sync) _observers.Remove(observer); });
        }
        /// <inheritdoc />
        public WebRequestDecision Evaluate(WebRequestEvent request) => EvaluateCoreAsync(request, CancellationToken.None, false).GetAwaiter().GetResult();
        /// <inheritdoc />
        public Task<WebRequestDecision> EvaluateAsync(WebRequestEvent request, CancellationToken cancellationToken) => EvaluateCoreAsync(request, cancellationToken, true);
        private async Task<WebRequestDecision> EvaluateCoreAsync(WebRequestEvent request, CancellationToken token, bool canWait)
        {
            RuleSet[] sets;
            Tuple<WebRequestFilter, EventHandler<WebRequestEvent>>[] observers;
            lock (_sync) { if (_disposed) return new WebRequestDecision(); sets = _sets.ToArray(); observers = _observers.ToArray(); }
            foreach (var observer in observers) try { if (observer.Item1.Matches(request)) observer.Item2(this, request); } catch { }
            if (request.Phase != WebRequestPhase.OnBeforeRequest || request.Frame == null ||
                !new[] { "sub_frame", "script", "xhr", "websocket" }.Contains(request.ResourceType)) return new WebRequestDecision();
            foreach (var set in sets)
            {
                token.ThrowIfCancellationRequested();
                if (!set.Active || !set.Scope(request)) continue;
                if (set.IsStatic && sets.Any(other => other.Active && !other.IsStatic && other.InstallationId == set.InstallationId && other.Scope(request))) continue;
                foreach (var rule in set.Rules)
                {
                    WebRequestDecision decision;
                    try
                    {
                        if (!rule.Matches(request.Url)) continue;
                        decision = rule.Decide(request.Url);
                        if (set.Authorize != null)
                        {
                            var permission = set.Authorize(request, token);
                            if (!canWait && !permission.IsCompleted)
                                return new WebRequestDecision(WebRequestActionKind.Block, description: "Asynchronous policy cannot run in a synchronous redirect callback.");
                            if (!await permission.ConfigureAwait(false)) return new WebRequestDecision(WebRequestActionKind.Block, description: "Host policy denied this rule.");
                            if (decision.Kind == WebRequestActionKind.Redirect)
                            {
                                var redirected = new WebRequestEvent(request.Phase, request.RequestId, decision.RedirectUrl,
                                    request.Method, request.ResourceType, frame: request.Frame);
                                permission = set.Authorize(redirected, token);
                                if (!canWait && !permission.IsCompleted)
                                    return new WebRequestDecision(WebRequestActionKind.Block, description: "Asynchronous redirect authorization is unavailable.");
                                if (!await permission.ConfigureAwait(false))
                                    decision = new WebRequestDecision(WebRequestActionKind.Block, description: "Host policy denied the expanded redirect target.");
                            }
                        }
                        if (!set.Active) break;
                    }
                    catch (RegexMatchTimeoutException) { decision = new WebRequestDecision(WebRequestActionKind.Block, description: "Rule regular expression timed out."); }
                    if (set.Listener != null)
                    {
                        lock (_sync)
                            set.PendingNotifications = set.PendingNotifications.ContinueWith(_ =>
                            {
                                if (set.Active) try { set.Listener(rule, request, decision); } catch { }
                            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                    }
                    return decision;
                }
            }
            return new WebRequestDecision();
        }
        /// <inheritdoc />
        public void Dispose() { lock (_sync) { _disposed = true; foreach (var set in _sets) set.Active = false; _sets.Clear(); _observers.Clear(); } }
        private sealed class RuleSet
        {
            public string Owner;
            public string InstallationId;
            public bool IsStatic;
            public WebRequestRule[] Rules;
            public Func<WebRequestEvent, bool> Scope;
            public Action<WebRequestRule, WebRequestEvent, WebRequestDecision> Listener;
            public Func<WebRequestEvent, CancellationToken, Task<bool>> Authorize;
            public volatile bool Active = true;
            public Task PendingNotifications = Task.CompletedTask;
        }
        private sealed class Registration : IWebRequestRegistration
        {
            private Action _dispose;
            public Registration(string id, Action dispose) { Id = id; _dispose = dispose; }
            public string Id { get; }
            public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }
}
