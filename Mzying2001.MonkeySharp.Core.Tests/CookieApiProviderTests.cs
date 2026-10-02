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

        [Fact]
        public async Task CookieDefaultsToFrameUrlAndUsesTampermonkeyFields()
        {
            var installation = await InstallAsync("// @grant GM.cookie");
            using (var service = new InMemoryCookieService())
            using (var provider = new CookieApiProvider(service))
            {
                var result = await provider.InvokeAsync(Context(installation, new
                {
                    operation = "set",
                    details = new
                    {
                        name = "sid", value = "abc", httpOnly = true,
                        expirationDate = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(),
                        firstPartyDomain = "example.com"
                    }
                }), CancellationToken.None);

                var cookie = Json(result.Json);
                Assert.Equal("example.com", cookie.GetProperty("domain").GetString());
                Assert.True(cookie.GetProperty("httpOnly").GetBoolean());
                Assert.Equal("example.com", cookie.GetProperty("firstPartyDomain").GetString());
                Assert.True(cookie.GetProperty("hostOnly").GetBoolean());
                Assert.False(cookie.GetProperty("session").GetBoolean());
                Assert.True(cookie.GetProperty("expirationDate").GetInt64() > 0);
            }
        }

        [Fact]
        public async Task CookieRejectsUrlsOutsideScriptMatchRules()
        {
            var installation = await InstallAsync("// @grant GM.cookie");
            using (var service = new InMemoryCookieService())
            using (var provider = new CookieApiProvider(service))
            {
                var exception = await Assert.ThrowsAsync<BridgeProtocolException>(() => provider.InvokeAsync(
                    Context(installation, new
                    {
                        operation = "list",
                        details = new { url = "https://outside.example/" }
                    }), CancellationToken.None));

                Assert.Equal(BridgeErrorCodes.PermissionDenied, exception.Code);
            }
        }

        [Fact]
        public async Task PendingListenerRegistrationIsUniqueAndReleasedWhenExecutionEnds()
        {
            var installation = await InstallAsync("// @grant GM.cookie");
            var service = new BlockingCookieService();
            using (var provider = new CookieApiProvider(service))
            {
                var add = new
                {
                    operation = "addListener",
                    listenerId = 3,
                    details = new { url = "https://example.com/" }
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

        private sealed class BlockingCookieService : ICookieService
        {
            public ManualResetEventSlim Entered { get; } = new ManualResetEventSlim();
            public ManualResetEventSlim Release { get; } = new ManualResetEventSlim();
            public TestCookieRegistration Registration { get; } = new TestCookieRegistration();

            public ICookieListenerRegistration AddListener(
                UserScriptCookieQuery query,
                EventHandler<UserScriptCookieChangedEventArgs> changed)
            {
                Entered.Set();
                Release.Wait();
                return Registration;
            }

            public Task<IReadOnlyList<UserScriptCookie>> ListAsync(
                UserScriptCookieQuery query,
                CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<UserScriptCookie> SetAsync(
                UserScriptCookieMutation mutation,
                CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<bool> DeleteAsync(
                UserScriptCookieMutation mutation,
                CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class TestCookieRegistration : ICookieListenerRegistration
        {
            public bool Disposed { get; private set; }

            public void Dispose()
            {
                Disposed = true;
            }
        }
    }
}
