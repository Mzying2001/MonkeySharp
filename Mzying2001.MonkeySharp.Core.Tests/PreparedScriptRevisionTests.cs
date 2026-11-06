using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using Mzying2001.MonkeySharp.Core.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class PreparedScriptRevisionTests
    {
        internal sealed class Content : IResourceProvider, IUserScriptDependencyProvider
        {
            public int Calls;
            public bool Offline;
            public TaskCompletionSource<bool> Waiting;
            public ResourceContent Resource = new ResourceContent(new byte[] { 0, 255, 1 }, "image/png");
            public Task<ResourceContent> GetAsync(UserScriptInstallation installation, ResourceDeclaration resource, CancellationToken token)
            {
                Calls++;
                if (Offline) throw new InvalidOperationException("offline");
                return Task.FromResult(Resource);
            }
            public async Task<ResourceContent> GetScriptAsync(UserScriptInstallation installation, UserScriptDependencyDeclaration dependency, CancellationToken token)
            {
                Calls++;
                if (Offline) throw new InvalidOperationException("offline");
                if (Waiting != null) await Waiting.Task;
                token.ThrowIfCancellationRequested();
                return dependency.Url.EndsWith("one.js")
                    ? new ResourceContent(Encoding.UTF8.GetBytes("'use strict'; var shared = 1 // trailing comment"), "text/javascript")
                    : new ResourceContent(new byte[] { 47, 47, 32, 233, 10, 115, 104, 97, 114, 101, 100, 43, 43 }, "text/javascript", charset: "windows-1252");
            }
        }

        private static string Source(string extra = "", string body = "globalThis.sharedResult = shared;") =>
            MetadataAndMatchingTests.Script("// @name prepared\n// @match https://example.com/*\n// @grant GM_getResourceText\n// @grant GM_getResourceURL\n// @run-at document-end\n" + extra, body);

        [Fact]
        public async Task RepeatedFramesUseOneImmutablePreparedRevisionAndOriginalBytes()
        {
            var content = new Content();
            var repository = new InMemoryUserScriptRepository(preparer: new UserScriptPreparer(content, content));
            var script = await repository.InstallAsync(Source("// @require https://cdn.example/one.js\n// @require https://cdn.example/two.js\n// @resource binary https://cdn.example/binary"), "test", true, CancellationToken.None);
            Assert.Equal(3, content.Calls);
            Assert.Contains("comment\n;\n// @require https://cdn.example/two.js\n// é\nshared++\n;\n", script.PreparedRevision.Source);
            var bytes = script.PreparedRevision.Resources["binary"].Bytes;
            bytes[1] = 0;
            Assert.Equal(255, script.PreparedRevision.Resources["binary"].Bytes[1]);
            content.Offline = true;
            using (var engine = new UserScriptEngine(repository))
            using (var provider = new ResourceAndNetworkApiProvider())
            using (var gateway = new UserScriptBridgeGateway(engine, new Mzying2001.MonkeySharp.Core.Storage.InMemoryUserScriptValueStore(), providers: new[] { provider }))
            {
                for (var index = 0; index < 3; index++)
                {
                    var frame = new DocumentFrame("browser", "doc-" + index, "frame-" + index,
                        new Uri("https://example.com/"), true, TimingGuarantee.BestEffortDocumentStart, bridgeIntegrity: BridgeIntegrityGuarantee.Verified);
                    var plan = await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, frame), CancellationToken.None);
                    Assert.Equal(script.PreparedRevision.Source, Assert.Single(plan.Invocations).Source);
                }
            }
            Assert.Equal(3, content.Calls);
        }

        [Fact]
        public async Task FailedPreparationDoesNotInstallOrReplacePreviousRevision()
        {
            var content = new Content();
            var repository = new InMemoryUserScriptRepository(preparer: new UserScriptPreparer(content, content));
            var original = await repository.InstallAsync(Source(), "old", true, CancellationToken.None);
            var bad = Source("// @resource binary https://cdn.example/binary#sha256=" + new string('0', 64));
            await Assert.ThrowsAsync<ResourceIntegrityException>(() => repository.UpdateAsync(original.ScriptKey, bad, "new", CancellationToken.None));
            Assert.Same(original, await repository.GetAsync(original.ScriptKey, CancellationToken.None));
            await Assert.ThrowsAsync<ResourceIntegrityException>(() => repository.InstallAsync(bad, "new", true, CancellationToken.None));
            Assert.Single(await repository.GetSnapshotAsync(CancellationToken.None));
            var limited = new InMemoryUserScriptRepository(preparer: new UserScriptPreparer(content, content, 1));
            await Assert.ThrowsAsync<InvalidOperationException>(() => limited.InstallAsync(Source("// @require https://cdn.example/one.js"), "test", true, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => new InMemoryUserScriptRepository().InstallAsync(
                Source("// @require https://cdn.example/one.js"), "test", true, CancellationToken.None));
        }

        [Fact]
        public async Task PendingUpdateKeepsOldSnapshotAndRejectsConcurrentEdit()
        {
            var content = new Content();
            var repository = new InMemoryUserScriptRepository(preparer: new UserScriptPreparer(content, content));
            var original = await repository.InstallAsync(Source(), "old", true, CancellationToken.None);
            content.Waiting = new TaskCompletionSource<bool>();
            var pending = repository.UpdateAsync(original.ScriptKey, Source("// @require https://cdn.example/one.js"), "new", CancellationToken.None);
            Assert.Same(original, Assert.Single(await repository.GetSnapshotAsync(CancellationToken.None)));
            var edited = await repository.UpdateAsync(original.ScriptKey, Source(body: "edited();"), "edited", CancellationToken.None);
            content.Waiting.SetResult(true);
            await Assert.ThrowsAsync<RepositoryRevisionMismatchException>(() => pending);
            Assert.Same(edited, await repository.GetAsync(original.ScriptKey, CancellationToken.None));
        }

        [Theory]
        [InlineData("sha1")]
        [InlineData("sha384")]
        [InlineData("sha512")]
        public void IntegrityUsesLastSupportedAlgorithmAndRejectsTampering(string name)
        {
            using (var hash = name == "sha1" ? (HashAlgorithm)SHA1.Create() : name == "sha384" ? SHA384.Create() : SHA512.Create())
            {
                var bytes = Encoding.UTF8.GetBytes("hello");
                var digest = Convert.ToBase64String(hash.ComputeHash(bytes));
                var parsed = new Mzying2001.MonkeySharp.Core.Parsing.UserScriptMetadataParser().Parse(Source("// @require https://cdn.example/lib.js#md5=" + new string('0', 32) + "," + name + "=" + digest));
                Assert.True(parsed.CanEnable);
                ResourceIntegrityVerifier.Verify(bytes, parsed.Metadata.Requires[0].Integrity);
                Assert.Throws<ResourceIntegrityException>(() => ResourceIntegrityVerifier.Verify(new byte[0], parsed.Metadata.Requires[0].Integrity));
            }
        }
    }
}
