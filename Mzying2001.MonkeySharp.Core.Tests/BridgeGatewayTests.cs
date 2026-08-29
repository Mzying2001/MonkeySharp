using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Permissions;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using Mzying2001.MonkeySharp.Core.Storage;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class BridgeGatewayTests
    {
        [Fact]
        public async Task HelloReturnsVersionLimitsAndGrantedApis()
        {
            using (var fixture = await BridgeFixture.CreateAsync("GM.getValue", "GM.setValue"))
            {
                var response = await fixture.HelloAsync();

                Assert.Equal("hello-result", response.GetProperty("type").GetString());
                Assert.True(response.GetProperty("ok").GetBoolean());
                Assert.Equal(1024 * 1024, response.GetProperty("limits").GetProperty("requestBytes").GetInt32());
                Assert.Contains("GM.getValue", response.GetProperty("apis").EnumerateArray().Select(item => item.GetString()));
            }
        }

        [Fact]
        public async Task ForgedIdentityAndStaleDocumentAreRejected()
        {
            using (var fixture = await BridgeFixture.CreateAsync("GM.getValue"))
            {
                var forgedKey = await fixture.RequestAsync(
                    "GM.getValue", new { key = "x" }, scriptKey: Guid.NewGuid().ToString("D"));
                var forgedCapability = await fixture.RequestAsync(
                    "GM.getValue", new { key = "x" }, capability: "forged");
                await fixture.Engine.InvalidateDocumentAsync(fixture.Frame.DocumentId, CancellationToken.None);
                var stale = await fixture.RequestAsync("GM.getValue", new { key = "x" });

                AssertError(forgedKey, BridgeErrorCodes.SessionExpired);
                AssertError(forgedCapability, BridgeErrorCodes.SessionExpired);
                AssertError(stale, BridgeErrorCodes.SessionExpired);
            }
        }

        [Fact]
        public async Task GrantAndHostSupportAreCheckedIndependently()
        {
            using (var fixture = await BridgeFixture.CreateAsync("GM.getValue", "GM.download"))
            {
                var denied = await fixture.RequestAsync("GM.setValue", new { key = "x", value = 1 });
                var unsupported = await fixture.RequestAsync("GM.download", new { url = "https://example.com/a" });

                AssertError(denied, BridgeErrorCodes.GrantDenied);
                AssertError(unsupported, BridgeErrorCodes.NotSupported);
            }
        }

        [Fact]
        public async Task BasicStorageApiPreservesJsonNullAndMissing()
        {
            using (var fixture = await BridgeFixture.CreateAsync(
                "GM.getValue", "GM.setValue", "GM.deleteValue", "GM.listValues"))
            {
                var missing = await fixture.RequestAsync("GM.getValue", new { key = "missing", defaultValue = "fallback" });
                await fixture.RequestAsync("GM.setValue", new { key = "null", value = (object)null });
                await fixture.RequestAsync("GM.setValue", new { key = "object", value = new { nested = new[] { 1, 2 } } });
                var storedNull = await fixture.RequestAsync("GM.getValue", new { key = "null", defaultValue = "fallback" });
                var storedObject = await fixture.RequestAsync("GM.getValue", new { key = "object" });
                var keys = await fixture.RequestAsync("GM.listValues", new { });
                var deleted = await fixture.RequestAsync("GM.deleteValue", new { key = "null" });
                var absentWithoutDefault = await fixture.RequestAsync("GM.getValue", new { key = "null" });

                Assert.Equal("fallback", missing.GetProperty("result").GetString());
                Assert.Equal(JsonValueKind.Null, storedNull.GetProperty("result").ValueKind);
                Assert.Equal(2, storedObject.GetProperty("result").GetProperty("nested")[1].GetInt32());
                Assert.Equal(new[] { "null", "object" }, keys.GetProperty("result").EnumerateArray().Select(item => item.GetString()));
                Assert.True(deleted.GetProperty("result").GetBoolean());
                Assert.Equal("undefined", absentWithoutDefault.GetProperty("result").GetProperty("$monkeySharpType").GetString());
            }
        }

        [Fact]
        public async Task RequestIdsCannotBeReplayed()
        {
            using (var fixture = await BridgeFixture.CreateAsync("GM.getValue"))
            {
                var requestId = Guid.NewGuid().ToString("D");
                var first = await fixture.RequestAsync("GM.getValue", new { key = "x" }, requestId: requestId);
                var replay = await fixture.RequestAsync("GM.getValue", new { key = "x" }, requestId: requestId);

                Assert.True(first.GetProperty("ok").GetBoolean());
                AssertError(replay, BridgeErrorCodes.MalformedMessage);
            }
        }

        [Fact]
        public async Task MalformedVersionAndInvalidParamsUseStableErrors()
        {
            using (var fixture = await BridgeFixture.CreateAsync("GM.getValue"))
            {
                var malformed = Parse(await fixture.Gateway.DispatchAsync("{", CancellationToken.None));
                var invalidParams = await fixture.RequestAsync("GM.getValue", new { key = 42 });
                var versionMessage = new Dictionary<string, object>
                {
                    ["type"] = "hello",
                    ["protocol"] = 99,
                    ["documentId"] = fixture.Frame.DocumentId,
                    ["scriptKey"] = fixture.Invocation.ScriptKey.ToString(),
                    ["capability"] = fixture.Invocation.Capability
                };
                var version = Parse(await fixture.Gateway.DispatchAsync(
                    JsonSerializer.Serialize(versionMessage), CancellationToken.None));

                AssertError(malformed, BridgeErrorCodes.MalformedMessage);
                AssertError(invalidParams, BridgeErrorCodes.InvalidParams);
                AssertError(version, BridgeErrorCodes.ProtocolVersion);
                Assert.Equal("hello-result", version.GetProperty("type").GetString());
                Assert.False(version.TryGetProperty("requestId", out _));
            }
        }

        [Fact]
        public async Task PermissionPolicyCanDenyADeclaredApi()
        {
            using (var fixture = await BridgeFixture.CreateAsync(
                new DenyPolicy(), null, null, "GM.getValue"))
            {
                var response = await fixture.RequestAsync("GM.getValue", new { key = "x" });

                AssertError(response, BridgeErrorCodes.PermissionDenied);
            }
        }

        [Fact]
        public async Task TimeoutAndCancelReturnStableErrors()
        {
            var provider = new BlockingProvider();
            using (var fixture = await BridgeFixture.CreateAsync(
                null,
                new[] { provider },
                new BridgeOptions(requestTimeout: TimeSpan.FromMilliseconds(30)),
                "GM.download"))
            {
                var timeout = await fixture.RequestAsync("GM.download", new { url = "https://example.com" });
                AssertError(timeout, BridgeErrorCodes.Timeout);
            }

            provider = new BlockingProvider();
            using (var fixture = await BridgeFixture.CreateAsync(
                null,
                new[] { provider },
                new BridgeOptions(requestTimeout: TimeSpan.FromSeconds(5)),
                "GM.download"))
            {
                var requestId = Guid.NewGuid().ToString("D");
                var request = fixture.RequestAsync("GM.download", new { url = "https://example.com" }, requestId: requestId);
                await provider.Entered.Task;
                var cancel = await fixture.CancelAsync(requestId);
                var canceled = await request;

                Assert.True(cancel.GetProperty("ok").GetBoolean());
                AssertError(canceled, BridgeErrorCodes.Canceled);
            }
        }

        [Fact]
        public async Task PendingAndPayloadLimitsRejectExcessWork()
        {
            var provider = new BlockingProvider();
            using (var fixture = await BridgeFixture.CreateAsync(
                null,
                new[] { provider },
                new BridgeOptions(requestTimeout: TimeSpan.FromSeconds(5), maxPendingRequestsPerDocument: 1),
                "GM.download"))
            {
                var firstId = Guid.NewGuid().ToString("D");
                var first = fixture.RequestAsync("GM.download", new { }, requestId: firstId);
                await provider.Entered.Task;
                var excess = await fixture.RequestAsync("GM.download", new { });
                await fixture.CancelAsync(firstId);
                await first;

                AssertError(excess, BridgeErrorCodes.PayloadTooLarge);
            }

            using (var fixture = await BridgeFixture.CreateAsync(
                null,
                null,
                new BridgeOptions(maxRequestBytes: 128),
                "GM.getValue"))
            {
                var oversized = Parse(await fixture.Gateway.DispatchAsync(
                    new string('x', 129), CancellationToken.None));
                AssertError(oversized, BridgeErrorCodes.PayloadTooLarge);
            }
        }

        [Fact]
        public async Task ValueListenersOnlyReceiveSameScriptActiveSessions()
        {
            using (var fixture = await BridgeFixture.CreateAsync(
                "GM.addValueChangeListener", "GM.removeValueChangeListener"))
            {
                var secondFrame = BridgeFixture.CreateFrame("doc-two");
                var secondPlan = await fixture.Engine.ProcessLifecycleAsync(
                    new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, secondFrame),
                    CancellationToken.None);
                var secondInvocation = Assert.Single(secondPlan.Invocations);
                var notifications = new List<BridgeNotificationEventArgs>();
                fixture.Gateway.Notification += (_, item) => notifications.Add(item);

                await fixture.RequestAsync("GM.addValueChangeListener", new { listenerId = 1, key = "theme" });
                await fixture.RequestAsync(
                    secondFrame,
                    secondInvocation,
                    "GM.addValueChangeListener",
                    new { listenerId = 1, key = "theme" });
                await fixture.Store.SetAsync(fixture.Invocation.ScriptKey.ToString(), "theme", "\"dark\"", CancellationToken.None);

                Assert.Equal(2, notifications.Count);
                Assert.All(notifications, item => Assert.Equal("value-change", item.EventName));
                Assert.Equal(2, notifications.Select(item => item.ExecutionId).Distinct().Count());

                await fixture.RequestAsync("GM.removeValueChangeListener", new { listenerId = 1 });
                await fixture.RequestAsync(
                    secondFrame,
                    secondInvocation,
                    "GM.removeValueChangeListener",
                    new { listenerId = 1 });
                notifications.Clear();
                await fixture.Store.SetAsync(fixture.Invocation.ScriptKey.ToString(), "theme", "\"light\"", CancellationToken.None);
                Assert.Empty(notifications);
            }
        }

        [Fact]
        public async Task ProviderNotificationsAreRoutedWithExecutionDeliveryToken()
        {
            var provider = new NotificationProvider();
            using (var fixture = await BridgeFixture.CreateAsync(
                null,
                new[] { provider },
                null,
                "GM.download"))
            {
                BridgeNotificationEventArgs delivered = null;
                fixture.Gateway.Notification += (_, item) => delivered = item;

                provider.Emit(fixture.Invocation.ScriptKey, fixture.Invocation.ExecutionId);

                Assert.NotNull(delivered);
                Assert.Equal(fixture.Invocation.ExecutionId, delivered.ExecutionId);
                Assert.Equal(fixture.Invocation.DeliveryToken, delivered.DeliveryToken);
                Assert.Equal("provider-event", delivered.EventName);

                delivered = null;
                await fixture.Engine.InvalidateDocumentAsync(fixture.Frame.DocumentId, CancellationToken.None);
                provider.Emit(fixture.Invocation.ScriptKey, fixture.Invocation.ExecutionId);

                Assert.Equal(fixture.Invocation.ExecutionId, provider.EndedExecutionId);
                Assert.Null(delivered);
            }
        }

        [Fact]
        public async Task OversizedResponseKeepsCorrelationId()
        {
            using (var fixture = await BridgeFixture.CreateAsync(
                null,
                new[] { new LargeProvider() },
                new BridgeOptions(maxResponseBytes: 300),
                "GM.download"))
            {
                var requestId = Guid.NewGuid().ToString("D");
                var response = await fixture.RequestAsync("GM.download", new { }, requestId: requestId);

                Assert.Equal(requestId, response.GetProperty("requestId").GetString());
                AssertError(response, BridgeErrorCodes.PayloadTooLarge);
            }
        }

        [Fact]
        public async Task InjectionPayloadIsJsonEncodedAndContainsCompatibilityMetadata()
        {
            using (var fixture = await BridgeFixture.CreateAsync("GM.getValue"))
            {
                var script = BridgeScriptBuilder.BuildInjection(fixture.Plan);
                Assert.DoesNotContain(fixture.Invocation.Source, script);
                Assert.DoesNotContain(fixture.Invocation.Capability, script);
                Assert.DoesNotContain("__MONKEYSHARP_PAYLOAD_BASE64__", script);

                const string prefix = "decodePayload(\"";
                var start = script.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
                var end = script.IndexOf("\")", start, StringComparison.Ordinal);
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(script.Substring(start, end - start)));
                using (var payload = JsonDocument.Parse(json))
                {
                    Assert.Equal(fixture.Invocation.Source,
                        payload.RootElement.GetProperty("invocations")[0].GetProperty("source").GetString());
                    Assert.Contains("GM.getValue", payload.RootElement.GetProperty("invocations")[0]
                        .GetProperty("grants").EnumerateArray().Select(item => item.GetString()));
                    Assert.Contains("GM.getValue", payload.RootElement.GetProperty("invocations")[0]
                        .GetProperty("declaredGrants").EnumerateArray().Select(item => item.GetString()));
                }
            }
        }

        private static void AssertError(JsonElement response, string code)
        {
            Assert.False(response.GetProperty("ok").GetBoolean());
            Assert.Equal(code, response.GetProperty("error").GetProperty("code").GetString());
        }

        private static JsonElement Parse(string json)
        {
            using (var document = JsonDocument.Parse(json))
                return document.RootElement.Clone();
        }

        private sealed class DenyPolicy : IUserScriptPermissionPolicy
        {
            public Task<PermissionDecision> AuthorizeAsync(ApiAuthorizationRequest request, CancellationToken cancellationToken)
            {
                return Task.FromResult(PermissionDecision.Deny);
            }
        }

        private sealed class BlockingProvider : IUserScriptApiProvider
        {
            public TaskCompletionSource<object> Entered { get; } =
                new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            public IReadOnlyCollection<string> Methods { get; } =
                new ReadOnlyCollection<string>(new[] { "GM.download" });

            public async Task<ApiResult> InvokeAsync(ApiInvocationContext context, CancellationToken cancellationToken)
            {
                Entered.TrySetResult(null);
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return ApiResult.Undefined;
            }
        }

        private sealed class LargeProvider : IUserScriptApiProvider
        {
            public IReadOnlyCollection<string> Methods { get; } =
                new ReadOnlyCollection<string>(new[] { "GM.download" });

            public Task<ApiResult> InvokeAsync(ApiInvocationContext context, CancellationToken cancellationToken)
            {
                return Task.FromResult(ApiResult.FromValue(new string('x', 2000)));
            }
        }

        private sealed class NotificationProvider :
            IUserScriptApiProvider,
            IUserScriptNotificationSource,
            IUserScriptExecutionObserver
        {
            public event EventHandler<ApiNotificationEventArgs> Notification;
            public IReadOnlyCollection<string> Methods { get; } =
                new ReadOnlyCollection<string>(new[] { "GM.download" });
            public string EndedExecutionId { get; private set; }

            public Task<ApiResult> InvokeAsync(ApiInvocationContext context, CancellationToken cancellationToken)
            {
                return Task.FromResult(ApiResult.Undefined);
            }

            public void Emit(ScriptKey scriptKey, string executionId)
            {
                Notification?.Invoke(this, new ApiNotificationEventArgs(
                    scriptKey,
                    executionId,
                    "provider-event",
                    "{}"));
            }

            public void OnExecutionEnded(string executionId)
            {
                EndedExecutionId = executionId;
            }
        }

        private sealed class BridgeFixture : IDisposable
        {
            private BridgeFixture(
                InMemoryUserScriptValueStore store,
                UserScriptEngine engine,
                UserScriptBridgeGateway gateway,
                DocumentFrame frame,
                InjectionPlan plan)
            {
                Store = store;
                Engine = engine;
                Gateway = gateway;
                Frame = frame;
                Plan = plan;
                Invocation = Assert.Single(plan.Invocations);
            }

            public InMemoryUserScriptValueStore Store { get; }
            public UserScriptEngine Engine { get; }
            public UserScriptBridgeGateway Gateway { get; }
            public DocumentFrame Frame { get; }
            public InjectionPlan Plan { get; }
            public ScriptInvocation Invocation { get; }

            public static Task<BridgeFixture> CreateAsync(params string[] grants)
            {
                return CreateAsync(null, null, null, grants);
            }

            public static async Task<BridgeFixture> CreateAsync(
                IUserScriptPermissionPolicy permissionPolicy,
                IEnumerable<IUserScriptApiProvider> providers,
                BridgeOptions options,
                params string[] grants)
            {
                var repository = new InMemoryUserScriptRepository();
                var grantLines = string.Join("\n", grants.Select(item => "// @grant " + item));
                var source = MetadataAndMatchingTests.Script(
                    "// @name bridge\n// @match https://example.com/*\n// @run-at document-end\n" + grantLines,
                    "window.bridgeTest = \"'</script>\";");
                await repository.InstallAsync(source, "test", true, CancellationToken.None);
                var engine = new UserScriptEngine(repository);
                var frame = CreateFrame("doc-one");
                var plan = await engine.ProcessLifecycleAsync(
                    new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, frame),
                    CancellationToken.None);
                var store = new InMemoryUserScriptValueStore();
                var gateway = new UserScriptBridgeGateway(engine, store, permissionPolicy, providers, options);
                return new BridgeFixture(store, engine, gateway, frame, plan);
            }

            public static DocumentFrame CreateFrame(string documentId)
            {
                return new DocumentFrame(
                    "browser",
                    documentId,
                    "main",
                    new Uri("https://example.com/page"),
                    true,
                    TimingGuarantee.BestEffortDocumentStart,
                    BridgeIntegrityGuarantee.Verified);
            }

            public Task<JsonElement> HelloAsync()
            {
                var envelope = Proof(Frame, Invocation);
                envelope["type"] = "hello";
                return DispatchAsync(envelope);
            }

            public Task<JsonElement> RequestAsync(
                string method,
                object parameters,
                string requestId = null,
                string scriptKey = null,
                string capability = null)
            {
                return RequestAsync(Frame, Invocation, method, parameters, requestId, scriptKey, capability);
            }

            public Task<JsonElement> RequestAsync(
                DocumentFrame frame,
                ScriptInvocation invocation,
                string method,
                object parameters,
                string requestId = null,
                string scriptKey = null,
                string capability = null)
            {
                var envelope = Proof(frame, invocation);
                envelope["type"] = "request";
                envelope["requestId"] = requestId ?? Guid.NewGuid().ToString("D");
                envelope["method"] = method;
                envelope["params"] = parameters;
                if (scriptKey != null) envelope["scriptKey"] = scriptKey;
                if (capability != null) envelope["capability"] = capability;
                return DispatchAsync(envelope);
            }

            public Task<JsonElement> CancelAsync(string requestId)
            {
                var envelope = Proof(Frame, Invocation);
                envelope["type"] = "cancel";
                envelope["requestId"] = requestId;
                return DispatchAsync(envelope);
            }

            public void Dispose()
            {
                Gateway.Dispose();
                Engine.Dispose();
                Store.Dispose();
            }

            private async Task<JsonElement> DispatchAsync(Dictionary<string, object> envelope)
            {
                var response = await Gateway.DispatchAsync(JsonSerializer.Serialize(envelope), CancellationToken.None);
                using (var document = JsonDocument.Parse(response))
                    return document.RootElement.Clone();
            }

            private static Dictionary<string, object> Proof(DocumentFrame frame, ScriptInvocation invocation)
            {
                return new Dictionary<string, object>
                {
                    ["protocol"] = 1,
                    ["documentId"] = frame.DocumentId,
                    ["scriptKey"] = invocation.ScriptKey.ToString(),
                    ["capability"] = invocation.Capability
                };
            }
        }
    }
}
