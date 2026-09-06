using Microsoft.Data.Sqlite;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Storage;
using Mzying2001.MonkeySharp.Demo.Persistence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Demo.Tests
{
    public sealed class PersistenceTests
    {
        internal const string Source = "// ==UserScript==\n// @name sqlite\n// @match https://example.com/*\n// @grant none\n// ==/UserScript==\n";
        [Fact]
        public async Task RepositoryRestoresFilesKeysAndOrderAndDeletesSource()
        {
            using (var fixture = await Fixture.Create())
            {
                var first = await fixture.Repository.InstallAsync(Source, "first", true, CancellationToken.None);
                var second = await fixture.Repository.InstallAsync(Source, "second", false, CancellationToken.None);
                await fixture.Repository.UpdateAsync(first.ScriptKey, Source.Replace("sqlite", "changed"), "updated", CancellationToken.None);
                var restored = new PersistentUserScriptRepository(fixture.Persistence);
                await restored.InitializeAsync(CancellationToken.None);
                var scripts = await restored.GetSnapshotAsync(CancellationToken.None);
                Assert.Equal(new[] { first.ScriptKey, second.ScriptKey }, scripts.Select(item => item.ScriptKey));
                Assert.Equal("changed", scripts[0].Definition.Metadata.Name);
                Assert.Equal(first.InstalledAt, scripts[0].InstalledAt);
                await restored.RemoveAsync(first.ScriptKey, CancellationToken.None);
                Assert.False(File.Exists(fixture.SourcePath(first.ScriptKey.ToString())));
            }
        }

        [Fact]
        public async Task DatabaseFailureDoesNotReplaceOldSource()
        {
            using (var fixture = await Fixture.Create())
            {
                var installation = await fixture.Repository.InstallAsync(Source, "first", true, CancellationToken.None);
                using (var connection = fixture.Database.OpenReady())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "CREATE TRIGGER reject_update BEFORE UPDATE ON scripts BEGIN SELECT RAISE(ABORT, 'injected failure'); END;";
                    command.ExecuteNonQuery();
                }
                await Assert.ThrowsAsync<SqliteException>(() => fixture.Repository.UpdateAsync(installation.ScriptKey, Source.Replace("sqlite", "changed"), "new", CancellationToken.None));
                Assert.Equal(Source, File.ReadAllText(fixture.SourcePath(installation.ScriptKey.ToString())));
                Assert.Equal("sqlite", (await fixture.Repository.GetAsync(installation.ScriptKey, CancellationToken.None)).Definition.Metadata.Name);
            }
        }

        [Fact]
        public async Task MissingSourceProducesVisibleDisabledRecordAndDiagnostic()
        {
            using (var fixture = await Fixture.Create())
            {
                var script = await fixture.Repository.InstallAsync(Source, "first", true, CancellationToken.None);
                File.Delete(fixture.SourcePath(script.ScriptKey.ToString()));
                var restored = new PersistentUserScriptRepository(fixture.Persistence);
                await restored.InitializeAsync(CancellationToken.None);
                var unavailable = Assert.Single(await restored.GetSnapshotAsync(CancellationToken.None));
                Assert.False(unavailable.IsEnabled);
                Assert.False(unavailable.Definition.ParseResult.CanEnable);
                Assert.Contains(fixture.Messages, message => message.Contains("unavailable"));
            }
        }

        [Fact]
        public async Task InterruptedFileReplacementRecoversDatabaseCommittedRevision()
        {
            using (var fixture = await Fixture.Create())
            {
                var script = await fixture.Repository.InstallAsync(Source, "first", true, CancellationToken.None);
                var path = fixture.SourcePath(script.ScriptKey.ToString());
                File.Copy(path, path + ".bak");
                File.WriteAllText(path, Source.Replace("sqlite", "uncommitted"));
                var records = await fixture.Persistence.LoadAsync(CancellationToken.None);
                Assert.Equal(Source, records[0].Source);
                Assert.False(File.Exists(path + ".bak"));
            }
        }

        [Fact]
        public async Task ValuesAreDurableCanonicalPartitionedAndHaveOrderedNotifications()
        {
            using (var fixture = await Fixture.Create())
            using (var store = new SqliteUserScriptValueStore(fixture.Database))
            {
                var changes = new List<UserScriptValueChangedEventArgs>();
                store.ValueChanged += (sender, args) => changes.Add(args);
                await store.SetAsync("one", "key", "{ \"value\" : 2 }", CancellationToken.None);
                Assert.False((await store.GetAsync("two", "key", CancellationToken.None)).Exists);
                await Task.WhenAll(Enumerable.Range(0, 30).Select(index => store.SetAsync("one", "key", index.ToString(), CancellationToken.None)));
                Assert.Equal(Enumerable.Range(1, 31).Select(index => (long)index), changes.Select(change => change.Sequence));
                for (var index = 1; index < changes.Count; index++) Assert.Equal(changes[index - 1].NewValue.JsonValue, changes[index].OldValue.JsonValue);
                using (var restored = new SqliteUserScriptValueStore(fixture.Database))
                    Assert.Equal(changes.Last().NewValue.JsonValue, (await restored.GetAsync("one", "key", CancellationToken.None)).JsonValue);
                Assert.True(await store.DeleteAsync("one", "key", CancellationToken.None));
                Assert.False(await store.DeleteAsync("one", "key", CancellationToken.None));
                Assert.Empty(await store.ListKeysAsync("one", CancellationToken.None));
            }
        }

        [Fact]
        public async Task LockedSourceAbortsDeletionAndInterruptedRemovalIsRestored()
        {
            using (var fixture = await Fixture.Create())
            {
                var installation = await fixture.Repository.InstallAsync(Source, "origin", true, CancellationToken.None);
                var path = fixture.SourcePath(installation.ScriptKey.ToString());
                using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    await Assert.ThrowsAsync<IOException>(() => fixture.Repository.RemoveAsync(installation.ScriptKey, CancellationToken.None));
                Assert.Equal(installation.ScriptKey, Assert.Single(await fixture.Repository.GetSnapshotAsync(CancellationToken.None)).ScriptKey);
                File.Move(path, path + ".deleted");
                var records = await fixture.Persistence.LoadAsync(CancellationToken.None);
                Assert.Equal(Source, Assert.Single(records).Source);
                Assert.True(File.Exists(path));
                await fixture.Repository.RemoveAsync(installation.ScriptKey, CancellationToken.None);
                Assert.False(File.Exists(path));
            }
        }

        [Fact]
        public async Task TabStateIsScopedToLiveTabIdsNotDocumentIds()
        {
            using (var fixture = await Fixture.Create())
            {
                var script = await fixture.Repository.InstallAsync(Source, "first", true, CancellationToken.None);
                var states = new SqliteTabStateService(fixture.Database);
                var first = states.ForTab("first");
                var second = states.ForTab("second");
                await first.SaveAsync(script.ScriptKey, null, "{\"test\":1}", CancellationToken.None);
                Assert.Equal("{}", await second.GetAsync(script.ScriptKey, null, CancellationToken.None));
                Assert.Contains("first", (await second.GetAllAsync(script.ScriptKey, CancellationToken.None)).Keys);
                await Assert.ThrowsAsync<ArgumentException>(() => first.SaveAsync(script.ScriptKey, null, "[]", CancellationToken.None));
                states.CloseTab("first");
                Assert.Empty(await second.GetAllAsync(script.ScriptKey, CancellationToken.None));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.SaveAsync(script.ScriptKey, null, "{}", CancellationToken.None));
            }
        }

        internal sealed class Fixture : IDisposable
        {
            private readonly string _root = Path.Combine(Path.GetTempPath(), "MonkeySharpDemoTests", Guid.NewGuid().ToString("N"));
            public AppDataPaths Paths { get; private set; }
            public SqliteDatabase Database { get; private set; }
            public SqliteUserScriptRepositoryPersistence Persistence { get; private set; }
            public PersistentUserScriptRepository Repository { get; private set; }
            public List<string> Messages { get; } = new List<string>();
            public static async Task<Fixture> Create()
            {
                var fixture = new Fixture();
                fixture.Paths = new AppDataPaths(fixture._root); fixture.Paths.EnsureCreated();
                fixture.Database = new SqliteDatabase(fixture.Paths.DatabasePath);
                await fixture.Database.InitializeAsync(CancellationToken.None);
                fixture.Persistence = new SqliteUserScriptRepositoryPersistence(fixture.Database, fixture.Paths, fixture.Messages.Add);
                fixture.Repository = new PersistentUserScriptRepository(fixture.Persistence);
                await fixture.Repository.InitializeAsync(CancellationToken.None);
                return fixture;
            }
            public string SourcePath(string key) => Path.Combine(Paths.UserScriptsDirectory, key + ".user.js");
            public void Dispose()
            {
                Database.Dispose();
                var root = Path.GetFullPath(_root);
                var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MonkeySharpDemoTests")) + Path.DirectorySeparatorChar;
                if (root.StartsWith(parent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
