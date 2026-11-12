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
    public sealed class CookieApiProviderTests
    {
        [Fact]
        public async Task MutationResultsAreUndefinedAndListUsesActualFields()
        {
            var installation = await Install();
            using var service = new InMemoryCookieService();
            using var provider = new CookieApiProvider(service);
            var value = await provider.InvokeAsync(Context(installation, new { operation = "set", details = new { name = "sid", value = "actual", httpOnly = true, sameSite = "strict" } }), CancellationToken.None);
            Assert.Contains("undefined", value.Json);
            var listed = await provider.InvokeAsync(Context(installation, new { operation = "list" }), CancellationToken.None);
            using var document = JsonDocument.Parse(listed.Json);
            var cookie = document.RootElement[0];
            Assert.Equal("actual", cookie.GetProperty("value").GetString());
            Assert.True(cookie.GetProperty("httpOnly").GetBoolean());
            Assert.True(cookie.GetProperty("hostOnly").GetBoolean());
            Assert.True(cookie.GetProperty("session").GetBoolean());
            Assert.Equal("strict", cookie.GetProperty("sameSite").GetString());
            Assert.False(cookie.TryGetProperty("expirationDate", out _));
            var deleted = await provider.InvokeAsync(Context(installation, new { operation = "delete", details = new { name = "sid" } }), CancellationToken.None);
            Assert.Contains("undefined", deleted.Json);
        }

        [Fact]
        public async Task ProviderFiltersHostOnlySecureAndForeignResultsFromService()
        {
            var installation = await Install();
            var service = new UnfilteredService();
            using var provider = new CookieApiProvider(service);
            var listed = await provider.InvokeAsync(Context(installation, new { operation = "list", details = new { url = "https://sub.example.com/path" } }), CancellationToken.None);
            Assert.Equal("domain", JsonDocument.Parse(listed.Json).RootElement[0].GetProperty("value").GetString());
            Assert.Equal(1, JsonDocument.Parse(listed.Json).RootElement.GetArrayLength());
            var count = service.Reads;
            await Assert.ThrowsAsync<BridgeProtocolException>(() => provider.InvokeAsync(Context(installation, new { details = new { url = "https://outside.test/" } }), CancellationToken.None));
            Assert.Equal(count, service.Reads);
        }

        [Theory]
        [InlineData("partitionKey")]
        [InlineData("cookiePartition")]
        [InlineData("firstPartyDomain")]
        public async Task UnsupportedParametersFailBeforeAnyServiceCall(string field)
        {
            var installation = await Install();
            var service = new UnfilteredService();
            using var provider = new CookieApiProvider(service);
            await Assert.ThrowsAsync<UnsupportedApiException>(() => provider.InvokeAsync(Context(installation, new { details = new Dictionary<string, object> { [field] = new { } } }), CancellationToken.None));
            Assert.Equal(0, service.Reads);
        }

        [Fact]
        public async Task DeletionSelectsLongestPathAndPreservesOtherIdentity()
        {
            using var service = new InMemoryCookieService();
            var url = new Uri("https://example.com/deep/page");
            Assert.Equal("/deep", new UserScriptCookieMutation(url, "default").Path);
            await service.SetAsync(new UserScriptCookieMutation(url, "same", "root", path: "/"), CancellationToken.None);
            await service.SetAsync(new UserScriptCookieMutation(url, "same", "deep", path: "/deep"), CancellationToken.None);
            Assert.True(await service.DeleteAsync(new UserScriptCookieMutation(url, "same"), CancellationToken.None));
            Assert.Equal("root", Assert.Single(await service.ListAsync(new UserScriptCookieQuery(url), CancellationToken.None)).Value);
            await service.SetAsync(new UserScriptCookieMutation(url, "expired", "gone", expirationDate: DateTime.UtcNow.AddSeconds(-1)), CancellationToken.None);
            Assert.Empty(await service.ListAsync(new UserScriptCookieQuery(url, "expired"), CancellationToken.None));
        }

        [Fact]
        public async Task InvalidDomainPathSameSiteAndRemovedListenerOperationsAreRejected()
        {
            var installation = await Install();
            using var service = new InMemoryCookieService();
            using var provider = new CookieApiProvider(service);
            foreach (var details in new object[] { new { name = "sid", domain = "outside.test" }, new { name = "sid", path = "relative" }, new { name = "sid", sameSite = "None" } })
                await Assert.ThrowsAsync<BridgeProtocolException>(() => provider.InvokeAsync(Context(installation, new { operation = "set", details }), CancellationToken.None));
            await Assert.ThrowsAsync<BridgeProtocolException>(() => provider.InvokeAsync(Context(installation, new { operation = "addListener" }), CancellationToken.None));
        }

        [Fact]
        public async Task CanonicalPartitionFilterIsAppliedAfterAnUnfilteredBackendResponse()
        {
            var installation = await Install();
            var service = new PartitionedService();
            using var provider = new CookieApiProvider(service);
            var listed = await provider.InvokeAsync(Context(installation, new { details = new { partitionKey = new { topLevelSite = "https://sub.top-a.test:9443/input" } } }), CancellationToken.None);
            using var document = JsonDocument.Parse(listed.Json);
            Assert.Equal("https://sub.top-a.test:9443/input", service.OriginalKey.TopLevelSite);
            Assert.Single(document.RootElement.EnumerateArray());
            Assert.Equal("a", document.RootElement[0].GetProperty("value").GetString());
            Assert.Equal("https://top-a.test", document.RootElement[0].GetProperty("partitionKey").GetProperty("topLevelSite").GetString());
            var all = await provider.InvokeAsync(Context(installation, new { details = new { partitionKey = new { } } }), CancellationToken.None);
            Assert.Equal(3, JsonDocument.Parse(all.Json).RootElement.GetArrayLength());
            var normal = await provider.InvokeAsync(Context(installation, new { details = new { } }), CancellationToken.None);
            Assert.Single(JsonDocument.Parse(normal.Json).RootElement.EnumerateArray());
        }

        [Fact]
        public async Task SecurePartitionMutationsUseTheNormalizedIdentityAndPreserveVoidResults()
        {
            var installation = await Install();
            var service = new PartitionedService();
            using var provider = new CookieApiProvider(service);
            await Assert.ThrowsAsync<BridgeProtocolException>(() => provider.InvokeAsync(Context(installation,
                new { operation = "set", details = new { name = "sid", value = "a", partitionKey = new { topLevelSite = "https://top-a.test" } } }), CancellationToken.None));
            Assert.Null(service.Mutation);
            var result = await provider.InvokeAsync(Context(installation, new { operation = "set", details = new {
                name = "sid", value = "a", secure = true, partitionKey = new { topLevelSite = "https://sub.top-a.test" } } }), CancellationToken.None);
            Assert.Equal(ApiResult.Undefined.Json, result.Json);
            Assert.Equal("https://top-a.test", service.Mutation.PartitionKey.TopLevelSite);
            Assert.True(service.Mutation.PartitionKey.HasCrossSiteAncestor);
            await Assert.ThrowsAsync<BridgeProtocolException>(() => provider.InvokeAsync(Context(installation,
                new { details = new { partitionKey = new { topLevelSite = "file:///site" } } }), CancellationToken.None));
        }

        private sealed class PartitionedService : IPartitionedCookieService
        {
            public UserScriptCookiePartitionKey OriginalKey;
            public UserScriptCookieMutation Mutation;
            public Task<UserScriptCookiePartitionKey> NormalizePartitionKeyAsync(UserScriptCookiePartitionKey key, Uri url, CookiePartitionOperation operation, CancellationToken token)
            {
                OriginalKey = key;
                return Task.FromResult(key.TopLevelSite == null ? key : new UserScriptCookiePartitionKey("https://top-a.test", operation == CookiePartitionOperation.List ? key.HasCrossSiteAncestor : (bool?)true));
            }
            public Task<IReadOnlyList<UserScriptCookie>> ListAsync(UserScriptCookieQuery query, CancellationToken token) => Task.FromResult<IReadOnlyList<UserScriptCookie>>(new[] {
                new UserScriptCookie("sid", "plain", "example.com", "/", secure:true, hostOnly:true),
                new UserScriptCookie("sid", "a", "example.com", "/", secure:true, hostOnly:true, partitionKey:new UserScriptCookiePartitionKey("https://top-a.test", true)),
                new UserScriptCookie("sid", "b", "example.com", "/", secure:true, hostOnly:true, partitionKey:new UserScriptCookiePartitionKey("https://top-b.test", true)) });
            public Task<UserScriptCookie> SetAsync(UserScriptCookieMutation mutation, CancellationToken token) { Mutation = mutation; return Task.FromResult<UserScriptCookie>(null); }
            public Task<bool> DeleteAsync(UserScriptCookieMutation mutation, CancellationToken token) { Mutation = mutation; return Task.FromResult(true); }
        }

        private static Task<UserScriptInstallation> Install() => new InMemoryUserScriptRepository().InstallAsync(
            MetadataAndMatchingTests.Script("// @name cookie\n// @match https://*.example.com/*\n// @grant GM.cookie"), "test", true, CancellationToken.None);
        private static ApiInvocationContext Context(UserScriptInstallation installation, object value)
            => new ApiInvocationContext(installation, new DocumentFrame("browser", "document", "frame", new Uri("https://example.com/path"), true,
                TimingGuarantee.BestEffortDocumentStart, BridgeIntegrityGuarantee.Verified), "execution", Guid.NewGuid().ToString(), "GM.cookie", JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(value)));
        private sealed class UnfilteredService : ICookieService
        {
            public int Reads;
            public Task<IReadOnlyList<UserScriptCookie>> ListAsync(UserScriptCookieQuery query, CancellationToken cancellationToken)
            {
                Reads++;
                return Task.FromResult<IReadOnlyList<UserScriptCookie>>(new[] {
                    new UserScriptCookie("a", "host", "example.com", "/", hostOnly:true),
                    new UserScriptCookie("a", "domain", ".example.com", "/"),
                    new UserScriptCookie("a", "foreign", "outside.test", "/", hostOnly:true) });
            }
            public Task<UserScriptCookie> SetAsync(UserScriptCookieMutation mutation, CancellationToken cancellationToken) => throw new InvalidOperationException();
            public Task<bool> DeleteAsync(UserScriptCookieMutation mutation, CancellationToken cancellationToken) => throw new InvalidOperationException();
        }
    }
}
