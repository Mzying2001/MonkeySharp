using CefSharp;
using Moq;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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

            public BrowserFixture(bool initialized = false)
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
                Browser.SetupProperty(item => item.RenderProcessMessageHandler);

                CefBrowser = new Mock<IBrowser>();
                Frame = new Mock<IFrame>();
                Frame.SetupGet(item => item.Identifier).Returns(42);
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
    }
}
