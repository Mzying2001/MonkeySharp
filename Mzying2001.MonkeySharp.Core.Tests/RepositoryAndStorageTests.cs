using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Storage;
using static Mzying2001.MonkeySharp.Core.Storage.UserScriptValue;
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
                await store.SetAsync(first.ScriptKey.ToString(), "key", FromJson("1"), CancellationToken.None);

                Assert.NotEqual(first.ScriptKey, second.ScriptKey);
                Assert.True((await store.GetAsync(first.ScriptKey.ToString(), "key", CancellationToken.None)).Exists);
                Assert.False((await store.GetAsync(second.ScriptKey.ToString(), "key", CancellationToken.None)).Exists);
            }
        }

        [Fact]
        public async Task ConditionalRepositoryUpdateRejectsStaleRevisionWithoutReplacingLatestSource()
        {
            var repository = new InMemoryUserScriptRepository();
            var source = MetadataAndMatchingTests.Script(
                "// @name conditional\n// @namespace test\n// @version 1\n// @match https://example.com/*");
            var installation = await repository.InstallAsync(source, "origin", true, CancellationToken.None);
            var expectedRevisionId = installation.RevisionId;
            var latestSource = source.Replace("@version 1", "@version 2");
            await repository.UpdateIfUnchangedAsync(
                installation.ScriptKey, expectedRevisionId, latestSource, "latest", CancellationToken.None);

            var staleSource = source.Replace("@version 1", "@version 3");
            await Assert.ThrowsAsync<RepositoryRevisionMismatchException>(() => repository.UpdateIfUnchangedAsync(
                installation.ScriptKey, expectedRevisionId, staleSource, "stale", CancellationToken.None));

            var current = await repository.GetAsync(installation.ScriptKey, CancellationToken.None);
            Assert.Equal(latestSource, current.Definition.Source);
            Assert.Equal("latest", current.SourceOrigin);
        }

        [Fact]
        public async Task SetThenDeleteIsLinearizedAndNotifiedInOrder()
        {
            using (var store = new BarrierStore())
            {
                var changes = new List<UserScriptValueChangedEventArgs>();
                store.ValueChanged += (_, args) => changes.Add(args);
                var set = store.SetAsync("script", "key", FromJson("{ \"value\": 1 }"), CancellationToken.None);
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
                Assert.Equal("o{\"value\":1}", changes[0].NewValue.Value.EncodedValue);
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
                var set = store.SetAsync("script", "key", FromJson("null"), cancellation.Token);
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

                await store.SetAsync("script", "key", FromJson("null"), cancellation.Token);

                var stored = await store.GetAsync("script", "key", CancellationToken.None);
                Assert.True(stored.Exists);
                Assert.Equal("onull", stored.Value.EncodedValue);
                Assert.Equal(1, notifications);
            }
        }

        [Fact]
        public async Task KeysRetainInsertionOrder()
        {
            using (var store = new InMemoryUserScriptValueStore())
            {
                await store.SetAsync("script", "z", FromJson("1"), CancellationToken.None);
                await store.SetAsync("script", "A", FromJson("2"), CancellationToken.None);
                await store.SetAsync("script", "a", FromJson("3"), CancellationToken.None);

                Assert.Equal(new[] { "z", "A", "a" }, await store.ListKeysAsync("script", CancellationToken.None));
            }
        }

        [Theory]
        [InlineData("u", "u")]
        [InlineData("s", "s")]
        [InlineData("sundefined", "sundefined")]
        [InlineData("btrue", "btrue")]
        [InlineData("bfalse", "bfalse")]
        [InlineData("nNaN", "nNaN")]
        [InlineData("nInfinity", "nInfinity")]
        [InlineData("n-Infinity", "n-Infinity")]
        [InlineData("n-0", "n0")]
        [InlineData("n1.0", "n1")]
        [InlineData("onull", "onull")]
        [InlineData("o{ \"a\": 1 }", "o{\"a\":1}")]
        public void TypedValuesCanonicalizeWithoutConfusingUndefinedAndJson(string input, string canonical)
        {
            var value = FromEncoded(input);
            Assert.Equal(canonical, value.EncodedValue);
            Assert.Equal(value, FromEncoded(canonical));
        }

        [Theory]
        [InlineData("")]
        [InlineData("undefined")]
        [InlineData("b1")]
        [InlineData("nnull")]
        [InlineData("n1e999")]
        [InlineData("o42")]
        [InlineData("o\"text\"")]
        [InlineData("xunknown")]
        public void TypedValuesRejectInvalidEncodings(string encoded)
        {
            Assert.ThrowsAny<FormatException>(() => FromEncoded(encoded));
        }

        [Fact]
        public async Task UndefinedKeysSnapshotsAndChangeSuppressionUseExistenceAndTypedValues()
        {
            using (var store = new InMemoryUserScriptValueStore())
            {
                var changes = new List<UserScriptValueChangedEventArgs>();
                store.ValueChanged += (_, args) => changes.Add(args);
                Assert.False((await store.GetAsync("script", "", CancellationToken.None)).Exists);
                await store.SetAsync("script", "", Undefined, CancellationToken.None);
                await store.SetAsync("script", "", Undefined, CancellationToken.None);
                await store.SetAsync("script", "null", FromJson("null"), CancellationToken.None);
                var stored = await store.GetAsync("script", "", CancellationToken.None);
                Assert.True(stored.Exists);
                Assert.Equal(Undefined, stored.Value);
                var snapshot = await store.GetSnapshotAsync("script", CancellationToken.None);
                Assert.Equal(new[] { "", "null" }, System.Linq.Enumerable.Select(snapshot, item => item.Key));
                Assert.Equal(Undefined, snapshot[0].Value);
                await store.DeleteAsync("script", "", CancellationToken.None);
                await store.DeleteAsync("script", "", CancellationToken.None);
                Assert.Equal(3, changes.Count);
                Assert.False(changes[0].OldValue.Exists);
                Assert.True(changes[0].NewValue.Exists);
                Assert.Equal(Undefined, changes[2].OldValue.Value);
                Assert.False(changes[2].NewValue.Exists);
                await store.SetAsync("script", "", Undefined, CancellationToken.None);
                Assert.Equal(new[] { "null", "" }, await store.ListKeysAsync("script", CancellationToken.None));
                Assert.Equal(new[] { "", "null" }, System.Linq.Enumerable.Select(snapshot, item => item.Key));
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
