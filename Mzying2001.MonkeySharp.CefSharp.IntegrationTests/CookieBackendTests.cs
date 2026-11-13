using CefSharp;
using Moq;
using Mzying2001.MonkeySharp.Core.Apis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.CefSharp.IntegrationTests
{
    public sealed class CookieBackendTests
    {
        private const string Root = "{\"name\":\"sid\",\"value\":\"actual\",\"domain\":\"example.com\",\"path\":\"/\",\"secure\":false,\"httpOnly\":true,\"session\":true,\"expires\":-1,\"sameSite\":\"Lax\"}";

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RejectedOrSilentlyDiscardedCookieNeverReportsSuccess(bool protocolSuccess)
        {
            using (var harness = new Harness((method, _) => method == "Network.setCookie" ? "{\"success\":" + protocolSuccess.ToString().ToLowerInvariant() + "}" : "{\"cookies\":[]}"))
            using (var service = harness.Service())
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetAsync(new UserScriptCookieMutation(new Uri("https://example.com/"), "sid", "actual"), CancellationToken.None));
        }

        [Fact]
        public async Task ActualStateMapsSameSiteAndServiceDisposalKeepsSharedSessionAlive()
        {
            using (var harness = new Harness((method, _) => method == "Network.setCookie" ? "{\"success\":true}" : "{\"cookies\":[" + Root + "]}"))
            {
                using (var service = harness.Service())
                {
                    var cookie = await service.SetAsync(new UserScriptCookieMutation(new Uri("https://example.com/"), "sid", "actual", httpOnly: true, sameSite: "lax"), CancellationToken.None);
                    Assert.Equal("lax", cookie.SameSite);
                    Assert.True(cookie.HostOnly);
                    Assert.True(cookie.Session);
                    Assert.Null(cookie.ExpirationDate);
                    Assert.Equal("Lax", harness.Commands[0].Item2.GetProperty("sameSite").GetString());
                    Assert.False(harness.Commands[0].Item2.TryGetProperty("domain", out _));
                }
                Assert.Equal(1, (await harness.Session.ExecuteAsync("Network.getAllCookies")).GetProperty("cookies").GetArrayLength());
            }
        }

        [Fact]
        public async Task DeleteUsesOneExactDomainPathAndVerifiesAbsence()
        {
            var deleted = false;
            var deep = Root.Replace("\"/\"", "\"/deep\"");
            using (var harness = new Harness((method, parameters) =>
            {
                if (method == "Network.setCookie")
                {
                    Assert.False(parameters.TryGetProperty("domain", out _));
                    Assert.Equal("/deep", parameters.GetProperty("path").GetString());
                    Assert.True(parameters.GetProperty("expires").GetDouble() < DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    Assert.Equal("https://example.com/deep/page", parameters.GetProperty("url").GetString());
                    deleted = true; return "{\"success\":true}";
                }
                return "{\"cookies\":[" + Root + (deleted ? "" : "," + deep) + "]}";
            }))
            using (var service = harness.Service())
                Assert.True(await service.DeleteAsync(new UserScriptCookieMutation(new Uri("https://example.com/deep/page"), "sid"), CancellationToken.None));
        }

        [Fact]
        public async Task FailedDeletionReadbackIsAnError()
        {
            using (var harness = new Harness((method, _) => method == "Network.setCookie" ? "{\"success\":true}" : "{\"cookies\":[" + Root + "]}"))
            using (var service = harness.Service())
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(new UserScriptCookieMutation(new Uri("https://example.com/"), "sid"), CancellationToken.None));
        }

        [Theory]
        [InlineData(114, false, false)]
        [InlineData(121, false, false)]
        [InlineData(126, false, false)]
        [InlineData(127, true, false)]
        [InlineData(128, true, true)]
        [InlineData(151, true, true)]
        [InlineData(156, true, true)]
        [InlineData(200, true, true)]
        public async Task PartitionKeysUseTheNativeProtocolShapeAndActualReadback(int major, bool objectKey, bool ancestorDimension)
        {
            var key = objectKey ? "{\"topLevelSite\":\"https://top-a.test\",\"hasCrossSiteAncestor\":true}" : "\"https://top-a.test\"";
            var cookie = Root.Replace("\"secure\":false", "\"secure\":true").TrimEnd('}') + ",\"partitionKey\":" + key + "}";
            using (var harness = new Harness((method, parameters) =>
            {
                if (method == "Browser.getVersion") return "{\"product\":\"Chrome/" + major + ".0.0.0\"}";
                if (method == "Network.setCookie")
                {
                    var actual = parameters.GetProperty("partitionKey");
                    Assert.Equal(objectKey ? JsonValueKind.Object : JsonValueKind.String, actual.ValueKind);
                    if (objectKey) Assert.True(actual.GetProperty("hasCrossSiteAncestor").GetBoolean());
                    return "{\"success\":true}";
                }
                return "{\"cookies\":[" + cookie + "]}";
            }))
            using (var service = harness.Service())
            {
                var actual = await service.SetAsync(new UserScriptCookieMutation(new Uri("https://example.com/"), "sid", "actual", secure:true,
                    httpOnly:true, sameSite:"lax", partitionKey:new UserScriptCookiePartitionKey("https://sub.top-a.test:9443/path")), CancellationToken.None);
                Assert.Equal("https://top-a.test", actual.PartitionKey.TopLevelSite);
                Assert.Equal(ancestorDimension ? (bool?)true : null, actual.PartitionKey.HasCrossSiteAncestor);
                Assert.Equal(ancestorDimension ? "available:serializable-keys" : "available:without-ancestor-dimension", new CookieProtocolProfile(major).CapabilityStatus);
            }
        }

        [Theory]
        [InlineData(114, false)]
        [InlineData(127, true)]
        [InlineData(128, true)]
        [InlineData(156, true)]
        [InlineData(200, true)]
        public async Task PartitionListingAndDeletionPreserveOrdinaryAndOtherPartitionCookies(int major, bool objectKey)
        {
            var a = objectKey ? "{\"topLevelSite\":\"https://a.test\",\"hasCrossSiteAncestor\":true}" : "\"https://a.test\"";
            var b = objectKey ? "{\"topLevelSite\":\"https://b.test\",\"hasCrossSiteAncestor\":true}" : "\"https://b.test\"";
            var plain = Root.Replace("\"secure\":false", "\"secure\":true");
            var removed = false;
            using (var harness = new Harness((method, parameters) =>
            {
                if (method == "Browser.getVersion") return "{\"product\":\"Chrome/" + major + ".0.0.0\"}";
                if (method == "Network.setCookie")
                {
                    Assert.Equal(a, parameters.GetProperty("partitionKey").GetRawText());
                    Assert.Equal(1, parameters.GetProperty("expires").GetDouble());
                    removed = true;
                    return "{\"success\":true}";
                }
                return "{\"cookies\":[" + plain + "," + plain.TrimEnd('}') + ",\"partitionKey\":" + b + "}" +
                    (removed ? "" : "," + plain.TrimEnd('}') + ",\"partitionKey\":" + a + "}") + "]}";
            }))
            using (var service = harness.Service())
            {
                var url = new Uri("https://example.com/");
                var key = new UserScriptCookiePartitionKey("https://sub.a.test:9443/path");
                Assert.Null(Assert.Single(await service.ListAsync(new UserScriptCookieQuery(url), CancellationToken.None)).PartitionKey);
                Assert.Single(await service.ListAsync(new UserScriptCookieQuery(url, partitionKey:key), CancellationToken.None));
                Assert.Equal(3, (await service.ListAsync(new UserScriptCookieQuery(url, partitionKey:new UserScriptCookiePartitionKey()), CancellationToken.None)).Count);
                Assert.True(await service.DeleteAsync(new UserScriptCookieMutation(url, "sid", path:"/", partitionKey:key), CancellationToken.None));
                var remaining = await service.ListAsync(new UserScriptCookieQuery(url, partitionKey:new UserScriptCookiePartitionKey()), CancellationToken.None);
                Assert.Equal(2, remaining.Count);
                Assert.Contains(remaining, cookie => cookie.PartitionKey == null);
                Assert.Contains(remaining, cookie => cookie.PartitionKey?.TopLevelSite == "https://b.test");
                Assert.Empty(await service.ListAsync(new UserScriptCookieQuery(url, partitionKey:key), CancellationToken.None));
                Assert.Single(harness.Commands.FindAll(command => command.Item1 == "Browser.getVersion"));
            }
        }

        [Theory]
        [InlineData(156, false)]
        [InlineData(156, true)]
        [InlineData(200, false)]
        public async Task PartitionSetNeverFallsBackWhenNativeStateIsWrong(int major, bool otherPartition)
        {
            var cookie = Root.Replace("\"secure\":false", "\"secure\":true");
            if (otherPartition) cookie = cookie.TrimEnd('}') + ",\"partitionKey\":{\"topLevelSite\":\"https://other.test\",\"hasCrossSiteAncestor\":true}}";
            using (var harness = new Harness((method, _) => method == "Browser.getVersion" ? "{\"product\":\"Chrome/" + major + ".0.0.0\"}" :
                method == "Network.setCookie" ? "{\"success\":true}" : "{\"cookies\":[" + cookie + "]}"))
            using (var service = harness.Service())
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetAsync(new UserScriptCookieMutation(new Uri("https://example.com/"), "sid", "actual",
                    secure:true, partitionKey:new UserScriptCookiePartitionKey("https://a.test")), CancellationToken.None));
                Assert.Single(harness.Commands.FindAll(command => command.Item1 == "Network.setCookie"));
            }
        }

        [Theory]
        [InlineData(84)]
        [InlineData(113)]
        public async Task UnsupportedEnginesRejectPartitionParametersBeforeCookieCommands(int major)
        {
            using (var harness = new Harness((method, _) => method == "Browser.getVersion" ? "{\"product\":\"Chrome/" + major + ".0.0.0\"}" : throw new InvalidOperationException("Cookie effect is forbidden.")))
            using (var service = harness.Service())
            {
                var url = new Uri("https://example.com/");
                var key = new UserScriptCookiePartitionKey("https://top.test");
                var mutation = new UserScriptCookieMutation(url, "sid", "value", secure:true, partitionKey:key);
                await Assert.ThrowsAsync<Mzying2001.MonkeySharp.Core.Domain.UnsupportedApiException>(() => service.ListAsync(
                    new UserScriptCookieQuery(url, partitionKey:key), CancellationToken.None));
                await Assert.ThrowsAsync<Mzying2001.MonkeySharp.Core.Domain.UnsupportedApiException>(() => service.SetAsync(
                    mutation, CancellationToken.None));
                await Assert.ThrowsAsync<Mzying2001.MonkeySharp.Core.Domain.UnsupportedApiException>(() => service.DeleteAsync(
                    mutation, CancellationToken.None));
                Assert.Single(harness.Commands);
                Assert.Equal("Browser.getVersion", harness.Commands[0].Item1);
            }
        }

        [Theory]
        [InlineData(128)]
        [InlineData(151)]
        [InlineData(156)]
        [InlineData(200)]
        public async Task AncestorDefaultsDistinguishSetFromDelete(int major)
        {
            using (var harness = new Harness((method, _) => "{\"product\":\"Chrome/" + major + ".0.0.0\"}"))
            using (var service = harness.Service())
            {
                var key = new UserScriptCookiePartitionKey("https://example.com");
                var url = new Uri("https://sub.example.com/path");
                Assert.False((await service.NormalizePartitionKeyAsync(key, url, CookiePartitionOperation.Set, CancellationToken.None)).HasCrossSiteAncestor);
                Assert.True((await service.NormalizePartitionKeyAsync(key, url, CookiePartitionOperation.Delete, CancellationToken.None)).HasCrossSiteAncestor);
                Assert.Null((await service.NormalizePartitionKeyAsync(key, url, CookiePartitionOperation.List, CancellationToken.None)).HasCrossSiteAncestor);
                await Assert.ThrowsAsync<ArgumentException>(() => service.NormalizePartitionKeyAsync(
                    new UserScriptCookiePartitionKey("https://other.test", false), url, CookiePartitionOperation.Set, CancellationToken.None));
            }
        }

        [Theory]
        [InlineData(114)]
        [InlineData(126)]
        [InlineData(127)]
        public async Task EnginesWithoutAncestorDimensionRejectItBeforeCookieCommands(int major)
        {
            using (var harness = new Harness((method, _) => method == "Browser.getVersion" ? "{\"product\":\"Chrome/" + major + ".0.0.0\"}" : throw new InvalidOperationException("Cookie effect is forbidden.")))
            using (var service = harness.Service())
            {
                var url = new Uri("https://example.com/");
                foreach (var ancestor in new[] { false, true })
                {
                    var key = new UserScriptCookiePartitionKey("https://top.test", ancestor);
                    var mutation = new UserScriptCookieMutation(url, "sid", "value", secure:true, partitionKey:key);
                    await Assert.ThrowsAsync<Mzying2001.MonkeySharp.Core.Domain.UnsupportedApiException>(() => service.ListAsync(new UserScriptCookieQuery(url, partitionKey:key), CancellationToken.None));
                    await Assert.ThrowsAsync<Mzying2001.MonkeySharp.Core.Domain.UnsupportedApiException>(() => service.SetAsync(mutation, CancellationToken.None));
                    await Assert.ThrowsAsync<Mzying2001.MonkeySharp.Core.Domain.UnsupportedApiException>(() => service.DeleteAsync(mutation, CancellationToken.None));
                }
                Assert.Single(harness.Commands);
            }
        }

        [Theory]
        [InlineData(114)]
        [InlineData(121)]
        [InlineData(126)]
        [InlineData(127)]
        [InlineData(128)]
        [InlineData(151)]
        [InlineData(156)]
        [InlineData(200)]
        public void SchemefulSitesAcceptVersionRanges(int major)
        {
            Assert.Equal("https://example.co.uk", SchemefulSite.FromUrl(new Uri("https://a.b.example.co.uk:9443/path"), major));
            Assert.Equal("https://foo.github.io", SchemefulSite.FromUrl(new Uri("https://a.foo.github.io"), major));
        }

        [Theory]
        [InlineData(114, "https://book.online", "https://aem.page")]
        [InlineData(150, "https://book.online", "https://aem.page")]
        [InlineData(151, "https://book.online", "https://tenant.aem.page")]
        [InlineData(155, "https://book.online", "https://tenant.aem.page")]
        [InlineData(156, "https://tenant.book.online", "https://tenant.aem.page")]
        [InlineData(200, "https://tenant.book.online", "https://tenant.aem.page")]
        public void SchemefulSitesUseTheLatestSnapshotNotNewerThanTheEngine(int major, string bookSite, string aemSite)
        {
            Assert.Equal(bookSite, SchemefulSite.FromUrl(new Uri("https://sub.tenant.book.online"), major));
            Assert.Equal(aemSite, SchemefulSite.FromUrl(new Uri("https://sub.tenant.aem.page"), major));
        }

        [Theory]
        [InlineData("https://a.b.example.co.uk:9443/path", "https://example.co.uk")]
        [InlineData("https://co.uk", "https://co.uk")]
        [InlineData("https://a.b.ck", "https://a.b.ck")]
        [InlineData("https://a.www.ck", "https://www.ck")]
        [InlineData("http://a.foo.github.io", "http://foo.github.io")]
        [InlineData("https://127.0.0.1:9443", "https://127.0.0.1")]
        [InlineData("https://[::1]:9443", "https://[::1]")]
        [InlineData("https://a.b.example.com.", "https://example.com.")]
        public void SchemefulSitesFollowPinnedSuffixWildcardsExceptionsAndPrivateRegistries(string input, string expected)
        {
            Assert.Equal(expected, SchemefulSite.FromUrl(new Uri(input), 121));
            Assert.Equal(expected, SchemefulSite.FromUrl(new Uri(input), 151));
        }

        private sealed class Harness : IDisposable
        {
            public readonly CefSharpCdpSession Session;
            public readonly List<Tuple<string, JsonElement>> Commands = new List<Tuple<string, JsonElement>>();
            public Harness(Func<string, JsonElement, string> respond)
            {
                var host = new Mock<IBrowserHost>();
                Session = new CefSharpCdpSession(host.Object, action => { action(); return Task.CompletedTask; });
                var id = 0;
                host.Setup(value => value.ExecuteDevToolsMethod(0, It.IsAny<string>(), It.IsAny<string>())).Returns((int _, string method, string json) =>
                {
                    var parameters = JsonSerializer.Deserialize<JsonElement>(json ?? "{}");
                    Commands.Add(Tuple.Create(method, parameters));
                    var result = respond(method, parameters);
                    Session.OnDevToolsMethodResult(null, ++id, true, new MemoryStream(Encoding.UTF8.GetBytes(result)));
                    return id;
                });
            }
            public CefSharpCookieService Service() => new CefSharpCookieService(_ => Task.FromResult(Session));
            public void Dispose() => Session.Dispose();
        }
    }
}
