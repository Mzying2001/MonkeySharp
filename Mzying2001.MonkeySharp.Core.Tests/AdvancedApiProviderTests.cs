using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using Mzying2001.MonkeySharp.Core.Parsing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class AdvancedApiProviderTests
    {
        [Fact]
        public async Task ResourceApisRequireDeclaredNameAndEnforceSize()
        {
            var installation = await InstallAsync(
                "// @grant GM.getResourceText\n// @grant GM.getResourceURL\n" +
                "// @resource logo https://cdn.example/logo.txt");
            var resources = new FakeResourceProvider(new ResourceContent(
                Encoding.UTF8.GetBytes("hello"), "text/plain", "hello"));
            var provider = new ResourceAndNetworkApiProvider(resources, options: new BridgeOptions(maxResourceBytes: 8));

            var text = await provider.InvokeAsync(Context(installation, "GM.getResourceText", new { name = "logo" }), CancellationToken.None);
            var url = await provider.InvokeAsync(Context(installation, "GM.getResourceURL", new { name = "logo" }), CancellationToken.None);

            Assert.Equal("hello", Json(text.Json).GetString());
            Assert.StartsWith("data:text/plain;base64,", Json(url.Json).GetString());
            await Assert.ThrowsAsync<BridgeProtocolException>(() => provider.InvokeAsync(
                Context(installation, "GM.getResourceText", new { name = "missing" }), CancellationToken.None));

            var oversized = new ResourceAndNetworkApiProvider(
                new FakeResourceProvider(new ResourceContent(new byte[9])),
                options: new BridgeOptions(maxResourceBytes: 8));
            var exception = await Assert.ThrowsAsync<BridgeProtocolException>(() => oversized.InvokeAsync(
                Context(installation, "GM.getResourceURL", new { name = "logo" }), CancellationToken.None));
            Assert.Equal(BridgeErrorCodes.PayloadTooLarge, exception.Code);

            var oversizedText = new ResourceAndNetworkApiProvider(
                new FakeResourceProvider(new ResourceContent(new byte[0], text: "123456789")),
                options: new BridgeOptions(maxResourceBytes: 8));
            exception = await Assert.ThrowsAsync<BridgeProtocolException>(() => oversizedText.InvokeAsync(
                Context(installation, "GM.getResourceText", new { name = "logo" }), CancellationToken.None));
            Assert.Equal(BridgeErrorCodes.PayloadTooLarge, exception.Code);
        }

        [Fact]
        public async Task HttpApiEnforcesConnectOnRequestAndRedirectAndReportsProgress()
        {
            var installation = await InstallAsync(
                "// @grant GM.xmlHttpRequest\n// @connect api.example.com");
            var service = new FakeHttpService(new UserScriptHttpResponse(
                200,
                "OK",
                new Uri("https://api.example.com/final"),
                new Dictionary<string, string> { ["Content-Type"] = "text/plain" },
                "Content-Type: text/plain\r\n",
                "text/plain",
                "utf-8"), Encoding.UTF8.GetBytes("done"));
            var provider = new ResourceAndNetworkApiProvider(http: service);
            ApiNotificationEventArgs progress = null;
            provider.Notification += (_, item) =>
            {
                if (item.EventName == "xhr-progress") progress = item;
            };

            var created = await provider.InvokeAsync(Context(
                installation,
                "GM.xmlHttpRequest",
                new
                {
                    operation = "create",
                    xhrId = 7,
                    url = "https://api.example.com/start",
                    method = "post",
                    headers = new { Accept = "text/plain" }
                }), CancellationToken.None);
            var sessionId = Json(created.Json).GetProperty("sessionId").GetString();
            await provider.InvokeAsync(Context(installation, "GM.xmlHttpRequest", new
            {
                operation = "appendBody", sessionId, chunk = Convert.ToBase64String(Encoding.UTF8.GetBytes("body"))
            }), CancellationToken.None);
            var result = await provider.InvokeAsync(Context(installation, "GM.xmlHttpRequest", new
            {
                operation = "execute", sessionId
            }), CancellationToken.None);
            var body = await provider.InvokeAsync(Context(installation, "GM.xmlHttpRequest", new
            {
                operation = "readBody", sessionId, offset = 0
            }), CancellationToken.None);

            Assert.Equal(4, Json(result.Json).GetProperty("bodyLength").GetInt64());
            Assert.Equal("done", Encoding.UTF8.GetString(Convert.FromBase64String(
                Json(body.Json).GetProperty("chunk").GetString())));
            Assert.Equal("POST", service.Request.Method);
            Assert.Null(service.Request.MaxResponseBytes);
            using (var stream = service.Request.Body.OpenRead())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                Assert.Equal("body", reader.ReadToEnd());
            Assert.True(service.Request.RedirectAllowed(new Uri("https://api.example.com/next")));
            Assert.False(service.Request.RedirectAllowed(new Uri("https://escape.example/")));
            Assert.NotNull(progress);
            Assert.Equal("xhr-progress", progress.EventName);
            Assert.Equal(7, Json(progress.DataJson).GetProperty("xhrId").GetInt32());

            var denied = await Assert.ThrowsAsync<BridgeProtocolException>(() => provider.InvokeAsync(Context(
                installation,
                "GM.xmlHttpRequest",
                new { operation = "create", xhrId = 8, url = "https://other.example/" }), CancellationToken.None));
            Assert.Equal(BridgeErrorCodes.PermissionDenied, denied.Code);

            service.Response = new UserScriptHttpResponse(
                200, "OK", new Uri("https://api.example.com/final"),
                new Dictionary<string, string>(), string.Empty, null, null,
                new[] { new Uri("https://escape.example/intermediate") });
            created = await provider.InvokeAsync(Context(installation, "GM.xmlHttpRequest",
                new { operation = "create", xhrId = 9, url = "https://api.example.com/" }), CancellationToken.None);
            sessionId = Json(created.Json).GetProperty("sessionId").GetString();
            var intermediateRedirect = await Assert.ThrowsAsync<BridgeProtocolException>(() => provider.InvokeAsync(Context(
                installation,
                "GM.xmlHttpRequest",
                new { operation = "execute", sessionId }), CancellationToken.None));
            Assert.Equal(BridgeErrorCodes.PermissionDenied, intermediateRedirect.Code);

            service.Response = new UserScriptHttpResponse(
                200, "OK", new Uri("https://escape.example/"),
                new Dictionary<string, string>(), string.Empty, null, null);
            created = await provider.InvokeAsync(Context(installation, "GM.xmlHttpRequest",
                new { operation = "create", xhrId = 10, url = "https://api.example.com/" }), CancellationToken.None);
            sessionId = Json(created.Json).GetProperty("sessionId").GetString();
            var redirect = await Assert.ThrowsAsync<BridgeProtocolException>(() => provider.InvokeAsync(Context(
                installation,
                "GM.xmlHttpRequest",
                new { operation = "execute", sessionId }), CancellationToken.None));
            Assert.Equal(BridgeErrorCodes.PermissionDenied, redirect.Code);

            var invalidHeader = await Assert.ThrowsAsync<BridgeProtocolException>(() => provider.InvokeAsync(Context(
                installation,
                "GM.xmlHttpRequest",
                new
                {
                    operation = "create",
                    xhrId = 12,
                    url = "https://api.example.com/",
                    headers = new Dictionary<string, string> { ["X-Test"] = "value\r\ninjected" }
                }), CancellationToken.None));
            Assert.Equal(BridgeErrorCodes.InvalidParams, invalidHeader.Code);
        }

        [Fact]
        public async Task HttpApiAcceptsExtensionMethodsSpecialHeadersAndRequestOptions()
        {
            var installation = await InstallAsync("// @grant GM.xmlHttpRequest\n// @connect api.example.com");
            var response = new UserScriptHttpResponse(
                204, "No Content", new Uri("https://api.example.com/"),
                new Dictionary<string, string>(), string.Empty, null, null);
            var service = new FakeHttpService(response, null);
            var provider = new ResourceAndNetworkApiProvider(http: service);

            var created = await provider.InvokeAsync(Context(installation, "GM.xmlHttpRequest", new
            {
                operation = "create",
                xhrId = 13,
                url = "https://api.example.com/",
                method = "PROPFIND",
                headers = new Dictionary<string, string>
                {
                    ["User-Agent"] = "MonkeySharp",
                    ["Referer"] = "https://example.com/",
                    ["Origin"] = "https://example.com",
                    ["Cookie"] = "from=header"
                },
                cookie = "from=option",
                user = "alice",
                password = "secret",
                anonymous = true,
                overrideMimeType = "text/plain;charset=iso-8859-1"
            }), CancellationToken.None);
            var sessionId = Json(created.Json).GetProperty("sessionId").GetString();
            await provider.InvokeAsync(Context(installation, "GM.xmlHttpRequest", new
            {
                operation = "execute", sessionId
            }), CancellationToken.None);

            Assert.Equal("PROPFIND", service.Request.Method);
            Assert.Equal("MonkeySharp", service.Request.Headers["User-Agent"]);
            Assert.Equal("from=option", service.Request.Options.Cookie);
            Assert.Equal("alice", service.Request.Options.Username);
            Assert.Equal("secret", service.Request.Options.Password);
            Assert.True(service.Request.Options.Anonymous);
            Assert.Equal("text/plain;charset=iso-8859-1", service.Request.Options.OverrideMimeType);

            foreach (var method in new[] { "CONNECT", "TRACE", "TRACK" })
            {
                var error = await Assert.ThrowsAsync<BridgeProtocolException>(() => provider.InvokeAsync(
                    Context(installation, "GM.xmlHttpRequest", new
                    {
                        operation = "create", xhrId = 14, url = "https://api.example.com/", method
                    }), CancellationToken.None));
                Assert.Equal(BridgeErrorCodes.InvalidParams, error.Code);
            }

            var unsupported = await Assert.ThrowsAsync<BridgeProtocolException>(() => provider.InvokeAsync(
                Context(installation, "GM.xmlHttpRequest", new
                {
                    operation = "create", xhrId = 15, url = "https://api.example.com/", proxy = new { }
                }), CancellationToken.None));
            Assert.Equal(BridgeErrorCodes.NotSupported, unsupported.Code);
        }

        [Fact]
        public async Task DependencyResolverPreservesRequireOrderAndLimit()
        {
            var installation = await InstallAsync(
                "// @grant none\n// @require https://cdn.example/one.js\n// @require https://cdn.example/two.js");
            var dependencies = new FakeDependencyProvider(new Dictionary<string, string>
            {
                ["https://cdn.example/one.js"] = "const one = 1;",
                ["https://cdn.example/two.js"] = "const two = 2;"
            });
            var resolver = new ResourceScriptSourceResolver(dependencies, 100);

            var source = await resolver.ResolveSourceAsync(installation, CancellationToken.None);

            Assert.True(source.IndexOf("const one", StringComparison.Ordinal) < source.IndexOf("const two", StringComparison.Ordinal));
            Assert.EndsWith(installation.Definition.Source, source);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ResourceScriptSourceResolver(dependencies, 5)
                    .ResolveSourceAsync(installation, CancellationToken.None));
        }

        [Fact]
        public async Task HostInteractionServicesReceiveValidatedRequestsAndMenuCallbacks()
        {
            var installation = await InstallAsync(
                "// @grant GM.registerMenuCommand\n// @grant GM.unregisterMenuCommand\n" +
                "// @grant GM.notification\n// @grant GM.setClipboard\n// @grant GM.openInTab\n// @grant GM.download");
            var services = new FakeHostServices();
            using (var provider = new HostInteractionApiProvider(
                services, services, services, services, services))
            {
                ApiNotificationEventArgs notification = null;
                provider.Notification += (_, item) => notification = item;
                var registered = await provider.InvokeAsync(Context(
                    installation,
                    "GM.registerMenuCommand",
                    new { commandId = 3, name = "Open", accessKey = "o" }), CancellationToken.None);
                services.InvokeMenu();
                await provider.InvokeAsync(Context(
                    installation,
                    "GM.notification",
                    new { title = "Title", text = "Text", imageUrl = (string)null }), CancellationToken.None);
                await provider.InvokeAsync(Context(
                    installation,
                    "GM.setClipboard",
                    new { text = "copy", type = "text/plain" }), CancellationToken.None);
                var tab = await provider.InvokeAsync(Context(
                    installation,
                    "GM.openInTab",
                    new { url = "https://example.com/", active = false, insert = true, setParent = false }), CancellationToken.None);
                var download = await provider.InvokeAsync(Context(
                    installation,
                    "GM.download",
                    new { url = "https://example.com/file", name = "file", saveAs = true }), CancellationToken.None);
                var removed = await provider.InvokeAsync(Context(
                    installation,
                    "GM.unregisterMenuCommand",
                    new { commandId = 3 }), CancellationToken.None);

                Assert.Equal(3, Json(registered.Json).GetInt32());
                Assert.Equal("menu-command", notification.EventName);
                Assert.Equal("copy", services.ClipboardText);
                Assert.False(services.OpenTab.Active);
                Assert.Equal("tab", Json(tab.Json).GetProperty("id").GetString());
                Assert.Equal("download", Json(download.Json).GetProperty("id").GetString());
                Assert.True(Json(removed.Json).GetBoolean());
                Assert.True(services.MenuRegistration.Disposed);
            }
        }

        [Fact]
        public async Task ExecutionEndDisposesRegisteredMenuCommands()
        {
            var installation = await InstallAsync(
                "// @grant GM.registerMenuCommand\n// @grant GM.unregisterMenuCommand");
            var services = new FakeHostServices();
            using (var provider = new HostInteractionApiProvider(menu: services))
            {
                await provider.InvokeAsync(Context(
                    installation,
                    "GM.registerMenuCommand",
                    new { commandId = 3, name = "Open" }), CancellationToken.None);

                provider.OnExecutionEnded("execution");

                Assert.True(services.MenuRegistration.Disposed);
                var removed = await provider.InvokeAsync(Context(
                    installation,
                    "GM.unregisterMenuCommand",
                    new { commandId = 3 }), CancellationToken.None);
                Assert.False(Json(removed.Json).GetBoolean());
            }
        }

        [Fact]
        public async Task TabStateUsesCanonicalJsonObjects()
        {
            var installation = await InstallAsync(
                "// @grant GM.getTab\n// @grant GM.saveTab\n// @grant GM.getTabs");
            var state = new FakeTabStateService();
            using (var provider = new HostInteractionApiProvider(tabState: state))
            {
                await provider.InvokeAsync(Context(
                    installation, "GM.saveTab", new { value = new { count = 2 } }), CancellationToken.None);
                var current = await provider.InvokeAsync(Context(
                    installation, "GM.getTab", new { }), CancellationToken.None);
                var all = await provider.InvokeAsync(Context(
                    installation, "GM.getTabs", new { }), CancellationToken.None);

                Assert.Equal(2, Json(current.Json).GetProperty("count").GetInt32());
                Assert.Equal(2, Json(all.Json).GetProperty("main").GetProperty("count").GetInt32());
            }
        }

        [Fact]
        public void DuplicateResourcesAndInvalidConnectAreMetadataErrors()
        {
            var parser = new UserScriptMetadataParser();
            var result = parser.Parse(MetadataAndMatchingTests.Script(
                "// @name invalid\n// @match https://example.com/*\n" +
                "// @resource same https://example.com/a\n// @resource same https://example.com/b\n" +
                "// @connect bad*host"));

            Assert.False(result.CanEnable);
            Assert.Contains(result.Diagnostics, item => item.Code == "MSM051_DUPLICATE_RESOURCE");
            Assert.Contains(result.Diagnostics, item => item.Code == "MSM052_INVALID_CONNECT");
        }

        private static async Task<UserScriptInstallation> InstallAsync(string extraMetadata)
        {
            var repository = new InMemoryUserScriptRepository();
            return await repository.InstallAsync(MetadataAndMatchingTests.Script(
                "// @name advanced\n// @match https://example.com/*\n// @run-at document-end\n" + extraMetadata,
                "window.test = true;"), "test", true, CancellationToken.None);
        }

        private static ApiInvocationContext Context(
            UserScriptInstallation installation,
            string method,
            object parameters)
        {
            return new ApiInvocationContext(
                installation,
                new DocumentFrame(
                    "browser", "document", "frame", new Uri("https://example.com/page"), true,
                    TimingGuarantee.BestEffortDocumentStart, BridgeIntegrityGuarantee.Verified),
                "execution",
                Guid.NewGuid().ToString("D"),
                method,
                Json(JsonSerializer.Serialize(parameters)));
        }

        private static JsonElement Json(string json)
        {
            using (var document = JsonDocument.Parse(json))
                return document.RootElement.Clone();
        }

        private sealed class FakeResourceProvider : IResourceProvider
        {
            private readonly ResourceContent _content;
            public FakeResourceProvider(ResourceContent content) { _content = content; }
            public Task<ResourceContent> GetAsync(UserScriptInstallation installation, ResourceDeclaration resource, CancellationToken cancellationToken)
                => Task.FromResult(_content);
        }

        private sealed class FakeHttpService : IHttpRequestService
        {
            public FakeHttpService(UserScriptHttpResponse response, byte[] body) { Response = response; Body = body; }
            public UserScriptHttpRequest Request { get; private set; }
            public UserScriptHttpResponse Response { get; set; }
            public byte[] Body { get; set; }
            public IHttpRequestOperation SendAsync(
                UserScriptHttpRequest request,
                IUserScriptHttpObserver observer,
                CancellationToken cancellationToken)
            {
                Request = request;
                return new FakeHttpOperation(Response, Body, observer);
            }
        }

        private sealed class FakeHttpOperation : IHttpRequestOperation
        {
            private readonly UserScriptHttpResponse _response;
            private readonly byte[] _body;
            private readonly IUserScriptHttpObserver _observer;
            public FakeHttpOperation(UserScriptHttpResponse response, byte[] body, IUserScriptHttpObserver observer)
            { _response = response; _body = body ?? new byte[0]; _observer = observer; }
            public Task<UserScriptHttpResponse> Completion => CompleteAsync();
            public void Abort() { }
            private async Task<UserScriptHttpResponse> CompleteAsync()
            {
                await Task.Yield();
                _observer.OnResponseStarted(_response);
                _observer.OnUploadProgress(4, 4);
                _observer.OnResponseData(_body, 0, _body.Length);
                _observer.OnDownloadProgress(_body.Length, _body.Length);
                return _response;
            }
        }

        private sealed class FakeDependencyProvider : IUserScriptDependencyProvider
        {
            private readonly IDictionary<string, string> _sources;
            public FakeDependencyProvider(IDictionary<string, string> sources) { _sources = sources; }
            public Task<string> GetScriptAsync(UserScriptInstallation installation, string url, CancellationToken cancellationToken)
                => Task.FromResult(_sources[url]);
        }

        private sealed class FakeHostServices :
            IMenuService, INotificationService, IClipboardService, ITabService, IDownloadService
        {
            private Action _menuCallback;
            public FakeMenuRegistration MenuRegistration { get; } = new FakeMenuRegistration();
            public string ClipboardText { get; private set; }
            public OpenTabRequest OpenTab { get; private set; }

            public Task<IMenuRegistration> RegisterAsync(MenuCommandRequest request, Action invoked, CancellationToken cancellationToken)
            {
                _menuCallback = invoked;
                return Task.FromResult<IMenuRegistration>(MenuRegistration);
            }
            public void InvokeMenu() => _menuCallback();
            public INotificationHandle ShowAsync(UserScriptNotificationRequest request, CancellationToken cancellationToken)
                => new FakeNotificationHandle();
            public Task SetTextAsync(string text, string mediaType, CancellationToken cancellationToken)
            {
                ClipboardText = text;
                return Task.CompletedTask;
            }
            public Task<ITabHandle> OpenAsync(OpenTabRequest request, CancellationToken cancellationToken)
            {
                OpenTab = request;
                return Task.FromResult<ITabHandle>(new FakeTabHandle("tab"));
            }
            public Task<IDownloadOperation> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken)
                => Task.FromResult<IDownloadOperation>(new FakeDownloadOperation("download"));
        }

        private sealed class FakeNotificationHandle : INotificationHandle
        {
            public Task Completion { get; } = Task.CompletedTask;
            public event EventHandler Clicked;
            public event EventHandler Closed;
            public void Dispose() { }
        }

        private sealed class FakeTabHandle : ITabHandle
        {
            public FakeTabHandle(string id) { TabId = id; }
            public string TabId { get; }
            public bool Closed { get; private set; }
            public event EventHandler OnClose;
            public Task CloseAsync(CancellationToken cancellationToken)
            {
                if (!Closed) { Closed = true; OnClose?.Invoke(this, EventArgs.Empty); }
                return Task.CompletedTask;
            }
            public void Dispose() { }
        }

        private sealed class FakeDownloadOperation : IDownloadOperation
        {
            public FakeDownloadOperation(string id) { DownloadId = id; }
            public string DownloadId { get; }
            public Task Completion { get; } = Task.CompletedTask;
            public event EventHandler<UserScriptDownloadProgress> Progress;
            public event EventHandler Completed;
            public event EventHandler<UserScriptDownloadFailure> Failed;
            public event EventHandler Aborted;
            public void Abort() { Aborted?.Invoke(this, EventArgs.Empty); }
            public void Dispose() { }
        }

        private sealed class FakeMenuRegistration : IMenuRegistration
        {
            public bool Disposed { get; private set; }
            public void Dispose() { Disposed = true; }
        }

        private sealed class FakeTabStateService : ITabStateService
        {
            private string _json = "{}";
            public Task<string> GetAsync(ScriptKey scriptKey, DocumentFrame frame, CancellationToken cancellationToken)
                => Task.FromResult(_json);
            public Task SaveAsync(ScriptKey scriptKey, DocumentFrame frame, string jsonValue, CancellationToken cancellationToken)
            {
                _json = jsonValue;
                return Task.CompletedTask;
            }
            public Task<IReadOnlyDictionary<string, string>> GetAllAsync(ScriptKey scriptKey, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyDictionary<string, string>>(
                    new Dictionary<string, string> { ["main"] = _json });
        }
    }
}
