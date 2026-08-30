using CefSharp;
using Mzying2001.MonkeySharp.Core.Apis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

#pragma warning disable CS1591

namespace Mzying2001.MonkeySharp.CefSharp
{
    /// <summary>Bridges synchronous Chromium request callbacks to an <see cref="IWebRequestService"/>.</summary>
    public sealed class CefSharpWebRequestHandler : IRequestHandler
    {
        private readonly IWebRequestService _service;

        public CefSharpWebRequestHandler(IWebRequestService service)
        { _service = service ?? throw new ArgumentNullException(nameof(service)); }

        public bool OnBeforeBrowse(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool userGesture, bool isRedirect)
        { return false; }
        public void OnDocumentAvailableInMainFrame(IWebBrowser chromiumWebBrowser, IBrowser browser) { }
        public bool OnOpenUrlFromTab(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, string targetUrl, WindowOpenDisposition targetDisposition, bool userGesture)
        { return false; }
        public IResourceRequestHandler GetResourceRequestHandler(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool isNavigation, bool isDownload, string requestInitiator, ref bool disableDefaultHandling)
        { return new ResourceHandler(_service); }
        public bool GetAuthCredentials(IWebBrowser chromiumWebBrowser, IBrowser browser, string originUrl, bool isProxy, string host, int port, string realm, string scheme, IAuthCallback callback)
        {
            var eventData = new WebRequestEvent(WebRequestPhase.OnAuthRequired, 0, originUrl ?? string.Empty, "GET");
            var decision = _service.Evaluate(eventData);
            if (decision.Kind != WebRequestActionKind.AuthResponse) return false;
            callback.Continue(decision.Username, decision.Password);
            return true;
        }
        public bool OnCertificateError(IWebBrowser chromiumWebBrowser, IBrowser browser, CefErrorCode errorCode, string requestUrl, ISslInfo sslInfo, IRequestCallback callback) { return false; }
        public bool OnSelectClientCertificate(IWebBrowser chromiumWebBrowser, IBrowser browser, bool isProxy, string host, int port, X509Certificate2Collection certificates, ISelectClientCertificateCallback callback) { return false; }
        public void OnRenderViewReady(IWebBrowser chromiumWebBrowser, IBrowser browser) { }
#if CEF_SHARP_EXTENDED_RENDER_TERMINATED
        public void OnRenderProcessTerminated(IWebBrowser chromiumWebBrowser, IBrowser browser, CefTerminationStatus status, int errorCode, string errorMessage) { }
#else
        public void OnRenderProcessTerminated(IWebBrowser chromiumWebBrowser, IBrowser browser, CefTerminationStatus status) { }
#endif

        private sealed class ResourceHandler : IResourceRequestHandler
        {
            private readonly IWebRequestService _service;
            public ResourceHandler(IWebRequestService service) { _service = service; }
            public ICookieAccessFilter GetCookieAccessFilter(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request) { return null; }
            public CefReturnValue OnBeforeResourceLoad(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IRequestCallback callback)
            {
                var decision = _service.Evaluate(CreateEvent(WebRequestPhase.OnBeforeRequest, request));
                ApplyRequestHeaders(request, decision.Headers);
                if (decision.Kind == WebRequestActionKind.Block) return CefReturnValue.Cancel;
                if (decision.Kind == WebRequestActionKind.Redirect && !string.IsNullOrEmpty(decision.RedirectUrl)) request.Url = decision.RedirectUrl;
                return CefReturnValue.Continue;
            }
            public IResourceHandler GetResourceHandler(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request) { return null; }
            public void OnResourceRedirect(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IResponse response, ref string newUrl)
            {
                var decision = _service.Evaluate(CreateEvent(WebRequestPhase.OnBeforeRequest, request));
                if (decision.Kind == WebRequestActionKind.Block) newUrl = "about:blank";
                else if (decision.Kind == WebRequestActionKind.Redirect && !string.IsNullOrEmpty(decision.RedirectUrl)) newUrl = decision.RedirectUrl;
            }
            public bool OnResourceResponse(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IResponse response)
            {
                var decision = _service.Evaluate(CreateEvent(WebRequestPhase.OnHeadersReceived, request, response));
                foreach (var header in decision.Headers) response.SetHeaderByName(header.Key, header.Value, true);
                return false;
            }
            public IResponseFilter GetResourceResponseFilter(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IResponse response) { return null; }
            public void OnResourceLoadComplete(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IResponse response, UrlRequestStatus status, long receivedContentLength)
            {
                var phase = status == UrlRequestStatus.Success ? WebRequestPhase.OnCompleted : WebRequestPhase.OnErrorOccurred;
                _service.Evaluate(CreateEvent(phase, request, response, status.ToString()));
            }
            public bool OnProtocolExecution(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request) { return false; }
            public void Dispose() { }

