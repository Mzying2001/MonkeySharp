using CefSharp;
using Moq;
using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.CefSharp.IntegrationTests
{
    public sealed class NavigationMenuTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task NewMainDocumentRemovesOldMainAndChildMenus(bool changesFrameId)
        {
            using (var fixture = await Fixture.CreateAsync())
            {
                var main = new Frame(42);
                var child = new Frame(44, false);
                await fixture.CreateContextAsync(main);
                var oldMain = await fixture.RegisterAsync(main);
                await fixture.CreateContextAsync(child);
                var oldChild = await fixture.RegisterAsync(child);
                Assert.Equal(2, fixture.Menu.ActiveCount);

                var next = new Frame(changesFrameId ? 43 : 42, url: "https://other.example.test/next");
                // Cross-process navigation may never report release of the old frames.
                await fixture.CreateContextAsync(next);
                Assert.Equal(0, fixture.Menu.ActiveCount);
                Assert.Equal(1, oldMain.Registration.DisposeCount);
                Assert.Equal(1, oldChild.Registration.DisposeCount);
                using (var response = JsonDocument.Parse(await fixture.Host.Gateway.DispatchAsync(oldMain.Hello, CancellationToken.None)))
                    Assert.False(response.RootElement.GetProperty("ok").GetBoolean());

                var current = await fixture.RegisterAsync(next);
                // A late release for a retired frame must not remove the new menu.
                if (changesFrameId) fixture.ReleaseContext(main);
                fixture.ReleaseContext(child);
                Assert.Equal(1, fixture.Menu.ActiveCount);
                var clicks = 0;
                fixture.Provider.Notification += (_, args) => { if (args.EventName == "menu-command") clicks++; };
                oldMain.Registration.Invoke();
                oldChild.Registration.Invoke();
                Assert.Equal(0, clicks);
                current.Registration.Invoke();
                Assert.Equal(1, clicks);
                Assert.Equal(0, current.Registration.DisposeCount);
            }
        }

        [Fact]
        public async Task ChildNavigationKeepsMainMenuAndOtherTabsKeepTheirMenus()
        {
            using (var fixture = await Fixture.CreateAsync())
            using (var other = await Fixture.CreateAsync())
            {
                var main = new Frame(42);
                var child = new Frame(44, false);
                var otherMain = new Frame(42);
                await fixture.CreateContextAsync(main);
                var mainMenu = await fixture.RegisterAsync(main);
                await fixture.CreateContextAsync(child);
                var childMenu = await fixture.RegisterAsync(child);
                await other.CreateContextAsync(otherMain);
                var otherMenu = await other.RegisterAsync(otherMain);

                await fixture.CreateContextAsync(new Frame(44, false));
                Assert.Equal(1, fixture.Menu.ActiveCount);
                Assert.Equal(0, mainMenu.Registration.DisposeCount);
                Assert.Equal(1, childMenu.Registration.DisposeCount);
                await fixture.CreateContextAsync(new Frame(43));
                Assert.Equal(0, fixture.Menu.ActiveCount);
                Assert.Equal(1, other.Menu.ActiveCount);
                Assert.Equal(0, otherMenu.Registration.DisposeCount);
            }
        }

        [Fact]
        public async Task RapidMainFrameReplacementAndDetachReleaseEachDocumentOnce()
        {
            using (var fixture = await Fixture.CreateAsync())
            {
                var main = new Frame(42);
                await fixture.CreateContextAsync(main);
                var menu = await fixture.RegisterAsync(main);
                var pending = new Frame(43);
                var location = new TaskCompletionSource<JavascriptResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
                pending.SetLocation(location.Task);
                fixture.CreateContext(pending);
                var next = new Frame(45);
                fixture.CreateContext(next);
                fixture.ReleaseContext(main);
                fixture.ReleaseContext(pending);
                location.SetCanceled();
                Assert.Same(next.Injected.Task, await Task.WhenAny(next.Injected.Task, Task.Delay(5000)));
                var current = await fixture.RegisterAsync(next);
                await fixture.Host.DetachAsync(CancellationToken.None);

                Assert.Equal(0, fixture.Menu.ActiveCount);
                Assert.Equal(1, menu.Registration.DisposeCount);
                Assert.Equal(1, current.Registration.DisposeCount);
                Assert.DoesNotContain(fixture.Diagnostics, diagnostic => diagnostic.Code == "MSC101_CONTEXT_INITIALIZATION_FAILED" || diagnostic.Code == "MSC102_CONTEXT_RELEASE_FAILED");
            }
        }

        private sealed class Fixture : IDisposable
        {
            private readonly Mock<IWebBrowser> _browser = new Mock<IWebBrowser>();
            private readonly Mock<IBrowser> _native = new Mock<IBrowser>();
            private readonly ConcurrentQueue<DocumentFrame> _documents = new ConcurrentQueue<DocumentFrame>();
            private UserScriptInstallation _installation;
            public CefSharpUserScriptHost Host;
            public HostInteractionApiProvider Provider;
            public readonly MenuService Menu = new MenuService();
            public readonly ConcurrentQueue<UserScriptDiagnostic> Diagnostics = new ConcurrentQueue<UserScriptDiagnostic>();

            public static async Task<Fixture> CreateAsync()
            {
                var fixture = new Fixture();
                var repository = new InMemoryUserScriptRepository();
                fixture._installation = await repository.InstallAsync("// ==UserScript==\n// @name navigation menu\n// @match *://*/*\n// @grant GM.registerMenuCommand\n// @run-at document-end\n// ==/UserScript==", "test", true, CancellationToken.None);
                fixture.Provider = new HostInteractionApiProvider(menu: fixture.Menu);
                fixture.Host = new CefSharpUserScriptHostBuilder(repository).AddApiProvider(fixture.Provider)
                    .Configure(new CefSharpHostOptions { TrustedPageWorld = true }).Build();
                fixture.Host.DocumentLifecycle += (_, args) =>
                {
                    if (args.Kind == DocumentLifecycleKind.ContextCreated) fixture._documents.Enqueue(args.Frame);
                };
                fixture.Host.Diagnostic += (_, args) => fixture.Diagnostics.Enqueue(args);
                fixture._browser.SetupGet(browser => browser.JavascriptObjectRepository).Returns(Mock.Of<IJavascriptObjectRepository>());
                fixture._browser.SetupProperty(browser => browser.RenderProcessMessageHandler);
                fixture._browser.SetupProperty(browser => browser.RequestHandler);
                fixture._browser.SetupProperty(browser => browser.MenuHandler);
                fixture.Host.Attach(fixture._browser.Object);
                return fixture;
            }

            public void CreateContext(Frame frame) => _browser.Object.RenderProcessMessageHandler.OnContextCreated(_browser.Object, _native.Object, frame.Native.Object);
            public void ReleaseContext(Frame frame) => _browser.Object.RenderProcessMessageHandler.OnContextReleased(_browser.Object, _native.Object, frame.Native.Object);

            public async Task CreateContextAsync(Frame frame)
            {
                CreateContext(frame);
                Assert.Same(frame.Injected.Task, await Task.WhenAny(frame.Injected.Task, Task.Delay(5000)));
            }

            public async Task<RegisteredMenu> RegisterAsync(Frame frame)
            {
                var document = _documents.Last(item => item.FrameId == frame.Id);
                var plan = await Host.Engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, document), CancellationToken.None);
                var invocation = Assert.Single(plan.Invocations);
                using (var parameters = JsonDocument.Parse("{\"commandId\":1,\"name\":\"Shared menu\"}"))
                    await Provider.InvokeAsync(new ApiInvocationContext(_installation, document, invocation.ExecutionId, "register", "GM.registerMenuCommand", parameters.RootElement), CancellationToken.None);
                return new RegisteredMenu
                {
                    Registration = Menu.Registrations.Last(),
                    Hello = JsonSerializer.Serialize(new { type = "hello", protocol = 2, documentId = document.DocumentId, scriptKey = invocation.ScriptKey.ToString(), capability = invocation.Capability })
                };
            }

            public void Dispose() => Host.Dispose();
        }

        private sealed class Frame
        {
            public readonly Mock<IFrame> Native = new Mock<IFrame>();
            public readonly TaskCompletionSource<bool> Injected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public string Id { get; }
            public Frame(long id, bool main = true, string url = "https://example.com/page")
            {
                Id = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
#if CEF_SHARP_STRING_FRAME_IDS
                Native.SetupGet(frame => frame.Identifier).Returns(Id);
#else
                Native.SetupGet(frame => frame.Identifier).Returns(id);
#endif
                Native.SetupGet(frame => frame.IsMain).Returns(main);
                Native.SetupGet(frame => frame.IsValid).Returns(true);
                Native.SetupGet(frame => frame.Url).Returns(url);
                SetLocation(Task.FromResult(new JavascriptResponse { Success = true, Result = url }));
                Native.Setup(frame => frame.ExecuteJavaScriptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                    .Callback(() => Injected.TrySetResult(true));
            }

            public void SetLocation(Task<JavascriptResponse> response) => Native.Setup(frame => frame.EvaluateScriptAsync(
                "location.href", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan?>()
#if !CEF_SHARP_LEGACY_EVALUATION
                , false
#endif
                )).Returns(response);
        }

        private sealed class RegisteredMenu
        {
            public MenuRegistration Registration;
            public string Hello;
        }

        private sealed class MenuService : IMenuService
        {
            public readonly ConcurrentQueue<MenuRegistration> Registrations = new ConcurrentQueue<MenuRegistration>();
            public int ActiveCount => Registrations.Count(item => item.DisposeCount == 0);
            public Task<IMenuRegistration> RegisterAsync(MenuCommandRequest request, Action<MenuCommandInvocation> invoked, CancellationToken cancellationToken)
            {
                var registration = new MenuRegistration(invoked);
                Registrations.Enqueue(registration);
                return Task.FromResult<IMenuRegistration>(registration);
            }
        }

        private sealed class MenuRegistration : IMenuRegistration
        {
            private readonly Action<MenuCommandInvocation> _invoked;
            public int DisposeCount;
            public MenuRegistration(Action<MenuCommandInvocation> invoked) { _invoked = invoked; }
            public void Invoke() => _invoked(new MenuCommandInvocation());
            public void Dispose() => Interlocked.Increment(ref DisposeCount);
        }
    }
}
