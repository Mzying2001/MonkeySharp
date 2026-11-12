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
        [InlineData(121)]
        [InlineData(151)]
        public async Task PartitionKeysUseTheNativeProtocolShapeAndActualReadback(int major)
        {
            var key = major == 121 ? "\"https://top-a.test\"" : "{\"topLevelSite\":\"https://top-a.test\",\"hasCrossSiteAncestor\":true}";
            var cookie = Root.Replace("\"secure\":false", "\"secure\":true").TrimEnd('}') + ",\"partitionKey\":" + key + "}";
            using (var harness = new Harness((method, parameters) =>
            {
                if (method == "Browser.getVersion") return "{\"product\":\"Chrome/" + major + ".0.0.0\"}";
                if (method == "Network.setCookie")
                {
                    var actual = parameters.GetProperty("partitionKey");
                    Assert.Equal(major == 121 ? JsonValueKind.String : JsonValueKind.Object, actual.ValueKind);
                    if (major == 151) Assert.True(actual.GetProperty("hasCrossSiteAncestor").GetBoolean());
                    return "{\"success\":true}";
                }
                return "{\"cookies\":[" + cookie + "]}";
            }))
            using (var service = harness.Service())
            {
                var actual = await service.SetAsync(new UserScriptCookieMutation(new Uri("https://example.com/"), "sid", "actual", secure:true,
                    httpOnly:true, sameSite:"lax", partitionKey:new UserScriptCookiePartitionKey("https://sub.top-a.test:9443/path")), CancellationToken.None);
                Assert.Equal("https://top-a.test", actual.PartitionKey.TopLevelSite);
                Assert.Equal(major == 151 ? (bool?)true : null, actual.PartitionKey.HasCrossSiteAncestor);
            }
        }

        [Fact]
        public async Task OldEngineRejectsPartitionParametersBeforeCookieCommands()
        {
            using (var harness = new Harness((method, _) => method == "Browser.getVersion" ? "{\"product\":\"Chrome/84.0.4147.105\"}" : throw new InvalidOperationException("Cookie effect is forbidden.")))
            using (var service = harness.Service())
            {
                await Assert.ThrowsAsync<Mzying2001.MonkeySharp.Core.Domain.UnsupportedApiException>(() => service.SetAsync(
                    new UserScriptCookieMutation(new Uri("https://example.com/"), "sid", "value", secure:true, partitionKey:new UserScriptCookiePartitionKey("https://top.test")), CancellationToken.None));
                Assert.Single(harness.Commands);
                Assert.Equal("Browser.getVersion", harness.Commands[0].Item1);
            }
        }

        [Fact]
        public async Task AncestorDefaultsDistinguishSetFromDelete()
        {
            using (var harness = new Harness((method, _) => "{\"product\":\"Chrome/151.0.7922.174\"}"))
            using (var service = harness.Service())
            {
                var key = new UserScriptCookiePartitionKey("https://example.com");
                var url = new Uri("https://sub.example.com/path");
                Assert.False((await service.NormalizePartitionKeyAsync(key, url, CookiePartitionOperation.Set, CancellationToken.None)).HasCrossSiteAncestor);
                Assert.True((await service.NormalizePartitionKeyAsync(key, url, CookiePartitionOperation.Delete, CancellationToken.None)).HasCrossSiteAncestor);
                Assert.Null((await service.NormalizePartitionKeyAsync(key, url, CookiePartitionOperation.List, CancellationToken.None)).HasCrossSiteAncestor);
            }
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
