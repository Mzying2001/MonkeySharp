using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Permissions;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class PermissionPolicyTests
    {
        [Fact]
        public async Task AuthorizationWaitPausesTheActiveTransportBudget()
        {
            using (var deadline = new PausableRequestTimeout(TimeSpan.FromMilliseconds(60)))
            {
                using (deadline.Pause()) { deadline.Start(); await Task.Delay(120); Assert.False(deadline.Token.IsCancellationRequested); }
                var expired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (deadline.Token.Register(() => expired.TrySetResult(true)))
                    Assert.Same(expired.Task, await Task.WhenAny(expired.Task, Task.Delay(2000)));
            }
        }

        [Fact]
        public async Task SharedPromptSurvivesOneCanceledWaiterAndAllowOnceDoesNotPersist()
        {
            var request = await Request("GM.xmlHttpRequest", "https://api.example/one");
            var prompt = new ControlledPrompt();
            var store = new InMemoryUserScriptPermissionDecisionStore();
            using (var policy = new InteractiveUserScriptPermissionPolicy(new SensitiveUserScriptPermissionsPolicy(), store, prompt))
            using (var canceled = new CancellationTokenSource())
            {
                var first = policy.AuthorizeAsync(request, canceled.Token);
                var second = policy.AuthorizeAsync(request, CancellationToken.None);
                var dialog = await prompt.Next();
                canceled.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
                Assert.False(dialog.Token.IsCancellationRequested);
                dialog.Completion.SetResult(new UserScriptPermissionChoice(PermissionDecision.Allow));
                Assert.Equal(PermissionDecision.Allow, await second);
                Assert.Null(await store.GetAsync(UserScriptPermissionScope.From(request), CancellationToken.None));
                var later = policy.AuthorizeAsync(request, CancellationToken.None);
                var another = await prompt.Next();
                another.Completion.SetResult(new UserScriptPermissionChoice(PermissionDecision.Deny, true));
                Assert.Equal(PermissionDecision.Deny, await later);
                Assert.Equal(PermissionDecision.Deny, await policy.AuthorizeAsync(request, CancellationToken.None));
                Assert.Equal(2, prompt.Calls);
                await store.RevokeAsync(request.Installation.ScriptKey.ToString(), CancellationToken.None);
                var afterRevoke = policy.AuthorizeAsync(request, CancellationToken.None);
                (await prompt.Next()).Completion.SetResult(new UserScriptPermissionChoice(PermissionDecision.Allow));
                Assert.Equal(PermissionDecision.Allow, await afterRevoke);
            }
        }

        [Fact]
        public async Task AllCanceledWaitersCancelInteractionAndCannotRememberAChoice()
        {
            var prompt = new ControlledPrompt();
            var store = new InMemoryUserScriptPermissionDecisionStore();
            var request = await Request("GM.download", "https://files.example/file");
            using (var policy = new InteractiveUserScriptPermissionPolicy(new SensitiveUserScriptPermissionsPolicy(), store, prompt))
            using (var lifetime = new CancellationTokenSource())
            {
                var pending = policy.AuthorizeAsync(request, lifetime.Token);
                var dialog = await prompt.Next();
                lifetime.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
                Assert.True(dialog.Token.IsCancellationRequested);
                Assert.Null(await store.GetAsync(UserScriptPermissionScope.From(request), CancellationToken.None));
            }
        }

        [Fact]
        public async Task DeclarationsAreCheckedBeforePromptAndScopesDoNotLeakAcrossOriginOrCapability()
        {
            var prompt = new ControlledPrompt();
            var store = new InMemoryUserScriptPermissionDecisionStore();
            using (var policy = new InteractiveUserScriptPermissionPolicy(new SensitiveUserScriptPermissionsPolicy(), store, prompt))
            {
                Assert.Equal(PermissionDecision.Deny, await policy.AuthorizeAsync(await Request("GM.xmlHttpRequest", "https://other.example/"), CancellationToken.None));
                Assert.Equal(PermissionDecision.Deny, await policy.AuthorizeAsync(await Request("GM.cookie", "https://other.example/"), CancellationToken.None));
                Assert.Equal(0, prompt.Calls);
                var request = await Request("GM.xmlHttpRequest", "https://user:secret@api.example/path?secret=one");
                var scope = UserScriptPermissionScope.From(request);
                Assert.Equal("https://api.example", scope.Target);
                await store.SetAsync(scope, PermissionDecision.Allow, CancellationToken.None);
                var same = Copy(request, "GM.xmlHttpRequest", "https://api.example/another");
                Assert.Equal(PermissionDecision.Allow, await policy.AuthorizeAsync(same, CancellationToken.None));
                var different = policy.AuthorizeAsync(Copy(request, "GM.xmlHttpRequest", "https://api.example:8443/path"), CancellationToken.None);
                (await prompt.Next()).Completion.SetResult(new UserScriptPermissionChoice(PermissionDecision.Deny));
                Assert.Equal(PermissionDecision.Deny, await different);
                Assert.Null(await store.GetAsync(UserScriptPermissionScope.From(Copy(request, "GM.cookie", "https://api.example/")), CancellationToken.None));
            }
        }

        [Fact]
        public async Task PromptWaitDoesNotConsumeGenericRequestTimeoutAndDisabledRevisionsLoseAccess()
        {
            var repository = new InMemoryUserScriptRepository();
            var script = await repository.InstallAsync(MetadataAndMatchingTests.Script("// @name permissions\n// @match https://api.example/*\n// @grant GM.download\n// @run-at document-end"), "test", true, CancellationToken.None);
            var prompt = new ControlledPrompt();
            using (var policy = new InteractiveUserScriptPermissionPolicy(new SensitiveUserScriptPermissionsPolicy(), new InMemoryUserScriptPermissionDecisionStore(), prompt))
            using (var engine = new UserScriptEngine(repository))
            using (var store = new Mzying2001.MonkeySharp.Core.Storage.InMemoryUserScriptValueStore())
            using (var gateway = new UserScriptBridgeGateway(engine, store, policy, new[] { new ImmediateProvider() }, new BridgeOptions(requestTimeout: TimeSpan.FromMilliseconds(30))))
            {
                var frame = Frame();
                var invocation = Assert.Single((await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, frame), CancellationToken.None)).Invocations);
                Func<Task<string>> request = () => gateway.DispatchAsync(JsonSerializer.Serialize(new { type = "request", protocol = 2, requestId = Guid.NewGuid().ToString("D"),
                    documentId = frame.DocumentId, scriptKey = script.ScriptKey.ToString(), capability = invocation.Capability, method = "GM.download", @params = new { url = "https://files.example/file" } }), CancellationToken.None);
                var pending = request();
                var dialog = await prompt.Next();
                await Task.Delay(80);
                Assert.False(pending.IsCompleted);
                dialog.Completion.SetResult(new UserScriptPermissionChoice(PermissionDecision.Allow));
                using (var result = JsonDocument.Parse(await pending)) Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
                var changedWhileWaiting = request();
                var obsolete = await prompt.Next();
                await repository.SetEnabledAsync(script.ScriptKey, false, CancellationToken.None);
                obsolete.Completion.SetResult(new UserScriptPermissionChoice(PermissionDecision.Allow));
                using (var result = JsonDocument.Parse(await changedWhileWaiting)) Assert.Equal(BridgeErrorCodes.SessionExpired, result.RootElement.GetProperty("error").GetProperty("code").GetString());
                using (var result = JsonDocument.Parse(await request())) Assert.Equal(BridgeErrorCodes.SessionExpired, result.RootElement.GetProperty("error").GetProperty("code").GetString());
            }
        }

        private static async Task<ApiAuthorizationRequest> Request(string method, string target)
        {
            var script = await new InMemoryUserScriptRepository().InstallAsync(MetadataAndMatchingTests.Script("// @name prompt\n// @match https://api.example/*\n// @connect api.example\n// @grant GM.xmlHttpRequest"), "test", true, CancellationToken.None);
            return new ApiAuthorizationRequest(script, Frame(), method, target, "safe summary", new[] { method });
        }
        private static ApiAuthorizationRequest Copy(ApiAuthorizationRequest source, string method, string target) => new ApiAuthorizationRequest(source.Installation, source.Frame, method, target, "safe summary", new[] { method });
        private static DocumentFrame Frame() => new DocumentFrame("browser", "document", "main", new Uri("https://api.example/"), true, TimingGuarantee.BestEffortDocumentStart, BridgeIntegrityGuarantee.Verified);
        private sealed class ImmediateProvider : IUserScriptApiProvider
        {
            public System.Collections.Generic.IReadOnlyCollection<string> Methods => new[] { "GM.download" };
            public Task<ApiResult> InvokeAsync(ApiInvocationContext context, CancellationToken token) => Task.FromResult(ApiResult.Undefined);
        }
        private sealed class ControlledPrompt : IUserScriptPermissionPrompt
        {
            private readonly ConcurrentQueue<Dialog> _dialogs = new ConcurrentQueue<Dialog>();
            internal int Calls;
            public Task<UserScriptPermissionChoice> PromptAsync(ApiAuthorizationRequest request, UserScriptPermissionScope scope, CancellationToken token)
            {
                Interlocked.Increment(ref Calls);
                var dialog = new Dialog(token); _dialogs.Enqueue(dialog);
                token.Register(() => dialog.Completion.TrySetCanceled());
                return dialog.Completion.Task;
            }
            internal async Task<Dialog> Next()
            { for (var index = 0; index < 500; index++) { if (_dialogs.TryDequeue(out var value)) return value; await Task.Delay(5); } throw new TimeoutException("Permission dialog not created."); }
        }
        private sealed class Dialog
        {
            internal Dialog(CancellationToken token) { Token = token; }
            internal readonly CancellationToken Token;
            internal readonly TaskCompletionSource<UserScriptPermissionChoice> Completion = new TaskCompletionSource<UserScriptPermissionChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
