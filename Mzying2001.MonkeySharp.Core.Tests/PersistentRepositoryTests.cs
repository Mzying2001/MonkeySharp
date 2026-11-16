using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class PersistentRepositoryTests
    {
        private const string Source = "// ==UserScript==\n// @name durable\n// @match https://example.com/*\n// @grant none\n// ==/UserScript==\n";
        private static readonly CancellationToken None = CancellationToken.None;

        internal sealed class Storage : IUserScriptPersistence
        {
            internal readonly InMemoryUserScriptPersistence Inner = new InMemoryUserScriptPersistence();
            internal readonly Dictionary<ScriptKey, UserScriptLoadResult> External = new Dictionary<ScriptKey, UserScriptLoadResult>();
            internal Action AfterSave;
            internal Action AfterEnable;
            internal bool Fail;
            internal bool GlobalFailure;
            internal int Saves;
            internal int Loads;
            internal Func<UserScriptLoadResult, UserScriptLoadResult> OnLoad;
            public async Task<IReadOnlyList<UserScriptLoadResult>> LoadAllAsync(CancellationToken token) =>
                (await Inner.LoadAllAsync(token)).Select(item => External.TryGetValue(item.Record.ScriptKey, out var actual) ? actual : item).ToArray();
            public async Task<UserScriptLoadResult> LoadAsync(ScriptKey key, CancellationToken token)
            {
                Loads++;
                var result = External.TryGetValue(key, out var actual) ? actual : await Inner.LoadAsync(key, token);
                return OnLoad == null ? result : OnLoad(result);
            }
            public async Task<string> SaveAsync(UserScriptSaveRequest request, CancellationToken token)
            {
                if (GlobalFailure) throw new InvalidOperationException("database offline");
                if (Fail) throw new UserScriptPersistenceException(request.Record.ScriptKey, "write failed", new IOException());
                if (External.TryGetValue(request.Record.ScriptKey, out var actual))
                {
                    if (request.ExpectedSourceVersion == null || actual.SourceVersion != request.ExpectedSourceVersion) throw new UserScriptSourceConflictException(request.Record.ScriptKey);
                    if (request.SourceWriteMode == SourceWriteMode.Preserve && request.Record.Source != actual.Record.Source) throw new UserScriptSourceConflictException(request.Record.ScriptKey);
                    External.Remove(request.Record.ScriptKey);
                    await Inner.DeleteAsync(request.Record.ScriptKey, None);
                    request = new UserScriptSaveRequest(request.Record, null, SourceWriteMode.Replace);
                }
                var version = await Inner.SaveAsync(request, token);
                Saves++;
                AfterSave?.Invoke();
                return version;
            }
            public async Task SetEnabledAsync(ScriptKey key, bool enabled, DateTimeOffset updated, CancellationToken token)
            {
                if (Fail) throw new UserScriptPersistenceException(key, "write failed", new IOException());
                await Inner.SetEnabledAsync(key, enabled, updated, token);
                if (External.TryGetValue(key, out var loaded)) External[key] = new UserScriptLoadResult(new UserScriptPersistenceRecord(key, loaded.Record.Source, loaded.Record.SourceOrigin, enabled,
                    loaded.Record.InstalledAt, updated, loaded.Record.Assets), loaded.SourceVersion, loaded.SourceChangedSinceSave, loaded.Diagnostics);
                AfterEnable?.Invoke();
            }
            public Task DeleteAsync(ScriptKey key, CancellationToken token)
            {
                if (Fail) throw new UserScriptPersistenceException(key, "delete failed", new IOException());
                External.Remove(key);
                return Inner.DeleteAsync(key, token);
            }
            internal async Task Change(ScriptKey key, string source, bool changed = true, bool missingAssets = false, string version = "external")
            {
                var old = (await Inner.LoadAsync(key, None)).Record;
                External[key] = new UserScriptLoadResult(new UserScriptPersistenceRecord(key, source, old.SourceOrigin, old.IsEnabled, old.InstalledAt, old.UpdatedAt,
                    old.Assets.Select(asset => new UserScriptAsset(asset.Kind, asset.Ordinal, asset.Name, asset.Url, missingAssets ? null : asset.Content))), version, changed,
                    source == null ? new[] { new UserScriptDiagnostic("MSR420_SOURCE_UNAVAILABLE", DiagnosticSeverity.Error, "Missing source", scriptKey: key) } : Array.Empty<UserScriptDiagnostic>());
            }
        }
        private static async Task<UserScriptRepository> Open(IUserScriptPersistence storage, UserScriptPreparer preparer = null)
        {
            var repository = new UserScriptRepository(storage, preparer: preparer);
            await repository.InitializeAsync(None);
            return repository;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task StoresOrderIdentityTimestampsBytesAndEnabledIntent(bool injected)
        {
            IUserScriptPersistence storage = injected ? (IUserScriptPersistence)new Storage() : new InMemoryUserScriptPersistence();
            var repository = await Open(storage);
            var first = await repository.InstallAsync(Source, "first", true, None);
            var second = await repository.InstallAsync(Source, "second", false, None);
            var updated = await repository.UpdateAsync(first.ScriptKey, Source + "updated();", "changed", None);
            var restored = await Open(storage);
            var snapshot = await restored.GetSnapshotAsync(None);
            Assert.Equal(new[] { first.ScriptKey, second.ScriptKey }, snapshot.Select(item => item.ScriptKey));
            Assert.Equal(first.InstalledAt, snapshot[0].InstalledAt);
            Assert.Equal(updated.UpdatedAt, snapshot[0].UpdatedAt);
            Assert.False(snapshot[1].IsEnabled);
            Assert.All(snapshot, item => Assert.True(item.IsReady));
        }

        [Fact]
        public async Task FailedWritesLeavePreviousSnapshotAndEventsUnchanged()
        {
            var storage = new Storage(); var repository = await Open(storage);
            var original = await repository.InstallAsync(Source, "origin", true, None);
            var events = 0; repository.Changed += (_, __) => events++;
            storage.Fail = true;
            await Assert.ThrowsAsync<UserScriptPersistenceException>(() => repository.UpdateAsync(original.ScriptKey, Source + "new();", "new", None));
            await Assert.ThrowsAsync<UserScriptPersistenceException>(() => repository.SetEnabledAsync(original.ScriptKey, false, None));
            await Assert.ThrowsAsync<UserScriptPersistenceException>(() => repository.RemoveAsync(original.ScriptKey, None));
            Assert.Same(original, await repository.GetAsync(original.ScriptKey, None));
            Assert.Equal(0, events);
        }

        [Fact]
        public async Task CommitThenCancellationAndThrowingEventsStillSucceed()
        {
            var storage = new Storage(); var repository = await Open(storage);
            using (var cancellation = new CancellationTokenSource())
            {
                storage.AfterSave = cancellation.Cancel;
                var events = 0;
                repository.Changed += (_, __) => throw new InvalidOperationException("handler failed");
                repository.Changed += (_, __) => events++;
                var installation = await repository.InstallAsync(Source, "origin", true, cancellation.Token);
                Assert.True(installation.CanExecute);
                Assert.Equal(1, events);
            }
        }

        [Fact]
        public async Task ExternalChangesRejectStaleSaveAndFailedReloadRevokesOldSnapshot()
        {
            var storage = new Storage(); var repository = await Open(storage);
            var original = await repository.InstallAsync(Source, "origin", true, None);
            await storage.Change(original.ScriptKey, "invalid external text");
            await Assert.ThrowsAsync<UserScriptSourceConflictException>(() => repository.UpdateIfUnchangedAsync(original.ScriptKey, original.RevisionId, Source + "edit();", "origin", None));
            Assert.Same(original, await repository.GetAsync(original.ScriptKey, None));
            var reloaded = await repository.ReloadAsync(original.ScriptKey, None);
            Assert.True(reloaded.IsEnabled);
            Assert.False(reloaded.CanExecute);
            Assert.NotEmpty(reloaded.Diagnostics);
            Assert.Equal("invalid external text", reloaded.Definition.Source);
        }

        [Fact]
        public async Task MissingSourceCanBeRepairedButUnreliableVersionCannot()
        {
            var storage = new Storage(); var repository = await Open(storage);
            var original = await repository.InstallAsync(Source, "origin", true, None);
            await storage.Change(original.ScriptKey, null, version: "missing");
            var missing = await repository.ReloadAsync(original.ScriptKey, None);
            Assert.True(missing.IsEnabled); Assert.False(missing.IsReady);
            var repaired = await repository.UpdateIfUnchangedAsync(missing.ScriptKey, missing.RevisionId, Source, "repair", None);
            Assert.True(repaired.CanExecute);
            await storage.Change(original.ScriptKey, null, version: null);
            await repository.ReloadAsync(original.ScriptKey, None);
            await Assert.ThrowsAsync<UserScriptPersistenceException>(() => repository.UpdateAsync(original.ScriptKey, Source, "repair", None));
            await repository.SetEnabledAsync(original.ScriptKey, false, None);
            Assert.True(await repository.RemoveAsync(original.ScriptKey, None));
        }

        [Fact]
        public async Task UnchangedReloadDoesNotSaveChangeTimeOrRevision()
        {
            var storage = new Storage(); var repository = await Open(storage);
            var original = await repository.InstallAsync(Source, "origin", true, None);
            Assert.Same(original, await repository.ReloadAsync(original.ScriptKey, None));
            Assert.Equal(1, storage.Saves);
            Assert.Equal(2, storage.Loads);
        }

        [Fact]
        public async Task PublishWithoutSaveRechecksOriginalSourceVersion()
        {
            var storage = new Storage(); var repository = await Open(storage);
            var original = await repository.InstallAsync(Source, "origin", true, None);
            storage.OnLoad = loaded => storage.Loads == 2 ? new UserScriptLoadResult(loaded.Record, "changed-during-prepare", false, loaded.Diagnostics) : loaded;
            var reloaded = await repository.ReloadAsync(original.ScriptKey, None);
            Assert.False(reloaded.IsReady);
            Assert.Contains(reloaded.Diagnostics, item => item.Code == "MSR422_SOURCE_CHANGED");
        }

        [Fact]
        public async Task DisabledStartupUsesOnlyCacheAndEnablingFailurePreservesIntent()
        {
            var content = new PreparedScriptRevisionTests.Content();
            var preparer = new UserScriptPreparer(content, content);
            var storage = new Storage(); var initial = await Open(storage, preparer);
            var script = Source.Replace("// ==/UserScript==", "// @require https://cdn.example/one.js\n// ==/UserScript==");
            var installed = await initial.InstallAsync(script, "origin", false, None);
            await storage.Change(installed.ScriptKey, script, changed: false, missingAssets: true);
            content.Calls = 0; content.Offline = true;
            var repository = await Open(storage, preparer);
            var disabled = await repository.GetAsync(installed.ScriptKey, None);
            Assert.False(disabled.IsReady); Assert.False(disabled.IsEnabled); Assert.Equal(0, content.Calls);
            var enabled = await repository.SetEnabledAsync(installed.ScriptKey, true, None);
            Assert.True(enabled.IsEnabled); Assert.False(enabled.IsReady);
            Assert.Contains(enabled.Diagnostics, item => item.Code == "MSR425_ASSET_FETCH_FAILED");
            Assert.True((await storage.Inner.LoadAsync(installed.ScriptKey, None)).Record.IsEnabled);
        }

        [Fact]
        public async Task LocalFetchTimeoutDoesNotCancelInitializationOfOtherInstallations()
        {
            var storage = new Storage(); var repository = await Open(storage);
            var bad = await repository.InstallAsync(Source, "bad", true, None);
            var good = await repository.InstallAsync(Source, "good", true, None);
            await storage.Change(bad.ScriptKey, Source.Replace("// ==/UserScript==", "// @require https://cdn.example/timeout\n// ==/UserScript=="));
            repository = await Open(storage, new UserScriptPreparer(new TimeoutProvider()));
            Assert.False((await repository.GetAsync(bad.ScriptKey, None)).IsReady);
            Assert.True((await repository.GetAsync(good.ScriptKey, None)).CanExecute);
        }
        private sealed class TimeoutProvider : IUserScriptDependencyProvider
        {
            public Task<ResourceContent> GetScriptAsync(UserScriptInstallation installation, UserScriptDependencyDeclaration declaration, CancellationToken token) => throw new OperationCanceledException("timeout");
        }

        [Fact]
        public async Task EnableCancellationAfterIntentCommitReturnsEnabledUnavailable()
        {
            var storage = new Storage(); var repository = await Open(storage);
            var installed = await repository.InstallAsync(Source, null, false, None);
            await storage.Change(installed.ScriptKey, Source.Replace("// ==/UserScript==", "// @require https://cdn.example/a\n// ==/UserScript=="));
            await repository.ReloadAsync(installed.ScriptKey, None);
            using (var cancellation = new CancellationTokenSource())
            {
                storage.AfterEnable = cancellation.Cancel;
                var enabled = await repository.SetEnabledAsync(installed.ScriptKey, true, cancellation.Token);
                Assert.True(enabled.IsEnabled); Assert.False(enabled.IsReady);
                Assert.Contains(enabled.Diagnostics, item => item.Code == "MSR426_PREPARATION_CANCELLED");
            }
        }

        [Fact]
        public async Task ReloadCancellationPublishesUnavailableBeforeThrowing()
        {
            var storage = new Storage(); var repository = await Open(storage);
            var installed = await repository.InstallAsync(Source, null, true, None);
            using (var cancellation = new CancellationTokenSource())
            {
                storage.OnLoad = loaded => { cancellation.Cancel(); return loaded; };
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.ReloadAsync(installed.ScriptKey, cancellation.Token));
                var current = await repository.GetAsync(installed.ScriptKey, None);
                Assert.True(current.IsEnabled); Assert.False(current.IsReady);
                Assert.Contains(current.Diagnostics, item => item.Code == "MSR426_PREPARATION_CANCELLED");
            }
        }

        [Fact]
        public async Task GlobalFailureDoesNotExposePartialInitialization()
        {
            var storage = new Storage(); var repository = await Open(storage);
            var installed = await repository.InstallAsync(Source, "origin", true, None);
            await storage.Change(installed.ScriptKey, Source + "external();");
            storage.GlobalFailure = true;
            repository = new UserScriptRepository(storage);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.InitializeAsync(None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.GetSnapshotAsync(None));
        }
    }
}
