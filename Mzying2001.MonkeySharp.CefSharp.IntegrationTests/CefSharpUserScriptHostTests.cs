using CefSharp;
using Moq;
using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.CefSharp.IntegrationTests
{
    public sealed class CefSharpUserScriptHostTests
    {
        [Fact]
        public async Task AttachRequiresAnUninitializedBrowserAndFreeHandlerSlot()
        {
            using (var host = await CreateHostAsync(Script("none", "document-start")))
            {
                var initialized = new BrowserFixture(initialized: true);
                Assert.Throws<InvalidOperationException>(() => host.Attach(initialized.Browser.Object));

                var occupied = new BrowserFixture();
                occupied.Browser.Object.RenderProcessMessageHandler = Mock.Of<IRenderProcessMessageHandler>();
                Assert.Throws<InvalidOperationException>(() => host.Attach(occupied.Browser.Object));

                var occupiedRequest = new BrowserFixture();
                var existingRequestHandler = Mock.Of<IRequestHandler>();
                occupiedRequest.Browser.Object.RequestHandler = existingRequestHandler;
                Assert.Throws<InvalidOperationException>(() => host.Attach(occupiedRequest.Browser.Object));
                Assert.Null(occupiedRequest.Browser.Object.RenderProcessMessageHandler);
                Assert.Same(existingRequestHandler, occupiedRequest.Browser.Object.RequestHandler);
            }
        }

        [Fact]
        public async Task AttachAndDetachAreRepeatableAndDoNotOverwriteSharedMultiplexer()
        {
            using (var host = await CreateHostAsync(Script("none", "document-start")))
            {
                var browser = new BrowserFixture();
                var existing = new Mock<IRenderProcessMessageHandler>();
                var multiplexer = new RenderProcessMessageHandlerMultiplexer(existing.Object);
                browser.Browser.Object.RenderProcessMessageHandler = multiplexer;

                host.Attach(browser.Browser.Object);
                await host.DetachAsync(CancellationToken.None);
                Assert.Same(multiplexer, browser.Browser.Object.RenderProcessMessageHandler);

                host.Attach(browser.Browser.Object);
                await host.DetachAsync(CancellationToken.None);

                browser.Repository.Verify(item => item.Register(
                    CefSharpUserScriptHost.BridgeObjectName,
                    It.IsAny<object>(),
                    true,
                    It.IsAny<BindingOptions>()), Times.Exactly(2));
                browser.Repository.Verify(item => item.UnRegister(
                    CefSharpUserScriptHost.BridgeObjectName), Times.Exactly(2));
            }
        }

        [Fact]
        public void MultiplexerContinuesAfterAHandlerFails()
        {
            var failing = new Mock<IRenderProcessMessageHandler>();
            var following = new Mock<IRenderProcessMessageHandler>();
            var browser = Mock.Of<IWebBrowser>();
            var cefBrowser = Mock.Of<IBrowser>();
            var frame = Mock.Of<IFrame>();
            failing.Setup(item => item.OnContextCreated(browser, cefBrowser, frame))
                .Throws(new InvalidOperationException("failure"));
            var multiplexer = new RenderProcessMessageHandlerMultiplexer(failing.Object, following.Object);
            var failures = 0;
            multiplexer.HandlerFailed += (_, __) => failures++;

            multiplexer.OnContextCreated(browser, cefBrowser, frame);

            Assert.Equal(1, failures);
            following.Verify(item => item.OnContextCreated(browser, cefBrowser, frame), Times.Once);
        }

        [Fact]
        public async Task ContextCreationExecutesPerFrameAndReportsBestEffortTiming()
        {
            using (var host = await CreateHostAsync(Script("none", "document-start")))
            {
                var browser = new BrowserFixture();
                var diagnostics = new ConcurrentQueue<UserScriptDiagnostic>();
                host.Diagnostic += (_, item) => diagnostics.Enqueue(item);
                host.Attach(browser.Browser.Object);

                browser.CreateContext();
                await browser.WaitForScriptCountAsync(1);

                var script = Assert.Single(browser.Scripts);
                Assert.Contains("__MonkeySharpRuntime", script);
                Assert.DoesNotContain("window.fixtureExecuted = true", script);
                Assert.Contains(diagnostics, item => item.Code == "MSR100_DOCUMENT_START_BEST_EFFORT");
            }
        }

        [Fact]
        public async Task DefaultModeSuppressesHostApisAndTrustedModeReportsRisk()
        {
            using (var safeHost = await CreateHostAsync(Script("GM.getValue", "document-start")))
            {
                var browser = new BrowserFixture();
                var diagnostics = new ConcurrentQueue<UserScriptDiagnostic>();
                safeHost.Diagnostic += (_, item) => diagnostics.Enqueue(item);
                safeHost.Attach(browser.Browser.Object);

                browser.CreateContext();
                await browser.WaitForScriptCountAsync(1);

                Assert.DoesNotContain("__MonkeySharpRuntime", Assert.Single(browser.Scripts));
                Assert.Contains(diagnostics, item => item.Code == "MSR201_BRIDGE_INTEGRITY_REQUIRED");
            }

            using (var trustedHost = await CreateHostAsync(
                Script("GM.getValue", "document-start"),
                new CefSharpHostOptions { TrustedPageWorld = true }))
            {
                var browser = new BrowserFixture();
                var diagnostics = new ConcurrentQueue<UserScriptDiagnostic>();
                trustedHost.Diagnostic += (_, item) => diagnostics.Enqueue(item);
                trustedHost.Attach(browser.Browser.Object);

                browser.CreateContext();
                await browser.WaitForScriptCountAsync(1);

                Assert.Contains("__MonkeySharpRuntime", Assert.Single(browser.Scripts));
                Assert.Contains(diagnostics, item => item.Code == "MSR200_UNVERIFIED_BRIDGE");
            }
        }

        [Fact]
        public async Task LoadFallbackRunsBodyEndAndIdleOnceInOrder()
        {
            var repository = new InMemoryUserScriptRepository();
            await repository.InstallAsync(Script("none", "document-body", "body"), "test", true, CancellationToken.None);
            await repository.InstallAsync(Script("none", "document-end", "end"), "test", true, CancellationToken.None);
            await repository.InstallAsync(Script("none", "document-idle", "idle"), "test", true, CancellationToken.None);
            using (var host = new CefSharpUserScriptHostBuilder(repository).Build())
            {
                var browser = new BrowserFixture();
                var lifecycle = new ConcurrentQueue<DocumentLifecycleKind>();
                host.DocumentLifecycle += (_, item) => lifecycle.Enqueue(item.Kind);
                host.Attach(browser.Browser.Object);
                browser.CreateContext();
                await browser.WaitForScriptCountAsync(1);

                browser.RaiseFrameLoadEnd();
                await browser.WaitForScriptCountAsync(4);

                Assert.Equal(1, lifecycle.Count(item => item == DocumentLifecycleKind.BodyAvailable));
                Assert.Equal(1, lifecycle.Count(item => item == DocumentLifecycleKind.DomContentLoaded));
                Assert.Equal(1, lifecycle.Count(item => item == DocumentLifecycleKind.Load));
            }
        }

        [Fact]
        public async Task DelayedInjectionReacquiresAFrameAfterTheCallbackWrapperIsDisposed()
        {
            using (var host = await CreateHostAsync(Script("none", "document-end")))
            {
                var browser = new BrowserFixture();
                var diagnostics = new ConcurrentQueue<UserScriptDiagnostic>();
                host.Diagnostic += (_, item) => diagnostics.Enqueue(item);
                host.Attach(browser.Browser.Object);
                browser.CreateContext();
                await browser.WaitForScriptCountAsync(1);

                browser.UseRefreshedFrame();
                browser.RaiseFrameLoadEnd();
                await browser.WaitForScriptCountAsync(2);

                Assert.DoesNotContain(diagnostics, item => item.Code == "MSC103_LOAD_FALLBACK_FAILED");
                Assert.Contains(browser.Scripts, script => script.Contains("__MonkeySharpRuntime"));
            }
        }

        [Fact]
        public async Task RapidContextReleaseAndRepeatedDisposeCompleteCleanly()
        {
            var host = await CreateHostAsync(Script("none", "document-start"));
            var browser = new BrowserFixture();
            var lifecycle = new ConcurrentQueue<DocumentLifecycleKind>();
            host.DocumentLifecycle += (_, item) => lifecycle.Enqueue(item.Kind);
            host.Attach(browser.Browser.Object);
            browser.CreateContext();
            await browser.WaitForScriptCountAsync(1);

            browser.ReleaseContext();
            host.Dispose();
            host.Dispose();

            Assert.Contains(DocumentLifecycleKind.ContextReleased, lifecycle);
        }

        [Fact]
        public async Task UncaughtJavascriptExceptionBecomesStructuredDiagnostic()
        {
            using (var host = await CreateHostAsync(Script("none", "document-start")))
            {
                var browser = new BrowserFixture();
                var diagnostics = new ConcurrentQueue<UserScriptDiagnostic>();
                host.Diagnostic += (_, item) => diagnostics.Enqueue(item);
                host.Attach(browser.Browser.Object);
                browser.CreateContext();
                await browser.WaitForScriptCountAsync(1);

                browser.Browser.Object.RenderProcessMessageHandler.OnUncaughtException(
                    browser.Browser.Object,
                    browser.CefBrowser.Object,
                    browser.Frame.Object,
                    new JavascriptException { Message = "fixture failure" });

                Assert.Contains(diagnostics, item =>
                    item.Code == "MSC300_JAVASCRIPT_EXCEPTION" && item.Message == "fixture failure");
            }
        }

        [Fact]
        public async Task BuilderServicesBecomeHelloCapabilitiesAndResolveDependencies()
        {
            var repository = new InMemoryUserScriptRepository();
            var grants = new[]
            {
                "GM.getResourceText", "GM.getResourceURL", "GM.xmlHttpRequest",
                "GM.registerMenuCommand", "GM.unregisterMenuCommand", "GM.notification",
                "GM.setClipboard", "GM.openInTab", "GM.download", "GM.getTab", "GM.saveTab", "GM.getTabs"
            };
            var metadata = string.Join("\n", grants.Select(item => "// @grant " + item));
            var source = "// ==UserScript==\n// @name capabilities\n// @match https://example.com/*\n" +
                "// @run-at document-end\n// @connect api.example.com\n" +
                "// @resource template https://cdn.example/template.txt\n" +
                "// @require https://cdn.example/dependency.js\n" + metadata + "\n// ==/UserScript==\nwindow.mainLoaded = true;";
            await repository.InstallAsync(source, "test", true, CancellationToken.None);
            var services = new AllHostServices();
            using (var host = new CefSharpUserScriptHostBuilder(repository)
                .UseDependencyProvider(services)
                .UseResourceProvider(services)
                .UseHttpRequestService(services)
                .UseMenuService(services)
                .UseNotificationService(services)
                .UseClipboardService(services)
                .UseTabService(services)
                .UseDownloadService(services)
                .UseTabStateService(services)
                .Configure(new CefSharpHostOptions { TrustedPageWorld = true })
                .Build())
            {
                var frame = new DocumentFrame(
                    "browser", "document", "main", new Uri("https://example.com/page"), true,
                    TimingGuarantee.BestEffortDocumentStart, host.BridgeIntegrity);
                var plan = await host.Engine.ProcessLifecycleAsync(
                    new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, frame),
                    CancellationToken.None);
                var invocation = Assert.Single(plan.Invocations);
                Assert.StartsWith("window.dependencyLoaded = true;", invocation.Source);

                var responseJson = await host.Gateway.DispatchAsync(JsonSerializer.Serialize(new
                {
                    type = "hello",
                    protocol = 1,
                    documentId = frame.DocumentId,
                    scriptKey = invocation.ScriptKey.ToString(),
                    capability = invocation.Capability
                }), CancellationToken.None);
                using (var response = JsonDocument.Parse(responseJson))
                {
                    Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
                    var capabilities = response.RootElement.GetProperty("apis")
                        .EnumerateArray().Select(item => item.GetString()).ToList();
                    foreach (var grant in grants)
                        Assert.Contains(grant, capabilities);
                }
            }
        }

        [Fact]
        public async Task DefaultNetworkServicesAreExposedAndCanBeDisabled()
        {
            var repository = new InMemoryUserScriptRepository();
            await repository.InstallAsync(
                Script("GM.xmlHttpRequest\n// @grant GM.webRequest\n// @grant GM.cookie", "document-end"),
                "test", true, CancellationToken.None);

            async Task<IReadOnlyCollection<string>> CapabilitiesAsync(CefSharpHostOptions options)
            {
                using (var host = new CefSharpUserScriptHostBuilder(repository)
                    .Configure(options)
                    .Build())
                {
                    var frame = new DocumentFrame("browser", Guid.NewGuid().ToString("D"), "main",
                        new Uri("https://example.com/page"), true,
                        TimingGuarantee.BestEffortDocumentStart, host.BridgeIntegrity);
                    var plan = await host.Engine.ProcessLifecycleAsync(
                        new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, frame),
                        CancellationToken.None);
                    var invocation = Assert.Single(plan.Invocations);
                    var json = await host.Gateway.DispatchAsync(JsonSerializer.Serialize(new
                    {
                        type = "hello", protocol = 1, documentId = frame.DocumentId,
                        scriptKey = invocation.ScriptKey.ToString(), capability = invocation.Capability
                    }), CancellationToken.None);
                    using (var response = JsonDocument.Parse(json))
                    {
                        return response.RootElement.GetProperty("apis").EnumerateArray()
                            .Select(item => item.GetString()).ToArray();
                    }
                }
            }

            var defaults = await CapabilitiesAsync(new CefSharpHostOptions { TrustedPageWorld = true });
            Assert.Contains("GM.xmlHttpRequest", defaults);
            Assert.Contains("GM.webRequest", defaults);
            Assert.Contains("GM.cookie", defaults);

            var disabled = await CapabilitiesAsync(new CefSharpHostOptions
            {
                TrustedPageWorld = true,
                EnableDefaultNetworkServices = false
            });
            Assert.DoesNotContain("GM.xmlHttpRequest", disabled);
            Assert.DoesNotContain("GM.webRequest", disabled);
            Assert.DoesNotContain("GM.cookie", disabled);
        }

        [Fact]
        public void WebRequestHandlerPublishesResponseHeadersForResponseLifecycleEvents()
        {
            var events = new ConcurrentQueue<WebRequestEvent>();
            using (var service = new InMemoryWebRequestService())
            using (service.AddListener(new WebRequestFilter(), (_, item) => events.Enqueue(item)))
            {
                var requestHeaders = new NameValueCollection { ["X-Request"] = "request" };
                var responseHeaders = new NameValueCollection { ["X-Response"] = "response" };
                var request = new Mock<IRequest>();
                request.SetupGet(item => item.Identifier).Returns(7UL);
                request.SetupGet(item => item.Url).Returns("https://example.test/data");
                request.SetupGet(item => item.Method).Returns("GET");
                request.SetupGet(item => item.ResourceType).Returns(ResourceType.Xhr);
                request.SetupGet(item => item.Headers).Returns(requestHeaders);
                var response = new Mock<IResponse>();
                response.SetupGet(item => item.Headers).Returns(responseHeaders);
                response.SetupGet(item => item.StatusCode).Returns(200);

                var handler = new CefSharpWebRequestHandler(service);
                var disableDefaultHandling = false;
                using (var resource = handler.GetResourceRequestHandler(null, null, null, request.Object,
                    false, false, string.Empty, ref disableDefaultHandling))
                {
                    Assert.Equal(CefReturnValue.Continue,
                        resource.OnBeforeResourceLoad(null, null, null, request.Object, null));
                    Assert.False(resource.OnResourceResponse(null, null, null, request.Object, response.Object));
                    resource.OnResourceLoadComplete(null, null, null, request.Object, response.Object,
                        UrlRequestStatus.Success, 1);
                }

                Assert.Equal(new[]
                {
                    WebRequestPhase.OnBeforeRequest,
                    WebRequestPhase.OnBeforeSendHeaders,
                    WebRequestPhase.OnHeadersReceived,
                    WebRequestPhase.OnResponseStarted,
                    WebRequestPhase.OnCompleted
                }, events.Select(item => item.Phase));
                foreach (var item in events.Where(item => item.Phase == WebRequestPhase.OnHeadersReceived ||
                    item.Phase == WebRequestPhase.OnResponseStarted || item.Phase == WebRequestPhase.OnCompleted))
                {
                    Assert.Equal("response", item.Headers["X-Response"]);
                    Assert.False(item.Headers.ContainsKey("X-Request"));
                }
            }
        }

        [Fact]
        public void RequestHandlerMultiplexerCombinesResourceHandlers()
        {
            var firstEvents = new ConcurrentQueue<WebRequestEvent>();
            var secondEvents = new ConcurrentQueue<WebRequestEvent>();
            using (var first = new InMemoryWebRequestService())
            using (var second = new InMemoryWebRequestService())
            using (first.AddListener(new WebRequestFilter(), (_, item) => firstEvents.Enqueue(item)))
            using (second.AddListener(new WebRequestFilter(), (_, item) => secondEvents.Enqueue(item)))
            {
                var multiplexer = new CefSharpWebRequestHandlerMultiplexer();
                multiplexer.Add(new CefSharpWebRequestHandler(first));
                multiplexer.Add(new CefSharpWebRequestHandler(second));
                var request = new Mock<IRequest>();
                request.SetupGet(item => item.Identifier).Returns(8UL);
                request.SetupGet(item => item.Url).Returns("https://example.test/data");
                request.SetupGet(item => item.Method).Returns("GET");
                request.SetupGet(item => item.ResourceType).Returns(ResourceType.Xhr);
                request.SetupGet(item => item.Headers).Returns(new NameValueCollection());
                var disableDefaultHandling = false;

                using (var resource = multiplexer.GetResourceRequestHandler(null, null, null, request.Object,
                    false, false, string.Empty, ref disableDefaultHandling))
                {
                    Assert.Equal(CefReturnValue.Continue,
                        resource.OnBeforeResourceLoad(null, null, null, request.Object, null));
                }

                Assert.Equal(new[] { WebRequestPhase.OnBeforeRequest, WebRequestPhase.OnBeforeSendHeaders },
                    firstEvents.Select(item => item.Phase));
                Assert.Equal(new[] { WebRequestPhase.OnBeforeRequest, WebRequestPhase.OnBeforeSendHeaders },
                    secondEvents.Select(item => item.Phase));
            }
        }

        [Fact]
        public async Task DetachAndReattachSwitchTheDefaultRequestContext()
        {
            using (var host = await CreateHostAsync(Script("none", "document-end")))
            {
                var firstContext = Mock.Of<IRequestContext>();
                var secondContext = Mock.Of<IRequestContext>();
                var first = new BrowserFixture(requestContext: firstContext);
                var second = new BrowserFixture(requestContext: secondContext);

                host.Attach(first.Browser.Object);
                Assert.Same(firstContext, GetCurrentRequestContext(host));
                await host.DetachAsync(CancellationToken.None);
                Assert.Null(GetCurrentRequestContext(host));

                host.Attach(second.Browser.Object);
                Assert.Same(secondContext, GetCurrentRequestContext(host));
                await host.DetachAsync(CancellationToken.None);
                Assert.Null(GetCurrentRequestContext(host));
            }
        }

        [Fact]
        public async Task ExplicitNetworkServicesRemainApplicationOwned()
        {
            var repository = new InMemoryUserScriptRepository();
            await repository.InstallAsync(Script("none", "document-end"), "test", true,
                CancellationToken.None);
            var httpDisposed = false;
            var cookiesDisposed = false;
            var webRequestsDisposed = false;
            var http = new Mock<IHttpRequestService>();
            http.As<IDisposable>().Setup(item => item.Dispose()).Callback(() => httpDisposed = true);
            var cookies = new Mock<ICookieService>();
            cookies.As<IDisposable>().Setup(item => item.Dispose()).Callback(() => cookiesDisposed = true);
            var webRequests = new Mock<IWebRequestService>();
            webRequests.As<IDisposable>().Setup(item => item.Dispose()).Callback(() => webRequestsDisposed = true);

            using (var host = new CefSharpUserScriptHostBuilder(repository)
                .UseHttpRequestService(http.Object)
                .UseCookieService(cookies.Object)
                .UseWebRequestService(webRequests.Object)
                .Configure(new CefSharpHostOptions { EnableDefaultNetworkServices = false })
                .Build())
            {
            }

            Assert.False(httpDisposed);
            Assert.False(cookiesDisposed);
            Assert.False(webRequestsDisposed);
        }

        private static IRequestContext GetCurrentRequestContext(CefSharpUserScriptHost host)
        {
            return (IRequestContext)typeof(CefSharpUserScriptHost)
                .GetProperty("CurrentRequestContext", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(host);
        }

        private static async Task<CefSharpUserScriptHost> CreateHostAsync(
            string source,
            CefSharpHostOptions options = null)
        {
            var repository = new InMemoryUserScriptRepository();
            await repository.InstallAsync(source, "test", true, CancellationToken.None);
            var builder = new CefSharpUserScriptHostBuilder(repository);
            if (options != null)
                builder.Configure(options);
            return builder.Build();
        }

        private static string Script(string grant, string runAt, string name = "fixture")
        {
            return "// ==UserScript==\n" +
                "// @name " + name + "\n" +
                "// @match https://example.com/*\n" +
                "// @grant " + grant + "\n" +
                "// @run-at " + runAt + "\n" +
                "// ==/UserScript==\n" +
                "window.fixtureExecuted = true;";
        }

        private sealed class BrowserFixture
        {
            private bool _bound;

            public BrowserFixture(bool initialized = false, IRequestContext requestContext = null)
            {
                Repository = new Mock<IJavascriptObjectRepository>();
                Repository.Setup(item => item.IsBound(It.IsAny<string>())).Returns(() => _bound);
                Repository.Setup(item => item.Register(
                        It.IsAny<string>(), It.IsAny<object>(), It.IsAny<bool>(), It.IsAny<BindingOptions>()))
                    .Callback(() => _bound = true);
                Repository.Setup(item => item.UnRegister(It.IsAny<string>()))
                    .Returns(() =>
                    {
                        var wasBound = _bound;
                        _bound = false;
                        return wasBound;
                    });

                Browser = new Mock<IWebBrowser>();
                Browser.SetupGet(item => item.IsBrowserInitialized).Returns(initialized);
                Browser.SetupGet(item => item.IsDisposed).Returns(false);
                Browser.SetupGet(item => item.JavascriptObjectRepository).Returns(Repository.Object);
                Browser.SetupGet(item => item.RequestContext).Returns(requestContext);
                Browser.SetupProperty(item => item.RenderProcessMessageHandler);
                Browser.SetupProperty(item => item.RequestHandler);

                CefBrowser = new Mock<IBrowser>();
                Frame = new Mock<IFrame>();
#if CEF_SHARP_STRING_FRAME_IDS
                Frame.SetupGet(item => item.Identifier).Returns("42");
#else
                Frame.SetupGet(item => item.Identifier).Returns(42);
#endif
                Frame.SetupGet(item => item.Url).Returns("https://example.com/page");
                Frame.SetupGet(item => item.IsMain).Returns(true);
                Frame.SetupGet(item => item.IsValid).Returns(true);
                Frame.SetupGet(item => item.IsDisposed).Returns(false);
                Frame.Setup(item => item.ExecuteJavaScriptAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                    .Callback<string, string, int>((script, _, __) => Scripts.Enqueue(script));
            }

            public Mock<IWebBrowser> Browser { get; }
            public Mock<IJavascriptObjectRepository> Repository { get; }
            public Mock<IBrowser> CefBrowser { get; }
            public Mock<IFrame> Frame { get; }
            public ConcurrentQueue<string> Scripts { get; } = new ConcurrentQueue<string>();

            public void CreateContext()
            {
                Browser.Object.RenderProcessMessageHandler.OnContextCreated(
                    Browser.Object, CefBrowser.Object, Frame.Object);
            }

            public void RaiseFrameLoadEnd()
            {
                Browser.Raise(
                    item => item.FrameLoadEnd += null,
                    new FrameLoadEndEventArgs(CefBrowser.Object, Frame.Object, 200));
            }

            public void UseRefreshedFrame()
            {
                Frame.SetupGet(item => item.IsDisposed).Returns(true);
                var refreshed = new Mock<IFrame>();
#if CEF_SHARP_STRING_FRAME_IDS
                refreshed.SetupGet(item => item.Identifier).Returns("42");
#else
                refreshed.SetupGet(item => item.Identifier).Returns(42);
#endif
                refreshed.SetupGet(item => item.Url).Returns("https://example.com/page");
                refreshed.SetupGet(item => item.IsMain).Returns(true);
                refreshed.SetupGet(item => item.IsValid).Returns(true);
                refreshed.SetupGet(item => item.IsDisposed).Returns(false);
                refreshed.Setup(item => item.ExecuteJavaScriptAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                    .Callback<string, string, int>((script, _, __) => Scripts.Enqueue(script));
#if CEF_SHARP_STRING_FRAME_IDS
                CefBrowser.Setup(item => item.GetFrameByIdentifier("42")).Returns(refreshed.Object);
#else
                CefBrowser.Setup(item => item.GetFrame(42)).Returns(refreshed.Object);
#endif
                Browser.Setup(item => item.GetBrowser()).Returns(CefBrowser.Object);
            }

            public void ReleaseContext()
            {
                Browser.Object.RenderProcessMessageHandler.OnContextReleased(
                    Browser.Object, CefBrowser.Object, Frame.Object);
            }

            public async Task WaitForScriptCountAsync(int count)
            {
                var timeout = DateTime.UtcNow.AddSeconds(5);
                while (Scripts.Count < count && DateTime.UtcNow < timeout)
                    await Task.Delay(10);
                Assert.True(Scripts.Count >= count, "Timed out waiting for CefSharp frame execution.");
            }
        }

        private sealed class AllHostServices :
            IUserScriptDependencyProvider,
            IResourceProvider,
            IHttpRequestService,
            IMenuService,
            INotificationService,
            IClipboardService,
            ITabService,
            IDownloadService,
            ITabStateService
        {
            public Task<string> GetScriptAsync(
                UserScriptInstallation installation,
                string url,
                CancellationToken cancellationToken)
                => Task.FromResult("window.dependencyLoaded = true;");

            public Task<ResourceContent> GetAsync(
                UserScriptInstallation installation,
                ResourceDeclaration resource,
                CancellationToken cancellationToken)
                => Task.FromResult(new ResourceContent(Encoding.UTF8.GetBytes("resource"), "text/plain", "resource"));

            public IHttpRequestOperation SendAsync(
                UserScriptHttpRequest request,
                IUserScriptHttpObserver observer,
                CancellationToken cancellationToken)
            {
                var response = new UserScriptHttpResponse(
                    200, "OK", request.Url, new Dictionary<string, string>(), string.Empty, null, null);
                observer.OnResponseStarted(response);
                return new CompletedHttpOperation(response);
            }

            public Task<IMenuRegistration> RegisterAsync(
                MenuCommandRequest request,
                Action invoked,
                CancellationToken cancellationToken)
                => Task.FromResult<IMenuRegistration>(new NoopMenuRegistration());

            public INotificationHandle ShowAsync(UserScriptNotificationRequest request, CancellationToken cancellationToken)
                => new NoopNotificationHandle();

            public Task SetTextAsync(string text, string mediaType, CancellationToken cancellationToken)
                => Task.CompletedTask;

            public Task<ITabHandle> OpenAsync(OpenTabRequest request, CancellationToken cancellationToken)
                => Task.FromResult<ITabHandle>(new NoopTabHandle("tab"));

            public Task<IDownloadOperation> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken)
                => Task.FromResult<IDownloadOperation>(new NoopDownloadOperation("download"));

            public Task<string> GetAsync(
                ScriptKey scriptKey,
                DocumentFrame frame,
                CancellationToken cancellationToken)
                => Task.FromResult("{}");

            public Task SaveAsync(
                ScriptKey scriptKey,
                DocumentFrame frame,
                string jsonValue,
                CancellationToken cancellationToken)
                => Task.CompletedTask;

            public Task<IReadOnlyDictionary<string, string>> GetAllAsync(
                ScriptKey scriptKey,
                CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyDictionary<string, string>>(
                    new Dictionary<string, string> { ["tab"] = "{}" });
        }

        private sealed class NoopMenuRegistration : IMenuRegistration
        {
            public void Dispose()
            {
            }
        }

        private sealed class CompletedHttpOperation : IHttpRequestOperation
        {
            private readonly UserScriptHttpResponse _response;
            public CompletedHttpOperation(UserScriptHttpResponse response) { _response = response; }
            public Task<UserScriptHttpResponse> Completion => Task.FromResult(_response);
            public void Abort() { }
        }

#pragma warning disable CS0067
        private sealed class NoopNotificationHandle : INotificationHandle
        {
            public Task Completion { get; } = Task.CompletedTask;
            public event EventHandler Clicked;
            public event EventHandler Closed;
            public void Dispose() { }
        }

        private sealed class NoopTabHandle : ITabHandle
        {
            public NoopTabHandle(string id) { TabId = id; }
            public string TabId { get; }
            public bool Closed { get; private set; }
            public event EventHandler OnClose;
            public Task CloseAsync(CancellationToken cancellationToken) { Closed = true; return Task.CompletedTask; }
            public void Dispose() { }
        }

        private sealed class NoopDownloadOperation : IDownloadOperation
        {
            public NoopDownloadOperation(string id) { DownloadId = id; }
            public string DownloadId { get; }
            public Task Completion { get; } = Task.CompletedTask;
            public event EventHandler<UserScriptDownloadProgress> Progress;
            public event EventHandler Completed;
            public event EventHandler<UserScriptDownloadFailure> Failed;
            public event EventHandler Aborted;
            public void Abort() { }
            public void Dispose() { }
        }
#pragma warning restore CS0067
    }
}
