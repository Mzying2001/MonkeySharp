using Microsoft.Data.Sqlite;
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
    public sealed class SourceRecoveryTests
    {
        private static readonly CancellationToken None = CancellationToken.None;
        private static readonly byte[] NewBytes = Encoding.UTF8.GetBytes(PersistenceTests.Source + "new();");
        private static UserScriptSourceFiles Files(PersistenceTests.Fixture fixture) => new UserScriptSourceFiles(fixture.Paths.UserScriptsDirectory, fixture.Messages.Add);
        private static string Commit(PersistenceTests.Fixture fixture, ScriptKey key)
        {
            using (var connection = fixture.Database.OpenReady())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT commit_id FROM scripts WHERE script_key=$key"; command.Parameters.AddWithValue("$key", key.ToString());
                return command.ExecuteScalar()?.ToString();
            }
        }
        private static void SetCommit(PersistenceTests.Fixture fixture, ScriptKey key, string commit)
        {
            using (var connection = fixture.Database.OpenReady())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE scripts SET commit_id=$commit WHERE script_key=$key";
                command.Parameters.AddWithValue("$commit", commit); command.Parameters.AddWithValue("$key", key.ToString()); command.ExecuteNonQuery();
            }
        }

        [Theory]
        [InlineData(false, "old")]
        [InlineData(false, "new")]
        [InlineData(false, "missing")]
        [InlineData(true, "old")]
        [InlineData(true, "new")]
        [InlineData(true, "missing")]
        public async Task ReplacementRecoveryUsesCommitIdAndActualPreviousFileHash(bool committed, string main)
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            {
                var script = await fixture.Repository.InstallAsync(PersistenceTests.Source, null, true, None);
                var files = Files(fixture); var path = files.PathFor(script.ScriptKey);
                var externalOld = PersistenceTests.Source + "externalOld();";
                File.WriteAllBytes(path, Encoding.UTF8.GetBytes(externalOld)); // Intentionally differs from the database hash.
                var journal = files.Stage(script.ScriptKey, "replace", Commit(fixture, script.ScriptKey), NewBytes);
                if (!committed || main == "new") files.Switch(script.ScriptKey, journal);
                if (committed) SetCommit(fixture, script.ScriptKey, journal.CommitId);
                if (main == "old" && !committed) File.WriteAllBytes(path, Encoding.UTF8.GetBytes(externalOld));
                if (main == "missing") File.Delete(path);
                var loaded = await fixture.Persistence.LoadAsync(script.ScriptKey, None);
                Assert.NotNull(loaded.SourceVersion);
                Assert.Equal(committed ? Encoding.UTF8.GetString(NewBytes) : externalOld, loaded.Record.Source);
                Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(path), "transaction.json")));
            }
        }

        [Fact]
        public async Task CommittedReplacementCannotPretendAnOldBackupIsTheNewSource()
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            {
                var script = await fixture.Repository.InstallAsync(PersistenceTests.Source, null, true, None);
                var files = Files(fixture); var path = files.PathFor(script.ScriptKey);
                var journal = files.Stage(script.ScriptKey, "replace", Commit(fixture, script.ScriptKey), NewBytes);
                files.Switch(script.ScriptKey, journal);
                SetCommit(fixture, script.ScriptKey, journal.CommitId);
                File.Delete(path); // pending was consumed; only the old backup survives.
                var loaded = await fixture.Persistence.LoadAsync(script.ScriptKey, None);
                Assert.Null(loaded.SourceVersion);
                Assert.Null(loaded.Record.Source);
                Assert.Contains(loaded.Diagnostics, item => item.Code == "MSR424_RECOVERY_CONFLICT");
                Assert.True(File.Exists(path + ".backup"));
                Assert.False(File.Exists(path));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task UnknownMainContentIsPreservedAndOnlyThisInstallationIsBlocked(bool committed)
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            {
                var script = await fixture.Repository.InstallAsync(PersistenceTests.Source, null, true, None);
                var good = await fixture.Repository.InstallAsync(PersistenceTests.Source, null, true, None);
                var files = Files(fixture); var path = files.PathFor(script.ScriptKey);
                var journal = files.Stage(script.ScriptKey, "replace", Commit(fixture, script.ScriptKey), NewBytes);
                files.Switch(script.ScriptKey, journal);
                if (committed) SetCommit(fixture, script.ScriptKey, journal.CommitId);
                File.WriteAllText(path, "unknown third content");
                var reloaded = await fixture.Repository.ReloadAsync(script.ScriptKey, None);
                Assert.False(reloaded.IsReady);
                Assert.Contains(reloaded.Diagnostics, item => item.Code == "MSR424_RECOVERY_CONFLICT");
                Assert.Equal("unknown third content", File.ReadAllText(path));
                Assert.True(File.Exists(path + ".backup"));
                Assert.True((await fixture.Repository.ReloadAsync(good.ScriptKey, None)).CanExecute);
                await fixture.Repository.SetEnabledAsync(script.ScriptKey, false, None); // No file or recovery access.
                await Assert.ThrowsAsync<UserScriptPersistenceException>(() => fixture.Repository.RemoveAsync(script.ScriptKey, None));
            }
        }

        [Fact]
        public async Task MissingSourceRepairRollsBackToMissingInsteadOfBecomingAnInsertion()
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            {
                var script = await fixture.Repository.InstallAsync(PersistenceTests.Source, null, true, None);
                var files = Files(fixture); var path = files.PathFor(script.ScriptKey); File.Delete(path);
                var journal = files.Stage(script.ScriptKey, "replace", Commit(fixture, script.ScriptKey), NewBytes);
                files.Switch(script.ScriptKey, journal);
                var loaded = await fixture.Persistence.LoadAsync(script.ScriptKey, None);
                Assert.Equal("missing", loaded.SourceVersion); Assert.Null(loaded.Record.Source); Assert.False(File.Exists(path));
                var unavailable = await fixture.Repository.ReloadAsync(script.ScriptKey, None);
                Assert.True((await fixture.Repository.UpdateIfUnchangedAsync(script.ScriptKey, unavailable.RevisionId, PersistenceTests.Source, null, None)).CanExecute);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task OrphanInstallationMarkerDoesNotRegisterOrDeleteUnknownFiles(bool unknown)
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            {
                var key = ScriptKey.Parse(Guid.NewGuid().ToString()); var files = Files(fixture); var path = files.PathFor(key);
                var journal = files.Stage(key, "install", null, NewBytes); files.Switch(key, journal);
                if (unknown) File.WriteAllText(path, "unregistered user file");
                Assert.Empty(await fixture.Persistence.LoadAllAsync(None));
                Assert.Equal(unknown, File.Exists(path));
                if (unknown) { Assert.Equal("unregistered user file", File.ReadAllText(path)); Assert.NotEmpty(fixture.Messages); }
                else Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(path), "transaction.json")));
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task DeletionRecoveryUsesDatabasePresenceAndPreservesUnregisteredNewSource(bool committed, bool missingBefore)
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            {
                var script = await fixture.Repository.InstallAsync(PersistenceTests.Source, null, true, None);
                var files = Files(fixture); var path = files.PathFor(script.ScriptKey);
                if (missingBefore) File.Delete(path);
                var journal = files.Stage(script.ScriptKey, "delete", Commit(fixture, script.ScriptKey), null); files.Switch(script.ScriptKey, journal);
                if (committed)
                {
                    using (var connection = fixture.Database.OpenReady())
                    using (var command = connection.CreateCommand())
                    { command.CommandText = "DELETE FROM scripts WHERE script_key=$key"; command.Parameters.AddWithValue("$key", script.ScriptKey.ToString()); command.ExecuteNonQuery(); }
                    File.WriteAllText(path, "unregistered new source");
                    Assert.Empty(await fixture.Persistence.LoadAllAsync(None));
                    Assert.Equal("unregistered new source", File.ReadAllText(path));
                }
                else
                {
                    var loaded = await fixture.Persistence.LoadAsync(script.ScriptKey, None);
                    Assert.Equal(missingBefore ? null : PersistenceTests.Source, loaded.Record.Source);
                    Assert.Equal(!missingBefore, File.Exists(path));
                }
                Assert.False(File.Exists(path + ".deleted"));
            }
        }

        [Fact]
        public async Task InvalidMarkerOrUnexpectedCommitPreservesRecoveryEvidence()
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            {
                var script = await fixture.Repository.InstallAsync(PersistenceTests.Source, null, true, None);
                var files = Files(fixture); var path = files.PathFor(script.ScriptKey);
                var journal = files.Stage(script.ScriptKey, "replace", Commit(fixture, script.ScriptKey), NewBytes);
                SetCommit(fixture, script.ScriptKey, Guid.NewGuid().ToString());
                Assert.Null((await fixture.Persistence.LoadAsync(script.ScriptKey, None)).SourceVersion);
                var marker = Path.Combine(Path.GetDirectoryName(path), "transaction.json");
                File.WriteAllText(marker, "{bad-json");
                var loaded = await fixture.Persistence.LoadAsync(script.ScriptKey, None);
                Assert.Null(loaded.SourceVersion); Assert.True(File.Exists(path + ".pending"));
                Assert.Equal(PersistenceTests.Source, File.ReadAllText(path));
            }
        }

        [Theory]
        [InlineData("objects-written")]
        [InlineData("marker-written")]
        [InlineData("rows-written")]
        [InlineData("source-switched")]
        public async Task FailureBeforeCommitRestoresSourceAndRepositorySnapshot(string phase)
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            {
                var script = await fixture.Repository.InstallAsync(PersistenceTests.Source, null, true, None);
                fixture.Persistence.Checkpoint = point => { if (point == phase) throw new IOException("injected " + phase); };
                await Assert.ThrowsAsync<UserScriptPersistenceException>(() => fixture.Repository.UpdateAsync(script.ScriptKey, Encoding.UTF8.GetString(NewBytes), null, None));
                Assert.Equal(PersistenceTests.Source, File.ReadAllText(fixture.SourcePath(script.ScriptKey.ToString())));
                Assert.Same(script, await fixture.Repository.GetAsync(script.ScriptKey, None));
            }
        }

        [Fact]
        public async Task RollbackDoesNotOverwriteExternalContentAndRetainsMarker()
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            {
                var script = await fixture.Repository.InstallAsync(PersistenceTests.Source, null, true, None); var path = fixture.SourcePath(script.ScriptKey.ToString());
                fixture.Persistence.Checkpoint = phase => { if (phase == "source-switched") { File.WriteAllText(path, "external during commit"); throw new IOException("rollback required"); } };
                var failure = await Assert.ThrowsAsync<UserScriptPersistenceException>(() => fixture.Repository.UpdateAsync(script.ScriptKey, Encoding.UTF8.GetString(NewBytes), null, None));
                Assert.IsType<AggregateException>(failure.InnerException);
                Assert.Equal("external during commit", File.ReadAllText(path));
                Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(path), "transaction.json")));
                Assert.Same(script, await fixture.Repository.GetAsync(script.ScriptKey, None));
            }
        }

        [Fact]
        public async Task SourceSwitchCancellationAndCleanupFailureCannotUndoCommittedSuccess()
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            using (var cancellation = new CancellationTokenSource())
            {
                var script = await fixture.Repository.InstallAsync(PersistenceTests.Source, null, true, None); var path = fixture.SourcePath(script.ScriptKey.ToString());
                FileStream lockedBackup = null;
                try
                {
                    fixture.Persistence.Checkpoint = phase => { if (phase == "source-switched") { lockedBackup = new FileStream(path + ".backup", FileMode.Open, FileAccess.Read, FileShare.Read); cancellation.Cancel(); } };
                    var updated = await fixture.Repository.UpdateAsync(script.ScriptKey, Encoding.UTF8.GetString(NewBytes), null, cancellation.Token);
                    Assert.True(updated.CanExecute); Assert.Equal(Encoding.UTF8.GetString(NewBytes), updated.Definition.Source);
                    Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(path), "transaction.json")));
                }
                finally { lockedBackup?.Dispose(); }
                var loaded = await fixture.Persistence.LoadAsync(script.ScriptKey, None);
                Assert.NotNull(loaded.SourceVersion); Assert.Equal(Encoding.UTF8.GetString(NewBytes), loaded.Record.Source);
                Assert.False(File.Exists(path + ".backup"));
            }
        }
    }
}
