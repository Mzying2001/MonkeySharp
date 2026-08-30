using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Apis
{
    /// <summary>
    /// Adapts host resource and HTTP services to userscript resource and network GM APIs.
    /// </summary>
    public sealed class ResourceAndNetworkApiProvider :
        IUserScriptApiProvider,
        IUserScriptNotificationSource,
        IUserScriptCompatibilityBootstrapProvider,
        IUserScriptExecutionObserver,
        IDisposable
    {
        private static readonly HashSet<string> BlockedMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CONNECT", "TRACE", "TRACK"
        };
        private static readonly HashSet<string> BlockedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Host", "Content-Length", "Connection", "Proxy-Connection",
            "Transfer-Encoding", "Upgrade", "Keep-Alive", "TE", "Trailer"
        };

        private readonly IResourceProvider _resources;
        private readonly IHttpRequestService _http;
        private readonly BridgeOptions _options;
        private readonly IReadOnlyCollection<string> _methods;
        private readonly object _sessionLock = new object();
        private readonly Dictionary<string, HttpSession> _sessions = new Dictionary<string, HttpSession>(StringComparer.Ordinal);
        private bool _disposed;

        /// <summary>Initializes a resource and network API provider.</summary>
        /// <param name="resources">The optional declared-resource provider.</param>
        /// <param name="http">The optional HTTP request service.</param>
        /// <param name="options">Bridge limits used for resources and responses, or <see langword="null"/> for defaults.</param>
        public ResourceAndNetworkApiProvider(
            IResourceProvider resources = null,
            IHttpRequestService http = null,
            BridgeOptions options = null)
        {
            if (resources == null && http == null)
                throw new ArgumentException("At least one resource or HTTP service is required.");
            _resources = resources;
            _http = http;
            _options = options ?? new BridgeOptions();
            var methods = new List<string>();
            if (resources != null)
            {
                methods.Add("GM.getResourceText");
                methods.Add("GM.getResourceURL");
            }
            if (http != null)
                methods.Add("GM.xmlHttpRequest");
            _methods = new ReadOnlyCollection<string>(methods);
        }

        /// <inheritdoc />
        public event EventHandler<ApiNotificationEventArgs> Notification;

        /// <inheritdoc />
        public IReadOnlyCollection<string> Methods => _methods;

        /// <inheritdoc />
        public async Task<IReadOnlyDictionary<string, CompatibilityResourceSnapshot>> PrepareAsync(
            UserScriptInstallation installation,
            DocumentFrame frame,
            IReadOnlyCollection<string> capabilities,
            CancellationToken cancellationToken)
        {
            if (_resources == null || !capabilities.Contains("GM.getResourceText") &&
                !capabilities.Contains("GM.getResourceURL"))
                return new ReadOnlyDictionary<string, CompatibilityResourceSnapshot>(
                    new Dictionary<string, CompatibilityResourceSnapshot>(StringComparer.Ordinal));

            var result = new Dictionary<string, CompatibilityResourceSnapshot>(StringComparer.Ordinal);
            foreach (var declaration in installation.Definition.Metadata.Resources)
            {
                var content = await _resources.GetAsync(installation, declaration, cancellationToken)
                    .ConfigureAwait(false);
                if (content == null)
                    throw new BridgeProtocolException(BridgeErrorCodes.Internal, "The resource provider returned no content.");
                if (content.Bytes.Length > _options.MaxResourceBytes ||
                    (content.Text != null && Encoding.UTF8.GetByteCount(content.Text) > _options.MaxResourceBytes))
                    throw new BridgeProtocolException(BridgeErrorCodes.PayloadTooLarge, "The resource exceeds the configured limit.");
                var text = content.Text ?? Encoding.UTF8.GetString(content.Bytes);
                var url = "data:" + content.MediaType + ";base64," + Convert.ToBase64String(content.Bytes);
                result[declaration.Name] = new CompatibilityResourceSnapshot(text, url);
            }
            return new ReadOnlyDictionary<string, CompatibilityResourceSnapshot>(result);
        }

        /// <inheritdoc />
        public async Task<ApiResult> InvokeAsync(ApiInvocationContext context, CancellationToken cancellationToken)
        {
            switch (context.Method)
            {
                case "GM.getResourceText":
                case "GM.getResourceURL":
                    return await InvokeResourceAsync(context, cancellationToken).ConfigureAwait(false);
                case "GM.xmlHttpRequest":
                    return await InvokeHttpAsync(context, cancellationToken).ConfigureAwait(false);
                default:
                    throw new UnsupportedApiException("The API '" + context.Method + "' is not supported.");
            }
        }

        private async Task<ApiResult> InvokeResourceAsync(
            ApiInvocationContext context,
            CancellationToken cancellationToken)
        {
            var name = ProviderParameters.RequiredString(context.Parameters, "name");
            var declaration = context.Installation.Definition.Metadata.Resources
                .SingleOrDefault(item => item.Name == name);
            if (declaration == null)
                throw ProviderParameters.Invalid("The resource '" + name + "' was not declared.");
            var content = await _resources.GetAsync(
                context.Installation,
                declaration,
                cancellationToken);
            if (content == null)
                throw new BridgeProtocolException(BridgeErrorCodes.Internal, "The resource provider returned no content.");
            if (content.Bytes.Length > _options.MaxResourceBytes ||
                (content.Text != null && Encoding.UTF8.GetByteCount(content.Text) > _options.MaxResourceBytes))
                throw new BridgeProtocolException(BridgeErrorCodes.PayloadTooLarge, "The resource exceeds the configured limit.");
            if (context.Method == "GM.getResourceText")
            {
                var text = content.Text ?? Encoding.UTF8.GetString(content.Bytes);
                return ApiResult.FromValue(text);
            }
            return ApiResult.FromValue(
                "data:" + content.MediaType + ";base64," + Convert.ToBase64String(content.Bytes));
        }

        private async Task<ApiResult> InvokeHttpAsync(
            ApiInvocationContext context,
            CancellationToken cancellationToken)
        {
            ProviderParameters.RequireObject(context.Parameters);
            var operation = ProviderParameters.RequiredString(context.Parameters, "operation");
            switch (operation)
            {
                case "create":
                    return CreateHttpSession(context);
                case "appendBody":
                    return AppendHttpBody(context);
                case "execute":
                    return await ExecuteHttpSessionAsync(context, cancellationToken).ConfigureAwait(false);
                case "readBody":
                    return ReadHttpBody(context);
                case "abort":
                    return AbortHttpSession(context);
                case "release":
                    return ReleaseHttpSession(context);
                default:
                    throw ProviderParameters.Invalid("Unknown HTTP request operation.");
            }
        }

        private ApiResult CreateHttpSession(ApiInvocationContext context)
        {
            if (context.Parameters.TryGetProperty("proxy", out _) ||
                context.Parameters.TryGetProperty("cookiePartition", out _))
                throw new BridgeProtocolException(
                    BridgeErrorCodes.NotSupported,
                    "proxy and cookiePartition are not supported by this host.");
            var urlText = ProviderParameters.RequiredString(context.Parameters, "url");
            if (!Uri.TryCreate(urlText, UriKind.Absolute, out var url) ||
                (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
                throw ProviderParameters.Invalid("url must be an absolute HTTP or HTTPS URL.");
            if (!ConnectAllows(context.Installation.Definition.Metadata.Connects, context.Frame.Url, url))
                throw new BridgeProtocolException(BridgeErrorCodes.PermissionDenied, "The target is not allowed by @connect.");

            var method = ProviderParameters.OptionalString(context.Parameters, "method") ?? "GET";
            method = method.ToUpperInvariant();
            if (!IsHeaderName(method) || BlockedMethods.Contains(method))
                throw ProviderParameters.Invalid("The HTTP method is not allowed.");
            var headers = ReadHeaders(context.Parameters);
            TimeSpan? timeout = null;
            if (context.Parameters.TryGetProperty("timeout", out var timeoutValue))
            {
                if (timeoutValue.ValueKind != JsonValueKind.Number ||
                    !timeoutValue.TryGetInt32(out var milliseconds) || milliseconds <= 0)
                    throw ProviderParameters.Invalid("timeout must be a positive integer.");
                timeout = TimeSpan.FromMilliseconds(milliseconds);
            }
            var xhrId = ProviderParameters.RequiredInt32(context.Parameters, "xhrId");
            if (xhrId <= 0)
                throw ProviderParameters.Invalid("xhrId must be positive.");

            var sessionId = Guid.NewGuid().ToString("D");
            var session = new HttpSession(
                sessionId,
                context.Installation.ScriptKey,
                context.ExecutionId,
                xhrId,
                new UserScriptHttpRequest(
                    method,
                    url,
                    headers,
                    null,
                    timeout,
                    null,
                    redirect => ConnectAllows(context.Installation.Definition.Metadata.Connects, context.Frame.Url, redirect),
                    new UserScriptHttpRequestOptions
                    {
                        Cookie = OptionalSafeString(context.Parameters, "cookie"),
                        Username = OptionalSafeString(context.Parameters, "user"),
                        Password = OptionalSafeString(context.Parameters, "password"),
                        Anonymous = OptionalBoolean(context.Parameters, "anonymous"),
                        OverrideMimeType = OptionalSafeString(context.Parameters, "overrideMimeType")
                    }),
                RaiseHttpNotification);
            lock (_sessionLock)
            {
                ThrowIfDisposed();
                _sessions.Add(SessionKey(context.ExecutionId, sessionId), session);
            }
            session.ReportReadyState(1);
            return ApiResult.FromValue(new { sessionId });
        }

        private ApiResult AppendHttpBody(ApiInvocationContext context)
        {
            var session = GetSession(context);
            var encoded = ProviderParameters.RequiredString(context.Parameters, "chunk");
            byte[] chunk;
            try { chunk = Convert.FromBase64String(encoded); }
            catch (FormatException) { throw ProviderParameters.Invalid("chunk must be valid Base64."); }
            if (chunk.Length > HttpSpoolingBuffer.BridgeChunkSize)
                throw ProviderParameters.Invalid("The HTTP body chunk exceeds 64 KiB.");
            session.AppendRequestBody(chunk);
            return ApiResult.FromValue(new { length = session.RequestBodyLength });
        }

        private async Task<ApiResult> ExecuteHttpSessionAsync(
            ApiInvocationContext context,
            CancellationToken cancellationToken)
        {
            var session = GetSession(context);
            session.PrepareRequestBody();
            var operation = _http.SendAsync(session.Request, session, cancellationToken);
            if (operation == null)
                throw new BridgeProtocolException(BridgeErrorCodes.Internal, "The HTTP service returned no operation.");
            session.Start(operation);
            UserScriptHttpResponse response;
            try
            {
                response = await operation.Completion.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                operation.Abort();
                throw;
            }
            if (response == null)
                throw new BridgeProtocolException(BridgeErrorCodes.Internal, "The HTTP service returned no response.");
            if (response.RedirectUrls.Any(redirect =>
                !ConnectAllows(context.Installation.Definition.Metadata.Connects, context.Frame.Url, redirect)))
                throw new BridgeProtocolException(BridgeErrorCodes.PermissionDenied, "A redirect escaped the @connect allowlist.");
            if (!ConnectAllows(context.Installation.Definition.Metadata.Connects, context.Frame.Url, response.FinalUrl))
                throw new BridgeProtocolException(BridgeErrorCodes.PermissionDenied, "A redirect escaped the @connect allowlist.");
            session.Complete(response);
            return ApiResult.FromValue(new
            {
                status = response.Status,
                statusText = response.StatusText,
                finalUrl = response.FinalUrl.AbsoluteUri,
                responseHeaders = response.RawHeaders,
                mimeType = response.MimeType,
                charset = response.Charset,
                bodyLength = session.ResponseBodyLength
            });
        }

        private ApiResult ReadHttpBody(ApiInvocationContext context)
        {
            var session = GetSession(context);
            if (!context.Parameters.TryGetProperty("offset", out var value) ||
                value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var offset) || offset < 0)
                throw ProviderParameters.Invalid("offset must be a non-negative integer.");
            var chunk = session.ReadResponseBody(offset);
            return ApiResult.FromValue(new
            {
                chunk = Convert.ToBase64String(chunk),
                offset,
                done = offset + chunk.Length >= session.ResponseBodyLength
            });
        }

        private ApiResult AbortHttpSession(ApiInvocationContext context)
        {
            GetSession(context).Abort();
            return ApiResult.FromValue(true);
        }

        private ApiResult ReleaseHttpSession(ApiInvocationContext context)
        {
            var sessionId = ProviderParameters.RequiredString(context.Parameters, "sessionId");
            HttpSession session;
            lock (_sessionLock)
            {
                if (!_sessions.TryGetValue(SessionKey(context.ExecutionId, sessionId), out session))
                    return ApiResult.FromValue(false);
                _sessions.Remove(SessionKey(context.ExecutionId, sessionId));
            }
            session.Dispose();
            return ApiResult.FromValue(true);
        }

        /// <inheritdoc />
        public void OnExecutionEnded(string executionId)
        {
            HttpSession[] sessions;
            lock (_sessionLock)
            {
                var prefix = executionId + ":";
                var keys = _sessions.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
                sessions = keys.Select(key => _sessions[key]).ToArray();
                foreach (var key in keys) _sessions.Remove(key);
            }
            foreach (var session in sessions) session.Dispose();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            HttpSession[] sessions;
            lock (_sessionLock)
            {
                if (_disposed) return;
                _disposed = true;
                sessions = _sessions.Values.ToArray();
                _sessions.Clear();
            }
            foreach (var session in sessions) session.Dispose();
        }

        private HttpSession GetSession(ApiInvocationContext context)
        {
            var sessionId = ProviderParameters.RequiredString(context.Parameters, "sessionId");
            lock (_sessionLock)
            {
                ThrowIfDisposed();
                if (_sessions.TryGetValue(SessionKey(context.ExecutionId, sessionId), out var session))
                    return session;
            }
            throw new BridgeProtocolException(BridgeErrorCodes.SessionExpired, "The HTTP request session is not active.");
        }

        private void RaiseHttpNotification(HttpSession session, string eventName, object value)
        {
            Notification?.Invoke(this, new ApiNotificationEventArgs(
                session.ScriptKey,
                session.ExecutionId,
                eventName,
                JsonSerializer.Serialize(value)));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ResourceAndNetworkApiProvider));
        }

        private static string SessionKey(string executionId, string sessionId)
        {
            return executionId + ":" + sessionId;
        }

        private sealed class HttpSession : IUserScriptHttpObserver, IDisposable
        {
            private readonly object _sync = new object();
            private readonly HttpSpoolingBuffer _requestBody = new HttpSpoolingBuffer();
            private readonly HttpSpoolingBuffer _responseBody = new HttpSpoolingBuffer();
            private readonly Action<HttpSession, string, object> _notify;
            private IHttpRequestOperation _operation;
            private bool _started;
            private bool _responseStarted;
            private bool _loadingReported;
            private bool _disposed;

            public HttpSession(
                string sessionId,
                ScriptKey scriptKey,
                string executionId,
                int xhrId,
                UserScriptHttpRequest request,
                Action<HttpSession, string, object> notify)
            {
                SessionId = sessionId;
                ScriptKey = scriptKey;
                ExecutionId = executionId;
                XhrId = xhrId;
                Request = request;
                _notify = notify;
            }

            public string SessionId { get; }
            public ScriptKey ScriptKey { get; }
            public string ExecutionId { get; }
            public int XhrId { get; }
            public UserScriptHttpRequest Request { get; private set; }
            public long RequestBodyLength => _requestBody.Length;
            public long ResponseBodyLength => _responseBody.Length;

            public void AppendRequestBody(byte[] chunk)
            {
                lock (_sync)
                {
                    ThrowIfDisposed();
                    if (_started) throw ProviderParameters.Invalid("The HTTP request has already started.");
                    _requestBody.Append(chunk, 0, chunk.Length);
                }
            }

            public void PrepareRequestBody()
            {
                lock (_sync)
                {
                    ThrowIfDisposed();
                    if (_started) throw ProviderParameters.Invalid("The HTTP request has already started.");
                    _started = true;
                    if (_requestBody.Length != 0)
                    {
                        Request = new UserScriptHttpRequest(
                            Request.Method,
                            Request.Url,
                            Request.Headers.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase),
                            _requestBody,
                            Request.Timeout,
                            Request.MaxResponseBytes,
                            Request.RedirectAllowed,
                            Request.Options);
                    }
                }
            }

            public void Start(IHttpRequestOperation operation)
            {
                if (operation == null) throw new ArgumentNullException(nameof(operation));
                lock (_sync)
                {
                    ThrowIfDisposed();
                    _operation = operation;
                }
            }

            public void OnResponseStarted(UserScriptHttpResponse response)
            {
                lock (_sync)
                {
                    if (_disposed || _responseStarted) return;
                    _responseStarted = true;
                }
                ReportReadyState(2, response);
            }

            public void OnUploadProgress(long loaded, long? total)
            {
                Notify("xhr-upload-progress", new { xhrId = XhrId, loaded, total });
            }

            public void OnDownloadProgress(long loaded, long? total)
            {
                Notify("xhr-progress", new { xhrId = XhrId, loaded, total });
            }

            public void OnResponseData(byte[] buffer, int offset, int count)
            {
                if (count <= 0) return;
                lock (_sync)
                {
                    ThrowIfDisposed();
                    _responseBody.Append(buffer, offset, count);
                    if (_loadingReported) return;
                    _loadingReported = true;
                }
                ReportReadyState(3);
            }

            public byte[] ReadResponseBody(long offset)
            {
                lock (_sync)
                {
                    ThrowIfDisposed();
                    return _responseBody.Read(offset, HttpSpoolingBuffer.BridgeChunkSize);
                }
            }

            public void Complete(UserScriptHttpResponse response)
            {
                OnResponseStarted(response);
                ReportReadyState(4, response);
            }

            public void Abort()
            {
                IHttpRequestOperation operation;
                lock (_sync)
                {
                    if (_disposed) return;
                    operation = _operation;
                }
                operation?.Abort();
            }

            public void ReportReadyState(int readyState, UserScriptHttpResponse response = null)
            {
                Notify("xhr-state", new
                {
                    xhrId = XhrId,
                    readyState,
                    status = response?.Status ?? 0,
                    statusText = response?.StatusText ?? string.Empty,
                    finalUrl = response?.FinalUrl?.AbsoluteUri,
                    responseHeaders = response?.RawHeaders ?? string.Empty
                });
            }

            public void Dispose()
            {
                IHttpRequestOperation operation;
                lock (_sync)
                {
                    if (_disposed) return;
                    _disposed = true;
                    operation = _operation;
                    _operation = null;
                }
                operation?.Abort();
                _requestBody.Dispose();
                _responseBody.Dispose();
            }

            private void Notify(string eventName, object value)
            {
                try { _notify(this, eventName, value); }
                catch { }
            }

            private void ThrowIfDisposed()
            {
                if (_disposed) throw new ObjectDisposedException(nameof(HttpSession));
            }
        }

        private static IDictionary<string, string> ReadHeaders(JsonElement parameters)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!parameters.TryGetProperty("headers", out var headers) || headers.ValueKind == JsonValueKind.Null)
                return result;
            if (headers.ValueKind != JsonValueKind.Object)
                throw ProviderParameters.Invalid("headers must be an object.");
            foreach (var header in headers.EnumerateObject())
            {
                if (!IsHeaderName(header.Name) ||
                    BlockedHeaders.Contains(header.Name) ||
                    header.Name.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase) ||
                    header.Name.StartsWith("Sec-", StringComparison.OrdinalIgnoreCase) ||
                    header.Value.ValueKind != JsonValueKind.String ||
                    header.Value.GetString().IndexOfAny(new[] { '\r', '\n' }) >= 0 ||
                    result.ContainsKey(header.Name))
                    throw ProviderParameters.Invalid("The header '" + header.Name + "' is not allowed.");
                result.Add(header.Name, header.Value.GetString());
            }
            return result;
        }

        private static string OptionalSafeString(JsonElement parameters, string name)
        {
            var value = ProviderParameters.OptionalString(parameters, name);
            if (value != null && value.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw ProviderParameters.Invalid(name + " cannot contain line breaks.");
            return value;
        }

        private static bool OptionalBoolean(JsonElement parameters, string name)
        {
            if (!parameters.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
                return false;
            if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
                throw ProviderParameters.Invalid(name + " must be a boolean.");
            return value.GetBoolean();
        }

        private static bool IsHeaderName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            const string punctuation = "!#$%&'*+-.^_`|~";
            return name.All(character =>
                (character >= '0' && character <= '9') ||
                (character >= 'A' && character <= 'Z') ||
                (character >= 'a' && character <= 'z') ||
                punctuation.IndexOf(character) >= 0);
        }

        internal static bool ConnectAllows(IReadOnlyList<string> declarations, Uri source, Uri target)
        {
            if (declarations == null || declarations.Count == 0 || target == null || !target.IsAbsoluteUri ||
                (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps))
                return false;
            var targetHost = new IdnMapping().GetAscii(target.IdnHost).ToLowerInvariant();
            foreach (var declaration in declarations)
            {
                if (declaration == "*")
                    return true;
                if (declaration == "self" && source != null &&
                    source.Scheme == target.Scheme &&
                    string.Equals(source.IdnHost, target.IdnHost, StringComparison.OrdinalIgnoreCase) &&
                    source.Port == target.Port)
                    return true;
                var value = declaration;
                if (Uri.TryCreate(declaration, UriKind.Absolute, out var declaredUri))
                {
                    if (declaredUri.Scheme != target.Scheme || declaredUri.Port != target.Port)
                        continue;
                    value = declaredUri.IdnHost;
                }
                var subdomains = value.StartsWith("*.", StringComparison.Ordinal);
                if (subdomains)
                    value = value.Substring(2);
                try
                {
                    value = new IdnMapping().GetAscii(value).ToLowerInvariant();
                }
                catch (ArgumentException)
                {
                    continue;
                }
                if (subdomains
                    ? targetHost.Length > value.Length + 1 && targetHost.EndsWith("." + value, StringComparison.Ordinal)
                    : targetHost == value)
                    return true;
            }
            return false;
        }

    }

    internal static class ProviderParameters
    {
        public static void RequireObject(JsonElement parameters)
        {
            if (parameters.ValueKind != JsonValueKind.Object)
                throw Invalid("params must be an object.");
        }

        public static string RequiredString(JsonElement parameters, string name)
        {
            RequireObject(parameters);
            if (!parameters.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
                throw Invalid(name + " must be a string.");
            var result = value.GetString();
            if (string.IsNullOrEmpty(result))
                throw Invalid(name + " cannot be empty.");
            return result;
        }

        public static string OptionalString(JsonElement parameters, string name)
        {
            RequireObject(parameters);
            if (!parameters.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
                return null;
            if (value.ValueKind != JsonValueKind.String)
                throw Invalid(name + " must be a string.");
            return value.GetString();
        }

        public static int RequiredInt32(JsonElement parameters, string name)
        {
            RequireObject(parameters);
            if (!parameters.TryGetProperty(name, out var value) ||
                value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
                throw Invalid(name + " must be an integer.");
            return result;
        }

        public static bool OptionalBoolean(JsonElement parameters, string name, bool defaultValue = false)
        {
            RequireObject(parameters);
            if (!parameters.TryGetProperty(name, out var value))
                return defaultValue;
            if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
                throw Invalid(name + " must be a boolean.");
            return value.GetBoolean();
        }

        public static BridgeProtocolException Invalid(string message)
        {
            return new BridgeProtocolException(BridgeErrorCodes.InvalidParams, message);
        }
    }
}
