using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Permissions;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using Mzying2001.MonkeySharp.Core.Storage;
using static Mzying2001.MonkeySharp.Core.Storage.UserScriptValue;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class BridgeGatewayTests
    {
        [Fact]
        public async Task HelloReturnsVersionLimitsAndGrantedApis()
        {
            using (var fixture = await BridgeFixture.CreateAsync("GM.getValue", "GM.setValue"))
            {
                var response = await fixture.HelloAsync();

                Assert.Equal("hello-result", response.GetProperty("type").GetString());
                Assert.True(response.GetProperty("ok").GetBoolean());
                Assert.Equal(1024 * 1024, response.GetProperty("limits").GetProperty("requestBytes").GetInt32());
                Assert.Contains("GM.getValue", response.GetProperty("apis").EnumerateArray().Select(item => item.GetString()));
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("none")]
        [InlineData("GM.info")]
        [InlineData("GM_info")]
        [InlineData("GM.getValue")]
        public async Task HelloIncludesInfoAndOnlyTheDeclaredModernFamily(string grant)
        {
            var grants = grant == null ? Array.Empty<string>() : new[] { grant };
            using (var fixture = await BridgeFixture.CreateAsync(grants))
            {
                var response = await fixture.HelloAsync();
                var apis = response.GetProperty("apis").EnumerateArray().Select(item => item.GetString()).ToList();
                Assert.Contains("GM.info", apis);
                Assert.Equal(grant == "GM.getValue", apis.Contains("GM.getValue"));
                Assert.Equal(grant == "GM.getValue", apis.Contains("GM.setValue"));
                Assert.DoesNotContain("GM.addStyle", apis);
                AssertError(await fixture.RequestAsync("GM.notification", new { }), BridgeErrorCodes.GrantDenied);
            }
        }

        [Fact]
        public async Task LegacyFixtureMetadataAndCookieGrantReachTheBridgeProvider()
        {
            using (var cookies = new InMemoryCookieService())
            using (var provider = new CookieApiProvider(cookies))
            using (var fixture = await BridgeFixture.CreateFromSourceAsync(
                LegacyUserscriptFixture.Read(), new IUserScriptApiProvider[] { provider }))
            {
                var hello = await fixture.HelloAsync();
                Assert.Contains("GM.cookie", hello.GetProperty("apis").EnumerateArray().Select(item => item.GetString()));

                var result = await fixture.RequestAsync("GM.cookie", new
                {
                    operation = "set",
                    details = new { name = "bridge-fixture", value = "ok" }
                });

                Assert.True(result.GetProperty("ok").GetBoolean());
                Assert.Equal("undefined", result.GetProperty("result").GetProperty("$monkeySharpType").GetString());
                var listed = await fixture.RequestAsync("GM.cookie", new { operation = "list" });
                Assert.Equal("example.com", listed.GetProperty("result")[0].GetProperty("domain").GetString());
            }
        }

        [Fact]
        public async Task HelloReturnsAtomicCompatibilityStorageSnapshot()
        {
            using (var fixture = await BridgeFixture.CreateAsync("GM.getValue", "GM.listValues"))
            {
                await fixture.Store.SetAsync(
                    fixture.Invocation.ScriptKey.ToString(), "theme", FromJson("{\"dark\":true}"), CancellationToken.None);

                var response = await fixture.HelloAsync();
                var storage = response.GetProperty("compatibility").GetProperty("storage");

                Assert.True(storage.GetProperty("complete").GetBoolean());
                Assert.Equal("theme", storage.GetProperty("entries")[0][0].GetString());
                Assert.Equal("o{\"dark\":true}", storage.GetProperty("entries")[0][1].GetString());
            }
        }

        [Theory]
        [InlineData("GM.getResourceUrl")]
        [InlineData("GM_getResourceURL")]
        public async Task ResourceUrlGrantsAuthorizeCanonicalBridgeAndBootstrapResources(string grant)
        {
            var source = MetadataAndMatchingTests.Script(
                "// @name resource\n// @match https://example.com/*\n// @run-at document-end\n" +
                "// @grant " + grant + "\n// @resource template https://cdn.example/template.txt");
            using (var provider = new ResourceAndNetworkApiProvider())
            using (var fixture = await BridgeFixture.CreateFromSourceAsync(
                source, new IUserScriptApiProvider[] { provider }))
            {
                Assert.Equal(new[] { grant }, fixture.Invocation.DeclaredGrants);
                Assert.Equal(new[] { "GM.getResourceText", "GM.getResourceUrl" }, fixture.Invocation.Grants.OrderBy(value => value));
                var hello = await fixture.HelloAsync();
                Assert.Contains("GM.getResourceUrl", hello.GetProperty("apis").EnumerateArray().Select(item => item.GetString()));
                var resources = hello.GetProperty("compatibility").GetProperty("resources");
                Assert.True(resources.GetProperty("complete").GetBoolean());
                Assert.Equal("data:text/plain;base64,aGVsbG8=",
                    resources.GetProperty("values").GetProperty("template").GetProperty("url").GetString());

                var response = await fixture.RequestAsync("GM.getResourceUrl", new { name = "template" });
                Assert.True(response.GetProperty("ok").GetBoolean());
                Assert.Equal("data:text/plain;base64,aGVsbG8=", response.GetProperty("result").GetString());
                Assert.Equal("hello", (await fixture.RequestAsync("GM.getResourceText", new { name = "template" })).GetProperty("result").GetString());
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("none")]
        public async Task ResourceUrlBridgeRequiresAResourceFamilyGrant(string grant)
        {
            var source = MetadataAndMatchingTests.Script(
                "// @name resource\n// @match https://example.com/*\n// @run-at document-end\n" +
                (grant == null ? "" : "// @grant " + grant + "\n") +
                "// @resource template https://cdn.example/template.txt");
            using (var provider = new ResourceAndNetworkApiProvider())
            using (var fixture = await BridgeFixture.CreateFromSourceAsync(
                source, new IUserScriptApiProvider[] { provider }))
            {
                var hello = await fixture.HelloAsync();
                Assert.DoesNotContain("GM.getResourceUrl", hello.GetProperty("apis").EnumerateArray().Select(item => item.GetString()));
                AssertError(await fixture.RequestAsync("GM.getResourceUrl", new { name = "template" }),
                    BridgeErrorCodes.GrantDenied);
            }
        }

        [Fact]
        public async Task ForgedIdentityAndStaleDocumentAreRejected()
        {
            using (var fixture = await BridgeFixture.CreateAsync("GM.getValue"))
            {
                var forgedKey = await fixture.RequestAsync(
                    "GM.getValue", new { key = "x" }, scriptKey: Guid.NewGuid().ToString("D"));
                var forgedCapability = await fixture.RequestAsync(
                    "GM.getValue", new { key = "x" }, capability: "forged");
                await fixture.Engine.InvalidateDocumentAsync(fixture.Frame.DocumentId, CancellationToken.None);
                var stale = await fixture.RequestAsync("GM.getValue", new { key = "x" });

                AssertError(forgedKey, BridgeErrorCodes.SessionExpired);
                AssertError(forgedCapability, BridgeErrorCodes.SessionExpired);
                AssertError(stale, BridgeErrorCodes.SessionExpired);
            }
        }

        [Theory]
        [InlineData("GM_getValues", "GM.getValues")]
        [InlineData("GM_setValues", "GM.setValues")]
        [InlineData("GM_deleteValues", "GM.deleteValues")]
        public async Task BulkGrantsEnableTheModernStorageFamilyAndBootstrap(string alias, string canonical)
        {
            using (var fixture = await BridgeFixture.CreateAsync(alias))
            {
                var hello = await fixture.HelloAsync();
                var apis = hello.GetProperty("apis").EnumerateArray().Select(item => item.GetString()).ToList();
                Assert.Contains(canonical, apis);
                Assert.Contains("GM.setValue", apis);
                Assert.Contains("GM.getValue", apis);
                Assert.True(hello.GetProperty("compatibility").GetProperty("storage").GetProperty("complete").GetBoolean());
                Assert.True((await fixture.RequestAsync("GM.setValue", new { key = "x", value = "n1" })).GetProperty("ok").GetBoolean());
                Assert.Equal("n1", (await fixture.RequestAsync("GM.getValue", new { key = "x" })).GetProperty("result").GetProperty("value").GetString());
                AssertError(await fixture.RequestAsync("GM.notification", new { }), BridgeErrorCodes.GrantDenied);
                Assert.Equal(new[] { alias }, fixture.Invocation.DeclaredGrants);
            }
        }

        [Fact]
        public async Task BulkStorageReadsDefaultsAllValuesAndDeletesDistinctKeys()
        {
            using (var fixture = await BridgeFixture.CreateAsync("GM.getValues", "GM.setValues", "GM.deleteValues"))
            {
                var values = new Dictionary<string, object>
                {
                    ["nullable"] = null, ["nested"] = new { items = new[] { 1, 2 }, enabled = true },
                    ["__proto__"] = "ordinary", ["constructor"] = 42
                };
                var set = await fixture.RequestAsync("GM.setValues", new { values = values.ToDictionary(item => item.Key, item => FromJson(JsonSerializer.Serialize(item.Value)).EncodedValue) });
                Assert.True(set.GetProperty("ok").GetBoolean());
                Assert.Equal((await fixture.Store.GetSnapshotAsync(fixture.Invocation.ScriptKey.ToString(), CancellationToken.None)).Sequence, set.GetProperty("result").GetProperty("sequence").GetInt64());
                var read = (await fixture.RequestAsync("GM.getValues", new { keys = new[] { "nullable", "nested", "missing", "nested" } }))
                    .GetProperty("result");
                Assert.Equal("onull", read.GetProperty("nullable").GetString());
                Assert.Equal("o{\"items\":[1,2],\"enabled\":true}", read.GetProperty("nested").GetString());
                Assert.False(read.TryGetProperty("missing", out _));
                await fixture.Store.SetAsync(Guid.NewGuid().ToString("D"), "other-script", FromJson("true"), CancellationToken.None);
                var all = (await fixture.RequestAsync("GM.getValues", new { keys = (string[])null })).GetProperty("result");
                Assert.Equal(4, all.EnumerateObject().Count());
                Assert.Equal("sordinary", all.GetProperty("__proto__").GetString());
                Assert.Equal("n42", all.GetProperty("constructor").GetString());
                Assert.Empty((await fixture.RequestAsync("GM.getValues", new { keys = Array.Empty<string>() }))
                    .GetProperty("result").EnumerateObject());

                var changes = new List<UserScriptValueChangedEventArgs>();
                fixture.Store.ValueChanged += (_, change) => changes.Add(change);
                var deleted = await fixture.RequestAsync("GM.deleteValues", new { keys = new[] { "nullable", "nullable", "missing" } });
                Assert.Equal((await fixture.Store.GetSnapshotAsync(fixture.Invocation.ScriptKey.ToString(), CancellationToken.None)).Sequence, deleted.GetProperty("result").GetProperty("sequence").GetInt64());
                Assert.Equal("nullable", Assert.Single(changes).Key);
                Assert.True((await fixture.RequestAsync("GM.setValues", new { values = new { } })).GetProperty("ok").GetBoolean());
                Assert.True((await fixture.RequestAsync("GM.deleteValues", new { keys = Array.Empty<string>() })).GetProperty("ok").GetBoolean());
            }
        }

        [Theory]
        [InlineData("GM.setValues", "{\"values\":{\"valid\":\"n2\",\"bad\":\"invalid\"}}")]
        [InlineData("GM.setValues", "{\"values\":[]}")]
        [InlineData("GM.setValues", "{\"values\":null}")]
        [InlineData("GM.deleteValues", "{\"keys\":[\"valid\",12]}")]
        [InlineData("GM.deleteValues", "{\"keys\":{}}")]
        [InlineData("GM.getValues", "{\"keys\":[\"valid\",false]}")]
        [InlineData("GM.getValues", "{\"keys\":false}")]
        public async Task BulkStorageValidatesEntireRequestBeforeChangingValues(string method, string json)
        {
            using (var fixture = await BridgeFixture.CreateAsync(method))
            using (var parameters = JsonDocument.Parse(json))
            {
                await fixture.Store.SetAsync(fixture.Invocation.ScriptKey.ToString(), "valid", FromJson("1"), CancellationToken.None);
                var changes = 0;
                fixture.Store.ValueChanged += (_, __) => changes++;
                AssertError(await fixture.RequestAsync(method, parameters.RootElement), BridgeErrorCodes.InvalidParams);
                Assert.Equal("n1", (await fixture.Store.GetAsync(fixture.Invocation.ScriptKey.ToString(), "valid", CancellationToken.None)).Value.EncodedValue);
                Assert.Equal(0, changes);
            }
        }

        [Fact]
        public async Task BulkStorageFailurePreservesWholePartitionAndPublishesNothing()
        {
            using (var fixture = await BridgeFixture.CreateWithStoreAsync(new FailingBulkStore(), "GM.setValues", "GM.addValueChangeListener"))
            {
                var notifications = new List<BridgeNotificationEventArgs>();
                fixture.Gateway.Notification += (_, notification) => notifications.Add(notification);
                await fixture.RequestAsync("GM.addValueChangeListener", new { key = "first", listenerId = 1 });
                var requestId = Guid.NewGuid().ToString("D");
                AssertError(await fixture.RequestAsync("GM.setValues", new
                {
                    values = new { first = "n1", fail = "n2", last = "n3" }
                }, requestId), BridgeErrorCodes.Internal);
                var scriptKey = fixture.Invocation.ScriptKey.ToString();
                Assert.False((await fixture.Store.GetAsync(scriptKey, "first", CancellationToken.None)).Exists);
                Assert.False((await fixture.Store.GetAsync(scriptKey, "last", CancellationToken.None)).Exists);
                Assert.Empty(notifications);
                notifications.Clear();
                await fixture.Store.SetAsync(scriptKey, "last", FromJson("4"), CancellationToken.None);
                using (var data = JsonDocument.Parse(Assert.Single(notifications).DataJson))
                    Assert.False(data.RootElement.TryGetProperty("originExecutionId", out _));
            }
        }

        [Fact]
        public async Task BulkStorageHonorsHostPermissionDenial()
        {
            using (var fixture = await BridgeFixture.CreateAsync(new DenyPolicy(), null, null, "GM.setValues"))
            {
                AssertError(await fixture.RequestAsync("GM.setValues", new { values = new { key = 1 } }), BridgeErrorCodes.PermissionDenied);
                Assert.Empty(await fixture.Store.ListKeysAsync(fixture.Invocation.ScriptKey.ToString(), CancellationToken.None));
            }
        }

        [Fact]
        public async Task BulkReadAllUsesRequiredAtomicSnapshot()
        {
            using (var fixture = await BridgeFixture.CreateWithStoreAsync(new InMemoryUserScriptValueStore(), "GM.getValues", "GM.setValues"))
            {
                await fixture.RequestAsync("GM.setValues", new { values = new { first = "n1", second = "stwo" } });
                var result = (await fixture.RequestAsync("GM.getValues", new { })).GetProperty("result");
                Assert.Equal("n1", result.GetProperty("first").GetString());
                Assert.Equal("stwo", result.GetProperty("second").GetString());
            }
        }

        [Fact]
        public async Task GrantAndHostSupportAreCheckedIndependently()
        {
            using (var fixture = await BridgeFixture.CreateAsync("GM.getValue", "GM.download"))
            {
                var denied = await fixture.RequestAsync("GM.notification", new { });
                var unsupported = await fixture.RequestAsync("GM.download", new { url = "https://example.com/a" });

                AssertError(denied, BridgeErrorCodes.GrantDenied);
                AssertError(unsupported, BridgeErrorCodes.NotSupported);
            }
        }

        [Fact]
        public async Task CookieAndWebRequestRemainUnsupportedWithoutProviders()
        {
            using (var fixture = await BridgeFixture.CreateAsync("GM.cookie", "GM.webRequest"))
            {
                var hello = await fixture.HelloAsync();
                var capabilities = hello.GetProperty("apis").EnumerateArray()
                    .Select(item => item.GetString()).ToList();
                Assert.DoesNotContain("GM.cookie", capabilities);
                Assert.DoesNotContain("GM.webRequest", capabilities);

                var cookie = await fixture.RequestAsync("GM.cookie", new { });
                var webRequest = await fixture.RequestAsync("GM.webRequest", new { });
                AssertError(cookie, BridgeErrorCodes.NotSupported);
                AssertError(webRequest, BridgeErrorCodes.NotSupported);
            }
        }

        [Fact]
        public async Task CookieAuthorizationUsesNestedOrFrameUrlTargets()
        {
            var policy = new CapturingAllowPolicy();
            using (var service = new InMemoryCookieService())
            using (var fixture = await BridgeFixture.CreateAsync(
                policy,
                new IUserScriptApiProvider[] { new CookieApiProvider(service) },
                null,
                "GM.cookie"))
            {
                await fixture.RequestAsync("GM.cookie", new
                {
                    operation = "list",
                    details = new { }
                });

                Assert.Equal(fixture.Frame.Url.AbsoluteUri, policy.LastRequest.Target);

                await fixture.RequestAsync("GM.cookie", new
                {
                    operation = "list",
                    details = new { url = "https://example.com/other" }
                });

                Assert.Equal("https://example.com/other", policy.LastRequest.Target);
            }
        }

        [Fact]
        public async Task BasicStorageApiPreservesJsonNullAndMissing()
        {
            using (var fixture = await BridgeFixture.CreateAsync(
                "GM.getValue", "GM.setValue", "GM.deleteValue", "GM.listValues"))
            {
                var missing = await fixture.RequestAsync("GM.getValue", new { key = "missing", defaultValue = "fallback" });
                await fixture.RequestAsync("GM.setValue", new { key = "null", value = "onull" });
                await fixture.RequestAsync("GM.setValue", new { key = "object", value = "o{\"nested\":[1,2]}" });
                var storedNull = await fixture.RequestAsync("GM.getValue", new { key = "null", defaultValue = "fallback" });
                var storedObject = await fixture.RequestAsync("GM.getValue", new { key = "object" });
                var keys = await fixture.RequestAsync("GM.listValues", new { });
                var deleted = await fixture.RequestAsync("GM.deleteValue", new { key = "null" });
                var absentWithoutDefault = await fixture.RequestAsync("GM.getValue", new { key = "null" });

                Assert.False(missing.GetProperty("result").GetProperty("exists").GetBoolean());
                Assert.Equal("onull", storedNull.GetProperty("result").GetProperty("value").GetString());
                Assert.Equal("o{\"nested\":[1,2]}", storedObject.GetProperty("result").GetProperty("value").GetString());
                Assert.Equal(new[] { "null", "object" }, keys.GetProperty("result").EnumerateArray().Select(item => item.GetString()));
                Assert.Equal((await fixture.Store.GetSnapshotAsync(fixture.Invocation.ScriptKey.ToString(), CancellationToken.None)).Sequence, deleted.GetProperty("result").GetProperty("sequence").GetInt64());
                Assert.False(absentWithoutDefault.GetProperty("result").GetProperty("exists").GetBoolean());
            }
        }

        [Fact]
        public async Task TypedUndefinedSurvivesSingleBulkSnapshotAndListenerBridgePaths()
        {
            using (var fixture = await BridgeFixture.CreateAsync("GM.getValue", "GM.setValue", "GM.getValues",
                "GM.setValues", "GM.deleteValue", "GM.addValueChangeListener"))
            {
                var notifications = new List<BridgeNotificationEventArgs>();
                fixture.Gateway.Notification += (_, item) => notifications.Add(item);
                await fixture.RequestAsync("GM.addValueChangeListener", new { listenerId = 1, key = "" });
                var set = await fixture.RequestAsync("GM.setValue", new { key = "", value = "u" });
                Assert.True(set.GetProperty("ok").GetBoolean());
                var read = (await fixture.RequestAsync("GM.getValue", new { key = "" })).GetProperty("result");
                Assert.True(read.GetProperty("exists").GetBoolean());
                Assert.Equal("u", read.GetProperty("value").GetString());
                var all = (await fixture.RequestAsync("GM.getValues", new { })).GetProperty("result");
                Assert.Equal("u", all.GetProperty("").GetString());
                var snapshot = (await fixture.HelloAsync()).GetProperty("compatibility").GetProperty("storage");
                Assert.Equal("", snapshot.GetProperty("entries")[0][0].GetString());
                Assert.Equal("u", snapshot.GetProperty("entries")[0][1].GetString());
                await fixture.RequestAsync("GM.setValue", new { key = "", value = "u" });
                Assert.Equal(2, notifications.Count);
                using (var created = JsonDocument.Parse(notifications[0].DataJson))
                {
                    Assert.False(created.RootElement.GetProperty("oldValueExists").GetBoolean());
                    Assert.True(created.RootElement.GetProperty("newValueExists").GetBoolean());
                    Assert.Equal("u", created.RootElement.GetProperty("newValue").GetString());
                }
                await fixture.RequestAsync("GM.deleteValue", new { key = "" });
                Assert.Equal(4, notifications.Count);
                using (var deleted = JsonDocument.Parse(notifications[2].DataJson))
                {
                    Assert.True(deleted.RootElement.GetProperty("oldValueExists").GetBoolean());
                    Assert.False(deleted.RootElement.GetProperty("newValueExists").GetBoolean());
                    Assert.Equal("u", deleted.RootElement.GetProperty("oldValue").GetString());
                }
                await fixture.RequestAsync("GM.setValues", new { values = new Dictionary<string, string>
                {
                    ["$monkeySharpType"] = "sundefined", ["__proto__"] = "o{\"safe\":true}",
                    ["constructor"] = "nInfinity", ["undefined"] = "u"
                } });
                all = (await fixture.RequestAsync("GM.getValues", new { })).GetProperty("result");
                Assert.Equal("sundefined", all.GetProperty("$monkeySharpType").GetString());
                Assert.Equal("o{\"safe\":true}", all.GetProperty("__proto__").GetString());
                Assert.Equal("nInfinity", all.GetProperty("constructor").GetString());
                Assert.Equal("u", all.GetProperty("undefined").GetString());
            }
        }

        [Theory]
        [InlineData("o{invalid")]
        [InlineData("o42")]
        [InlineData("n1e999")]
        [InlineData("invalid")]
        public async Task InvalidTypedSingleValuesReturnInvalidParamsWithoutMutation(string encoded)
        {
            using (var fixture = await BridgeFixture.CreateAsync("GM.setValue"))
            {
                AssertError(await fixture.RequestAsync("GM.setValue", new { key = "", value = encoded }), BridgeErrorCodes.InvalidParams);
                Assert.Empty(await fixture.Store.ListKeysAsync(fixture.Invocation.ScriptKey.ToString(), CancellationToken.None));
            }
        }

        [Fact]
        public async Task RequestIdsCannotBeReplayed()
        {
            using (var fixture = await BridgeFixture.CreateAsync("GM.getValue"))
            {
                var requestId = Guid.NewGuid().ToString("D");
                var first = await fixture.RequestAsync("GM.getValue", new { key = "x" }, requestId: requestId);
                var replay = await fixture.RequestAsync("GM.getValue", new { key = "x" }, requestId: requestId);

                Assert.True(first.GetProperty("ok").GetBoolean());
                AssertError(replay, BridgeErrorCodes.MalformedMessage);
            }
        }

        [Fact]
        public async Task ReplayTrackingEvictsOldRequestIdsAtTheConfiguredBound()
        {
            using (var fixture = await BridgeFixture.CreateAsync(
                new AllowDeclaredPermissionsPolicy(),
                null,
                new BridgeOptions(maxReplayEntriesPerExecution: 2),
                "GM.getValue"))
            {
                var firstId = Guid.NewGuid().ToString("D");
                var recentId = Guid.NewGuid().ToString("D");
                Assert.True((await fixture.RequestAsync(
                    "GM.getValue", new { key = "x" }, requestId: firstId)).GetProperty("ok").GetBoolean());
                AssertError(
                    await fixture.RequestAsync("GM.getValue", new { key = "x" }, requestId: firstId),
                    BridgeErrorCodes.MalformedMessage);
                Assert.True((await fixture.RequestAsync(
                    "GM.getValue", new { key = "x" }, requestId: recentId)).GetProperty("ok").GetBoolean());
                Assert.True((await fixture.RequestAsync(
                    "GM.getValue", new { key = "x" })).GetProperty("ok").GetBoolean());

                AssertError(
                    await fixture.RequestAsync("GM.getValue", new { key = "x" }, requestId: recentId),
                    BridgeErrorCodes.MalformedMessage);
                Assert.True((await fixture.RequestAsync(
                    "GM.getValue", new { key = "x" }, requestId: firstId)).GetProperty("ok").GetBoolean());
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(99)]
        public async Task MalformedVersionAndInvalidParamsUseStableErrors(int unsupportedVersion)
        {
            using (var fixture = await BridgeFixture.CreateAsync("GM.getValue"))
            {
                var malformed = Parse(await fixture.Gateway.DispatchAsync("{", CancellationToken.None));
                var invalidParams = await fixture.RequestAsync("GM.getValue", new { key = 42 });
                var versionMessage = new Dictionary<string, object>
                {
                    ["type"] = "hello",
                    ["protocol"] = unsupportedVersion,
                    ["documentId"] = fixture.Frame.DocumentId,
                    ["scriptKey"] = fixture.Invocation.ScriptKey.ToString(),
                    ["capability"] = fixture.Invocation.Capability
                };
                var version = Parse(await fixture.Gateway.DispatchAsync(
                    JsonSerializer.Serialize(versionMessage), CancellationToken.None));

                AssertError(malformed, BridgeErrorCodes.MalformedMessage);
                AssertError(invalidParams, BridgeErrorCodes.InvalidParams);
                AssertError(version, BridgeErrorCodes.ProtocolVersion);
                Assert.Equal("hello-result", version.GetProperty("type").GetString());
                Assert.False(version.TryGetProperty("requestId", out _));
            }
        }

        [Fact]
        public async Task PermissionPolicyCanDenyADeclaredApi()
        {
            using (var fixture = await BridgeFixture.CreateAsync(
                new DenyPolicy(), null, null, "GM.getValue"))
            {
                var response = await fixture.RequestAsync("GM.getValue", new { key = "x" });

                AssertError(response, BridgeErrorCodes.PermissionDenied);
            }
        }

        [Fact]
        public async Task ProviderDiagnosticsAreForwardedByTheGateway()
        {
            var provider = new DiagnosticProvider();
            using (var fixture = await BridgeFixture.CreateAsync(
                null,
                new[] { provider },
                null,
                "GM.download"))
            {
                UserScriptDiagnostic observed = null;
                fixture.Gateway.Diagnostic += (_, diagnostic) => observed = diagnostic;

                var response = await fixture.RequestAsync("GM.download", new { });

                Assert.True(response.GetProperty("ok").GetBoolean());
                Assert.NotNull(observed);
                Assert.Equal("MSA_TEST_DIAGNOSTIC", observed.Code);
            }
        }

        [Fact]
        public async Task TimeoutAndCancelReturnStableErrors()
        {
            var provider = new BlockingProvider();
            using (var fixture = await BridgeFixture.CreateAsync(
                null,
                new[] { provider },
                new BridgeOptions(requestTimeout: TimeSpan.FromMilliseconds(30)),
                "GM.download"))
            {
                var timeout = await fixture.RequestAsync("GM.download", new { url = "https://example.com" });
                AssertError(timeout, BridgeErrorCodes.Timeout);
            }

            provider = new BlockingProvider();
            using (var fixture = await BridgeFixture.CreateAsync(
                null,
                new[] { provider },
                new BridgeOptions(requestTimeout: TimeSpan.FromSeconds(5)),
                "GM.download"))
            {
                var requestId = Guid.NewGuid().ToString("D");
                var request = fixture.RequestAsync("GM.download", new { url = "https://example.com" }, requestId: requestId);
                await provider.Entered.Task;
                var cancel = await fixture.CancelAsync(requestId);
                var canceled = await request;

                Assert.True(cancel.GetProperty("ok").GetBoolean());
                AssertError(canceled, BridgeErrorCodes.Canceled);
            }
        }

        [Theory]
        [InlineData("execute")]
        [InlineData("readBody")]
        public async Task LongRunningHttpIsExemptFromGenericBridgeTimeoutButStillCancelable(string operation)
        {
            var provider = new BlockingProvider("GM.xmlHttpRequest");
            using (var fixture = await BridgeFixture.CreateAsync(
                null,
                new[] { provider },
                new BridgeOptions(requestTimeout: TimeSpan.FromMilliseconds(30)),
                "GM.xmlHttpRequest"))
            {
                var requestId = Guid.NewGuid().ToString("D");
                var request = fixture.RequestAsync(
                    "GM.xmlHttpRequest",
                    new { operation, sessionId = "session" },
                    requestId: requestId);
                await provider.Entered.Task;
                await Task.Delay(80);
                Assert.False(request.IsCompleted);

                await fixture.CancelAsync(requestId);
                AssertError(await request, BridgeErrorCodes.Canceled);
            }

            provider = new BlockingProvider("GM.xmlHttpRequest");
            using (var fixture = await BridgeFixture.CreateAsync(
                null,
                new[] { provider },
                new BridgeOptions(requestTimeout: TimeSpan.FromMilliseconds(30)),
                "GM.xmlHttpRequest"))
            {
                var timedOut = await fixture.RequestAsync(
                    "GM.xmlHttpRequest",
                    new { operation = "create", url = "https://example.com/" });
                AssertError(timedOut, BridgeErrorCodes.Timeout);
            }
        }

        [Fact]
        public async Task PendingAndPayloadLimitsRejectExcessWork()
        {
            var provider = new BlockingProvider();
            using (var fixture = await BridgeFixture.CreateAsync(
                null,
                new[] { provider },
                new BridgeOptions(requestTimeout: TimeSpan.FromSeconds(5), maxPendingRequestsPerDocument: 1),
                "GM.download"))
            {
                var firstId = Guid.NewGuid().ToString("D");
                var first = fixture.RequestAsync("GM.download", new { }, requestId: firstId);
                await provider.Entered.Task;
                var excess = await fixture.RequestAsync("GM.download", new { });
                await fixture.CancelAsync(firstId);
                await first;

                AssertError(excess, BridgeErrorCodes.PayloadTooLarge);
            }

            using (var fixture = await BridgeFixture.CreateAsync(
                null,
                null,
                new BridgeOptions(maxRequestBytes: 128),
                "GM.getValue"))
            {
                var oversized = Parse(await fixture.Gateway.DispatchAsync(
                    new string('x', 129), CancellationToken.None));
                AssertError(oversized, BridgeErrorCodes.PayloadTooLarge);
            }
        }

        [Fact]
        public async Task DisposeCancelsActiveDispatchBeforeReleasingProviders()
        {
            var provider = new DisposeAwareBlockingProvider();
            using (var fixture = await BridgeFixture.CreateAsync(
                null,
                new[] { provider },
                new BridgeOptions(requestTimeout: TimeSpan.FromSeconds(5)),
                "GM.download"))
            {
                var request = fixture.RequestAsync("GM.download", new { });
                await provider.Entered.Task;

                fixture.Gateway.Dispose();

                AssertError(await request, BridgeErrorCodes.Canceled);
                Assert.True(provider.Exited.Task.IsCompleted);
                Assert.True(provider.Disposed);
                AssertError(
                    await fixture.RequestAsync("GM.download", new { }),
                    BridgeErrorCodes.SessionExpired);
            }
        }

        [Fact]
        public async Task ValueListenersOnlyReceiveSameScriptActiveSessions()
        {
            using (var fixture = await BridgeFixture.CreateAsync(
                "GM.addValueChangeListener", "GM.removeValueChangeListener"))
            {
                var secondFrame = BridgeFixture.CreateFrame("doc-two");
                var secondPlan = await fixture.Engine.ProcessLifecycleAsync(
                    new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, secondFrame),
                    CancellationToken.None);
                var secondInvocation = Assert.Single(secondPlan.Invocations);
                var notifications = new List<BridgeNotificationEventArgs>();
                fixture.Gateway.Notification += (_, item) => notifications.Add(item);

                await fixture.RequestAsync("GM.addValueChangeListener", new { listenerId = 1, key = "theme" });
                await fixture.RequestAsync(
                    secondFrame,
                    secondInvocation,
                    "GM.addValueChangeListener",
                    new { listenerId = 1, key = "theme" });
                await fixture.Store.SetAsync(fixture.Invocation.ScriptKey.ToString(), "theme", FromJson("\"dark\""), CancellationToken.None);

                Assert.Equal(4, notifications.Count);
                Assert.Equal(2, notifications.Count(item => item.EventName == "storage-sync"));
                Assert.Equal(2, notifications.Count(item => item.EventName == "value-change"));
                Assert.Equal(2, notifications.Select(item => item.ExecutionId).Distinct().Count());
                Assert.All(notifications.Where(item => item.EventName == "storage-sync"), item =>
                {
                    using (var data = JsonDocument.Parse(item.DataJson))
                    {
                        Assert.Equal(1, data.RootElement.GetProperty("sequence").GetInt64());
                        Assert.Equal("theme", data.RootElement.GetProperty("key").GetString());
                    }
                });

                await fixture.RequestAsync("GM.removeValueChangeListener", new { listenerId = 1 });
                await fixture.RequestAsync(
                    secondFrame,
                    secondInvocation,
                    "GM.removeValueChangeListener",
                    new { listenerId = 1 });
                notifications.Clear();
                await fixture.Store.SetAsync(fixture.Invocation.ScriptKey.ToString(), "theme", FromJson("\"light\""), CancellationToken.None);
                Assert.Equal(2, notifications.Count(item => item.EventName == "storage-sync"));
                Assert.DoesNotContain(notifications, item => item.EventName == "value-change");
            }
        }

        [Fact]
        public async Task StorageSyncReachesExecutionWithoutListenerAndPreservesOriginMetadata()
        {
            using (var fixture = await BridgeFixture.CreateAsync("GM.setValue"))
            {
                var secondFrame = BridgeFixture.CreateFrame("doc-two");
                var secondPlan = await fixture.Engine.ProcessLifecycleAsync(
                    new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, secondFrame),
                    CancellationToken.None);
                var secondInvocation = Assert.Single(secondPlan.Invocations);
                var notifications = new List<BridgeNotificationEventArgs>();
                fixture.Gateway.Notification += (_, item) => notifications.Add(item);

                await fixture.RequestAsync("GM.setValue", new { key = "count", value = "n4" });

                Assert.Equal(2, notifications.Count);
                Assert.All(notifications, item => Assert.Equal("storage-sync", item.EventName));
                Assert.Contains(notifications, item => item.ExecutionId == fixture.Invocation.ExecutionId);
                Assert.Contains(notifications, item => item.ExecutionId == secondInvocation.ExecutionId);
                using (var data = JsonDocument.Parse(notifications[0].DataJson))
                {
                    Assert.Equal(fixture.Invocation.ExecutionId,
                        data.RootElement.GetProperty("originExecutionId").GetString());
                    Assert.False(string.IsNullOrEmpty(data.RootElement.GetProperty("mutationId").GetString()));
                }
            }
        }

        [Theory]
        [InlineData("GM.setValue")]
        [InlineData("GM.setValues")]
        public async Task AcceptedStorageMutationCompletesAfterExecutionEnds(string method)
        {
            var store = new DelayedCommitStore();
            using (var fixture = await BridgeFixture.CreateWithStoreAsync(store, method))
            {
                var request = fixture.RequestAsync(method, method == "GM.setValue"
                    ? (object)new { key = "count", value = "n1" } : new { values = new { count = "n1", second = "n2" } });
                await store.Started.Task;
                await fixture.Engine.InvalidateDocumentAsync(fixture.Frame.DocumentId, CancellationToken.None);
                store.Release.TrySetResult(null);
                var response = await request;
                Assert.True(response.GetProperty("ok").GetBoolean());
                Assert.Equal("n1", (await store.GetAsync(
                    fixture.Invocation.ScriptKey.ToString(), "count", CancellationToken.None)).Value.EncodedValue);
            }
        }

        [Fact]
        public async Task ProviderNotificationsAreRoutedWithExecutionDeliveryToken()
        {
            var provider = new NotificationProvider();
            using (var fixture = await BridgeFixture.CreateAsync(
                null,
                new[] { provider },
                null,
                "GM.download"))
            {
                BridgeNotificationEventArgs delivered = null;
                fixture.Gateway.Notification += (_, item) => delivered = item;

                provider.Emit(fixture.Invocation.ScriptKey, fixture.Invocation.ExecutionId);

                Assert.NotNull(delivered);
                Assert.Equal(fixture.Invocation.ExecutionId, delivered.ExecutionId);
                Assert.Equal(fixture.Invocation.DeliveryToken, delivered.DeliveryToken);
                Assert.Equal("provider-event", delivered.EventName);

                delivered = null;
                await fixture.Engine.InvalidateDocumentAsync(fixture.Frame.DocumentId, CancellationToken.None);
                Assert.Equal("execution-ended", delivered.EventName);
                Assert.Equal(fixture.Invocation.DeliveryToken, delivered.DeliveryToken);
                delivered = null;
                provider.Emit(fixture.Invocation.ScriptKey, fixture.Invocation.ExecutionId);

                Assert.Equal(fixture.Invocation.ExecutionId, provider.EndedExecutionId);
                Assert.Null(delivered);
            }
        }

        [Fact]
        public async Task OversizedResponseKeepsCorrelationId()
        {
            using (var fixture = await BridgeFixture.CreateAsync(
                null,
                new[] { new LargeProvider() },
                new BridgeOptions(maxResponseBytes: 300),
                "GM.download"))
            {
                var requestId = Guid.NewGuid().ToString("D");
                var response = await fixture.RequestAsync("GM.download", new { }, requestId: requestId);

                Assert.Equal(requestId, response.GetProperty("requestId").GetString());
                AssertError(response, BridgeErrorCodes.PayloadTooLarge);
            }
        }

        [Fact]
        public async Task InjectionPayloadIsJsonEncodedAndContainsCompatibilityMetadata()
        {
            using (var fixture = await BridgeFixture.CreateAsync("GM.getValue"))
            {
                var script = BridgeScriptBuilder.BuildInjection(fixture.Plan);
                Assert.DoesNotContain(fixture.Invocation.Source, script);
                Assert.DoesNotContain(fixture.Invocation.Capability, script);
                Assert.DoesNotContain("__MONKEYSHARP_PAYLOAD_BASE64__", script);

                const string prefix = "decodePayload(\"";
                var start = script.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
                var end = script.IndexOf("\")", start, StringComparison.Ordinal);
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(script.Substring(start, end - start)));
                using (var payload = JsonDocument.Parse(json))
                {
                    Assert.Equal(fixture.Invocation.Source,
                        payload.RootElement.GetProperty("invocations")[0].GetProperty("source").GetString());
                    Assert.Contains("GM.getValue", payload.RootElement.GetProperty("invocations")[0]
                        .GetProperty("grants").EnumerateArray().Select(item => item.GetString()));
                    Assert.Contains("GM.getValue", payload.RootElement.GetProperty("invocations")[0]
                        .GetProperty("declaredGrants").EnumerateArray().Select(item => item.GetString()));
                }
            }
        }

        private static void AssertError(JsonElement response, string code)
        {
            Assert.False(response.GetProperty("ok").GetBoolean());
            Assert.Equal(code, response.GetProperty("error").GetProperty("code").GetString());
        }

        private static JsonElement Parse(string json)
        {
            using (var document = JsonDocument.Parse(json))
                return document.RootElement.Clone();
        }

        private sealed class DenyPolicy : IUserScriptPermissionPolicy
        {
            public Task<PermissionDecision> AuthorizeAsync(ApiAuthorizationRequest request, CancellationToken cancellationToken)
            {
                return Task.FromResult(PermissionDecision.Deny);
            }
        }

        private sealed class CapturingAllowPolicy : IUserScriptPermissionPolicy
        {
            public ApiAuthorizationRequest LastRequest { get; private set; }

            public Task<PermissionDecision> AuthorizeAsync(
                ApiAuthorizationRequest request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                return Task.FromResult(PermissionDecision.Allow);
            }
        }

        private sealed class BlockingProvider : IUserScriptApiProvider
        {
            public BlockingProvider(string method = "GM.download")
            {
                Methods = new ReadOnlyCollection<string>(new[] { method });
            }

            public TaskCompletionSource<object> Entered { get; } =
                new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            public IReadOnlyCollection<string> Methods { get; }

            public async Task<ApiResult> InvokeAsync(ApiInvocationContext context, CancellationToken cancellationToken)
            {
                Entered.TrySetResult(null);
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return ApiResult.Undefined;
            }
        }

        private sealed class DiagnosticProvider : IUserScriptApiProvider, IUserScriptDiagnosticSource
        {
            public IReadOnlyCollection<string> Methods { get; } =
                new ReadOnlyCollection<string>(new[] { "GM.download" });
            public event EventHandler<UserScriptDiagnostic> Diagnostic;

            public Task<ApiResult> InvokeAsync(
                ApiInvocationContext context,
                CancellationToken cancellationToken)
            {
                Diagnostic?.Invoke(this, new UserScriptDiagnostic(
                    "MSA_TEST_DIAGNOSTIC",
                    DiagnosticSeverity.Warning,
                    "Test diagnostic."));
                return Task.FromResult(ApiResult.Undefined);
            }
        }

        private sealed class DisposeAwareBlockingProvider : IUserScriptApiProvider, IDisposable
        {
            public TaskCompletionSource<object> Entered { get; } =
                new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<object> Exited { get; } =
                new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            public IReadOnlyCollection<string> Methods { get; } =
                new ReadOnlyCollection<string>(new[] { "GM.download" });
            public bool Disposed { get; private set; }

            public async Task<ApiResult> InvokeAsync(
                ApiInvocationContext context,
                CancellationToken cancellationToken)
            {
                Entered.TrySetResult(null);
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    return ApiResult.Undefined;
                }
                finally
                {
                    Exited.TrySetResult(null);
                }
            }

            public void Dispose()
            {
                Assert.True(Exited.Task.IsCompleted);
                Disposed = true;
            }
        }

        private sealed class LargeProvider : IUserScriptApiProvider
        {
            public IReadOnlyCollection<string> Methods { get; } =
                new ReadOnlyCollection<string>(new[] { "GM.download" });

            public Task<ApiResult> InvokeAsync(ApiInvocationContext context, CancellationToken cancellationToken)
            {
                return Task.FromResult(ApiResult.FromValue(new string('x', 2000)));
            }
        }

        private sealed class DelayedCommitStore : InMemoryUserScriptValueStore
        {
            public TaskCompletionSource<object> Started { get; } =
                new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<object> Release { get; } =
                new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            protected override async Task BeforeCommitAsync(
                string scriptKey, string key, ValueChangeKind kind, CancellationToken cancellationToken)
            {
                Started.TrySetResult(null);
                await Release.Task.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private sealed class NotificationProvider :
            IUserScriptApiProvider,
            IUserScriptNotificationSource,
            IUserScriptExecutionObserver
        {
            public event EventHandler<ApiNotificationEventArgs> Notification;
            public IReadOnlyCollection<string> Methods { get; } =
                new ReadOnlyCollection<string>(new[] { "GM.download" });
            public string EndedExecutionId { get; private set; }

            public Task<ApiResult> InvokeAsync(ApiInvocationContext context, CancellationToken cancellationToken)
            {
                return Task.FromResult(ApiResult.Undefined);
            }

            public void Emit(ScriptKey scriptKey, string executionId)
            {
                Notification?.Invoke(this, new ApiNotificationEventArgs(
                    scriptKey,
                    executionId,
                    "provider-event",
                    "{}"));
            }

            public void OnExecutionEnded(string executionId)
            {
                EndedExecutionId = executionId;
            }
        }

        private sealed class FailingBulkStore : InMemoryUserScriptValueStore
        {
            protected override Task BeforeCommitAsync(string scriptKey, string key, ValueChangeKind kind, CancellationToken cancellationToken)
            {
                if (key == "fail") throw new InvalidOperationException("Simulated storage failure.");
                return Task.CompletedTask;
            }
        }

        private sealed class FixtureResourceProvider : IResourceProvider
        {
            public Task<ResourceContent> GetAsync(
                UserScriptInstallation installation, ResourceDeclaration resource, CancellationToken cancellationToken)
            {
                return Task.FromResult(new ResourceContent(Encoding.UTF8.GetBytes("hello"), "text/plain", "hello"));
            }
        }

        private sealed class BridgeFixture : IDisposable
        {
            private BridgeFixture(
                IUserScriptValueStore store,
                UserScriptEngine engine,
                UserScriptBridgeGateway gateway,
                DocumentFrame frame,
                InjectionPlan plan)
            {
                Store = store;
                Engine = engine;
                Gateway = gateway;
                Frame = frame;
                Plan = plan;
                Invocation = Assert.Single(plan.Invocations);
            }

            public IUserScriptValueStore Store { get; }
            public UserScriptEngine Engine { get; }
            public UserScriptBridgeGateway Gateway { get; }
            public DocumentFrame Frame { get; }
            public InjectionPlan Plan { get; }
            public ScriptInvocation Invocation { get; }

            public static Task<BridgeFixture> CreateAsync(params string[] grants)
            {
                return CreateAsync(null, null, null, grants);
            }

            public static async Task<BridgeFixture> CreateWithStoreAsync(
                IUserScriptValueStore store, params string[] grants)
            {
                var repository = new InMemoryUserScriptRepository();
                var grantLines = string.Join("\n", grants.Select(item => "// @grant " + item));
                var source = MetadataAndMatchingTests.Script(
                    "// @name bridge\n// @match https://example.com/*\n// @connect example.com\n// @run-at document-end\n" + grantLines,
                    "window.bridgeTest = \"'</script>\";");
                await repository.InstallAsync(source, "test", true, CancellationToken.None);
                var engine = new UserScriptEngine(repository);
                var frame = CreateFrame("doc-one");
                var plan = await engine.ProcessLifecycleAsync(
                    new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, frame),
                    CancellationToken.None);
                var gateway = new UserScriptBridgeGateway(engine, store, options: null);
                return new BridgeFixture(store, engine, gateway, frame, plan);
            }

            public static async Task<BridgeFixture> CreateAsync(
                IUserScriptPermissionPolicy permissionPolicy,
                IEnumerable<IUserScriptApiProvider> providers,
                BridgeOptions options,
                params string[] grants)
            {
                var repository = new InMemoryUserScriptRepository();
                var grantLines = string.Join("\n", grants.Select(item => "// @grant " + item));
                var source = MetadataAndMatchingTests.Script(
                    "// @name bridge\n// @match https://example.com/*\n// @connect example.com\n// @run-at document-end\n" + grantLines,
                    "window.bridgeTest = \"'</script>\";");
                await repository.InstallAsync(source, "test", true, CancellationToken.None);
                var engine = new UserScriptEngine(repository);
                var frame = CreateFrame("doc-one");
                var plan = await engine.ProcessLifecycleAsync(
                    new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, frame),
                    CancellationToken.None);
                var store = new InMemoryUserScriptValueStore();
                var gateway = new UserScriptBridgeGateway(engine, store, permissionPolicy, providers, options);
                return new BridgeFixture(store, engine, gateway, frame, plan);
            }

            public static async Task<BridgeFixture> CreateFromSourceAsync(
                string source,
                IEnumerable<IUserScriptApiProvider> providers)
            {
                var repository = new InMemoryUserScriptRepository(preparer: new UserScriptPreparer(new FixtureDependencyProvider(), new FixtureResourceProvider()));
                await repository.InstallAsync(source, "fixture", true, CancellationToken.None);
                var engine = new UserScriptEngine(repository);
                var frame = CreateFrame("legacy-fixture-document");
                var plan = await engine.ProcessLifecycleAsync(
                    new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, frame),
                    CancellationToken.None);
                var store = new InMemoryUserScriptValueStore();
                var gateway = new UserScriptBridgeGateway(engine, store, providers: providers);
                return new BridgeFixture(store, engine, gateway, frame, plan);
            }

            public static DocumentFrame CreateFrame(string documentId)
            {
                return new DocumentFrame(
                    "browser",
                    documentId,
                    "main",
                    new Uri("https://example.com/page"),
                    true,
                    TimingGuarantee.BestEffortDocumentStart,
                    BridgeIntegrityGuarantee.Verified);
            }

            public Task<JsonElement> HelloAsync()
            {
                var envelope = Proof(Frame, Invocation);
                envelope["type"] = "hello";
                return DispatchAsync(envelope);
            }

            public Task<JsonElement> RequestAsync(
                string method,
                object parameters,
                string requestId = null,
                string scriptKey = null,
                string capability = null)
            {
                return RequestAsync(Frame, Invocation, method, parameters, requestId, scriptKey, capability);
            }

            public Task<JsonElement> RequestAsync(
                DocumentFrame frame,
                ScriptInvocation invocation,
                string method,
                object parameters,
                string requestId = null,
                string scriptKey = null,
                string capability = null)
            {
                var envelope = Proof(frame, invocation);
                envelope["type"] = "request";
                envelope["requestId"] = requestId ?? Guid.NewGuid().ToString("D");
                envelope["method"] = method;
                envelope["params"] = parameters;
                if (scriptKey != null) envelope["scriptKey"] = scriptKey;
                if (capability != null) envelope["capability"] = capability;
                return DispatchAsync(envelope);
            }

            public Task<JsonElement> CancelAsync(string requestId)
            {
                var envelope = Proof(Frame, Invocation);
                envelope["type"] = "cancel";
                envelope["requestId"] = requestId;
                return DispatchAsync(envelope);
            }

            public void Dispose()
            {
                Gateway.Dispose();
                Engine.Dispose();
                (Store as IDisposable)?.Dispose();
            }

            private async Task<JsonElement> DispatchAsync(Dictionary<string, object> envelope)
            {
                var response = await Gateway.DispatchAsync(JsonSerializer.Serialize(envelope), CancellationToken.None);
                using (var document = JsonDocument.Parse(response))
                    return document.RootElement.Clone();
            }

            private static Dictionary<string, object> Proof(DocumentFrame frame, ScriptInvocation invocation)
            {
                return new Dictionary<string, object>
                {
                    ["protocol"] = 2,
                    ["documentId"] = frame.DocumentId,
                    ["scriptKey"] = invocation.ScriptKey.ToString(),
                    ["capability"] = invocation.Capability
                };
            }

            private sealed class FixtureDependencyProvider : IUserScriptDependencyProvider
            {
                public Task<ResourceContent> GetScriptAsync(
                    UserScriptInstallation installation,
                    UserScriptDependencyDeclaration dependency,
                    CancellationToken cancellationToken)
                {
                    var text = "/* legacy fixture dependency */";
                    return Task.FromResult(new ResourceContent(Encoding.UTF8.GetBytes(text), "application/javascript", text));
                }
            }
        }
    }
}
