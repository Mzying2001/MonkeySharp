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
                if (method == "Network.deleteCookies")
                {
                    Assert.Equal("example.com", parameters.GetProperty("domain").GetString());
                    Assert.Equal("/deep", parameters.GetProperty("path").GetString());
                    Assert.False(parameters.TryGetProperty("url", out _));
                    deleted = true; return "{}";
                }
                return "{\"cookies\":[" + Root + (deleted ? "" : "," + deep) + "]}";
            }))
            using (var service = harness.Service())
                Assert.True(await service.DeleteAsync(new UserScriptCookieMutation(new Uri("https://example.com/deep/page"), "sid"), CancellationToken.None));
        }

        [Fact]
        public async Task FailedDeletionReadbackIsAnError()
        {
            using (var harness = new Harness((method, _) => method == "Network.deleteCookies" ? "{}" : "{\"cookies\":[" + Root + "]}"))
            using (var service = harness.Service())
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(new UserScriptCookieMutation(new Uri("https://example.com/"), "sid"), CancellationToken.None));
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
