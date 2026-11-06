using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using Mzying2001.MonkeySharp.Core.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class AtomicStorageTests
    {
        [Fact]
        public async Task WatermarksArePartitionedAndReplayPreservesAtomicBatchOrigins()
        {
            using (var store = new InMemoryUserScriptValueStore())
            {
                await store.SetAsync("script", "first", UserScriptValue.Undefined, CancellationToken.None);
                var snapshot = await store.GetSnapshotAsync("script", CancellationToken.None);
                await store.SetAsync("another", "key", UserScriptValue.FromEncoded("n1"), CancellationToken.None);
                var origin = new UserScriptValueMutationOrigin("mutation", "execution", "GM.setValues");
                await store.ApplyAsync("script", new[] { new UserScriptValueMutation("second", UserScriptValue.FromEncoded("n2")),
                    new UserScriptValueMutation("first", UserScriptValue.FromEncoded("n3")) }, origin, CancellationToken.None);
                var replay = await store.GetChangesAsync("script", snapshot.Sequence, CancellationToken.None);
                Assert.True(replay.Complete);
                Assert.Equal(new long[] { 2, 3 }, replay.Changes.Select(change => change.Sequence));
                Assert.All(replay.Changes, change => { Assert.Equal("mutation", change.MutationId); Assert.Equal("execution", change.OriginExecutionId); Assert.Equal("GM.setValues", change.MutationMethod); });
                Assert.Equal(3, replay.Sequence);
                Assert.Equal(1, snapshot.Sequence);
                Assert.Single(snapshot);
                Assert.Equal(UserScriptValue.Undefined, snapshot[0].Value);
                Assert.False((await store.GetChangesAsync("script", 4, CancellationToken.None)).Complete);
                await store.SetAsync("script", "first", UserScriptValue.FromEncoded("n3"), CancellationToken.None);
                await store.DeleteAsync("script", "absent", CancellationToken.None);
                Assert.Equal(3, (await store.GetSnapshotAsync("script", CancellationToken.None)).Sequence);
                await Assert.ThrowsAsync<ArgumentException>(() => store.ApplyAsync("script", new[] {
                    new UserScriptValueMutation("duplicate", UserScriptValue.Undefined), new UserScriptValueMutation("duplicate", null) }, null, CancellationToken.None));
                Assert.False((await store.GetAsync("script", "duplicate", CancellationToken.None)).Exists);
            }
        }

        [Fact]
        public async Task SnapshotDuringBatchWaitsForWholeCommitAndSubscriberFailureDoesNotFailCommit()
        {
            using (var store = new BlockingStore())
            {
                store.ValueChanged += (_, __) => throw new InvalidOperationException("observer");
                var write = store.ApplyAsync("script", new[] { new UserScriptValueMutation("one", UserScriptValue.Undefined),
                    new UserScriptValueMutation("two", UserScriptValue.FromEncoded("n2")) }, null, CancellationToken.None);
                await store.Entered.Task;
                var snapshot = store.GetSnapshotAsync("script", CancellationToken.None);
                Assert.False(snapshot.IsCompleted);
                store.Release.SetResult(true);
                await write;
                Assert.Equal(2, (await snapshot).Count);
                Assert.Equal(2, (await snapshot).Sequence);
            }
        }

        [Fact]
        public void RetentionGapRequiresNewSnapshotInsteadOfReturningPartialHistory()
        {
            var journal = new UserScriptValueChangeJournal(2);
            for (var index = 0; index < 3; index++) journal.Append("script", "key", StoredValue.Missing, new StoredValue(true, UserScriptValue.Undefined), null);
            Assert.False(journal.Read("script", 0).Complete);
            Assert.Empty(journal.Read("script", 0).Changes);
            Assert.Equal(new long[] { 2, 3 }, journal.Read("script", 1).Changes.Select(change => change.Sequence));
            Assert.True(journal.Read("other", 0).Complete);
            Assert.Empty(journal.Read("script", 3).Changes);
        }

        [Fact]
        public async Task BridgePinsSnapshotChunksAndRequestsFreshSnapshotForOversizedReplay()
        {
            var repository = new InMemoryUserScriptRepository();
            var installation = await repository.InstallAsync(MetadataAndMatchingTests.Script(
                "// @name atomic\n// @match https://example.com/*\n// @run-at document-end\n// @grant GM_getValue"), "test", true, CancellationToken.None);
            using (var store = new InMemoryUserScriptValueStore())
            using (var engine = new UserScriptEngine(repository))
            using (var gateway = new UserScriptBridgeGateway(engine, store, options: new BridgeOptions(maxResponseBytes: 4096)))
            {
                var frame = new DocumentFrame("browser", "document", "main", new Uri("https://example.com/"), true,
                    TimingGuarantee.BestEffortDocumentStart, BridgeIntegrityGuarantee.Verified);
                var invocation = Assert.Single((await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, frame), CancellationToken.None)).Invocations);
                var original = "s" + new string('a', 8000);
                await store.SetAsync(installation.ScriptKey.ToString(), "large", UserScriptValue.FromEncoded(original), CancellationToken.None);
                Func<string, object, Task<JsonElement>> request = async (method, parameters) =>
                {
                    var json = await gateway.DispatchAsync(JsonSerializer.Serialize(new { protocol = 2, type = "request", requestId = Guid.NewGuid().ToString("D"),
                        documentId = frame.DocumentId, scriptKey = installation.ScriptKey.ToString(), capability = invocation.Capability, method, @params = parameters }), CancellationToken.None);
                    using (var document = JsonDocument.Parse(json)) return document.RootElement.Clone();
                };
                var descriptor = (await request("runtime.getStorageSnapshot", new { })).GetProperty("result");
                Assert.False(descriptor.GetProperty("complete").GetBoolean());
                var snapshotId = descriptor.GetProperty("snapshotId").GetString();
                await store.SetAsync(installation.ScriptKey.ToString(), "large", UserScriptValue.FromEncoded("snew"), CancellationToken.None);
                var chunks = new List<string>();
                for (var index = 0; index < descriptor.GetProperty("chunkCount").GetInt32(); index++)
                {
                    var part = (await request("runtime.getStorageChunk", new { snapshotId, index })).GetProperty("result");
                    Assert.Equal(index, part.GetProperty("index").GetInt32());
                    chunks.Add(part.GetProperty("chunk").GetString());
                }
                using (var snapshot = JsonDocument.Parse(string.Concat(chunks)))
                {
                    Assert.Equal(1, snapshot.RootElement.GetProperty("sequence").GetInt64());
                    Assert.Equal(original, snapshot.RootElement.GetProperty("entries")[0][1].GetString());
                }
                Assert.False((await request("runtime.getStorageChanges", new { afterSequence = 1 })).GetProperty("result").GetProperty("complete").GetBoolean());
                var current = (await request("runtime.getStorageSnapshot", new { })).GetProperty("result");
                Assert.Equal(2, current.GetProperty("sequence").GetInt64());
                Assert.Equal("snew", current.GetProperty("entries")[0][1].GetString());
                Assert.False((await request("runtime.getStorageChunk", new { snapshotId, index = 0 })).GetProperty("ok").GetBoolean());
                Assert.Equal(BridgeErrorCodes.InvalidParams, (await request("runtime.getStorageChanges", new { afterSequence = -1 })).GetProperty("error").GetProperty("code").GetString());
                Assert.Equal(BridgeErrorCodes.InvalidParams, (await request("runtime.getStorageChanges", new { afterSequence = "bad" })).GetProperty("error").GetProperty("code").GetString());
            }
        }

        private sealed class BlockingStore : InMemoryUserScriptValueStore
        {
            public TaskCompletionSource<bool> Entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> Release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            protected override async Task BeforeCommitAsync(string scriptKey, string key, ValueChangeKind kind, CancellationToken token)
            { Entered.TrySetResult(true); await Release.Task; }
        }
    }
}
