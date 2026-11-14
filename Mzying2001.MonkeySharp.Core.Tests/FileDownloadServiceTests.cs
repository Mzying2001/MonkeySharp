using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Permissions;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using Mzying2001.MonkeySharp.Core.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class FileDownloadServiceTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "MonkeySharpFileTests", Guid.NewGuid().ToString("N"));
        public FileDownloadServiceTests() => Directory.CreateDirectory(_directory);

        [Fact]
        public async Task GatewayDownloadRetainsAuthenticatedFrameForSharedWebRequestRules()
        {
            var repository = new InMemoryUserScriptRepository();
            await repository.InstallAsync(MetadataAndMatchingTests.Script("// @name download rules\n// @match https://example.com/*\n// @run-at document-end\n// @grant GM.download\n// @webRequest {\"selector\":\"*example.com*\",\"action\":\"cancel\"}"), "test", true, CancellationToken.None);
            using var engine = new UserScriptEngine(repository);
            using var store = new InMemoryUserScriptValueStore();
            using var rules = new InMemoryWebRequestService();
            using var staticRules = new StaticWebRequestRules(repository, rules, new AllowDeclaredPermissionsPolicy(), "browser", () => false);
            await staticRules.InitializeAsync(CancellationToken.None);
            var http = new Http(Array.Empty<byte>());
            using var files = new FileDownloadService(http, (_, __) => Task.FromResult(new DownloadDestination(Path.Combine(_directory, "rules.bin"), false)));
            using var gateway = new UserScriptBridgeGateway(engine, store, providers: new[] { new HostInteractionApiProvider(downloads: files) });
            var frame = new DocumentFrame("browser", "document", "frame", new Uri("https://example.com/page"), true, TimingGuarantee.BestEffortDocumentStart, BridgeIntegrityGuarantee.TrustedPageWorld);
            var invocation = Assert.Single((await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, frame), CancellationToken.None)).Invocations);
            using var result = JsonDocument.Parse(await gateway.DispatchAsync(JsonSerializer.Serialize(new
            {
                type = "request", protocol = 2, requestId = Guid.NewGuid().ToString(), documentId = frame.DocumentId,
                scriptKey = invocation.ScriptKey.ToString(), capability = invocation.Capability, method = "GM.download",
                @params = new { clientId = Guid.NewGuid().ToString(), url = "https://example.com/file", frameId = "untrusted" }
            }), CancellationToken.None));
            Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
            await files.DrainAsync();
            Assert.Same(frame, http.Request.OriginFrame);
            Assert.Equal(WebRequestActionKind.Block, rules.Evaluate(new WebRequestEvent(WebRequestPhase.OnBeforeRequest,
                1, http.Request.Url.AbsoluteUri, "GET", "xhr", frame: http.Request.OriginFrame)).Kind);
            var other = new DocumentFrame("other", frame.DocumentId, frame.FrameId, frame.Url, true, frame.TimingGuarantee);
            Assert.Equal(WebRequestActionKind.Allow, rules.Evaluate(new WebRequestEvent(WebRequestPhase.OnBeforeRequest,
                2, http.Request.Url.AbsoluteUri, "GET", "xhr", frame: other)).Kind);
        }

        [Fact]
        public async Task CommitPrecedesCompletionAndConflictPoliciesPreserveContents()
        {
            var http = new Http(new byte[] { 0, 1, 2, 255 });
            var path = Path.Combine(_directory, "file.bin");
            File.WriteAllText(path, "original");
            using var service = new FileDownloadService(http, (request, token) => Task.FromResult(new DownloadDestination(path, request.ConflictAction == "overwrite")));
            var operation = await service.CreateAsync(Request(), CancellationToken.None);
            Assert.Equal(0, http.Requests);
            var completed = 0;
            operation.Completed += (_, __) => { Assert.Equal(http.Bytes, File.ReadAllBytes(Path.Combine(_directory, "file (1).bin"))); completed++; };
            operation.Start(); operation.Start();
            await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, http.Requests);
            Assert.Equal(1, completed);
            Assert.Equal("original", File.ReadAllText(path));
            Assert.Equal("file (1).bin", operation.Result.Name);
            Assert.Equal(4, operation.Result.BytesReceived);
            var replacement = await service.CreateAsync(Request("overwrite"), CancellationToken.None);
            replacement.Start();
            await replacement.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(http.Bytes, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(_directory, "*.part"));
        }

        [Fact]
        public async Task EmptyFileIsCommittedAndCredentialsAndRedirectPolicyAreForwarded()
        {
            var http = new Http(Array.Empty<byte>());
            Func<Uri, CancellationToken, Task<bool>> authorize = (_, __) => Task.FromResult(false);
            using var service = new FileDownloadService(http, (_, __) => Task.FromResult(new DownloadDestination(Path.Combine(_directory, "empty"), false)));
            var operation = await service.CreateAsync(new DownloadRequest(default, new Uri("https://example.com/file"), "empty", false,
                new Dictionary<string, string> { ["X-Test"] = "yes" }, anonymous: true, redirectAllowed: authorize), CancellationToken.None);
            operation.Start();
            await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, new FileInfo(Path.Combine(_directory, "empty")).Length);
            Assert.True(http.Request.Options.Anonymous);
            Assert.Equal("yes", http.Request.Headers["X-Test"]);
            Assert.False(await http.Request.RedirectAllowed(new Uri("https://elsewhere.test"), CancellationToken.None));
        }

        [Theory]
        [InlineData("picker")]
        [InlineData("abort")]
        [InlineData("disk")]
        public async Task FailuresBeforeTransferHaveNoNetworkOrFinalFile(string kind)
        {
            var http = new Http(new byte[] { 1 });
            using var service = new FileDownloadService(http, (_, __) => Task.FromResult(kind == "picker" ? null :
                new DownloadDestination(Path.Combine(_directory, kind == "disk" ? "missing/file.bin" : "file.bin"), false)));
            var operation = await service.CreateAsync(Request(), CancellationToken.None);
            if (kind == "abort") operation.Abort();
            operation.Start();
            if (kind == "disk") await Assert.ThrowsAsync<DirectoryNotFoundException>(() => operation.Completion);
            else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.Completion);
            Assert.Equal(0, http.Requests);
            Assert.Empty(Directory.GetFiles(_directory));
        }

        [Fact]
        public async Task ProgressAbortRemovesPartialBeforeItsOnlyTerminalEvent()
        {
            var http = new Http(Enumerable.Range(0, 128).Select(value => (byte)value).ToArray());
            using var service = new FileDownloadService(http, (_, __) => Task.FromResult(new DownloadDestination(Path.Combine(_directory, "file.bin"), false)));
            var operation = await service.CreateAsync(Request(), CancellationToken.None);
            var terminals = 0;
            operation.Progress += (_, __) => operation.Abort();
            operation.Aborted += (_, __) => { Assert.Empty(Directory.GetFiles(_directory)); terminals++; };
            operation.Completed += (_, __) => terminals += 100;
            operation.Failed += (_, __) => terminals += 100;
            operation.Start();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.Completion);
            operation.Abort(); operation.Dispose();
            Assert.Equal(1, terminals);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RendererBodyUsesAtomicFilePathWithoutNetworkAndCleansAfterAbort(bool abort)
        {
            var http = new Http(Array.Empty<byte>());
            using var body = new HttpSpoolingBuffer();
            var bytes = Enumerable.Range(0, 350003).Select(i => (byte)(i % 251)).ToArray();
            body.Append(bytes, 0, bytes.Length);
            var path = Path.Combine(_directory, "blob.bin");
            using var service = new FileDownloadService(http, (_, __) => Task.FromResult(new DownloadDestination(path, false)));
            var operation = await service.CreateAsync(new DownloadRequest(default, new Uri("blob:https://example.com/id"), "blob.bin", false, body: body), CancellationToken.None);
            if (abort) operation.Progress += (_, __) => operation.Abort();
            operation.Start();
            if (abort) { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.Completion); Assert.False(File.Exists(path)); }
            else { await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(bytes, File.ReadAllBytes(path)); }
            Assert.Empty(Directory.GetFiles(_directory, "*.part"));
            Assert.Equal(0, http.Requests);
        }

        private static DownloadRequest Request(string conflict = "uniquify") => new DownloadRequest(default, new Uri("https://example.com/file"), "file.bin", false, conflictAction: conflict);
        public void Dispose()
        {
            foreach (var path in Directory.GetFiles(_directory)) File.Delete(path);
            Directory.Delete(_directory);
        }

        private sealed class Http : IHttpRequestService
        {
            public Http(byte[] bytes) { Bytes = bytes; }
            public byte[] Bytes { get; }
            public int Requests;
            public UserScriptHttpRequest Request;
            public IHttpRequestOperation SendAsync(UserScriptHttpRequest request, IUserScriptHttpObserver observer, CancellationToken cancellationToken)
            {
                Request = request; Requests++;
                return new Network(Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var response = new UserScriptHttpResponse(200, "OK", request.Url, new Dictionary<string, string>(), "", "application/octet-stream", null);
                    observer.OnResponseStarted(response);
                    foreach (var value in Bytes) observer.OnResponseData(new[] { value }, 0, 1);
                    cancellationToken.ThrowIfCancellationRequested();
                    return response;
                }));
            }
            private sealed class Network : IHttpRequestOperation
            {
                public Network(Task<UserScriptHttpResponse> completion) { Completion = completion; }
                public Task<UserScriptHttpResponse> Completion { get; }
                public void Abort() { }
            }
        }
    }
}
