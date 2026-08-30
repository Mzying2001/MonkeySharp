using CefSharp;
using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Domain;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.CefSharp
{
    /// <summary>
    /// Executes userscript HTTP requests through CefSharp's URL request API.
    /// </summary>
    public sealed class CefSharpHttpRequestService : IHttpRequestService, IDisposable
    {
        private const string HttpResourceType = "XHR";
        private static readonly HashSet<string> RestrictedRequestHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Host", "Content-Length", "Cookie", "Origin", "Referer", "Connection", "Proxy-Connection",
            "Transfer-Encoding", "Upgrade", "Keep-Alive", "TE", "Trailer"
        };
        private readonly Func<IRequestContext> _contextAccessor;
        private readonly IWebRequestService _webRequestService;
        private readonly object _sync = new object();
        private readonly HashSet<Operation> _operations = new HashSet<Operation>();
        private bool _disposed;

        /// <summary>Initializes a service for a fixed request context.</summary>
        public CefSharpHttpRequestService(IRequestContext context, IWebRequestService webRequestService = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            _contextAccessor = () => context;
            _webRequestService = webRequestService;
        }

        /// <summary>Initializes a service that resolves the current request context per request.</summary>
        public CefSharpHttpRequestService(Func<IRequestContext> contextAccessor, IWebRequestService webRequestService = null)
        {
            _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
            _webRequestService = webRequestService;
        }

        /// <summary>Initializes a service with a context factory and shared webRequest rules.</summary>
        public CefSharpHttpRequestService(Func<IRequestContext> contextAccessor, IWebRequestService webRequestService,
            int maximumRedirects)
        {
            if (maximumRedirects <= 0) throw new ArgumentOutOfRangeException(nameof(maximumRedirects));
            _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
            _webRequestService = webRequestService;
            MaximumRedirects = maximumRedirects;
        }

        /// <summary>Gets the maximum number of redirects followed by a request.</summary>
        public int MaximumRedirects { get; } = 20;

        /// <inheritdoc />
        public IHttpRequestOperation SendAsync(
            UserScriptHttpRequest request,
            IUserScriptHttpObserver observer,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            var operation = new Operation(this, request, observer, cancellationToken, _contextAccessor, _webRequestService);
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(CefSharpHttpRequestService));
                _operations.Add(operation);
            }
            operation.Start();
            return operation;
        }

        private void Remove(Operation operation)
        {
            lock (_sync) _operations.Remove(operation);
        }

        private void ThrowIfDisposed()
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(CefSharpHttpRequestService));
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Operation[] operations;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                operations = _operations.ToArray();
            }
            foreach (var operation in operations)
                operation.Abort();
        }

        private sealed class Operation : IHttpRequestOperation, IUrlRequestClient
        {
            private readonly CefSharpHttpRequestService _owner;
            private readonly UserScriptHttpRequest _request;
            private readonly IUserScriptHttpObserver _observer;
            private readonly CancellationTokenSource _cancellation;
            private readonly Func<IRequestContext> _contextAccessor;
            private readonly IWebRequestService _webRequests;
            private readonly TaskCompletionSource<UserScriptHttpResponse> _completion =
                new TaskCompletionSource<UserScriptHttpResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly List<Uri> _redirects = new List<Uri>();
            private readonly object _sync = new object();
            private IUrlRequest _urlRequest;
            private IRequest _cefRequest;
            private Uri _currentUrl;
            private Timer _timeout;
            private CancellationTokenRegistration _cancellationRegistration;
            private int _redirectCount;
            private int _completed;
            private UserScriptHttpResponse _responseMetadata;
            private long _downloaded;

            public Operation(CefSharpHttpRequestService owner, UserScriptHttpRequest request,
                IUserScriptHttpObserver observer,
                CancellationToken cancellationToken, Func<IRequestContext> contextAccessor,
                IWebRequestService webRequests)
            {
                _owner = owner;
                _request = request;
                _observer = observer;
                _contextAccessor = contextAccessor;
                _webRequests = webRequests;
                _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Completion = _completion.Task;
            }

            public Task<UserScriptHttpResponse> Completion { get; }

            public void Start()
            {
                _cancellationRegistration = _cancellation.Token.Register(() =>
                    Fail(new OperationCanceledException(_cancellation.Token), true));
                if (_request.Timeout.HasValue)
                    _timeout = new Timer(_ => Fail(new BridgeProtocolException(
                        BridgeErrorCodes.Timeout, "The HTTP request timed out."), true), null,
                        _request.Timeout.Value, Timeout.InfiniteTimeSpan);
                QueueUi(() => BeginOnUi(_request.Url));
            }

            public void Abort()
            {
                if (Interlocked.CompareExchange(ref _completed, 0, 0) != 0)
                    return;
                Fail(new OperationCanceledException("The HTTP request was aborted."), true);
            }

            private void BeginOnUi(Uri url)
            {
                if (IsCompleted) return;
                try
                {
                    var context = _contextAccessor();
                    if (context == null)
                        throw new InvalidOperationException("No active CefSharp request context is attached.");

#if CEF_SHARP_LEGACY_REQUEST_FACTORY
                    var cefRequest = new Request();
#else
                    var cefRequest = Request.Create();
#endif
                    _cefRequest = cefRequest;
                    _currentUrl = url;
                    cefRequest.Url = url.AbsoluteUri;
                    cefRequest.Method = _request.Method;
                    cefRequest.Flags = UrlRequestFlags.AllowStoredCredentials |
                        UrlRequestFlags.StopOnRedirect | UrlRequestFlags.ReportUploadProgress;
                    foreach (var header in _request.Headers)
                        cefRequest.SetHeaderByName(header.Key, header.Value, true);
                    if (_request.Body != null && _request.Body.Length != 0)
                    {
                        cefRequest.InitializePostData();
                        using (var body = _request.Body.OpenRead())
                        {
                            var buffer = new byte[64 * 1024];
                            int count;
                            while ((count = body.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                IPostDataElement element;
#if CEF_SHARP_LEGACY_REQUEST_FACTORY
                                element = new PostDataElement();
#else
                                element = PostDataElement.Create();
#endif
                                var bytes = new byte[count];
                                Buffer.BlockCopy(buffer, 0, bytes, 0, count);
                                element.Bytes = bytes;
                                cefRequest.PostData.AddElement(element);
                            }
                        }
                    }

                    if (_webRequests != null)
                    {
                        var before = _webRequests.Evaluate(CreateEvent(WebRequestPhase.OnBeforeRequest, url, cefRequest));
                        if (before.Kind == WebRequestActionKind.Block)
                            throw new BridgeProtocolException(BridgeErrorCodes.PermissionDenied,
                                "The HTTP request was blocked by a webRequest rule.");
                        if (before.Kind == WebRequestActionKind.Redirect && !string.IsNullOrEmpty(before.RedirectUrl))
                        {
                            var redirect = Resolve(url, before.RedirectUrl);
                            FollowRedirectOnUi(redirect, cefRequest);
                            return;
                        }
                        ApplyHeaders(cefRequest, before.Headers);
                        var headers = _webRequests.Evaluate(CreateEvent(WebRequestPhase.OnBeforeSendHeaders, url, cefRequest));
                        if (headers.Kind == WebRequestActionKind.Block)
                            throw new BridgeProtocolException(BridgeErrorCodes.PermissionDenied,
                                "The HTTP request was blocked by a webRequest rule.");
                        if (headers.Kind == WebRequestActionKind.Redirect && !string.IsNullOrEmpty(headers.RedirectUrl))
                        {
                            var redirect = Resolve(url, headers.RedirectUrl);
                            FollowRedirectOnUi(redirect, cefRequest);
                            return;
                        }
                        ApplyHeaders(cefRequest, headers.Headers);
                    }

                    _urlRequest = new global::CefSharp.UrlRequest(cefRequest, this, context);
                    if (_urlRequest == null)
                        throw new InvalidOperationException("CefSharp rejected the URL request.");
                }
                catch (Exception exception)
                {
                    Fail(MapException(exception), true);
                }
            }

            private void FollowRedirectOnUi(Uri target, IRequest oldRequest, IUrlRequest oldUrlRequest = null)
            {
                if (target == null || (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps))
                    throw new BridgeProtocolException(BridgeErrorCodes.PermissionDenied, "The redirect target is invalid.");
                if (!_request.RedirectAllowed(target))
                    throw new BridgeProtocolException(BridgeErrorCodes.PermissionDenied, "A redirect escaped the @connect allowlist.");
                if (++_redirectCount > _owner.MaximumRedirects)
                    throw new BridgeProtocolException(BridgeErrorCodes.Internal, "The HTTP request exceeded the maximum redirect count.");
                _redirects.Add(target);
                DisposeRequestOnUi(oldRequest, oldUrlRequest);
                BeginOnUi(target);
            }

            public bool GetAuthCredentials(bool isProxy, string host, int port, string realm, string scheme, IAuthCallback callback)
            {
                if (_webRequests == null) return false;
                var url = _currentUrl?.AbsoluteUri ?? _request.Url.AbsoluteUri;
                var requestId = responseIdentifier(null);
                var decision = _webRequests.Evaluate(new WebRequestEvent(
                    WebRequestPhase.OnAuthRequired, requestId, url, _request.Method, HttpResourceType));
                if (decision.Kind != WebRequestActionKind.AuthResponse) return false;
                callback.Continue(decision.Username, decision.Password);
                return true;
            }

            public void OnUploadProgress(IUrlRequest request, long current, long total)
            {
                try { _observer.OnUploadProgress(current, total > 0 ? (long?)total : null); }
                catch { }
            }

            public void OnDownloadProgress(IUrlRequest request, long current, long total)
            {
                try { _observer.OnDownloadProgress(current, total > 0 ? (long?)total : null); }
                catch { }
            }

            public void OnDownloadData(IUrlRequest request, Stream data)
            {
                if (data == null || IsCompleted) return;
                try
                {
                    EnsureResponseStarted(request);
                    var buffer = new byte[64 * 1024];
                    int count;
                    while ((count = data.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        lock (_sync)
                        {
                            if (_request.MaxResponseBytes.HasValue &&
                                _downloaded + count > _request.MaxResponseBytes.Value)
                            {
                                Fail(new BridgeProtocolException(BridgeErrorCodes.PayloadTooLarge,
                                    "The HTTP response exceeds the configured limit."), true);
                                return;
                            }
                            _downloaded += count;
                        }
                        _observer.OnResponseData(buffer, 0, count);
                    }
                }
                catch (Exception exception)
                {
                    Fail(MapException(exception), true);
                }
            }

            public void OnRequestComplete(IUrlRequest request)
            {
                QueueUi(() => CompleteOnUi(request));
            }

            private void CompleteOnUi(IUrlRequest request)
            {
                if (IsCompleted) return;
                IResponse response = null;
                try
                {
                    var status = request?.RequestStatus ?? UrlRequestStatus.Unknown;
                    response = request?.Response;
                    if (status != UrlRequestStatus.Success)
                    {
                        var message = "CefSharp URL request failed: " + status;
                        throw new BridgeProtocolException(status == UrlRequestStatus.Canceled
                            ? BridgeErrorCodes.Canceled : BridgeErrorCodes.Internal, message);
                    }
                    if (response == null)
                        throw new InvalidOperationException("CefSharp returned no HTTP response.");

                    var responseHeaders = ToHeaders(response.Headers);
                    var location = response.Headers == null ? null : response.Headers["Location"];
                    if (response.StatusCode >= 300 && response.StatusCode < 400 && !string.IsNullOrEmpty(location))
                    {
                        var redirect = Resolve(_currentUrl, location);
                        lock (_sync)
                        {
                            _responseMetadata = null;
                            _downloaded = 0;
                        }
                        DisposeResponseOnUi(response);
                        FollowRedirectOnUi(redirect, _cefRequest, request);
                        return;
                    }

                    if (_webRequests != null)
                    {
                        var headersDecision = _webRequests.Evaluate(new WebRequestEvent(
                            WebRequestPhase.OnHeadersReceived, responseIdentifier(request), _currentUrl.AbsoluteUri,
                            _request.Method, HttpResourceType, responseHeaders, response.StatusCode));
                        if (headersDecision.Kind == WebRequestActionKind.Block)
                            throw new BridgeProtocolException(BridgeErrorCodes.PermissionDenied,
                                "The HTTP response was blocked by a webRequest rule.");
                        foreach (var header in headersDecision.Headers)
                            responseHeaders[header.Key] = header.Value;
                        _webRequests.Evaluate(new WebRequestEvent(
                            WebRequestPhase.OnResponseStarted, responseIdentifier(request), _currentUrl.AbsoluteUri,
                            _request.Method, HttpResourceType, responseHeaders, response.StatusCode));
                    }

                    var result = EnsureResponseStarted(request, response, responseHeaders);
                    if (_webRequests != null)
                        _webRequests.Evaluate(new WebRequestEvent(WebRequestPhase.OnCompleted,
                            responseIdentifier(request), _currentUrl.AbsoluteUri, _request.Method, HttpResourceType,
                            responseHeaders, response.StatusCode));
                    DisposeResponseOnUi(response);
                    DisposeRequestOnUi(_cefRequest, request);
                    Complete(result);
                }
                catch (Exception exception)
                {
                    Fail(MapException(exception), false);
                    DisposeResponseOnUi(response);
                    DisposeRequestOnUi(_cefRequest, request);
                }
            }

            private void PublishError(Exception exception)
            {
                if (_webRequests == null) return;
                try
                {
                    _webRequests.Evaluate(new WebRequestEvent(WebRequestPhase.OnErrorOccurred,
                        responseIdentifier(null), _currentUrl?.AbsoluteUri ?? _request.Url.AbsoluteUri,
                        _request.Method, HttpResourceType, null, null, exception?.Message));
                }
                catch { }
            }

            private ulong responseIdentifier(IUrlRequest request)
            {
                lock (_sync) return _cefRequest == null ? 0UL : _cefRequest.Identifier;
            }

            private void QueueUi(Action callback)
            {
                try
                {
                    Cef.UIThreadTaskFactory.StartNew(callback).ContinueWith(task =>
                    {
                        if (task.IsFaulted)
                            Fail(MapException(task.Exception.InnerException), true);
                    }, TaskScheduler.Default);
                }
                catch (Exception exception)
                {
                    Fail(MapException(exception), true);
                }
            }

            private void Fail(Exception exception, bool abort)
            {
                if (Interlocked.Exchange(ref _completed, 1) != 0) return;
                _timeout?.Dispose();
                _cancellationRegistration.Dispose();
                _cancellation.Dispose();
                PublishError(exception);
                if (abort)
                    QueueUi(() => DisposeRequestOnUi(_cefRequest, _urlRequest));
                _completion.TrySetException(exception);
                _owner.Remove(this);
            }

            private void Complete(UserScriptHttpResponse response)
            {
                if (Interlocked.Exchange(ref _completed, 1) != 0) return;
                _timeout?.Dispose();
                _cancellationRegistration.Dispose();
                _cancellation.Dispose();
                _completion.TrySetResult(response);
                _owner.Remove(this);
            }

            private bool IsCompleted => Interlocked.CompareExchange(ref _completed, 0, 0) != 0;

            private void DisposeRequestOnUi(IRequest request, IUrlRequest urlRequest)
            {
                try { urlRequest?.Dispose(); } catch { }
                try { (request as IDisposable)?.Dispose(); } catch { }
                if (ReferenceEquals(_urlRequest, urlRequest)) _urlRequest = null;
                if (ReferenceEquals(_cefRequest, request)) _cefRequest = null;
            }

            private static void DisposeResponseOnUi(IResponse response)
            {
                try { (response as IDisposable)?.Dispose(); } catch { }
            }

            private WebRequestEvent CreateEvent(WebRequestPhase phase, Uri url, IRequest request)
            {
                return new WebRequestEvent(phase, request.Identifier, url.AbsoluteUri, request.Method,
                    HttpResourceType, ToHeaders(request));
            }

            private static void ApplyHeaders(IRequest request, IReadOnlyDictionary<string, string> headers)
            {
                foreach (var header in headers)
                {
                    if (RestrictedRequestHeaders.Contains(header.Key) ||
                        header.Key.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase) ||
                        header.Key.StartsWith("Sec-", StringComparison.OrdinalIgnoreCase))
                        continue;
                    request.SetHeaderByName(header.Key, header.Value, true);
                }
            }

            private static IDictionary<string, string> ToHeaders(NameValueCollection headers)
            {
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in headers?.AllKeys ?? new string[0]) result[name] = headers[name];
                return result;
            }

            private static IDictionary<string, string> ToHeaders(IRequest request)
            {
                return ToHeaders(request?.Headers);
            }

            private static Uri Resolve(Uri baseUri, string value)
            {
                if (Uri.TryCreate(value, UriKind.Absolute, out var absolute)) return absolute;
                if (Uri.TryCreate(baseUri, value, out var relative)) return relative;
                throw new BridgeProtocolException(BridgeErrorCodes.PermissionDenied, "The redirect target is invalid.");
            }

            private UserScriptHttpResponse EnsureResponseStarted(
                IUrlRequest request,
                IResponse response = null,
                IDictionary<string, string> headers = null)
            {
                lock (_sync)
                {
                    if (_responseMetadata != null) return _responseMetadata;
                    response = response ?? request?.Response;
                    if (response == null)
                        throw new InvalidOperationException("CefSharp returned no HTTP response metadata.");
                    headers = headers ?? ToHeaders(response.Headers);
                    _responseMetadata = new UserScriptHttpResponse(
                        response.StatusCode,
                        response.StatusText,
                        _currentUrl,
                        headers,
                        ToRawHeaders(response.Headers),
                        response.MimeType,
                        response.Charset,
                        _redirects);
                }
                try { _observer.OnResponseStarted(_responseMetadata); }
                catch { }
                return _responseMetadata;
            }

            private static string ToRawHeaders(NameValueCollection headers)
            {
                var builder = new StringBuilder();
                foreach (var name in headers?.AllKeys ?? new string[0])
                {
                    foreach (var value in headers.GetValues(name) ?? new string[0])
                        builder.Append(name).Append(": ").Append(value).Append("\r\n");
                }
                return builder.ToString();
            }

            private static Exception MapException(Exception exception)
            {
                if (exception is BridgeProtocolException) return exception;
                if (exception is OperationCanceledException)
                    return exception;
                return new BridgeProtocolException(BridgeErrorCodes.Internal,
                    exception?.Message ?? "The HTTP request failed.");
            }
        }
    }
}
