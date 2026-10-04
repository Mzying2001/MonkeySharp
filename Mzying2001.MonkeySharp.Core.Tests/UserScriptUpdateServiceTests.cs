using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Updates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class UserScriptUpdateServiceTests
    {
        [Theory]
        [InlineData("Alpha-v1", "Alpha-v10", -1)]
        [InlineData("Alpha-v10", "Alpha-v2", -1)]
        [InlineData("16.4", "16.04", 0)]
        [InlineData("1.0", "1.0.0", 0)]
        [InlineData("1.10.0-alpha", "1.10", -1)]
        [InlineData("2.0", "1.99.99", 1)]
        public void VersionComparerHandlesTampermonkeyExamples(string left, string right, int expectedSign)
        {
            Assert.Equal(expectedSign, Math.Sign(TampermonkeyVersionComparer.CompareVersions(left, right)));
        }

        [Fact]
        public async Task CheckReportsNotConfiguredAndDisabledWithoutFetching()
        {
            var repository = new InMemoryUserScriptRepository();
            var withoutVersion = await InstallAsync(repository, "// @name no version\n// @namespace tests");
            var fetcher = new FakeUpdateFetcher();
            var service = new UserScriptUpdateService(repository, fetcher);
            var unconfigured = await service.CheckAsync(withoutVersion.ScriptKey, CancellationToken.None);

            var disabled = await InstallAsync(repository,
                "// @name disabled\n// @namespace tests\n// @version 1.0\n// @updateURL https://updates.example/check.user.js\n// @downloadURL NONE");
            var disabledResult = await service.CheckAsync(disabled.ScriptKey, CancellationToken.None);

            Assert.Equal(UserScriptUpdateStatus.NotConfigured, unconfigured.Status);
            Assert.Equal(UserScriptUpdateStatus.Disabled, disabledResult.Status);
            Assert.Empty(fetcher.RequestedUrls);
        }

        [Fact]
        public async Task CheckUsesUpdateUrlAndApplyDownloadsThenAtomicallyReplacesInstallation()
        {
            var repository = new InMemoryUserScriptRepository();
            var current = await InstallAsync(repository,
                "// @name same\n// @namespace https://scripts.example/id\n// @version 1.0\n" +
                "// @updateURL https://updates.example/check.user.js\n// @downloadURL https://cdn.example/download.user.js",
                enabled: false);
            var fetcher = new FakeUpdateFetcher(
                Pair("https://updates.example/check.user.js", Script("2.0", "https://updates.example/check.user.js", scriptNamespace: "https://scripts.example/id")),
                Pair("https://cdn.example/download.user.js", Script("2.1", "https://cdn.example/download.user.js", scriptNamespace: "https://scripts.example/id")));
            var service = new UserScriptUpdateService(repository, fetcher);

            var checkResult = await service.CheckAsync(current.ScriptKey, CancellationToken.None);
            var updated = await service.ApplyAsync(checkResult, CancellationToken.None);

            Assert.Equal(UserScriptUpdateStatus.Available, checkResult.Status);
            Assert.Equal("2.0", checkResult.AvailableVersion);
            Assert.Equal(new[] { "https://updates.example/check.user.js", "https://cdn.example/download.user.js" }, fetcher.RequestedUrls);
            Assert.Equal(current.ScriptKey, updated.ScriptKey);
            Assert.Equal(current.InstalledAt, updated.InstalledAt);
            Assert.False(updated.IsEnabled);
            Assert.Equal("2.1", updated.Definition.Metadata.Version);
            Assert.Equal("https://cdn.example/download.user.js", updated.SourceOrigin);
        }

        [Fact]
        public async Task CheckFallsBackToDownloadUrlAndReportsUpToDate()
        {
            var repository = new InMemoryUserScriptRepository();
            var current = await InstallAsync(repository,
                "// @name same\n// @namespace tests\n// @version 2\n// @downloadURL https://scripts.example/script.user.js");
            var fetcher = new FakeUpdateFetcher(
                Pair("https://scripts.example/script.user.js", Script("2.0", "https://scripts.example/script.user.js")));

            var result = await new UserScriptUpdateService(repository, fetcher)
                .CheckAsync(current.ScriptKey, CancellationToken.None);

            Assert.Equal(UserScriptUpdateStatus.UpToDate, result.Status);
            Assert.Null(result.AvailableVersion);
            Assert.Equal(result.CheckUrl, result.DownloadUrl);
        }

        [Fact]
        public async Task ApplyRejectsIdentityMismatchAndKeepsInstalledSource()
        {
            var repository = new InMemoryUserScriptRepository();
            var current = await InstallAsync(repository,
                "// @name same\n// @namespace tests\n// @version 1\n// @downloadURL https://scripts.example/script.user.js");
            var originalSource = current.Definition.Source;
            var fetcher = new FakeUpdateFetcher(
                Pair("https://scripts.example/script.user.js", Script("2", "https://scripts.example/script.user.js", name: "other")));
            var service = new UserScriptUpdateService(repository, fetcher);
            var exception = await Assert.ThrowsAsync<UserScriptUpdateException>(() =>
                service.CheckAsync(current.ScriptKey, CancellationToken.None));

            Assert.Equal("MSU002_IDENTITY_MISMATCH", exception.Code);
            Assert.Equal(originalSource, (await repository.GetAsync(current.ScriptKey, CancellationToken.None)).Definition.Source);
        }

        [Fact]
        public async Task ApplyRejectsDownloadOlderThanCheckSourceWithoutReplacingOldScript()
        {
            var repository = new InMemoryUserScriptRepository();
            var current = await InstallAsync(repository,
                "// @name same\n// @namespace tests\n// @version 1\n" +
                "// @updateURL https://updates.example/check.user.js\n// @downloadURL https://cdn.example/download.user.js");
            var fetcher = new FakeUpdateFetcher(
                Pair("https://updates.example/check.user.js", Script("3", "https://updates.example/check.user.js")),
                Pair("https://cdn.example/download.user.js", Script("2", "https://cdn.example/download.user.js")));
            var service = new UserScriptUpdateService(repository, fetcher);
            var checkResult = await service.CheckAsync(current.ScriptKey, CancellationToken.None);

            var exception = await Assert.ThrowsAsync<UserScriptUpdateException>(() => service.ApplyAsync(checkResult, CancellationToken.None));

            Assert.Equal("MSU007_DOWNLOAD_VERSION_OUTDATED", exception.Code);
            Assert.Equal(current.Definition.Source, (await repository.GetAsync(current.ScriptKey, CancellationToken.None)).Definition.Source);
        }

        [Fact]
        public async Task ApplyRejectsExpiredOrChangedCheckResult()
        {
            var repository = new InMemoryUserScriptRepository();
            var current = await InstallAsync(repository,
                "// @name same\n// @namespace tests\n// @version 1\n// @downloadURL https://scripts.example/script.user.js");
            var clock = new MutableClock(DateTimeOffset.UtcNow);
            var fetcher = new FakeUpdateFetcher(
                Pair("https://scripts.example/script.user.js", Script("2", "https://scripts.example/script.user.js")));
            var service = new UserScriptUpdateService(repository, fetcher, utcNow: clock.Read);
            var expired = await service.CheckAsync(current.ScriptKey, CancellationToken.None);
            clock.Now += UserScriptUpdateService.CheckResultLifetime + TimeSpan.FromSeconds(1);

            var expiredException = await Assert.ThrowsAsync<UserScriptUpdateException>(() => service.ApplyAsync(expired, CancellationToken.None));
            Assert.Equal("MSU005_STALE_CHECK", expiredException.Code);

            clock.Now = DateTimeOffset.UtcNow;
            var changed = await service.CheckAsync(current.ScriptKey, CancellationToken.None);
            await repository.UpdateAsync(current.ScriptKey, Script("1.1", "application://edited"), "edited", CancellationToken.None);
            var changedException = await Assert.ThrowsAsync<UserScriptUpdateException>(() => service.ApplyAsync(changed, CancellationToken.None));
            Assert.Equal("MSU005_STALE_CHECK", changedException.Code);
        }

        [Fact]
        public async Task DownloadedVersionMustBeNewerAndInvalidSourcesDoNotMutateRepository()
        {
            var repository = new InMemoryUserScriptRepository();
            var current = await InstallAsync(repository,
                "// @name same\n// @namespace tests\n// @version 2\n// @updateURL https://updates.example/check.user.js\n// @downloadURL https://cdn.example/download.user.js");
            var fetcher = new FakeUpdateFetcher(
                Pair("https://updates.example/check.user.js", Script("3", "https://updates.example/check.user.js")),
                Pair("https://cdn.example/download.user.js", Script("2", "https://cdn.example/download.user.js")));
            var service = new UserScriptUpdateService(repository, fetcher);
            var checkResult = await service.CheckAsync(current.ScriptKey, CancellationToken.None);
            var exception = await Assert.ThrowsAsync<UserScriptUpdateException>(() => service.ApplyAsync(checkResult, CancellationToken.None));

            Assert.Equal("MSU006_DOWNLOAD_VERSION_INVALID", exception.Code);
            Assert.Equal(current.Definition.Source, (await repository.GetAsync(current.ScriptKey, CancellationToken.None)).Definition.Source);
        }

        private static Task<UserScriptInstallation> InstallAsync(
            InMemoryUserScriptRepository repository,
            string extraMetadata,
            bool enabled = true)
        {
            var source = MetadataAndMatchingTests.Script(
                "// @match https://example.com/*\n" + extraMetadata,
                "window.original = true;");
            return repository.InstallAsync(source, "https://install.example/script.user.js", enabled, CancellationToken.None);
        }

        private static KeyValuePair<string, string> Pair(string url, string source)
        {
            return new KeyValuePair<string, string>(url, source);
        }

        private static string Script(string version, string origin, string name = "same", string scriptNamespace = "tests")
        {
            return MetadataAndMatchingTests.Script(
                "// @name " + name + "\n// @namespace " + scriptNamespace + "\n" +
                "// @version " + version + "\n// @match https://example.com/*",
                "window.updated = true;");
        }

        private sealed class FakeUpdateFetcher : IUserScriptUpdateFetcher
        {
            private readonly IDictionary<string, string> _sources;
            public FakeUpdateFetcher(params KeyValuePair<string, string>[] sources)
            {
                _sources = sources.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            }

            public List<string> RequestedUrls { get; } = new List<string>();

            public Task<string> FetchSourceAsync(UserScriptInstallation installation, string url, CancellationToken cancellationToken)
            {
                RequestedUrls.Add(url);
                return Task.FromResult(_sources[url]);
            }
        }

        private sealed class MutableClock
        {
            public MutableClock(DateTimeOffset now) { Now = now; }
            public DateTimeOffset Now { get; set; }
            public DateTimeOffset Read() => Now;
        }
    }
}
