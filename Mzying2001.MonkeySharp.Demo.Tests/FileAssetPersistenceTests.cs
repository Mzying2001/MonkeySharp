using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Demo.Persistence;
using Mzying2001.MonkeySharp.Demo.Services;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Demo.Tests
{
    public sealed class FileAssetPersistenceTests
    {
        private static readonly CancellationToken None = CancellationToken.None;
        private sealed class Provider : IUserScriptDependencyProvider, IResourceProvider
        {
            internal int Calls;
            internal ResourceContent Content = new ResourceContent(Encoding.UTF8.GetBytes("requireCode();"), "text/javascript", "utf-8");
            public Task<ResourceContent> GetScriptAsync(UserScriptInstallation installation, UserScriptDependencyDeclaration declaration, CancellationToken token) { Calls++; return Task.FromResult(Content); }
            public Task<ResourceContent> GetAsync(UserScriptInstallation installation, ResourceDeclaration declaration, CancellationToken token) { Calls++; return Task.FromResult(Content); }
        }
        private static async Task<UserScriptRepository> Open(PersistenceTests.Fixture fixture, Provider provider = null)
        {
            var repository = new UserScriptRepository(fixture.Persistence, preparer: new UserScriptPreparer(provider, provider));
            await repository.InitializeAsync(None); return repository;
        }
        private static string Source => PersistenceTests.Source.Replace("// @grant none", "// @grant none\n// @require https://cdn.example/a\n// @resource text https://cdn.example/r");

        [Fact]
        public async Task DatabaseStoresOnlyDescriptorsAndOfflineReloadUsesExactContent()
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            {
                var provider = new Provider(); var repository = await Open(fixture, provider);
                var script = await repository.InstallAsync(Source, "origin", true, None);
                Assert.Equal(2, provider.Calls);
                var path = Assert.Single(Directory.GetFiles(Path.Combine(fixture.Paths.UserScriptCacheDirectory, "objects")));
                Assert.Equal(provider.Content.Bytes, File.ReadAllBytes(path));
                var restored = await Open(fixture);
                Assert.Equal(script.PreparedRevision.Source, (await restored.GetAsync(script.ScriptKey, None)).PreparedRevision.Source);
                using (var connection = fixture.Database.OpenReady())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
                    using (var reader = command.ExecuteReader())
                        while (reader.Read()) Assert.DoesNotContain("prepared", reader.GetString(0));
                    command.CommandText = "PRAGMA table_info(scripts)";
                    using (var reader = command.ExecuteReader())
                        while (reader.Read()) Assert.DoesNotContain(reader.GetString(1), new[] { "source", "source_path", "payload" });
                    command.CommandText = "PRAGMA table_info(script_assets)";
                    using (var reader = command.ExecuteReader())
                        while (reader.Read()) Assert.DoesNotContain(reader.GetString(1), new[] { "bytes", "text", "payload", "base64" });
                }
            }
        }

        [Fact]
        public async Task PreserveDoesNotRewriteBomNewlinesOrModificationTime()
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            {
                var provider = new Provider(); var repository = await Open(fixture, provider);
                var script = await repository.InstallAsync(Source, null, true, None); var path = fixture.SourcePath(script.ScriptKey.ToString());
                var external = Source.Replace("\n", "\r\n") + "external();\r\n";
                var bytes = new byte[] { 0xef, 0xbb, 0xbf }.Concat(Encoding.UTF8.GetBytes(external)).ToArray();
                File.WriteAllBytes(path, bytes); var time = File.GetLastWriteTimeUtc(path);
                script = await repository.ReloadAsync(script.ScriptKey, None);
                Assert.True(script.CanExecute); Assert.Equal(external, script.Definition.Source);
                Assert.Equal(2, provider.Calls); Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Equal(time, File.GetLastWriteTimeUtc(path));
                await repository.RefreshAssetsAsync(script.ScriptKey, script.RevisionId, None);
                Assert.Equal(4, provider.Calls); Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Equal(time, File.GetLastWriteTimeUtc(path));
            }
        }

        [Fact]
        public async Task InvalidUtf8CanBeConditionallyRepairedButLockedSourceHasNoVersion()
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            {
                var provider = new Provider(); var repository = await Open(fixture, provider);
                var script = await repository.InstallAsync(Source, null, true, None); var path = fixture.SourcePath(script.ScriptKey.ToString());
                File.WriteAllBytes(path, new byte[] { 0xff });
                var loaded = await fixture.Persistence.LoadAsync(script.ScriptKey, None);
                Assert.NotNull(loaded.SourceVersion); Assert.Null(loaded.Record.Source);
                script = await repository.ReloadAsync(script.ScriptKey, None);
                Assert.True((await repository.UpdateIfUnchangedAsync(script.ScriptKey, script.RevisionId, Source, null, None)).CanExecute);
                Assert.Equal(2, provider.Calls);
                using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    loaded = await fixture.Persistence.LoadAsync(script.ScriptKey, None);
                    Assert.Null(loaded.SourceVersion); Assert.Equal(2, loaded.Record.Assets.Count); Assert.All(loaded.Record.Assets, asset => Assert.NotNull(asset.Content));
                }
            }
        }

        [Fact]
        public async Task SharedObjectsAreImmutableAndCollectionProtectsDisabledReferences()
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            {
                var provider = new Provider(); var repository = await Open(fixture, provider);
                var first = await repository.InstallAsync(Source, null, true, None); var second = await repository.InstallAsync(Source, null, false, None);
                var directory = Path.Combine(fixture.Paths.UserScriptCacheDirectory, "objects"); var original = Assert.Single(Directory.GetFiles(directory));
                var originalTime = File.GetLastWriteTimeUtc(original);
                await repository.UpdateAsync(first.ScriptKey, Source + "body();", null, None);
                Assert.Equal(originalTime, File.GetLastWriteTimeUtc(original));
                provider.Content = new ResourceContent(Encoding.UTF8.GetBytes("updatedAsset();"), "text/javascript");
                first = await repository.GetAsync(first.ScriptKey, None);
                await repository.RefreshAssetsAsync(first.ScriptKey, first.RevisionId, None);
                Assert.Equal(2, Directory.GetFiles(directory).Length);
                await repository.RemoveAsync(first.ScriptKey, None);
                Assert.Equal(original, Assert.Single(Directory.GetFiles(directory)));
                Assert.True((await (await Open(fixture)).GetAsync(second.ScriptKey, None)).IsReady);
                await repository.RemoveAsync(second.ScriptKey, None);
                Assert.Empty(Directory.GetFiles(directory));
            }
        }

        [Fact]
        public async Task MissingCacheDisabledStartupAndCorruptReferencesHaveDistinctResults()
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            {
                var provider = new Provider(); var repository = await Open(fixture, provider);
                var script = await repository.InstallAsync(Source, null, false, None);
                var directory = Path.Combine(fixture.Paths.UserScriptCacheDirectory, "objects"); File.Delete(Assert.Single(Directory.GetFiles(directory)));
                provider.Calls = 0; var restarted = await Open(fixture, provider);
                Assert.False((await restarted.GetAsync(script.ScriptKey, None)).IsReady); Assert.Equal(0, provider.Calls);
                script = await restarted.ReloadAsync(script.ScriptKey, None);
                Assert.True(script.IsReady); Assert.False(script.IsEnabled); Assert.Equal(2, provider.Calls);
                var orphan = new string('a', 64); File.WriteAllText(Path.Combine(directory, orphan), "unreferenced");
                using (var connection = fixture.Database.OpenReady())
                using (var command = connection.CreateCommand())
                { command.CommandText = "UPDATE script_assets SET content_hash='invalid'"; command.ExecuteNonQuery(); }
                await fixture.Persistence.CollectAsync(None);
                Assert.True(File.Exists(Path.Combine(directory, orphan)));
                var loaded = await fixture.Persistence.LoadAsync(script.ScriptKey, None);
                Assert.Null(loaded.SourceVersion); Assert.Contains(loaded.Diagnostics, item => item.Code == "MSR423_PERSISTENCE_FAILED");
            }
        }

        [Fact]
        public void SourceCodecRoundTripsOneLogicalBomWithoutTrimmingAllLeadingCharacters()
        {
            var logical = "\ufeff" + PersistenceTests.Source;
            var bytes = UserScriptSourceFile.Encode(logical);
            Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf, 0xef, 0xbb, 0xbf }, bytes.Take(6));
            Assert.Equal(logical, UserScriptSourceFile.Decode(bytes));
        }
    }
}
