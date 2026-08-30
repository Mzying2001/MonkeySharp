using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Storage;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class RepositoryAndStorageTests
    {
        [Fact]
        public async Task DuplicateMetadataCreatesIndependentInstallationsAndValues()
        {
            var repository = new InMemoryUserScriptRepository();
            var source = MetadataAndMatchingTests.Script(
                "// @name same\n// @namespace same\n// @match https://example.com/*");
            var first = await repository.InstallAsync(source, "one", true, CancellationToken.None);
            var second = await repository.InstallAsync(source, "two", true, CancellationToken.None);
            using (var store = new InMemoryUserScriptValueStore())
            {
                await store.SetAsync(first.ScriptKey.ToString(), "key", "1", CancellationToken.None);

                Assert.NotEqual(first.ScriptKey, second.ScriptKey);
                Assert.True((await store.GetAsync(first.ScriptKey.ToString(), "key", CancellationToken.None)).Exists);
                Assert.False((await store.GetAsync(second.ScriptKey.ToString(), "key", CancellationToken.None)).Exists);
            }
        }

        [Fact]
        public async Task SetThenDeleteIsLinearizedAndNotifiedInOrder()
        {
            using (var store = new BarrierStore())
            {
                var changes = new List<UserScriptValueChangedEventArgs>();
                store.ValueChanged += (_, args) => changes.Add(args);
                var set = store.SetAsync("script", "key", "{ \"value\": 1 }", CancellationToken.None);
                await store.Entered.Task;
                var delete = store.DeleteAsync("script", "key", CancellationToken.None);
                store.Release.TrySetResult(null);
                await set;
                Assert.True(await delete);

                Assert.False((await store.GetAsync("script", "key", CancellationToken.None)).Exists);
                Assert.Equal(2, changes.Count);
                Assert.Equal(ValueChangeKind.Set, changes[0].Kind);
                Assert.Equal(ValueChangeKind.Deleted, changes[1].Kind);
                Assert.True(changes[0].Sequence < changes[1].Sequence);
                Assert.Equal("{\"value\":1}", changes[0].NewValue.JsonValue);
            }
        }

        [Fact]
        public async Task CancellationBeforeCommitDoesNotMutateOrNotify()
        {
            using (var store = new BarrierStore())
            using (var cancellation = new CancellationTokenSource())
            {
                var notifications = 0;
                store.ValueChanged += (_, __) => notifications++;
                var set = store.SetAsync("script", "key", "null", cancellation.Token);
                await store.Entered.Task;
                cancellation.Cancel();
                store.Release.TrySetResult(null);

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => set);
                Assert.False((await store.GetAsync("script", "key", CancellationToken.None)).Exists);
                Assert.Equal(0, notifications);
            }
        }

        [Fact]
        public async Task CancellationAfterCommitStillSucceedsAndNotifiesOnce()
        {
            using (var cancellation = new CancellationTokenSource())
            using (var store = new CancelAfterCommitStore(cancellation))
            {
                var notifications = 0;
                store.ValueChanged += (_, __) => notifications++;

                await store.SetAsync("script", "key", "null", cancellation.Token);

                var stored = await store.GetAsync("script", "key", CancellationToken.None);
                Assert.True(stored.Exists);
                Assert.Equal("null", stored.JsonValue);
                Assert.Equal(1, notifications);
            }
        }

        [Fact]
        public async Task KeysAreOrdinalSorted()
        {
            using (var store = new InMemoryUserScriptValueStore())
            {
                await store.SetAsync("script", "z", "1", CancellationToken.None);
                await store.SetAsync("script", "A", "2", CancellationToken.None);
                await store.SetAsync("script", "a", "3", CancellationToken.None);

                Assert.Equal(new[] { "A", "a", "z" }, await store.ListKeysAsync("script", CancellationToken.None));
            }
        }

        private sealed class BarrierStore : InMemoryUserScriptValueStore
        {
            public TaskCompletionSource<object> Entered { get; } =
                new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<object> Release { get; } =
                new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            protected override async Task BeforeCommitAsync(
                string scriptKey,
                string key,
                ValueChangeKind kind,
                CancellationToken cancellationToken)
            {
                if (kind != ValueChangeKind.Set)
                    return;
                Entered.TrySetResult(null);
                await Release.Task;
            }
        }

        private sealed class CancelAfterCommitStore : InMemoryUserScriptValueStore
        {
            private readonly CancellationTokenSource _cancellation;

            public CancelAfterCommitStore(CancellationTokenSource cancellation)
            {
                _cancellation = cancellation;
            }

            protected override void OnCommitted(string scriptKey, string key, ValueChangeKind kind)
            {
                _cancellation.Cancel();
            }
        }
    }
}
