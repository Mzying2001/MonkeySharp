using Mzying2001.MonkeySharp.Core.Apis;
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
                        id = "low", phase = "OnBeforeSendHeaders", priority = 1,
                        filter = new { urlPatterns = new[] { "https://example.com/*" } },
                        action = new { kind = "ModifyRequestHeaders", headers = new { X_Test = "low", X_Other = "1" } }
                    }
                }), CancellationToken.None);
                await provider.InvokeAsync(Context(installation, new
                {
                    operation = "addRule", rule = new
                    {
                        id = "high", phase = "OnBeforeSendHeaders", priority = 10,
                        filter = new { urlPatterns = new[] { "https://example.com/*" } },
                        action = new { kind = "ModifyRequestHeaders", headers = new { X_Test = "high" } }
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

        private static async Task<UserScriptInstallation> InstallAsync(string grant)
        {
            var repository = new InMemoryUserScriptRepository();
            return await repository.InstallAsync(MetadataAndMatchingTests.Script(
                "// @name request\n// @match https://example.com/*\n// @run-at document-end\n" + grant,
                "window.request = true;"), "test", true, CancellationToken.None);
        }

        private static ApiInvocationContext Context(UserScriptInstallation installation, object value)
        {
            return new ApiInvocationContext(installation,
                new DocumentFrame("browser", "document", "frame", new Uri("https://example.com/page"), true,
                    TimingGuarantee.BestEffortDocumentStart, BridgeIntegrityGuarantee.Verified), "execution",
                Guid.NewGuid().ToString("D"), "GM.webRequest",
                JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(value)));
        }
    }
}
