using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class PersistentRepositoryTests
    {
        private const string Source = "// ==UserScript==\n// @name durable\n// @match https://example.com/*\n// @grant none\n// ==/UserScript==\n";

        [Fact]
        public async Task RestoresIdentityTimestampsOrderAndEnabledState()
        {
            var persistence = new MemoryPersistence();
            var repository = await Open(persistence);
            var first = await repository.InstallAsync(Source, "first", true, CancellationToken.None);
            var second = await repository.InstallAsync(Source, "second", false, CancellationToken.None);
            await repository.UpdateAsync(first.ScriptKey, Source.Replace("durable", "updated"), "changed", CancellationToken.None);
            var restored = await Open(persistence);
            var snapshot = await restored.GetSnapshotAsync(CancellationToken.None);
            Assert.Equal(new[] { first.ScriptKey, second.ScriptKey }, snapshot.Select(item => item.ScriptKey));
            Assert.Equal(first.InstalledAt, snapshot[0].InstalledAt);
            Assert.Equal("updated", snapshot[0].Definition.Metadata.Name);
            Assert.Equal("changed", snapshot[0].SourceOrigin);
            Assert.False(snapshot[1].IsEnabled);
            Assert.Equal(persistence.Records[0].UpdatedAt, snapshot[0].UpdatedAt);
        }

        [Fact]
        public async Task FailedWritesAndInvalidUpdatesLeaveExistingStateAndEventsUnchanged()
        {
            var persistence = new MemoryPersistence();
            var repository = await Open(persistence);
            var installation = await repository.InstallAsync(Source, "origin", true, CancellationToken.None);
            var events = 0;
            repository.Changed += (sender, args) => events++;
            await Assert.ThrowsAsync<MetadataValidationException>(() => repository.UpdateAsync(installation.ScriptKey, "invalid", "changed", CancellationToken.None));
            persistence.Fail = true;
            await Assert.ThrowsAsync<IOException>(() => repository.SetEnabledAsync(installation.ScriptKey, false, CancellationToken.None));
            await Assert.ThrowsAsync<IOException>(() => repository.RemoveAsync(installation.ScriptKey, CancellationToken.None));
            Assert.Same(installation, await repository.GetAsync(installation.ScriptKey, CancellationToken.None));
            Assert.Equal(0, events);
        }

        [Fact]
        public async Task CancellationAfterPersistenceCommitStillPublishesAndSucceeds()
        {
            var persistence = new MemoryPersistence();
            var repository = await Open(persistence);
            using (var cancellation = new CancellationTokenSource())
            {
                persistence.AfterSave = cancellation.Cancel;
                var events = 0;
                repository.Changed += (sender, args) => events++;
                var installation = await repository.InstallAsync(Source, "origin", true, cancellation.Token);
                Assert.Equal(1, events);
                Assert.Equal(installation.ScriptKey, Assert.Single(await repository.GetSnapshotAsync(CancellationToken.None)).ScriptKey);
            }
        }

        [Fact]
        public async Task ConcurrentUpdatesAreSerializedWithoutLosingSourceOrEnabledState()
        {
            var persistence = new MemoryPersistence();
            var repository = await Open(persistence);
            var installation = await repository.InstallAsync(Source, "origin", true, CancellationToken.None);
            persistence.Delay = true;
            await Task.WhenAll(repository.UpdateAsync(installation.ScriptKey, Source.Replace("durable", "changed"), "updated", CancellationToken.None),
                repository.SetEnabledAsync(installation.ScriptKey, false, CancellationToken.None));
            var result = await repository.GetAsync(installation.ScriptKey, CancellationToken.None);
            Assert.Equal("changed", result.Definition.Metadata.Name);
            Assert.False(result.IsEnabled);
            Assert.False(persistence.ConcurrentWrite);
        }

        [Fact]
        public async Task UnavailableAndInvalidSourcesRemainVisibleDisabledAndRemovable()
        {
            var persistence = new MemoryPersistence();
            var key = ScriptKey.Parse(Guid.NewGuid().ToString());
            persistence.Records.Add(new UserScriptPersistenceRecord(key, null, "missing", true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            var repository = await Open(persistence);
            var installation = Assert.Single(await repository.GetSnapshotAsync(CancellationToken.None));
            Assert.Equal(key, installation.ScriptKey);
            Assert.False(installation.IsEnabled);
            Assert.False(installation.Definition.ParseResult.CanEnable);
            await Assert.ThrowsAsync<MetadataValidationException>(() => repository.SetEnabledAsync(key, true, CancellationToken.None));
            Assert.True(await repository.RemoveAsync(key, CancellationToken.None));
        }

        private static async Task<PersistentUserScriptRepository> Open(MemoryPersistence persistence)
        {
            var repository = new PersistentUserScriptRepository(persistence);
            await repository.InitializeAsync(CancellationToken.None);
            return repository;
        }
        private sealed class MemoryPersistence : IUserScriptRepositoryPersistence
        {
            public List<UserScriptPersistenceRecord> Records { get; } = new List<UserScriptPersistenceRecord>();
            public bool Fail { get; set; }
            public bool Delay { get; set; }
            public bool ConcurrentWrite { get; private set; }
            public Action AfterSave { get; set; }
            private int _writers;
            public Task<IReadOnlyList<UserScriptPersistenceRecord>> LoadAsync(CancellationToken token)
                => Task.FromResult<IReadOnlyList<UserScriptPersistenceRecord>>(Records.ToArray());
            public async Task SaveAsync(UserScriptPersistenceRecord record, CancellationToken token)
            {
                if (Interlocked.Increment(ref _writers) != 1) ConcurrentWrite = true;
                try
                {
                    if (Delay) await Task.Delay(30, token);
                    if (Fail) throw new IOException("Injected write failure");
                    var index = Records.FindIndex(item => item.ScriptKey == record.ScriptKey);
                    if (index < 0) Records.Add(record); else Records[index] = record;
                    AfterSave?.Invoke();
                }
                finally { Interlocked.Decrement(ref _writers); }
            }
            public Task DeleteAsync(ScriptKey key, CancellationToken token)
            { if (Fail) throw new IOException("Injected delete failure"); Records.RemoveAll(item => item.ScriptKey == key); return Task.CompletedTask; }
        }
    }
}
