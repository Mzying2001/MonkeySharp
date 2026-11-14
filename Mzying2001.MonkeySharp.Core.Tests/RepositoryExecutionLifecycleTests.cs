using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Permissions;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using Mzying2001.MonkeySharp.Core.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class RepositoryExecutionLifecycleTests
    {
        [Theory]
        [InlineData("disable")]
        [InlineData("remove")]
        [InlineData("update")]
        public async Task RepositoryChangesRevokeOldExecutionsWithoutAnotherBridgeCall(string change)
        {
            var repository = new InMemoryUserScriptRepository();
            var installation = await repository.InstallAsync(Source("changed"), "test", true, CancellationToken.None);
            var other = await repository.InstallAsync(Source("other"), "test", true, CancellationToken.None);
            using var engine = new UserScriptEngine(repository);
            using var store = new InMemoryUserScriptValueStore();
            var observer = new Observer();
            using var gateway = new UserScriptBridgeGateway(engine, store, providers: new[] { observer });
            var frame = Frame("old");
            var invocations = (await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, frame), CancellationToken.None)).Invocations;
            var old = invocations.Single(item => item.ScriptKey == installation.ScriptKey);
            var notifications = new List<BridgeNotificationEventArgs>();
            gateway.Notification += (_, args) => notifications.Add(args);
            if (change == "disable") await repository.SetEnabledAsync(installation.ScriptKey, false, CancellationToken.None);
            else if (change == "remove") await repository.RemoveAsync(installation.ScriptKey, CancellationToken.None);
            else await repository.UpdateAsync(installation.ScriptKey, Source("updated"), "test", CancellationToken.None);
            // Join the existing engine queue without sending a bridge request.
            await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.UrlChanged, frame), CancellationToken.None);
            Assert.Equal(old.ExecutionId, Assert.Single(observer.Ended));
            Assert.Equal(old.ExecutionId, Assert.Single(notifications).ExecutionId);
            Assert.Equal("execution-ended", notifications[0].EventName);
            notifications.Clear();
            await store.SetAsync(installation.ScriptKey.ToString(), "value", UserScriptValue.FromJson("42"), CancellationToken.None);
            observer.Emit(installation.ScriptKey, old.ExecutionId);
            Assert.Empty(notifications);
            await store.SetAsync(other.ScriptKey.ToString(), "value", UserScriptValue.FromJson("42"), CancellationToken.None);
            Assert.Equal("storage-sync", Assert.Single(notifications).EventName);
            Assert.Single(engine.GetExecutionsForScript(other.ScriptKey));
            if (change == "disable") await repository.SetEnabledAsync(installation.ScriptKey, true, CancellationToken.None);
            var fresh = await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, Frame("new")), CancellationToken.None);
            Assert.Equal(change == "remove" ? 1 : 2, fresh.Invocations.Count);
            await engine.InvalidateDocumentAsync(frame.DocumentId, CancellationToken.None);
            Assert.Equal(1, observer.Ended.Count(id => id == old.ExecutionId));
        }

        [Fact]
        public async Task DisableMutesAcceptedDownloadWithoutCancelingItsTransfer()
        {
            var repository = new InMemoryUserScriptRepository();
            var script = await repository.InstallAsync(Source("download"), "test", true, CancellationToken.None);
            using var engine = new UserScriptEngine(repository);
            using var store = new InMemoryUserScriptValueStore();
            var downloads = new Downloads();
            using var provider = new HostInteractionApiProvider(downloads: downloads);
            using var gateway = new UserScriptBridgeGateway(engine, store, providers: new[] { provider });
            var frame = Frame("download");
            var invocation = Assert.Single((await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, frame), CancellationToken.None)).Invocations);
            using var result = JsonDocument.Parse(await gateway.DispatchAsync(DownloadEnvelope(frame, invocation), CancellationToken.None));
            Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
            var callbacks = 0;
            provider.Notification += (_, __) => callbacks++;
            await repository.SetEnabledAsync(script.ScriptKey, false, CancellationToken.None);
            await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.UrlChanged, frame), CancellationToken.None);
            Assert.False(downloads.Operation.AbortedByHost);
            downloads.Operation.Complete();
            Assert.Equal(0, callbacks);
            Assert.True(downloads.Operation.Completion.IsCompletedSuccessfully);
        }

        [Theory]
        [InlineData("navigation")]
        [InlineData("disable")]
        [InlineData("update")]
        [InlineData("remove")]
        public async Task AcceptedDownloadRedirectsUseCapturedInstallationAndStillApplyTargetPolicy(string change)
        {
            var repository = new InMemoryUserScriptRepository();
            var script = await repository.InstallAsync(Source("captured"), "test", true, CancellationToken.None);
            using var engine = new UserScriptEngine(repository);
            using var store = new InMemoryUserScriptValueStore();
            var downloads = new Downloads();
            var policy = new TargetPolicy();
            using var gateway = new UserScriptBridgeGateway(engine, store, policy, new[] { new HostInteractionApiProvider(downloads: downloads) });
            var frame = Frame("captured");
            var invocation = Assert.Single((await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, frame), CancellationToken.None)).Invocations);
            using var result = JsonDocument.Parse(await gateway.DispatchAsync(DownloadEnvelope(frame, invocation), CancellationToken.None));
            Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
            if (change == "navigation") await engine.InvalidateDocumentAsync(frame.DocumentId, CancellationToken.None);
            else if (change == "disable") await repository.SetEnabledAsync(script.ScriptKey, false, CancellationToken.None);
            else if (change == "remove") await repository.RemoveAsync(script.ScriptKey, CancellationToken.None);
            else await repository.UpdateAsync(script.ScriptKey, Source("changed"), "test", CancellationToken.None);
            await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.UrlChanged, frame), CancellationToken.None);
            Assert.True(await downloads.Request.RedirectAllowed(new Uri("https://example.com/final"), CancellationToken.None));
            Assert.Same(script, policy.Last.Installation);
            Assert.False(policy.Last.CanPrompt);
            Assert.False(await downloads.Request.RedirectAllowed(new Uri("https://example.com/denied"), CancellationToken.None));
            using var stale = JsonDocument.Parse(await gateway.DispatchAsync(DownloadEnvelope(frame, invocation), CancellationToken.None));
            Assert.Equal(BridgeErrorCodes.SessionExpired, stale.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        private sealed class TargetPolicy : IUserScriptPermissionPolicy
        {
            public ApiAuthorizationRequest Last;
            public Task<PermissionDecision> AuthorizeAsync(ApiAuthorizationRequest request, CancellationToken token)
            { Last = request; return Task.FromResult(request.Target?.EndsWith("/denied", StringComparison.Ordinal) == true ? PermissionDecision.Deny : PermissionDecision.Allow); }
        }

        [Fact]
        public async Task RepositoryCallbackDoesNotBlockOnTheEngineQueueAndCanBeDisposedWhileQueued()
        {
            var repository = new InMemoryUserScriptRepository();
            var script = await repository.InstallAsync(Source("queued"), "test", true, CancellationToken.None);
            using var engine = new UserScriptEngine(repository);
            var queue = (SemaphoreSlim)typeof(UserScriptEngine).GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(engine);
            await queue.WaitAsync();
            Task disposing = null;
            try
            {
                await Task.Run(() => repository.SetEnabledAsync(script.ScriptKey, false, CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(2));
                disposing = Task.Run(engine.Dispose);
            }
            finally { queue.Release(); }
            await disposing.WaitAsync(TimeSpan.FromSeconds(2));
            await repository.SetEnabledAsync(script.ScriptKey, true, CancellationToken.None);
        }

        internal static string DownloadEnvelope(DocumentFrame frame, ScriptInvocation invocation) => JsonSerializer.Serialize(new
        {
            type = "request", protocol = 2, requestId = Guid.NewGuid().ToString(), documentId = frame.DocumentId,
            scriptKey = invocation.ScriptKey.ToString(), capability = invocation.Capability, method = "GM.download",
            @params = new { clientId = Guid.NewGuid().ToString(), url = "https://example.com/start" }
        });

        internal sealed class Downloads : IDownloadService
        {
            public string DownloadMode => "browser";
            public DownloadRequest Request;
            public readonly Download Operation = new Download();
            public Task<IDownloadOperation> CreateAsync(DownloadRequest request, CancellationToken token)
            { Request = request; return Task.FromResult<IDownloadOperation>(Operation); }
        }

        internal sealed class Download : IDownloadOperation
        {
            private readonly TaskCompletionSource<object> _completion = new TaskCompletionSource<object>();
            public bool AbortedByHost;
            public string DownloadId => "download";
            public DownloadResult Result => new DownloadResult(DownloadId);
            public Task Completion => _completion.Task;
            public event EventHandler Completed;
#pragma warning disable CS0067
            public event EventHandler<UserScriptDownloadProgress> Progress;
            public event EventHandler<UserScriptDownloadFailure> Failed;
            public event EventHandler Aborted;
            public event EventHandler TimedOut;
#pragma warning restore CS0067
            public void Start() { }
            public void Abort() => AbortedByHost = true;
            public void Dispose() { }
            public void Complete() { Completed?.Invoke(this, EventArgs.Empty); _completion.TrySetResult(null); }
        }

        internal static string Source(string name) => MetadataAndMatchingTests.Script("// @name " + name + "\n// @match https://example.com/*\n// @run-at document-end\n// @grant GM.getValue\n// @grant GM.download");
        internal static DocumentFrame Frame(string id) => new DocumentFrame("browser", id, "main", new Uri("https://example.com/page"), true, TimingGuarantee.BestEffortDocumentStart, BridgeIntegrityGuarantee.Verified);

        private sealed class Observer : IUserScriptApiProvider, IUserScriptExecutionObserver, IUserScriptNotificationSource
        {
            public IReadOnlyCollection<string> Methods => new[] { "GM.download" };
            public readonly List<string> Ended = new List<string>();
            public event EventHandler<ApiNotificationEventArgs> Notification;
            public Task<ApiResult> InvokeAsync(ApiInvocationContext context, CancellationToken token) => Task.FromResult(ApiResult.Undefined);
            public void OnExecutionEnded(string id) => Ended.Add(id);
            public void Emit(Mzying2001.MonkeySharp.Core.Domain.ScriptKey key, string id) => Notification?.Invoke(this, new ApiNotificationEventArgs(key, id, "provider", "{}"));
        }
    }
}