            private static WebRequestEvent CreateEvent(WebRequestPhase phase, IRequest request, IResponse response = null, string error = null)
            {
                return new WebRequestEvent(phase, request.Identifier, request.Url, request.Method, request.ResourceType.ToString(),
                    ToHeaders(request), response?.StatusCode, error);
            }
            private static IDictionary<string, string> ToHeaders(IRequest request)
            {
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in request.Headers.AllKeys ?? new string[0]) headers[name] = request.Headers[name];
                return headers;
            }
            private static void ApplyRequestHeaders(IRequest request, IReadOnlyDictionary<string, string> headers)
            { foreach (var header in headers) request.SetHeaderByName(header.Key, header.Value, true); }
        }
    }

    /// <summary>Composes existing request handlers without silently replacing them.</summary>
    public sealed class CefSharpWebRequestHandlerMultiplexer : IRequestHandler
    {
        private readonly List<IRequestHandler> _handlers = new List<IRequestHandler>();
        public void Add(IRequestHandler handler) { if (handler == null) throw new ArgumentNullException(nameof(handler)); if (!_handlers.Contains(handler)) _handlers.Add(handler); }
        public void Remove(IRequestHandler handler) { if (handler != null) _handlers.Remove(handler); }
        public bool OnBeforeBrowse(IWebBrowser a, IBrowser b, IFrame c, IRequest d, bool e, bool f) => _handlers.Any(x => x.OnBeforeBrowse(a, b, c, d, e, f));
        public void OnDocumentAvailableInMainFrame(IWebBrowser a, IBrowser b) { foreach (var x in _handlers) x.OnDocumentAvailableInMainFrame(a, b); }
        public bool OnOpenUrlFromTab(IWebBrowser a, IBrowser b, IFrame c, string d, WindowOpenDisposition e, bool f) => _handlers.Any(x => x.OnOpenUrlFromTab(a, b, c, d, e, f));
        public IResourceRequestHandler GetResourceRequestHandler(IWebBrowser a, IBrowser b, IFrame c, IRequest d, bool e, bool f, string g, ref bool h)
        { foreach (var x in _handlers) { var handler = x.GetResourceRequestHandler(a, b, c, d, e, f, g, ref h); if (handler != null) return handler; } return null; }
        public bool GetAuthCredentials(IWebBrowser a, IBrowser b, string c, bool d, string e, int f, string g, string h, IAuthCallback i) => _handlers.Any(x => x.GetAuthCredentials(a, b, c, d, e, f, g, h, i));
        public bool OnCertificateError(IWebBrowser a, IBrowser b, CefErrorCode c, string d, ISslInfo e, IRequestCallback f) => _handlers.Any(x => x.OnCertificateError(a, b, c, d, e, f));
        public bool OnSelectClientCertificate(IWebBrowser a, IBrowser b, bool c, string d, int e, X509Certificate2Collection f, ISelectClientCertificateCallback g) => _handlers.Any(x => x.OnSelectClientCertificate(a, b, c, d, e, f, g));
        public void OnRenderViewReady(IWebBrowser a, IBrowser b) { foreach (var x in _handlers) x.OnRenderViewReady(a, b); }
#if CEF_SHARP_EXTENDED_RENDER_TERMINATED
        public void OnRenderProcessTerminated(IWebBrowser a, IBrowser b, CefTerminationStatus c, int d, string e) { foreach (var x in _handlers) x.OnRenderProcessTerminated(a, b, c, d, e); }
#else
        public void OnRenderProcessTerminated(IWebBrowser a, IBrowser b, CefTerminationStatus c) { foreach (var x in _handlers) x.OnRenderProcessTerminated(a, b, c); }
#endif
    }
}
