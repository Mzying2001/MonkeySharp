using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using Mzying2001.MonkeySharp.Core.Parsing;
using Mzying2001.MonkeySharp.Core.Security;
using System;
using System.Collections.Concurrent;
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
        public async Task PreparedResourceApisCheckDeclarationAndResponseBudget()
        {
            var resources = new FakeResourceProvider(new ResourceContent(Encoding.UTF8.GetBytes("hello"), "text/plain", "hello"));
            var repository = new InMemoryUserScriptRepository(preparer: new UserScriptPreparer(resources: resources));
            var installation = await repository.InstallAsync(MetadataAndMatchingTests.Script(
                "// @name resources\n// @match https://example.com/*\n// @resource logo https://cdn.example/logo.txt"), "test", true, CancellationToken.None);
            using (var provider = new ResourceAndNetworkApiProvider(options: new BridgeOptions(maxResourceBytes: 8)))
            {
                Assert.Equal("hello", Json((await provider.InvokeAsync(Context(installation, "GM.getResourceText", new { name = "logo" }), CancellationToken.None)).Json).GetString());
                Assert.Equal("data:text/plain;base64,aGVsbG8=", Json((await provider.InvokeAsync(Context(installation, "GM.getResourceUrl", new { name = "logo" }), CancellationToken.None)).Json).GetString());
                await Assert.ThrowsAsync<BridgeProtocolException>(() => provider.InvokeAsync(Context(installation, "GM.getResourceText", new { name = "missing" }), CancellationToken.None));
            }
            using (var limited = new ResourceAndNetworkApiProvider(options: new BridgeOptions(maxResourceBytes: 4)))
            {
                var exception = await Assert.ThrowsAsync<BridgeProtocolException>(() => limited.InvokeAsync(Context(installation, "GM.getResourceText", new { name = "logo" }), CancellationToken.None));
                Assert.Equal(BridgeErrorCodes.PayloadTooLarge, exception.Code);
            }
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
            Assert.True(await service.Request.RedirectAllowed(
                new Uri("https://api.example.com/next"), CancellationToken.None));
            Assert.False(await service.Request.RedirectAllowed(
                new Uri("https://escape.example/"), CancellationToken.None));
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
        public async Task HttpApiAcceptsDocumentResponseType()
        {
            var installation = await InstallAsync(
                "// @grant GM.xmlHttpRequest\n// @connect api.example.com");
            var service = new FakeHttpService(new UserScriptHttpResponse(
                200, "OK", new Uri("https://api.example.com/xml"),
                new Dictionary<string, string>(), string.Empty, "application/xml", "utf-8"),
                Encoding.UTF8.GetBytes("<root/>"));
            using (var provider = new ResourceAndNetworkApiProvider(http: service))
            {
                var created = await provider.InvokeAsync(Context(installation, "GM.xmlHttpRequest", new
                {
                    operation = "create", xhrId = 19, url = "https://api.example.com/xml",
                    responseType = "document"
                }), CancellationToken.None);
                var sessionId = Json(created.Json).GetProperty("sessionId").GetString();
                await provider.InvokeAsync(Context(installation, "GM.xmlHttpRequest", new { operation = "execute", sessionId }), CancellationToken.None);
                Assert.Equal(UserScriptHttpResponseType.Document, service.Request.Options.ResponseType);
            }
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
            var notifications = new List<ApiNotificationEventArgs>();
            provider.Notification += (_, item) => notifications.Add(item);

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
                overrideMimeType = "text/plain;charset=iso-8859-1",
                redirect = "manual",
                nocache = true,
                revalidate = true,
                fetch = true,
                timeout = 10
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
            Assert.Equal(UserScriptHttpRedirectMode.Manual, service.Request.Options.Redirect);
            Assert.True(service.Request.Options.NoCache);
            Assert.True(service.Request.Options.Revalidate);
            Assert.True(service.Request.Options.Fetch);
            Assert.Null(service.Request.Timeout);
            Assert.Single(notifications);
            Assert.Equal("xhr-state", notifications[0].EventName);
            Assert.Equal(4, Json(notifications[0].DataJson).GetProperty("readyState").GetInt32());

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

        [Theory]
        [InlineData("https://example.com/start", "https://api.example.com/next", "https://api.example.com/final")]
        [InlineData("https://api.example.com/start", "https://example.com/next", "https://api.example.com/final")]
        [InlineData("https://api.example.com/start", "https://api.example.com/next", "https://example.com/final")]
        public async Task HttpRedirectsRecheckConnectAndHostPermissionPolicy(string initialUrl, string redirectUrl, string finalUrl)
        {
            var installation = await InstallAsync("// @grant GM.xmlHttpRequest\n// @connect *.example.com");
            var response = new UserScriptHttpResponse(
                200, "OK", new Uri(finalUrl),
                new Dictionary<string, string>(), string.Empty, null, null, new[] { new Uri(redirectUrl) });
            var service = new FakeHttpService(response, null);
            var provider = new ResourceAndNetworkApiProvider(http: service);
            var authorized = new List<string>();
            var created = await provider.InvokeAsync(Context(
                installation,
                "GM.xmlHttpRequest",
                new { operation = "create", xhrId = 16, url = initialUrl },
                (target, _) =>
                {
                    authorized.Add(target);
                    return Task.FromResult(!target.Contains("denied"));
                }), CancellationToken.None);
            var sessionId = Json(created.Json).GetProperty("sessionId").GetString();
            var result = await provider.InvokeAsync(Context(installation, "GM.xmlHttpRequest", new
            {
                operation = "execute", sessionId
            }), CancellationToken.None);

            Assert.Equal(finalUrl, Json(result.Json).GetProperty("finalUrl").GetString());
            Assert.True(await service.Request.RedirectAllowed(
                new Uri("https://example.com/next"), CancellationToken.None));
            Assert.False(await service.Request.RedirectAllowed(
                new Uri("https://example.com/denied"), CancellationToken.None));
            Assert.True(await service.Request.RedirectAllowed(
                new Uri("https://allowed.example.com/next"), CancellationToken.None));
            Assert.False(await service.Request.RedirectAllowed(
                new Uri("https://denied.example.com/next"), CancellationToken.None));
            Assert.False(await service.Request.RedirectAllowed(
                new Uri("https://outside.test/next"), CancellationToken.None));
            Assert.Equal(new[]
            {
                "https://example.com/next", "https://example.com/denied",
                "https://allowed.example.com/next", "https://denied.example.com/next"
            }, authorized);
        }

        [Theory]
        [InlineData("example.com", "https://example.com/path", true)]
        [InlineData("example.com", "https://api.example.com/path", true)]
        [InlineData("example.com", "https://badexample.com/path", false)]
        [InlineData("*.example.com", "https://api.example.com/path", true)]
        [InlineData("*.example.com", "https://example.com/path", true)]
        [InlineData("*.example.com", "https://a.b.example.com/path", true)]
        [InlineData("*.EXAMPLE.COM", "https://EXAMPLE.com/path", true)]
        [InlineData("*.example.com", "https://badexample.com/path", false)]
        [InlineData("*.example.com", "https://example.com.evil.test/path", false)]
        [InlineData("localhost", "http://localhost:8080/path", true)]
        [InlineData("localhost", "http://api.localhost/path", false)]
        [InlineData("127.0.0.1", "http://127.0.0.1:8080/path", true)]
        [InlineData("127.0.0.1", "http://127.0.0.2:8080/path", false)]
        [InlineData("例子.测试", "https://api.xn--fsqu00a.xn--0zwm56d/path", true)]
        [InlineData("*.例子.测试", "https://xn--fsqu00a.xn--0zwm56d/path", true)]
        [InlineData("*.例子.测试", "https://api.xn--fsqu00a.xn--0zwm56d/path", true)]
        [InlineData("https://example.com:8443", "https://api.example.com:8443/path", true)]
        [InlineData("https://example.com:8443", "https://api.example.com/path", false)]
        public void ConnectHostRulesNormalizeDomainsAndPorts(string declaration, string target, bool expected)
        {
            Assert.Equal(expected, ResourceAndNetworkApiProvider.ConnectAllows(
                new[] { declaration }, new Uri("https://example.com/source"), new Uri(target)));
        }

        [Fact]
        public async Task HttpStreamUsesBoundedPullChunksAndCompletesIndependentlyOfConsumption()
        {
            var installation = await InstallAsync("// @grant GM.xmlHttpRequest\n// @connect api.example.com");
            var response = new UserScriptHttpResponse(
                200, "OK", new Uri("https://api.example.com/stream"),
                new Dictionary<string, string> { ["Content-Type"] = "text/plain" },
                "Content-Type: text/plain\r\n", "text/plain", "utf-8");
            var service = new FakeHttpService(response, Encoding.UTF8.GetBytes("stream body"));
            var provider = new ResourceAndNetworkApiProvider(http: service);
            var notifications = new ConcurrentQueue<ApiNotificationEventArgs>();
            provider.Notification += (_, item) => notifications.Enqueue(item);
            var created = await provider.InvokeAsync(Context(installation, "GM.xmlHttpRequest", new
            {
                operation = "create", xhrId = 17, url = "https://api.example.com/stream", responseType = "stream"
            }), CancellationToken.None);
            var sessionId = Json(created.Json).GetProperty("sessionId").GetString();

            var result = await provider.InvokeAsync(Context(installation, "GM.xmlHttpRequest", new
            {
                operation = "execute", sessionId
            }), CancellationToken.None);

            Assert.True(Json(result.Json).GetProperty("streaming").GetBoolean());
            Assert.Equal(JsonValueKind.Null, Json(result.Json).GetProperty("bodyLength").ValueKind);
            var deadline = DateTime.UtcNow.AddSeconds(1);
            while (!notifications.Any(item => item.EventName == "xhr-complete") && DateTime.UtcNow < deadline)
                await Task.Delay(1);
            Assert.DoesNotContain(notifications, item => item.EventName == "xhr-chunk");
            var chunk = await provider.InvokeAsync(Context(installation, "GM.xmlHttpRequest", new { operation = "readBody", sessionId, offset = 0 }), CancellationToken.None);
            Assert.Equal("stream body", Encoding.UTF8.GetString(Convert.FromBase64String(Json(chunk.Json).GetProperty("chunk").GetString())));
            Assert.True(Json(chunk.Json).GetProperty("done").GetBoolean());
            Assert.Single(notifications, item => item.EventName == "xhr-complete");
        }

        [Fact]
        public async Task HttpNotificationCallbackFailuresProduceDiagnostics()
        {
            var installation = await InstallAsync("// @grant GM.xmlHttpRequest\n// @connect api.example.com");
            var service = new FakeHttpService(null, null);
            using (var provider = new ResourceAndNetworkApiProvider(http: service))
            {
                var diagnostics = new List<UserScriptDiagnostic>();
                provider.Notification += (_, __) => throw new InvalidOperationException("observer failed");
                provider.Diagnostic += (_, diagnostic) => diagnostics.Add(diagnostic);

                await provider.InvokeAsync(Context(installation, "GM.xmlHttpRequest", new
                {
                    operation = "create",
                    xhrId = 18,
                    url = "https://api.example.com/"
                }), CancellationToken.None);

                var diagnostic = Assert.Single(diagnostics);
                Assert.Equal("MSA300_NOTIFICATION_CALLBACK_FAILED", diagnostic.Code);
                Assert.Equal(installation.ScriptKey, diagnostic.ScriptKey);
                Assert.Equal("document", diagnostic.DocumentId);
                Assert.IsType<InvalidOperationException>(diagnostic.Exception);
            }
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
                provider.Notification += (_, item) => { if (item.EventName == "menu-command") notification = item; };
                var registered = await provider.InvokeAsync(Context(
                    installation,
                    "GM.registerMenuCommand",
                    new { commandId = 3, name = "Open", accessKey = "o" }), CancellationToken.None);
                services.InvokeMenu();
                await provider.InvokeAsync(Context(
                    installation,
                    "GM.notification",
                    new
                    {
                        clientId = Guid.NewGuid().ToString(), title = "Title", text = "Text", imageUrl = "https://example.com/notification.png",
                        highlight = true, silent = true, timeout = 300
                    }), CancellationToken.None);
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
                    new
                    {
                        clientId = Guid.NewGuid().ToString("D"),
                        url = "https://example.com/file",
                        name = "file",
                        saveAs = true,
                        headers = new { Authorization = "Bearer test" },
                        conflictAction = "overwrite",
                        timeout = 2500
                    }), CancellationToken.None);
                var removed = await provider.InvokeAsync(Context(
                    installation,
                    "GM.unregisterMenuCommand",
                    new { commandId = 3 }), CancellationToken.None);

                Assert.Equal(3, Json(registered.Json).GetInt32());
                Assert.Equal("menu-command", notification.EventName);
                Assert.Equal("Text", services.Notification.Text);
                Assert.Equal("https://example.com/notification.png", services.Notification.ImageUrl);
                Assert.True(services.Notification.Highlight);
                Assert.True(services.Notification.Silent);
                Assert.Equal(300, services.Notification.Timeout);
                Assert.Equal("copy", services.ClipboardText);
                Assert.False(services.OpenTab.Active);
                Assert.Equal("tab", Json(tab.Json).GetProperty("id").GetString());
                Assert.Equal("download", Json(download.Json).GetProperty("id").GetString());
                Assert.Equal("Bearer test", services.Download.Headers["Authorization"]);
                Assert.Equal("overwrite", services.Download.ConflictAction);
                Assert.Equal(TimeSpan.FromMilliseconds(2500), services.Download.Timeout);
                Assert.Equal("undefined", Json(removed.Json).GetProperty("$monkeySharpType").GetString());
                Assert.True(services.MenuRegistration.Disposed);

                await provider.InvokeAsync(Context(installation, "GM.notification", new
                {
                    clientId = Guid.NewGuid().ToString(), highlight = true, silent = true, timeout = 0
                }), CancellationToken.None);
                Assert.Null(services.Notification.Text);
                Assert.True(services.Notification.Highlight);
                Assert.Equal(0, services.Notification.Timeout);
            }
        }

        [Fact]
        public async Task WindowServicesAreExposedAsGrantGatedMethods()
        {
            var installation = await InstallAsync("// @grant window.close\n// @grant window.focus");
            var services = new FakeHostServices();
            using (var provider = new HostInteractionApiProvider(window: services))
            {
                var closed = await provider.InvokeAsync(Context(installation, "window.close", new { }), CancellationToken.None);
                var focused = await provider.InvokeAsync(Context(installation, "window.focus", new { }), CancellationToken.None);

                Assert.True(Json(closed.Json).GetBoolean());
                Assert.True(Json(focused.Json).GetBoolean());
                Assert.Equal(1, services.CloseCalls);
                Assert.Equal(1, services.FocusCalls);
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
                Assert.Equal("undefined", Json(removed.Json).GetProperty("$monkeySharpType").GetString());
            }
        }

        [Fact]
        public async Task HostHandlesRemoveTerminalOperationsAndRejectRepeatAbortOrClose()
        {
            var installation = await InstallAsync(
                "// @grant GM.openInTab\n// @grant GM.download");
            var services = new FakeHostServices();
            using (var provider = new HostInteractionApiProvider(tabs: services, downloads: services))
            {
                await provider.InvokeAsync(Context(installation, "GM.openInTab", new { url = "https://example.com" }), CancellationToken.None);
                var closed = await provider.InvokeAsync(Context(installation, "GM.openInTab", new { close = true, tabId = "tab" }), CancellationToken.None);
                var repeatedClose = await provider.InvokeAsync(Context(installation, "GM.openInTab", new { close = true, tabId = "tab" }), CancellationToken.None);
                Assert.True(Json(closed.Json).GetBoolean());
                Assert.False(Json(repeatedClose.Json).GetBoolean());

                var clientId = Guid.NewGuid().ToString("D");
                var download = await provider.InvokeAsync(Context(installation, "GM.download", new { url = "https://example.com/file", clientId }), CancellationToken.None);
                var aborted = await provider.InvokeAsync(Context(installation, "GM.download", new { operation = "abort", clientId }), CancellationToken.None);
                var repeatedAbort = await provider.InvokeAsync(Context(installation, "GM.download", new { operation = "abort", clientId }), CancellationToken.None);
                Assert.Equal("download", Json(download.Json).GetProperty("id").GetString());
                Assert.True(Json(aborted.Json).GetProperty("accepted").GetBoolean());
                Assert.False(Json(repeatedAbort.Json).GetProperty("accepted").GetBoolean());
            }
        }

        [Fact]
        public async Task DownloadSubscribesBeforeSynchronousCompletionAndIgnoresDuplicateTerminals()
        {
            var installation = await InstallAsync("// @grant GM.download");
            var service = new ControlledDownloadService { CompleteOnStart = true };
            using var provider = new HostInteractionApiProvider(downloads: service);
            var events = new List<string>();
            provider.Notification += (_, value) => events.Add(value.EventName);
            await provider.InvokeAsync(Context(installation, "GM.download", new { url = "https://example.com/file", clientId = Guid.NewGuid().ToString() }), CancellationToken.None);
            service.Operation.Complete();
            Assert.Equal(new[] { "download-complete" }, events);
            Assert.True(service.Operation.Disposed);
        }

        [Fact]
        public async Task DownloadCanBeCanceledWhileHostIsStillCreatingOperation()
        {
            var installation = await InstallAsync("// @grant GM.download");
            var service = new ControlledDownloadService { DelayCreation = true };
            using var provider = new HostInteractionApiProvider(downloads: service);
            var events = new List<string>();
            provider.Notification += (_, value) => events.Add(value.EventName);
            var clientId = Guid.NewGuid().ToString();
            var starting = provider.InvokeAsync(Context(installation, "GM.download", new { url = "https://example.com/file", clientId }), CancellationToken.None);
            await provider.InvokeAsync(Context(installation, "GM.download", new { operation = "abort", clientId }), CancellationToken.None);
            service.Created.SetResult(service.Operation);
            await starting;
            Assert.True(service.Operation.WasAborted);
            Assert.Equal(new[] { "download-aborted" }, events);
        }

        [Theory]
        [InlineData("unsupported", "not_supported")]
        [InlineData("permission", "not_permitted")]
        [InlineData("io", "not_succeeded")]
        public async Task DownloadFailuresPreserveTheirCauseAndTerminateOnce(string kind, string reason)
        {
            var installation = await InstallAsync("// @grant GM.download");
            var service = new ControlledDownloadService();
            using var provider = new HostInteractionApiProvider(downloads: service);
            var events = new List<ApiNotificationEventArgs>();
            provider.Notification += (_, value) => events.Add(value);
            await provider.InvokeAsync(Context(installation, "GM.download", new { url = "https://example.com/file", clientId = Guid.NewGuid().ToString() }), CancellationToken.None);
            Exception error = kind == "unsupported" ? new UnsupportedApiException("unsupported cookie input") :
                kind == "permission" ? new BridgeProtocolException(BridgeErrorCodes.PermissionDenied, "redirect denied") :
                new IOException("disk failed");
            service.Operation.Fail(error); service.Operation.Fail(error);
            var notification = Assert.Single(events);
            Assert.Equal("download-error", notification.EventName);
            Assert.Equal(reason, Json(notification.DataJson).GetProperty("error").GetString());
            Assert.Equal(error.Message, Json(notification.DataJson).GetProperty("details").GetString());
        }

        [Fact]
        public async Task DocumentEndMutesDownloadCallbacksButHostDisposalCancelsTasks()
        {
            var installation = await InstallAsync("// @grant GM.download");
            var service = new ControlledDownloadService();
            using var provider = new HostInteractionApiProvider(downloads: service);
            var events = new List<string>();
            provider.Notification += (_, value) => events.Add(value.EventName);
            await provider.InvokeAsync(Context(installation, "GM.download", new { url = "https://example.com/file", clientId = Guid.NewGuid().ToString() }), CancellationToken.None);
            provider.OnExecutionEnded("execution");
            Assert.False(service.Operation.WasAborted);
            service.Operation.Complete();
            Assert.Empty(events);
            service.Operation = new ControlledDownload();
            await provider.InvokeAsync(Context(installation, "GM.download", new { url = "https://example.com/file", clientId = Guid.NewGuid().ToString() }), CancellationToken.None);
            provider.Dispose();
            Assert.True(service.Operation.WasAborted);
            Assert.Empty(events);
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

        [Fact]
        public async Task TabCreationCorrelatesClosureBeforeAcknowledgementAndPreservesNumericInsertion()
        {
            var installation = await InstallAsync("// @grant GM.openInTab");
            var services = new FakeHostServices { CloseTabOnOpen = true };
            using var provider = new HostInteractionApiProvider(tabs: services);
            var events = new List<ApiNotificationEventArgs>();
            provider.Notification += (_, value) => events.Add(value);
            var id = Guid.NewGuid().ToString();
            var result = await provider.InvokeAsync(Context(installation, "GM.openInTab", new { url = "about:blank", insert = 0, clientId = id }), CancellationToken.None);
            Assert.True(Json(result.Json).GetProperty("closed").GetBoolean());
            Assert.Equal(id, Json(Assert.Single(events).DataJson).GetProperty("clientId").GetString());
            Assert.Equal(0, services.OpenedTab.InsertIndex);
            Assert.False(services.OpenedTab.Active);
        }

        [Fact]
        public async Task BinaryDownloadStagesContiguousBoundedBytesAndTransfersOwnershipUntilTerminal()
        {
            var installation = await InstallAsync("// @grant GM.download");
            var downloads = new ControlledDownloadService();
            using var provider = new HostInteractionApiProvider(downloads: downloads, options: new BridgeOptions(maxBinaryTransferBytes: 400000));
            var id = Guid.NewGuid().ToString();
            Task<ApiResult> Invoke(object value) => provider.InvokeAsync(Context(installation, "GM.download", value), CancellationToken.None);
            await Invoke(new { operation = "createBody", clientId = id, url = "blob:https://example.com/source", length = 300001 });
            await Assert.ThrowsAsync<BridgeProtocolException>(() => Invoke(new { operation = "startBody", clientId = id, name = "file" }));
            var bytes = Enumerable.Range(0, 300001).Select(i => (byte)(i % 251)).ToArray();
            for (var offset = 0; offset < bytes.Length; offset += 65536)
                await Invoke(new { operation = "appendBody", clientId = id, offset, chunk = Convert.ToBase64String(bytes.Skip(offset).Take(65536).ToArray()) });
            await Assert.ThrowsAsync<BridgeProtocolException>(() => Invoke(new { operation = "appendBody", clientId = id, offset = 0, chunk = "AQ==" }));
            await Invoke(new { operation = "startBody", clientId = id, name = "file" });
            var spool = Assert.IsType<HttpSpoolingBuffer>(downloads.Request.Body);
            Assert.True(spool.IsFileBacked);
            var temporary = spool.TemporaryPath;
            using (var read = spool.OpenRead()) using (var copy = new MemoryStream()) { await read.CopyToAsync(copy); Assert.Equal(bytes, copy.ToArray()); }
            provider.OnExecutionEnded("execution");
            Assert.True(File.Exists(temporary));
            downloads.Operation.Complete();
            Assert.False(File.Exists(temporary));
            var next = Guid.NewGuid().ToString();
            await Invoke(new { operation = "createBody", clientId = next, url = "blob:https://example.com/source", length = 10 });
            provider.OnExecutionEnded("execution");
            await Assert.ThrowsAsync<BridgeProtocolException>(() => Invoke(new { operation = "startBody", clientId = next, name = "file" }));
            await Assert.ThrowsAsync<BridgeProtocolException>(() => Invoke(new { operation = "createBody", clientId = next, url = "blob:https://example.com/source", length = 400001 }));
        }

        [Fact]
        public async Task PrivateTabOptionRequiresHostSupportAndIsNeverDropped()
        {
            var installation = await InstallAsync("// @grant GM_openInTab");
            var services = new FakeHostServices();
            using (var provider = new HostInteractionApiProvider(tabs: services))
            {
                await Assert.ThrowsAsync<UnsupportedApiException>(() => provider.InvokeAsync(Context(installation,
                    "GM.openInTab", new { url = "https://example.com/private", incognito = true }), CancellationToken.None));
                Assert.Null(services.OpenTab);
                services.SupportsIncognito = true;
                await provider.InvokeAsync(Context(installation, "GM.openInTab", new { url = "https://example.com/private", incognito = true }), CancellationToken.None);
                Assert.True(services.OpenTab.Incognito);
                await provider.InvokeAsync(Context(installation, "GM.openInTab", new { url = "https://example.com/normal", incognito = false }), CancellationToken.None);
                Assert.False(services.OpenTab.Incognito);
            }
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
            object parameters,
            Func<string, CancellationToken, Task<bool>> authorizeTargetAsync = null)
        {
            return new ApiInvocationContext(
                installation,
                new DocumentFrame(
                    "browser", "document", "frame", new Uri("https://example.com/page"), true,
                    TimingGuarantee.BestEffortDocumentStart, BridgeIntegrityGuarantee.Verified),
                "execution",
                Guid.NewGuid().ToString("D"),
                method,
                Json(JsonSerializer.Serialize(parameters)),
                authorizeTargetAsync);
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
            public Task<ResourceContent> GetScriptAsync(UserScriptInstallation installation, UserScriptDependencyDeclaration dependency, CancellationToken cancellationToken)
                => Task.FromResult(new ResourceContent(Encoding.UTF8.GetBytes(_sources[dependency.Url]), "application/javascript", _sources[dependency.Url]));
        }

        private sealed class FakeHostServices :
            IMenuService, INotificationService, IClipboardService, ITabService, IDownloadService, IUserScriptWindowService
        {
            public bool SupportsIncognito { get; set; }
            public string DownloadMode => "browser";
            public bool CloseTabOnOpen;
            public OpenTabRequest OpenedTab;
            private Action _menuCallback;
            public FakeMenuRegistration MenuRegistration { get; } = new FakeMenuRegistration();
            public string ClipboardText { get; private set; }
            public OpenTabRequest OpenTab { get; private set; }
            public DownloadRequest Download { get; private set; }
            public UserScriptNotificationRequest Notification { get; private set; }
            public int CloseCalls { get; private set; }
            public int FocusCalls { get; private set; }

            public Task<IMenuRegistration> RegisterAsync(MenuCommandRequest request, Action invoked, CancellationToken cancellationToken)
            {
                _menuCallback = invoked;
                return Task.FromResult<IMenuRegistration>(MenuRegistration);
            }
            public void InvokeMenu() => _menuCallback();
            public INotificationHandle Create(UserScriptNotificationRequest request, CancellationToken cancellationToken)
            {
                Notification = request;
                return new FakeNotificationHandle();
            }
            public Task SetTextAsync(string text, string mediaType, CancellationToken cancellationToken)
            {
                ClipboardText = text;
                return Task.CompletedTask;
            }
            public Task<ITabHandle> OpenAsync(OpenTabRequest request, CancellationToken cancellationToken)
            {
                OpenTab = request;
                OpenedTab = request;
                var handle = new FakeTabHandle("tab");
                if (CloseTabOnOpen) handle.CloseAsync(cancellationToken).GetAwaiter().GetResult();
                return Task.FromResult<ITabHandle>(handle);
            }
            public Task<IDownloadOperation> CreateAsync(DownloadRequest request, CancellationToken cancellationToken)
            {
                Download = request;
                return Task.FromResult<IDownloadOperation>(new FakeDownloadOperation("download"));
            }
            public Task<bool> CloseAsync(DocumentFrame frame, CancellationToken cancellationToken)
            {
                CloseCalls++;
                return Task.FromResult(true);
            }
            public Task<bool> FocusAsync(DocumentFrame frame, CancellationToken cancellationToken)
            {
                FocusCalls++;
                return Task.FromResult(true);
            }
        }

        private sealed class FakeNotificationHandle : INotificationHandle
        {
        public void Start() { }
        public void ResolveClick(bool preventDefault) { }
            public Task Completion { get; } = Task.CompletedTask;
            public event EventHandler Clicked { add { } remove { } }
            public event EventHandler Closed { add { } remove { } }
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
            public void Start() { }
            public DownloadResult Result => new DownloadResult(DownloadId);
            public FakeDownloadOperation(string id) { DownloadId = id; }
            public string DownloadId { get; }
            public Task Completion { get; } = Task.CompletedTask;
            public event EventHandler<UserScriptDownloadProgress> Progress { add { } remove { } }
            public event EventHandler Completed { add { } remove { } }
            public event EventHandler<UserScriptDownloadFailure> Failed { add { } remove { } }
            public event EventHandler Aborted;
            public event EventHandler TimedOut { add { } remove { } }
            public void Abort() { Aborted?.Invoke(this, EventArgs.Empty); }
            public void Dispose() { }
        }

        private sealed class ControlledDownloadService : IDownloadService
        {
            public string DownloadMode => "browser";
            public bool CompleteOnStart, DelayCreation;
            public ControlledDownload Operation = new ControlledDownload();
            public DownloadRequest Request;
            public TaskCompletionSource<IDownloadOperation> Created = new TaskCompletionSource<IDownloadOperation>();
            public Task<IDownloadOperation> CreateAsync(DownloadRequest request, CancellationToken cancellationToken)
            {
                Operation.CompleteOnStart = CompleteOnStart;
                Request = request;
                return DelayCreation ? Created.Task : Task.FromResult<IDownloadOperation>(Operation);
            }
        }
        private sealed class ControlledDownload : IDownloadOperation
        {
            public string DownloadId => "controlled";
            public DownloadResult Result => new DownloadResult(DownloadId, name: "empty.bin");
            private readonly TaskCompletionSource<object> _completion = new TaskCompletionSource<object>();
            public Task Completion => _completion.Task;
            public bool CompleteOnStart, WasAborted, Disposed;
            public event EventHandler<UserScriptDownloadProgress> Progress { add { } remove { } }
            public event EventHandler<UserScriptDownloadFailure> Failed;
            public event EventHandler TimedOut { add { } remove { } }
            public event EventHandler Completed, Aborted;
            public void Start() { if (WasAborted) Aborted?.Invoke(this, EventArgs.Empty); else if (CompleteOnStart) Complete(); }
            public void Complete() { Completed?.Invoke(this, EventArgs.Empty); _completion.TrySetResult(null); }
            public void Fail(Exception error) { Failed?.Invoke(this, new UserScriptDownloadFailure(error)); _completion.TrySetException(error); }
            public void Abort() { WasAborted = true; Aborted?.Invoke(this, EventArgs.Empty); _completion.TrySetCanceled(); }
            public void Dispose() { Disposed = true; }
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
