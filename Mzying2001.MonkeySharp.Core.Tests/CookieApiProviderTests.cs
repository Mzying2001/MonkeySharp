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
    public sealed class CookieApiProviderTests
    {
        [Fact]
        public async Task CookieCrudAndListenerExposeValidatedData()
        {
            var installation = await InstallAsync("// @grant GM.cookie");
            using (var service = new InMemoryCookieService())
            using (var provider = new CookieApiProvider(service))
            {
                ApiNotificationEventArgs notification = null;
                provider.Notification += (_, item) => notification = item;
                var set = await provider.InvokeAsync(Context(installation, new
                {
                    operation = "set",
                    details = new { url = "https://example.com/path", name = "sid", value = "abc", path = "/", secure = true }
                }), CancellationToken.None);
                Assert.Equal("sid", Json(set.Json).GetProperty("name").GetString());

                var listener = await provider.InvokeAsync(Context(installation, new
                {
                    operation = "addListener", listenerId = 4,
                    details = new { url = "https://example.com/path", name = "sid" }
                }), CancellationToken.None);
                Assert.Equal(4, Json(listener.Json).GetInt32());
                await provider.InvokeAsync(Context(installation, new
                {
                    operation = "set",
                    details = new { url = "https://example.com/path", name = "sid", value = "next" }
                }), CancellationToken.None);
                Assert.NotNull(notification);
                Assert.Equal("cookie-change", notification.EventName);
                Assert.Equal(4, Json(notification.DataJson).GetProperty("listenerId").GetInt32());

                var list = await provider.InvokeAsync(Context(installation, new
                {
                    operation = "list", details = new { url = "https://example.com/path" }
                }), CancellationToken.None);
                Assert.Equal("next", Json(list.Json)[0].GetProperty("value").GetString());
                var deleted = await provider.InvokeAsync(Context(installation, new
                {
                    operation = "delete", details = new { url = "https://example.com/path", name = "sid" }
                }), CancellationToken.None);
                Assert.True(Json(deleted.Json).GetBoolean());
            }
        }

        [Fact]
        public async Task CookieValidationRejectsNonHttpUrlsAndCleansListeners()
        {
            var installation = await InstallAsync("// @grant GM.cookie");
            using (var service = new InMemoryCookieService())
            using (var provider = new CookieApiProvider(service))
            {
                await Assert.ThrowsAsync<BridgeProtocolException>(() => provider.InvokeAsync(
                    Context(installation, new { operation = "list", details = new { url = "file:///tmp" } }),
                    CancellationToken.None));
                await provider.InvokeAsync(Context(installation, new
                {
                    operation = "addListener", listenerId = 1,
                    details = new { url = "https://example.com/" }
                }), CancellationToken.None);
                provider.OnExecutionEnded("execution");
                Assert.True(Json((await provider.InvokeAsync(Context(installation, new
                {
                    operation = "removeListener", listenerId = 1
                }), CancellationToken.None)).Json).GetBoolean() == false);
            }
        }

        private static async Task<UserScriptInstallation> InstallAsync(string grants)
        {
            var repository = new InMemoryUserScriptRepository();
            return await repository.InstallAsync(MetadataAndMatchingTests.Script(
                "// @name cookie\n// @match https://example.com/*\n// @run-at document-end\n" + grants,
                "window.cookie = true;"), "test", true, CancellationToken.None);
        }

        private static ApiInvocationContext Context(UserScriptInstallation installation, object parameters)
        {
            return new ApiInvocationContext(installation,
                new DocumentFrame("browser", "document", "frame", new Uri("https://example.com/path"), true,
                    TimingGuarantee.BestEffortDocumentStart, BridgeIntegrityGuarantee.Verified),
                "execution", Guid.NewGuid().ToString("D"), "GM.cookie",
                JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(parameters)));
        }

        private static JsonElement Json(string value)
        {
            using (var document = JsonDocument.Parse(value)) return document.RootElement.Clone();
        }
    }
}
