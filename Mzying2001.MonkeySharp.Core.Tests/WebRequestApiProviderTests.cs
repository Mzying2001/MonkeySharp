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
        [Theory]
        [InlineData("https://example.com/script.js")]
        [InlineData("https://api.example.com/script.js")]
        [InlineData("https://a.b.example.com/script.js")]
        public async Task TampermonkeyRulesCancelAndReturnThreeListenerArguments(string url)
        {
            var installation = await InstallAsync();
            using (var service = new InMemoryWebRequestService())
            using (var provider = new WebRequestApiProvider(service))
            {
                var notified = new TaskCompletionSource<ApiNotificationEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
                provider.Notification += (_, value) => notified.TrySetResult(value);
                var registration = await provider.InvokeAsync(Context(installation, new
                {
                    operation = "register",
                    listenerId = 7,
                    rules = new[] { new { selector = new { match = "https://*.example.com/*" }, action = "cancel" } }
                }), CancellationToken.None);
                var decision = service.Evaluate(new WebRequestEvent(WebRequestPhase.OnBeforeRequest, 3,
                    url, resourceType: "script", frame: Frame()));

                Assert.Equal(WebRequestActionKind.Block, decision.Kind);
                var observed = await notified.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal("webrequest-result", observed.EventName);
                using (var data = JsonDocument.Parse(observed.DataJson))
                {
                    Assert.Equal(7, data.RootElement.GetProperty("listenerId").GetInt32());
                    Assert.Equal("cancel", data.RootElement.GetProperty("info").GetString());
                    Assert.Equal("ok", data.RootElement.GetProperty("message").GetString());
                    Assert.Equal(url, data.RootElement.GetProperty("details").GetProperty("url").GetString());
                }
                Assert.Equal(ApiResult.Undefined.Json, registration.Json);
                await provider.InvokeAsync(Context(installation, new { operation = "register", rules = new object[0] }), CancellationToken.None);
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
                        selector = new { match = "https://*.example.com/*", exclude = "https://example.com/ignore" },
                        action = new { redirect = new { from = "(https://[^/]+)/old", to = "$1/new" } }
                    } }
                }), CancellationToken.None);
                var redirected = service.Evaluate(new WebRequestEvent(WebRequestPhase.OnBeforeRequest, 1,
                    "https://example.com/old", resourceType: "xhr", frame: Frame()));
                var excluded = service.Evaluate(new WebRequestEvent(WebRequestPhase.OnBeforeRequest, 2,
                    "https://example.com/ignore", resourceType: "xhr", frame: Frame()));
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
                    rules = new[] { new { selector = "https://example.com/*", action = new { redirect = "https://outside.example/target" } } }
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

        [Theory]
        [InlineData("image", "browser", "document", "frame")]
        [InlineData("script", "other", "document", "frame")]
        [InlineData("script", "browser", "old", "frame")]
        [InlineData("script", "browser", "document", "other")]
        public async Task RulesCannotEscapeResourceOrDocumentScope(string type, string browser, string document, string frame)
        {
            var installation = await InstallAsync();
            using (var service = new InMemoryWebRequestService())
            using (var provider = new WebRequestApiProvider(service))
            {
                await provider.InvokeAsync(Context(installation, new { operation = "register", rules = new[] { new { selector = "*", action = new { cancel = true } } } }), CancellationToken.None);
                var owner = new DocumentFrame(browser, document, frame, new Uri("https://example.com/page"), true, TimingGuarantee.BestEffortDocumentStart);
                Assert.Equal(WebRequestActionKind.Allow, service.Evaluate(new WebRequestEvent(WebRequestPhase.OnBeforeRequest, 1, "https://example.com/a", resourceType: type, frame: owner)).Kind);
                Assert.Equal(WebRequestActionKind.Allow, service.Evaluate(new WebRequestEvent(WebRequestPhase.OnBeforeRequest, 2, "https://example.com/a", resourceType: "script")).Kind);
            }
        }

        [Fact]
        public async Task StaticRulesAreReadyWithoutExecutingScriptAndTrackRepositoryChanges()
        {
            var repository = new InMemoryUserScriptRepository();
            var script = await repository.InstallAsync(MetadataAndMatchingTests.Script("// @name static request\n// @match https://example.com/*\n// @webRequest [{\"selector\":\"*\",\"action\":{\"cancel\":true}}]"), "test", true, CancellationToken.None);
            using (var service = new InMemoryWebRequestService())
            using (var rules = new StaticWebRequestRules(repository, service, new Permissions.AllowDeclaredPermissionsPolicy(), "browser", () => false))
            {
                await rules.InitializeAsync(CancellationToken.None);
                var request = new WebRequestEvent(WebRequestPhase.OnBeforeRequest, 1, "https://example.com/a.js", resourceType: "script", frame: Frame());
                Assert.Equal(WebRequestActionKind.Block, (await service.EvaluateAsync(request, CancellationToken.None)).Kind);
                await repository.SetEnabledAsync(script.ScriptKey, false, CancellationToken.None);
                Assert.Equal(WebRequestActionKind.Allow, (await service.EvaluateAsync(request, CancellationToken.None)).Kind);
                await repository.SetEnabledAsync(script.ScriptKey, true, CancellationToken.None);
                Assert.Equal(WebRequestActionKind.Block, (await service.EvaluateAsync(request, CancellationToken.None)).Kind);
                await repository.RemoveAsync(script.ScriptKey, CancellationToken.None);
                Assert.Equal(WebRequestActionKind.Allow, (await service.EvaluateAsync(request, CancellationToken.None)).Kind);
            }
        }

        private static DocumentFrame Frame() => new DocumentFrame("browser", "document", "frame", new Uri("https://example.com/page"), true,
            TimingGuarantee.BestEffortDocumentStart, BridgeIntegrityGuarantee.Verified);

        [Fact]
        public async Task RevokedRuleCannotApplyAfterPendingAuthorizationCompletes()
        {
            var installation = await InstallAsync();
            using (var service = new InMemoryWebRequestService())
            {
                var permission = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var rules = WebRequestApiProvider.CompileRules(Json(JsonSerializer.Serialize(new[] { new { selector = "*", action = "cancel" } })), installation);
                var registration = service.SetRules("owner", rules, _ => true, authorize: (_, __) => permission.Task);
                var pending = service.EvaluateAsync(new WebRequestEvent(WebRequestPhase.OnBeforeRequest, 1,
                    "https://example.com/a", resourceType: "script", frame: Frame()), CancellationToken.None);
                Assert.False(pending.IsCompleted);
                registration.Dispose();
                permission.SetResult(true);
                Assert.Equal(WebRequestActionKind.Allow, (await pending).Kind);
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
