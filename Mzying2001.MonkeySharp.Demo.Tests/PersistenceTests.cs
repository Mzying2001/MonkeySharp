using Microsoft.Data.Sqlite;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Storage;
using Mzying2001.MonkeySharp.Demo.Persistence;
using static Mzying2001.MonkeySharp.Core.Storage.UserScriptValue;
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
        [Fact]
        public async Task RememberedPermissionDecisionsPreserveExactScopesAndCanBeRevoked()
        {
            using (var fixture = await Fixture.Create())
            {
                var store = new SqlitePermissionDecisionStore(fixture.Database);
                var first = new Mzying2001.MonkeySharp.Core.Permissions.UserScriptPermissionScope("installation-one", "GM.xmlHttpRequest", "https://example.com");
                var other = new Mzying2001.MonkeySharp.Core.Permissions.UserScriptPermissionScope("installation-two", "GM.xmlHttpRequest", "https://example.com");
                await store.SetAsync(first, Mzying2001.MonkeySharp.Core.Permissions.PermissionDecision.Allow, CancellationToken.None);
                await store.SetAsync(other, Mzying2001.MonkeySharp.Core.Permissions.PermissionDecision.Deny, CancellationToken.None);
                var reopened = new SqlitePermissionDecisionStore(fixture.Database);
                Assert.Equal(Mzying2001.MonkeySharp.Core.Permissions.PermissionDecision.Allow, await reopened.GetAsync(first, CancellationToken.None));
                Assert.Null(await reopened.GetAsync(new Mzying2001.MonkeySharp.Core.Permissions.UserScriptPermissionScope(first.ScriptKey, "GM.cookie", first.Target), CancellationToken.None));
                Assert.Null(await reopened.GetAsync(new Mzying2001.MonkeySharp.Core.Permissions.UserScriptPermissionScope(first.ScriptKey, first.Kind, "https://example.com:8443"), CancellationToken.None));
                await reopened.RevokeAsync(first.ScriptKey, CancellationToken.None);
                Assert.Null(await store.GetAsync(first, CancellationToken.None));
                Assert.Equal(Mzying2001.MonkeySharp.Core.Permissions.PermissionDecision.Deny, await store.GetAsync(other, CancellationToken.None));
                await reopened.RevokeAsync(null, CancellationToken.None);
                Assert.Empty(await reopened.ListAsync(CancellationToken.None));
            }
        }

        [Fact]
        public async Task AtomicValueBatchRollsBackAllKeysAndSequenceOnSqliteFailure()
        {
            using (var fixture = await Fixture.Create())
            using (var store = new SqliteUserScriptValueStore(fixture.Database))
            {
                using (var connection = fixture.Database.OpenReady())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "CREATE TRIGGER reject_batch BEFORE INSERT ON script_values WHEN NEW.value_key='fail' BEGIN SELECT RAISE(ABORT, 'batch failure'); END;";
                    command.ExecuteNonQuery();
                }
                var events = new List<UserScriptValueChangedEventArgs>();
                store.ValueChanged += (_, change) => events.Add(change);
                var batch = new[] { new UserScriptValueMutation("first", Undefined), new UserScriptValueMutation("fail", FromEncoded("n2")), new UserScriptValueMutation("last", FromEncoded("n3")) };
                var origin = new UserScriptValueMutationOrigin("request", "execution", "GM.setValues");
                await Assert.ThrowsAsync<SqliteException>(() => store.ApplyAsync("script", batch, origin, CancellationToken.None));
                var snapshot = await store.GetSnapshotAsync("script", CancellationToken.None);
                Assert.Empty(snapshot);
                Assert.Equal(0, snapshot.Sequence);
                Assert.Empty(events);
                using (var connection = fixture.Database.OpenReady())
                using (var command = connection.CreateCommand())
                { command.CommandText = "DROP TRIGGER reject_batch"; command.ExecuteNonQuery(); }
                await store.SetAsync("other", "unrelated", Undefined, CancellationToken.None);
                await store.ApplyAsync("script", batch, origin, CancellationToken.None);
                snapshot = await store.GetSnapshotAsync("script", CancellationToken.None);
                Assert.Equal(new[] { "first", "fail", "last" }, snapshot.Select(item => item.Key));
                Assert.Equal(3, snapshot.Sequence);
                var replay = await store.GetChangesAsync("script", 0, CancellationToken.None);
                Assert.True(replay.Complete);
                Assert.Equal(new long[] { 1, 2, 3 }, replay.Changes.Select(change => change.Sequence));
                Assert.All(replay.Changes, change => { Assert.Equal("request", change.MutationId); Assert.Equal("execution", change.OriginExecutionId); Assert.Equal("GM.setValues", change.MutationMethod); });
                Assert.False((await store.GetChangesAsync("script", 4, CancellationToken.None)).Complete);
                await store.SetAsync("script", "first", Undefined, CancellationToken.None);
                await store.DeleteAsync("script", "absent", CancellationToken.None);
                Assert.Equal(3, (await store.GetSnapshotAsync("script", CancellationToken.None)).Sequence);
            }
        }

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
                await store.SetAsync("one", "key", FromJson("{ \"value\" : 2 }"), CancellationToken.None);
                Assert.False((await store.GetAsync("two", "key", CancellationToken.None)).Exists);
                await Task.WhenAll(Enumerable.Range(0, 30).Select(index => store.SetAsync("one", "key", FromJson(index.ToString()), CancellationToken.None)));
                Assert.Equal(Enumerable.Range(1, 31).Select(index => (long)index), changes.Select(change => change.Sequence));
                for (var index = 1; index < changes.Count; index++) Assert.Equal(changes[index - 1].NewValue.Value.EncodedValue, changes[index].OldValue.Value.EncodedValue);
                using (var restored = new SqliteUserScriptValueStore(fixture.Database))
                    Assert.Equal(changes.Last().NewValue.Value.EncodedValue, (await restored.GetAsync("one", "key", CancellationToken.None)).Value.EncodedValue);
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
                var navigated = states.ForTab("first");
                Assert.Equal("{\"test\":1}", await navigated.GetAsync(script.ScriptKey, null, CancellationToken.None));
                var otherScript = Mzying2001.MonkeySharp.Core.Domain.ScriptKey.Parse(Guid.NewGuid().ToString());
                Assert.Equal("{}", await navigated.GetAsync(otherScript, null, CancellationToken.None));
                Assert.Empty(await navigated.GetAllAsync(otherScript, CancellationToken.None));
                await Assert.ThrowsAsync<ArgumentException>(() => first.SaveAsync(script.ScriptKey, null, "[]", CancellationToken.None));
                states.CloseTab("first");
                Assert.Empty(await second.GetAllAsync(script.ScriptKey, CancellationToken.None));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.SaveAsync(script.ScriptKey, null, "{}", CancellationToken.None));
            }
        }

        [Fact]
        public async Task TypedValuesAndKeyOrderSurviveReopeningTheDatabase()
        {
            using (var fixture = await Fixture.Create())
            {
                using (var store = new SqliteUserScriptValueStore(fixture.Database))
                {
                    var changes = new List<UserScriptValueChangedEventArgs>();
                    store.ValueChanged += (_, args) => changes.Add(args);
                    await store.SetAsync("script", "z", FromEncoded("nNaN"), CancellationToken.None);
                    await store.SetAsync("script", "", Undefined, CancellationToken.None);
                    await store.SetAsync("script", "null", FromJson("null"), CancellationToken.None);
                    await store.SetAsync("script", "", Undefined, CancellationToken.None);
                    await store.SetAsync("script", "infinity", FromEncoded("nInfinity"), CancellationToken.None);
                    await store.SetAsync("script", "negative", FromEncoded("n-Infinity"), CancellationToken.None);
                    Assert.Equal(5, changes.Count);
                    await store.DeleteAsync("script", "z", CancellationToken.None);
                    await store.SetAsync("script", "z", FromEncoded("nNaN"), CancellationToken.None);
                }
                using (var reopened = new SqliteDatabase(fixture.Paths.DatabasePath))
                {
                    await reopened.InitializeAsync(CancellationToken.None);
                    using (var store = new SqliteUserScriptValueStore(reopened))
                    {
                        var values = await store.GetSnapshotAsync("script", CancellationToken.None);
                        Assert.Equal(new[] { "", "null", "infinity", "negative", "z" }, values.Select(item => item.Key));
                        Assert.Equal(new[] { "u", "onull", "nInfinity", "n-Infinity", "nNaN" }, values.Select(item => item.Value.EncodedValue));
                        Assert.True((await store.GetAsync("script", "", CancellationToken.None)).Exists);
                        Assert.False((await store.GetAsync("other", "", CancellationToken.None)).Exists);
                    }
                }
            }
        }

        [Fact]
        public async Task FailedValueCommitDoesNotPublishOrReplaceStoredUndefined()
        {
            using (var fixture = await Fixture.Create())
            using (var store = new SqliteUserScriptValueStore(fixture.Database))
            {
                await store.SetAsync("script", "", Undefined, CancellationToken.None);
                using (var connection = fixture.Database.OpenReady())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "CREATE TRIGGER reject_value BEFORE UPDATE ON script_values BEGIN SELECT RAISE(ABORT, 'injected failure'); END;";
                    command.ExecuteNonQuery();
                }
                var changes = 0;
                store.ValueChanged += (_, __) => changes++;
                await Assert.ThrowsAsync<SqliteException>(() => store.SetAsync("script", "", FromJson("42"), CancellationToken.None));
                Assert.Equal(Undefined, (await store.GetAsync("script", "", CancellationToken.None)).Value);
                Assert.Equal(0, changes);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(3)]
        public async Task UnsupportedDatabasesAreRejectedWithoutChangingTheFile(int version)
        {
            using (var fixture = await Fixture.Create())
            {
                var path = Path.Combine(fixture.Paths.UserScriptsDirectory, "unsupported.db");
                using (var database = new SqliteDatabase(path))
                {
                    using (var connection = database.OpenReady())
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "CREATE TABLE existing_data(value TEXT); INSERT INTO existing_data VALUES('keep'); PRAGMA user_version=" + version;
                        command.ExecuteNonQuery();
                    }
                    var before = File.ReadAllBytes(path);
                    var error = await Assert.ThrowsAsync<InvalidOperationException>(() => database.InitializeAsync(CancellationToken.None));
                    Assert.Contains("rebuild", error.Message);
                    Assert.Equal(before, File.ReadAllBytes(path));
                }
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
