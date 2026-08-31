using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class WebRequestApiProviderTests
    {
        [Fact]
        public async Task RulesAreMatchedByPriorityAndHeaderChangesAreMerged()
        {
            var installation = await InstallAsync("// @grant GM.webRequest");
            using (var service = new InMemoryWebRequestService())
            using (var provider = new WebRequestApiProvider(service))
            {
                await provider.InvokeAsync(Context(installation, new
                {
                    operation = "addRule", rule = new
                    {
                        id = "high", phase = "OnBeforeSendHeaders", priority = 10,
                        filter = new { urlPatterns = new[] { "https://example.com/*" } },
                        action = new { kind = "ModifyRequestHeaders", headers = new { X_Test = "high" } }
                    }
                }), CancellationToken.None);
                await provider.InvokeAsync(Context(installation, new
                {
                    operation = "addRule", rule = new
                    {
                        id = "low", phase = "OnBeforeSendHeaders", priority = 1,
                        filter = new { urlPatterns = new[] { "https://example.com/*" } },
                        action = new { kind = "ModifyRequestHeaders", headers = new { X_Test = "low", X_Other = "1" } }
                    }
                }), CancellationToken.None);
                var decision = service.Evaluate(new WebRequestEvent(WebRequestPhase.OnBeforeSendHeaders, 7,
                    "https://example.com/a", "GET", "Script", new Dictionary<string, string>()));
                Assert.Equal("high", decision.Headers["X_Test"]);
                Assert.Equal("1", decision.Headers["X_Other"]);
            }
        }

        [Fact]
        public async Task BlockAndRedirectTerminateEvaluationAndListenersAreReleased()
        {
            var installation = await InstallAsync("// @grant GM.webRequest");
            using (var service = new InMemoryWebRequestService())
            using (var provider = new WebRequestApiProvider(service))
            {
                ApiNotificationEventArgs notification = null;
                provider.Notification += (_, item) => notification = item;
                await provider.InvokeAsync(Context(installation, new
                {
                    operation = "addListener", listenerId = 2,
                    filter = new { urlPatterns = new[] { "https://example.com/*" } }
                }), CancellationToken.None);
                await provider.InvokeAsync(Context(installation, new
                {
                    operation = "addRule", rule = new
                    {
                        id = "block", phase = "OnBeforeRequest", priority = 0,
                        filter = new { urlPatterns = new[] { "https://example.com/*" } },
                        action = new { kind = "Block" }
                    }
                }), CancellationToken.None);
                var decision = service.Evaluate(new WebRequestEvent(WebRequestPhase.OnBeforeRequest, 4,
                    "https://example.com/a"));
                Assert.Equal(WebRequestActionKind.Block, decision.Kind);
                Assert.NotNull(notification);
                Assert.Equal("webrequest-event", notification.EventName);
                provider.OnExecutionEnded("execution");
                Assert.Empty(service.ListRules());
            }
        }

        [Fact]
        public async Task ListenerIdsAreUniqueAndRegisteredListenersCanBeRemoved()
        {
            var installation = await InstallAsync("// @grant GM.webRequest");
            using (var service = new InMemoryWebRequestService())
            using (var provider = new WebRequestApiProvider(service))
            {
                var notifications = 0;
                provider.Notification += (_, __) => notifications++;
                var add = new
                {
                    operation = "addListener",
                    listenerId = 7,
                    filter = new { urlPatterns = new[] { "https://example.com/*" } }
                };

                await provider.InvokeAsync(Context(installation, add), CancellationToken.None);
                await Assert.ThrowsAsync<BridgeProtocolException>(() =>
                    provider.InvokeAsync(Context(installation, add), CancellationToken.None));

                service.Evaluate(new WebRequestEvent(
                    WebRequestPhase.OnBeforeRequest, 1, "https://example.com/first"));
                Assert.Equal(1, notifications);

                var removed = await provider.InvokeAsync(Context(installation, new
                {
                    operation = "removeListener",
                    listenerId = 7
                }), CancellationToken.None);
                Assert.True(JsonSerializer.Deserialize<bool>(removed.Json));

                service.Evaluate(new WebRequestEvent(
                    WebRequestPhase.OnBeforeRequest, 2, "https://example.com/second"));
                Assert.Equal(1, notifications);
            }
        }

        [Fact]
        public async Task RulesAreListedAndRemovedOnlyByTheirOwningExecution()
        {
            var installation = await InstallAsync("// @grant GM.webRequest");
            using (var service = new InMemoryWebRequestService())
            using (var provider = new WebRequestApiProvider(service))
            {
                var rule = new
                {
                    operation = "addRule",
                    rule = new
                    {
                        id = "shared-name",
                        phase = "OnBeforeRequest",
                        filter = new { urlPatterns = new[] { "https://example.com/*" } },
                        action = new { kind = "Block" }
                    }
                };
                await provider.InvokeAsync(Context(installation, rule, "execution-a"), CancellationToken.None);
                await provider.InvokeAsync(Context(installation, rule, "execution-b"), CancellationToken.None);

                var firstRules = await provider.InvokeAsync(Context(installation, new
                {
                    operation = "listRules"
                }, "execution-a"), CancellationToken.None);
                Assert.Equal(new[] { "shared-name" },
                    JsonSerializer.Deserialize<string[]>(firstRules.Json));

                var removed = await provider.InvokeAsync(Context(installation, new
                {
                    operation = "removeRule",
                    id = "shared-name"
                }, "execution-a"), CancellationToken.None);
                Assert.True(JsonSerializer.Deserialize<bool>(removed.Json));
                Assert.Single(service.ListRules());

                var firstRulesAfterRemoval = await provider.InvokeAsync(Context(installation, new
                {
                    operation = "listRules"
                }, "execution-a"), CancellationToken.None);
                var secondRules = await provider.InvokeAsync(Context(installation, new
                {
                    operation = "listRules"
                }, "execution-b"), CancellationToken.None);
                Assert.Empty(JsonSerializer.Deserialize<string[]>(firstRulesAfterRemoval.Json));
                Assert.Equal(new[] { "shared-name" },
                    JsonSerializer.Deserialize<string[]>(secondRules.Json));
            }
        }

        [Fact]
        public async Task PendingListenerRegistrationIsUniqueAndReleasedWhenExecutionEnds()
        {
            var installation = await InstallAsync("// @grant GM.webRequest");
            var service = new BlockingWebRequestService();
            using (var provider = new WebRequestApiProvider(service))
            {
                var add = new
                {
                    operation = "addListener",
                    listenerId = 9,
                    filter = new { }
                };
                var pending = Task.Run(() => provider.InvokeAsync(
                    Context(installation, add), CancellationToken.None));
                Assert.True(service.Entered.Wait(TimeSpan.FromSeconds(5)));

                await Assert.ThrowsAsync<BridgeProtocolException>(() =>
                    provider.InvokeAsync(Context(installation, add), CancellationToken.None));
                provider.OnExecutionEnded("execution");
                service.Release.Set();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
                Assert.True(service.Registration.Disposed);
            }
        }

        [Theory]
        [InlineData("{\"operation\":\"addRule\"}", "rule must be an object.")]
        [InlineData("{\"operation\":\"addRule\",\"rule\":[]}", "rule must be an object.")]
        [InlineData("{\"operation\":\"addRule\",\"rule\":{\"id\":\"r\",\"phase\":\"OnBeforeRequest\",\"action\":{\"kind\":\"Block\"}}}", "rule.filter must be an object.")]
        [InlineData("{\"operation\":\"addRule\",\"rule\":{\"id\":\"r\",\"phase\":\"OnBeforeRequest\",\"filter\":{},\"action\":[]}}", "rule.action must be an object.")]
        [InlineData("{\"operation\":\"addRule\",\"rule\":{\"id\":\"r\",\"phase\":\"OnBeforeRequest\",\"priority\":\"high\",\"filter\":{},\"action\":{\"kind\":\"Block\"}}}", "rule.priority must be an integer.")]
        [InlineData("{\"operation\":\"addRule\",\"rule\":{\"id\":\"r\",\"phase\":\"OnBeforeRequest\",\"filter\":{\"methods\":[\"GET\",1]},\"action\":{\"kind\":\"Block\"}}}", "rule.filter.methods[1] must be a string.")]
        [InlineData("{\"operation\":\"addRule\",\"rule\":{\"id\":\"r\",\"phase\":\"OnBeforeSendHeaders\",\"filter\":{},\"action\":{\"kind\":\"ModifyRequestHeaders\",\"headers\":[]}}}", "rule.action.headers must be an object.")]
        [InlineData("{\"operation\":\"addRule\",\"rule\":{\"id\":\"r\",\"phase\":\"OnBeforeSendHeaders\",\"filter\":{},\"action\":{\"kind\":\"ModifyRequestHeaders\",\"headers\":{\"X-Test\":1}}}}", "rule.action.headers.X-Test must be a string.")]
        [InlineData("{\"operation\":\"addListener\",\"listenerId\":1}", "filter must be an object.")]
        [InlineData("{\"operation\":\"addListener\",\"listenerId\":1,\"filter\":{\"urlPatterns\":\"*\"}}", "filter.urlPatterns must be an array.")]
        public async Task InvalidNestedParametersReturnProtocolErrors(string json, string message)
        {
            var installation = await InstallAsync("// @grant GM.webRequest");
            using (var service = new InMemoryWebRequestService())
            using (var provider = new WebRequestApiProvider(service))
            {
                var exception = await Assert.ThrowsAsync<BridgeProtocolException>(() =>
                    provider.InvokeAsync(ContextJson(installation, json), CancellationToken.None));

                Assert.Equal(BridgeErrorCodes.InvalidParams, exception.Code);
                Assert.Equal(message, exception.Message);
            }
        }

        [Fact]
        public void RequestAndResponseHeaderRulesAreScopedToTheirPhases()
        {
            using (var service = new InMemoryWebRequestService())
            {
                service.AddRule(new WebRequestRule("request", new WebRequestFilter(),
                    WebRequestPhase.OnBeforeSendHeaders, 0,
                    new WebRequestAction(WebRequestActionKind.ModifyRequestHeaders,
                        headers: new Dictionary<string, string> { ["X-Request"] = "yes" })));
                service.AddRule(new WebRequestRule("response", new WebRequestFilter(),
                    WebRequestPhase.OnHeadersReceived, 0,
                    new WebRequestAction(WebRequestActionKind.ModifyResponseHeaders,
                        headers: new Dictionary<string, string> { ["X-Response"] = "yes" })));

                var request = service.Evaluate(new WebRequestEvent(WebRequestPhase.OnBeforeSendHeaders, 1,
                    "https://example.test/"));
                var response = service.Evaluate(new WebRequestEvent(WebRequestPhase.OnHeadersReceived, 1,
                    "https://example.test/"));
                Assert.True(request.Headers.ContainsKey("X-Request"));
                Assert.False(request.Headers.ContainsKey("X-Response"));
                Assert.True(response.Headers.ContainsKey("X-Response"));
                Assert.False(response.Headers.ContainsKey("X-Request"));
            }
        }

        private static async Task<UserScriptInstallation> InstallAsync(string grant)
        {
            var repository = new InMemoryUserScriptRepository();
            return await repository.InstallAsync(MetadataAndMatchingTests.Script(
                "// @name request\n// @match https://example.com/*\n// @run-at document-end\n" + grant,
                "window.request = true;"), "test", true, CancellationToken.None);
        }

        private static ApiInvocationContext Context(
            UserScriptInstallation installation,
            object value,
            string executionId = "execution")
        {
            return new ApiInvocationContext(installation,
                new DocumentFrame("browser", "document", "frame", new Uri("https://example.com/page"), true,
                    TimingGuarantee.BestEffortDocumentStart, BridgeIntegrityGuarantee.Verified), executionId,
                Guid.NewGuid().ToString("D"), "GM.webRequest",
                JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(value)));
        }

        private static ApiInvocationContext ContextJson(
            UserScriptInstallation installation,
            string json)
        {
            return new ApiInvocationContext(installation,
                new DocumentFrame("browser", "document", "frame", new Uri("https://example.com/page"), true,
                    TimingGuarantee.BestEffortDocumentStart, BridgeIntegrityGuarantee.Verified), "execution",
                Guid.NewGuid().ToString("D"), "GM.webRequest",
                JsonSerializer.Deserialize<JsonElement>(json));
        }

        private sealed class BlockingWebRequestService : IWebRequestService
        {
            public ManualResetEventSlim Entered { get; } = new ManualResetEventSlim();
            public ManualResetEventSlim Release { get; } = new ManualResetEventSlim();
            public TestRegistration Registration { get; } = new TestRegistration();

            public IWebRequestRegistration AddListener(
                WebRequestFilter filter,
                EventHandler<WebRequestEvent> listener)
            {
                Entered.Set();
                Release.Wait();
                return Registration;
            }

            public IWebRequestRegistration AddRule(WebRequestRule rule)
            {
                throw new NotSupportedException();
            }

            public bool RemoveRule(string id)
            {
                return false;
            }

            public IReadOnlyList<WebRequestRule> ListRules()
            {
                return new WebRequestRule[0];
            }

            public WebRequestDecision Evaluate(WebRequestEvent request)
            {
                return new WebRequestDecision();
            }
        }

        private sealed class TestRegistration : IWebRequestRegistration
        {
            public string Id { get; } = Guid.NewGuid().ToString("D");
            public bool Disposed { get; private set; }

            public void Dispose()
            {
                Disposed = true;
            }
        }
    }
}
