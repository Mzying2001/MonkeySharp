using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class WebRequestApiProviderTests
    {
        [Fact]
        public async Task TampermonkeyRulesCancelAndReturnThreeListenerArguments()
        {
            var installation = await InstallAsync();
            using (var service = new InMemoryWebRequestService())
            using (var provider = new WebRequestApiProvider(service))
            {
                ApiNotificationEventArgs observed = null;
                provider.Notification += (_, value) => observed = value;
                var registration = await provider.InvokeAsync(Context(installation, new
                {
                    operation = "register",
                    listenerId = 7,
                    rules = new[] { new { selector = "https://example.com/*", action = "cancel" } }
                }), CancellationToken.None);
                var decision = service.Evaluate(new WebRequestEvent(WebRequestPhase.OnBeforeRequest, 3,
                    "https://example.com/script.js"));

                Assert.Equal(WebRequestActionKind.Block, decision.Kind);
                Assert.NotNull(observed);
                Assert.Equal("webrequest-result", observed.EventName);
                using (var data = JsonDocument.Parse(observed.DataJson))
                {
                    Assert.Equal(7, data.RootElement.GetProperty("listenerId").GetInt32());
                    Assert.Equal("cancel", data.RootElement.GetProperty("message").GetString());
                    Assert.Equal((ulong)3, data.RootElement.GetProperty("info").GetProperty("requestId").GetUInt64());
                }
                Assert.Equal(36, Json(registration.Json).GetProperty("id").GetString().Length);
                var removed = await provider.InvokeAsync(Context(installation, new
                {
                    operation = "remove", id = Json(registration.Json).GetProperty("id").GetString()
                }), CancellationToken.None);
                Assert.True(Json(removed.Json).GetBoolean());
                Assert.Equal(WebRequestActionKind.Allow,
                    service.Evaluate(new WebRequestEvent(WebRequestPhase.OnBeforeRequest, 4, "https://example.com/next")).Kind);
            }
        }

        [Fact]
        public async Task MatchSelectorExcludesAndDynamicRedirectAreEvaluatedAtRequestTime()
        {
            var installation = await InstallAsync();
            using (var service = new InMemoryWebRequestService())
            using (var provider = new WebRequestApiProvider(service))
            {
                await provider.InvokeAsync(Context(installation, new
                {
                    operation = "register",
                    rules = new[] { new
                    {
                        selector = new { match = "https://example.com/*", exclude = "https://example.com/ignore" },
                        action = new { from = "/old", to = "/new" }
                    } }
                }), CancellationToken.None);
                var redirected = service.Evaluate(new WebRequestEvent(WebRequestPhase.OnBeforeRequest, 1,
                    "https://example.com/old"));
                var excluded = service.Evaluate(new WebRequestEvent(WebRequestPhase.OnBeforeRequest, 2,
                    "https://example.com/ignore"));
                Assert.Equal(WebRequestActionKind.Redirect, redirected.Kind);
                Assert.Equal("https://example.com/new", redirected.RedirectUrl);
                Assert.Equal(WebRequestActionKind.Allow, excluded.Kind);
            }
        }

        [Fact]
        public async Task RedirectTargetsMustBeAllowedByScriptUrlRules()
        {
            var installation = await InstallAsync();
            using (var service = new InMemoryWebRequestService())
            using (var provider = new WebRequestApiProvider(service))
            {
                var exception = await Assert.ThrowsAsync<BridgeProtocolException>(() => provider.InvokeAsync(Context(installation, new
                {
                    operation = "register",
                    rules = new[] { new { selector = "https://example.com/*", action = "https://outside.example/target" } }
                }), CancellationToken.None));
                Assert.Equal(BridgeErrorCodes.PermissionDenied, exception.Code);
            }
        }

        [Fact]
        public async Task LegacyOperationProtocolIsRejected()
        {
            var installation = await InstallAsync();
            using (var service = new InMemoryWebRequestService())
            using (var provider = new WebRequestApiProvider(service))
            {
                var exception = await Assert.ThrowsAsync<BridgeProtocolException>(() => provider.InvokeAsync(Context(installation, new
                {
                    operation = "addRule", rule = new { id = "legacy" }
                }), CancellationToken.None));
                Assert.Equal(BridgeErrorCodes.InvalidParams, exception.Code);
            }
        }

        private static async Task<UserScriptInstallation> InstallAsync()
        {
            var repository = new InMemoryUserScriptRepository();
            return await repository.InstallAsync(MetadataAndMatchingTests.Script(
                "// @name request\n// @match https://example.com/*\n// @run-at document-end\n// @grant GM.webRequest",
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

        private static JsonElement Json(string value)
        {
            using (var document = JsonDocument.Parse(value)) return document.RootElement.Clone();
        }
    }
}
