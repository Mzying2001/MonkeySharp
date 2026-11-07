using CefSharp;
using Mzying2001.MonkeySharp.Core.Apis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mzying2001.MonkeySharp.Core.Runtime;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace Mzying2001.MonkeySharp.CefSharp
{
    /// <summary>Bridges synchronous Chromium request callbacks to an <see cref="IWebRequestService"/>.</summary>
    public sealed class CefSharpWebRequestHandler : IRequestHandler, IDisposable
    {
        private static readonly WebRequestDecision DetachedDecision = new WebRequestDecision();
        private readonly object _sync = new object();
        private IWebRequestService _service;
        private readonly Func<IFrame, IRequest, DocumentFrame> _frame;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        /// <summary>Initializes a handler over an application-owned web request service.</summary>
        public CefSharpWebRequestHandler(IWebRequestService service, Func<IFrame, IRequest, DocumentFrame> frame = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _frame = frame;
        }

        /// <summary>Stops forwarding callbacks and waits for active service evaluations without disposing the application-owned service.</summary>
        public void Dispose()
        {
            lock (_sync) { _service = null; _lifetime.Cancel(); }
        }

        private WebRequestDecision Evaluate(WebRequestEvent request)
        {
            lock (_sync) return _service == null ? DetachedDecision : _service.Evaluate(request);
        }

        private Task<WebRequestDecision> EvaluateAsync(WebRequestEvent request)
        {
            lock (_sync)
            {
                if (_service == null) return Task.FromResult(DetachedDecision);
                return _service is IAsyncWebRequestService asyncService
                    ? asyncService.EvaluateAsync(request, _lifetime.Token) : Task.FromResult(_service.Evaluate(request));
            }
        }

        /// <inheritdoc />
        public bool OnBeforeBrowse(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool userGesture, bool isRedirect)
        { return false; }
        /// <inheritdoc />
        public void OnDocumentAvailableInMainFrame(IWebBrowser chromiumWebBrowser, IBrowser browser) { }
        /// <inheritdoc />
        public bool OnOpenUrlFromTab(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, string targetUrl, WindowOpenDisposition targetDisposition, bool userGesture)
        { return false; }
        /// <inheritdoc />
        public IResourceRequestHandler GetResourceRequestHandler(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool isNavigation, bool isDownload, string requestInitiator, ref bool disableDefaultHandling)
        { return new ResourceHandler(this); }
        /// <inheritdoc />
        public bool GetAuthCredentials(IWebBrowser chromiumWebBrowser, IBrowser browser, string originUrl, bool isProxy, string host, int port, string realm, string scheme, IAuthCallback callback)
        {
            return false;
        }
        /// <inheritdoc />
        public bool OnCertificateError(IWebBrowser chromiumWebBrowser, IBrowser browser, CefErrorCode errorCode, string requestUrl, ISslInfo sslInfo, IRequestCallback callback) { return false; }
        /// <inheritdoc />
        public bool OnQuotaRequest(IWebBrowser chromiumWebBrowser, IBrowser browser, string originUrl, long newSize, IRequestCallback callback) { return false; }
        /// <inheritdoc />
        public void OnPluginCrashed(IWebBrowser chromiumWebBrowser, IBrowser browser, string pluginPath) { }
        /// <inheritdoc />
        public bool OnSelectClientCertificate(IWebBrowser chromiumWebBrowser, IBrowser browser, bool isProxy, string host, int port, X509Certificate2Collection certificates, ISelectClientCertificateCallback callback) { return false; }
        /// <inheritdoc />
        public void OnRenderViewReady(IWebBrowser chromiumWebBrowser, IBrowser browser) { }
#if CEF_SHARP_EXTENDED_RENDER_TERMINATED
        /// <inheritdoc />
        public void OnRenderProcessTerminated(IWebBrowser chromiumWebBrowser, IBrowser browser, CefTerminationStatus status, int errorCode, string errorMessage) { }
#else
        /// <inheritdoc />
        public void OnRenderProcessTerminated(IWebBrowser chromiumWebBrowser, IBrowser browser, CefTerminationStatus status) { }
#endif

        private sealed class ResourceHandler : IResourceRequestHandler
        {
            private readonly CefSharpWebRequestHandler _owner;

            public ResourceHandler(CefSharpWebRequestHandler owner)
            {
                _owner = owner;
            }
            public ICookieAccessFilter GetCookieAccessFilter(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request) { return null; }
            public CefReturnValue OnBeforeResourceLoad(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IRequestCallback callback)
            {
                var evaluation = _owner.EvaluateAsync(CreateEvent(WebRequestPhase.OnBeforeRequest, frame, request));
                _owner.Evaluate(CreateEvent(WebRequestPhase.OnBeforeSendHeaders, frame, request));
                if (evaluation.IsCompleted)
                {
                    try { return ApplyDecision(request, evaluation.GetAwaiter().GetResult()); }
                    catch { return CefReturnValue.Cancel; }
                }
                ContinueAsync(evaluation, request, callback);
                return CefReturnValue.ContinueAsync;
            }
            private static CefReturnValue ApplyDecision(IRequest request, WebRequestDecision decision)
            {
                if (decision.Kind == WebRequestActionKind.Block) return CefReturnValue.Cancel;
                if (decision.Kind == WebRequestActionKind.Redirect) request.Url = decision.RedirectUrl;
                return CefReturnValue.Continue;
            }
            private async void ContinueAsync(Task<WebRequestDecision> evaluation, IRequest request, IRequestCallback callback)
            {
                using (callback)
                {
                    try
                    {
                        var decision = await evaluation.ConfigureAwait(false);
                        if (_owner._lifetime.IsCancellationRequested || ApplyDecision(request, decision) == CefReturnValue.Cancel) callback.Cancel();
                        else callback.Continue(true);
                    }
                    catch { if (!callback.IsDisposed) callback.Cancel(); }
                }
            }

            public IResourceHandler GetResourceHandler(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request) { return null; }
            public void OnResourceRedirect(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IResponse response, ref string newUrl)
            {
                var decision = _owner.Evaluate(CreateEvent(WebRequestPhase.OnBeforeRequest, frame, request, response, null, newUrl));
                if (decision.Kind == WebRequestActionKind.Block) newUrl = "about:blank";
                else if (decision.Kind == WebRequestActionKind.Redirect && !string.IsNullOrEmpty(decision.RedirectUrl)) newUrl = decision.RedirectUrl;
            }
            public bool OnResourceResponse(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IResponse response)
            {
                var decision = _owner.Evaluate(CreateEvent(WebRequestPhase.OnHeadersReceived, frame, request, response));
                _owner.Evaluate(CreateEvent(WebRequestPhase.OnResponseStarted, frame, request, response));
                return false;
            }
            public IResponseFilter GetResourceResponseFilter(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IResponse response)
            { return null; }
            public void OnResourceLoadComplete(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IResponse response, UrlRequestStatus status, long receivedContentLength)
            {
                var phase = status == UrlRequestStatus.Success ? WebRequestPhase.OnCompleted : WebRequestPhase.OnErrorOccurred;
                _owner.Evaluate(CreateEvent(phase, frame, request, response, status.ToString()));
            }
            public bool OnProtocolExecution(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request) { return false; }
            public void Dispose()
            {
            }

            private WebRequestEvent CreateEvent(WebRequestPhase phase, IFrame frame, IRequest request, IResponse response = null, string error = null, string url = null)
            {
                var responsePhase = phase == WebRequestPhase.OnHeadersReceived ||
                    phase == WebRequestPhase.OnResponseStarted ||
                    phase == WebRequestPhase.OnCompleted ||
                    phase == WebRequestPhase.OnErrorOccurred;
                return new WebRequestEvent(phase, request.Identifier, url ?? request.Url, request.Method, ResourceName(request.ResourceType.ToString()),
                    responsePhase ? ToHeaders(response) : ToHeaders(request), response?.StatusCode, error, _owner._frame?.Invoke(frame, request));
            }
            private static string ResourceName(string type)
            {
                switch (type.ToLowerInvariant())
                {
                    case "subframe": return "sub_frame";
                    case "script": return "script";
                    case "xhr": case "xmlhttprequest": return "xhr";
                    case "websocket": return "websocket";
                    default: return type.ToLowerInvariant();
                }
            }
            private static IDictionary<string, string> ToHeaders(IRequest request)
            {
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in request.Headers.AllKeys ?? new string[0]) headers[name] = request.Headers[name];
                return headers;
            }
            private static IDictionary<string, string> ToHeaders(IResponse response)
            {
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in response?.Headers?.AllKeys ?? new string[0])
                    headers[name] = response.Headers[name];
                return headers;
            }

        }
    }

    /// <summary>Composes existing request handlers without silently replacing them.</summary>
    public sealed class CefSharpWebRequestHandlerMultiplexer : IRequestHandler
    {
        private readonly object _sync = new object();
        private readonly List<IRequestHandler> _handlers = new List<IRequestHandler>();
        /// <summary>Adds a handler once; callbacks fan out to a snapshot of current handlers.</summary>
        public void Add(IRequestHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            lock (_sync) if (!_handlers.Contains(handler)) _handlers.Add(handler);
        }
        /// <summary>Removes a previously added handler.</summary>
        public void Remove(IRequestHandler handler)
        {
            if (handler != null)
            {
                lock (_sync)
                    _handlers.Remove(handler);
            }
        }
        /// <inheritdoc />
        public bool OnBeforeBrowse(IWebBrowser a, IBrowser b, IFrame c, IRequest d, bool e, bool f) => Snapshot().Any(x => x.OnBeforeBrowse(a, b, c, d, e, f));
        /// <inheritdoc />
        public void OnDocumentAvailableInMainFrame(IWebBrowser a, IBrowser b) { foreach (var x in Snapshot()) x.OnDocumentAvailableInMainFrame(a, b); }
        /// <inheritdoc />
        public bool OnOpenUrlFromTab(IWebBrowser a, IBrowser b, IFrame c, string d, WindowOpenDisposition e, bool f) => Snapshot().Any(x => x.OnOpenUrlFromTab(a, b, c, d, e, f));
        /// <inheritdoc />
        public IResourceRequestHandler GetResourceRequestHandler(IWebBrowser a, IBrowser b, IFrame c, IRequest d, bool e, bool f, string g, ref bool h)
        {
            var handlers = new List<IResourceRequestHandler>();
            foreach (var x in Snapshot())
            {
                var handler = x.GetResourceRequestHandler(a, b, c, d, e, f, g, ref h);
                if (handler != null) handlers.Add(handler);
            }
            if (handlers.Count == 0) return null;
            if (handlers.Count == 1) return handlers[0];
            return new ResourceRequestHandlerMultiplexer(handlers);
        }
        /// <inheritdoc />
        public bool GetAuthCredentials(IWebBrowser a, IBrowser b, string c, bool d, string e, int f, string g, string h, IAuthCallback i) => Snapshot().Any(x => x.GetAuthCredentials(a, b, c, d, e, f, g, h, i));
        /// <inheritdoc />
        public bool OnCertificateError(IWebBrowser a, IBrowser b, CefErrorCode c, string d, ISslInfo e, IRequestCallback f) => Snapshot().Any(x => x.OnCertificateError(a, b, c, d, e, f));
        /// <inheritdoc />
        public bool OnQuotaRequest(IWebBrowser a, IBrowser b, string c, long d, IRequestCallback e) =>
            Snapshot().Any(x => InvokeQuotaRequest(x, a, b, c, d, e));
        /// <inheritdoc />
        public void OnPluginCrashed(IWebBrowser a, IBrowser b, string c)
        {
            foreach (var x in Snapshot()) InvokePluginCrashed(x, a, b, c);
        }
        /// <inheritdoc />
        public bool OnSelectClientCertificate(IWebBrowser a, IBrowser b, bool c, string d, int e, X509Certificate2Collection f, ISelectClientCertificateCallback g) => Snapshot().Any(x => x.OnSelectClientCertificate(a, b, c, d, e, f, g));
        /// <inheritdoc />
        public void OnRenderViewReady(IWebBrowser a, IBrowser b) { foreach (var x in Snapshot()) x.OnRenderViewReady(a, b); }
#if CEF_SHARP_EXTENDED_RENDER_TERMINATED
        /// <inheritdoc />
        public void OnRenderProcessTerminated(IWebBrowser a, IBrowser b, CefTerminationStatus c, int d, string e) { foreach (var x in Snapshot()) x.OnRenderProcessTerminated(a, b, c, d, e); }
#else
        /// <inheritdoc />
        public void OnRenderProcessTerminated(IWebBrowser a, IBrowser b, CefTerminationStatus c) { foreach (var x in Snapshot()) x.OnRenderProcessTerminated(a, b, c); }
#endif

        private IRequestHandler[] Snapshot()
        {
            lock (_sync) return _handlers.ToArray();
        }

        private static bool InvokeQuotaRequest(IRequestHandler handler, IWebBrowser browser, IBrowser cefBrowser,
            string originUrl, long newSize, IRequestCallback callback)
        {
            var method = handler.GetType().GetMethod("OnQuotaRequest", new[] { typeof(IWebBrowser), typeof(IBrowser), typeof(string), typeof(long), typeof(IRequestCallback) });
            return method != null && (bool)method.Invoke(handler, new object[] { browser, cefBrowser, originUrl, newSize, callback });
        }

        private static void InvokePluginCrashed(IRequestHandler handler, IWebBrowser browser, IBrowser cefBrowser, string pluginPath)
        {
            var method = handler.GetType().GetMethod("OnPluginCrashed", new[] { typeof(IWebBrowser), typeof(IBrowser), typeof(string) });
            method?.Invoke(handler, new object[] { browser, cefBrowser, pluginPath });
        }

        private sealed class ResourceRequestHandlerMultiplexer : IResourceRequestHandler
        {
            private readonly IReadOnlyList<IResourceRequestHandler> _handlers;

            public ResourceRequestHandlerMultiplexer(IReadOnlyList<IResourceRequestHandler> handlers)
            {
                _handlers = handlers;
            }

            public ICookieAccessFilter GetCookieAccessFilter(IWebBrowser a, IBrowser b, IFrame c, IRequest d)
            {
                var filters = _handlers.Select(x => x.GetCookieAccessFilter(a, b, c, d))
                    .Where(x => x != null).ToArray();
                if (filters.Length == 0) return null;
                if (filters.Length == 1) return filters[0];
                return new CookieAccessFilterMultiplexer(filters);
            }

            public CefReturnValue OnBeforeResourceLoad(IWebBrowser a, IBrowser b, IFrame c, IRequest d, IRequestCallback e)
            {
                var result = CefReturnValue.Continue;
                foreach (var handler in _handlers)
                {
                    var current = handler.OnBeforeResourceLoad(a, b, c, d, e);
                    if (current == CefReturnValue.Cancel) return current;
                    if (current == CefReturnValue.ContinueAsync) result = current;
                }
                return result;
            }

            public IResourceHandler GetResourceHandler(IWebBrowser a, IBrowser b, IFrame c, IRequest d)
            {
                foreach (var handler in _handlers)
                {
                    var result = handler.GetResourceHandler(a, b, c, d);
                    if (result != null) return result;
                }
                return null;
            }

            public void OnResourceRedirect(IWebBrowser a, IBrowser b, IFrame c, IRequest d, IResponse e, ref string f)
            {
                foreach (var handler in _handlers) handler.OnResourceRedirect(a, b, c, d, e, ref f);
            }

            public bool OnResourceResponse(IWebBrowser a, IBrowser b, IFrame c, IRequest d, IResponse e)
            {
                var handled = false;
                foreach (var handler in _handlers) handled |= handler.OnResourceResponse(a, b, c, d, e);
                return handled;
            }

            public IResponseFilter GetResourceResponseFilter(IWebBrowser a, IBrowser b, IFrame c, IRequest d, IResponse e)
            {
                foreach (var handler in _handlers)
                {
                    var result = handler.GetResourceResponseFilter(a, b, c, d, e);
                    if (result != null) return result;
                }
                return null;
            }

            public void OnResourceLoadComplete(IWebBrowser a, IBrowser b, IFrame c, IRequest d, IResponse e,
                UrlRequestStatus f, long g)
            {
                foreach (var handler in _handlers) handler.OnResourceLoadComplete(a, b, c, d, e, f, g);
            }

            public bool OnProtocolExecution(IWebBrowser a, IBrowser b, IFrame c, IRequest d)
            {
                var handled = false;
                foreach (var handler in _handlers) handled |= handler.OnProtocolExecution(a, b, c, d);
                return handled;
            }

            public void Dispose()
            {
                foreach (var handler in _handlers) handler.Dispose();
            }
        }

        private sealed class CookieAccessFilterMultiplexer : ICookieAccessFilter
        {
            private readonly IReadOnlyList<ICookieAccessFilter> _filters;

            public CookieAccessFilterMultiplexer(IReadOnlyList<ICookieAccessFilter> filters)
            {
                _filters = filters;
            }

            public bool CanSendCookie(IWebBrowser a, IBrowser b, IFrame c, IRequest d, Cookie e)
            {
                return _filters.All(filter => filter.CanSendCookie(a, b, c, d, e));
            }

            public bool CanSaveCookie(IWebBrowser a, IBrowser b, IFrame c, IRequest d, IResponse e, Cookie f)
            {
                return _filters.All(filter => filter.CanSaveCookie(a, b, c, d, e, f));
            }

            public void Dispose()
            {
                foreach (var filter in _filters) (filter as IDisposable)?.Dispose();
            }
        }
    }
}
