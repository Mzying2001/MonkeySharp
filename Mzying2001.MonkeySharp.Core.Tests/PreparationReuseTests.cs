using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Security;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class PreparationReuseTests
    {
        private static readonly CancellationToken None = CancellationToken.None;
        private static string Script(string declarations) => MetadataAndMatchingTests.Script("// @name reuse\n// @match https://example.com/*\n" + declarations);
        private sealed class Provider : IUserScriptDependencyProvider, IResourceProvider
        {
            internal int Calls;
            internal ResourceContent Content = new ResourceContent(Encoding.UTF8.GetBytes("hello"), "text/plain", "utf-8");
            public Task<ResourceContent> GetScriptAsync(UserScriptInstallation installation, UserScriptDependencyDeclaration declaration, CancellationToken token) { Calls++; return Task.FromResult(Content); }
            public Task<ResourceContent> GetAsync(UserScriptInstallation installation, ResourceDeclaration declaration, CancellationToken token) { Calls++; return Task.FromResult(Content); }
        }

        [Fact]
        public async Task BodyReorderingRenamingAndDeletionReuseOnlyThisInstallationsAssets()
        {
            var provider = new Provider(); var storage = new InMemoryUserScriptPersistence();
            var repository = new UserScriptRepository(storage, preparer: new UserScriptPreparer(provider, provider));
            await repository.InitializeAsync(None);
            var installation = await repository.InstallAsync(Script("// @require https://cdn.example/a\n// @require https://cdn.example/b\n// @resource old https://cdn.example/r"), null, true, None);
            Assert.Equal(3, provider.Calls);
            installation = await repository.UpdateAsync(installation.ScriptKey, Script("// @require https://cdn.example/b\n// @require https://cdn.example/a\n// @resource renamed https://cdn.example/r") + "body();", null, None);
            Assert.Equal(3, provider.Calls);
            Assert.True(installation.PreparedRevision.Resources.ContainsKey("renamed"));
            installation = await repository.UpdateAsync(installation.ScriptKey, Script("// @require https://cdn.example/b"), null, None);
            Assert.Equal(3, provider.Calls);
            Assert.Single((await storage.LoadAsync(installation.ScriptKey, None)).Record.Assets);
            await repository.InstallAsync(Script("// @require https://cdn.example/b"), null, true, None);
            Assert.Equal(4, provider.Calls);
            await repository.RefreshAssetsAsync(installation.ScriptKey, installation.RevisionId, None);
            Assert.Equal(5, provider.Calls);
        }

        [Fact]
        public async Task DuplicateUrlsDownloadOnceButEveryIntegrityDeclarationIsChecked()
        {
            var provider = new Provider(); var repository = TestRepository.Create(preparer: new UserScriptPreparer(provider, provider));
            var declaration = "// @require https://cdn.example/a\n";
            var installed = await repository.InstallAsync(Script(declaration + declaration), null, true, None);
            Assert.Equal(1, provider.Calls);
            Assert.Equal(2, installed.PreparedRevision.Dependencies.Count);
            await Assert.ThrowsAsync<ResourceIntegrityException>(() => repository.UpdateAsync(installed.ScriptKey,
                Script(declaration + "// @require https://cdn.example/a#sha256=" + new string('0', 64)), null, None));
            Assert.Same(installed, await repository.GetAsync(installed.ScriptKey, None));
        }

        [Fact]
        public async Task ChangedDigestRejectsOldCacheAndFetchesNewContent()
        {
            var provider = new Provider(); var repository = TestRepository.Create(preparer: new UserScriptPreparer(provider, provider));
            var installed = await repository.InstallAsync(Script("// @require https://cdn.example/a"), null, true, None);
            provider.Content = new ResourceContent(Encoding.UTF8.GetBytes("new content"));
            string digest;
            using (var sha = SHA256.Create()) digest = Convert.ToBase64String(sha.ComputeHash(provider.Content.Bytes));
            installed = await repository.UpdateAsync(installed.ScriptKey, Script("// @require https://cdn.example/a#sha256=" + digest), null, None);
            Assert.Equal(2, provider.Calls);
            Assert.Contains("new content", installed.PreparedRevision.Source);
        }

        [Fact]
        public async Task BinaryResourceRoundTripsOfflineButCannotBeARequire()
        {
            var provider = new Provider { Content = new ResourceContent(new byte[] { 0xff }, "application/octet-stream") };
            var storage = new InMemoryUserScriptPersistence();
            var repository = new UserScriptRepository(storage, preparer: new UserScriptPreparer(provider, provider));
            await repository.InitializeAsync(None);
            var installed = await repository.InstallAsync(Script("// @resource binary https://cdn.example/a"), null, true, None);
            var restarted = new UserScriptRepository(storage);
            await restarted.InitializeAsync(None);
            var restored = await restarted.GetAsync(installed.ScriptKey, None);
            Assert.True(restored.CanExecute);
            Assert.Equal(installed.PreparedRevision.Resources["binary"].DecodeText(), restored.PreparedRevision.Resources["binary"].DecodeText());
            Assert.Equal(new byte[] { 0xff }, restored.PreparedRevision.Resources["binary"].Bytes);
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => repository.InstallAsync(Script("// @require https://cdn.example/a"), null, true, None));
        }

        [Fact]
        public async Task DerivedTextAndDuplicateDeclarationsCountAgainstBothBudgetPaths()
        {
            var provider = new Provider { Content = new ResourceContent(new byte[] { 0xff }) };
            var source = Script("// @resource binary https://cdn.example/a");
            var limit = Encoding.UTF8.GetByteCount(source) + 3; // one raw byte plus three replacement-character bytes cannot fit
            var limited = TestRepository.Create(preparer: new UserScriptPreparer(provider, provider, limit));
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => limited.InstallAsync(source, null, true, None));
            var storage = new InMemoryUserScriptPersistence();
            var repository = new UserScriptRepository(storage, preparer: new UserScriptPreparer(provider, provider));
            await repository.InitializeAsync(None);
            var installed = await repository.InstallAsync(source, null, true, None);
            var restarted = new UserScriptRepository(storage, preparer: new UserScriptPreparer(maxContentBytes: limit));
            await restarted.InitializeAsync(None);
            Assert.Contains((await restarted.GetAsync(installed.ScriptKey, None)).Diagnostics, item => item.Code == "MSR411_PREPARED_REVISION_LIMIT");
            var twice = Script("// @require https://cdn.example/a\n// @require https://cdn.example/a");
            provider.Content = new ResourceContent(Encoding.UTF8.GetBytes("1234"));
            limited = TestRepository.Create(preparer: new UserScriptPreparer(provider, provider, Encoding.UTF8.GetByteCount(twice) + 8));
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => limited.InstallAsync(twice, null, true, None));
        }
    }
}
