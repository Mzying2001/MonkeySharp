using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.CefSharp.IntegrationTests
{
    public sealed class MenuFrameMappingTests
    {
        [Fact]
        public async Task RequiresUniqueNativeAndCdpOwnership()
        {
            var test = await Fixture.CreateAsync("browser");
            var left = test.AddDocument("left", 11, "first");
            var right = test.AddDocument("right", 12, "first");
            var publish = test.Capture(new[] { "left", "right" }, new[] { "native-left", "native-right" });

            Assert.False(test.Executor.OwnsNativeMenuFrame(left, "native-left", false));
            publish(new[] { new[] { "native-left" }, new[] { "native-left" } });
            Assert.False(test.Executor.OwnsNativeMenuFrame(left, "native-left", false));
            Assert.False(test.Executor.OwnsNativeMenuFrame(right, "native-left", false));
            publish(new[] { new[] { "native-left", "native-right" }, Array.Empty<string>() });
            Assert.False(test.Executor.OwnsNativeMenuFrame(left, "native-left", false));
            Assert.False(test.Executor.OwnsNativeMenuFrame(left, "native-right", false));

            publish(new[] { new[] { "native-left" }, new[] { "native-right" } });
            Assert.True(test.Executor.OwnsNativeMenuFrame(left, "native-left", false));
            Assert.True(test.Executor.OwnsNativeMenuFrame(right, "native-right", false));
            Assert.False(test.Executor.OwnsNativeMenuFrame(right, "native-left", false));

            var updated = new DocumentFrame(left.BrowserSessionId, left.DocumentId, left.FrameId,
                new Uri(left.Url + "#updated"), left.IsMainFrame, left.TimingGuarantee, left.BridgeIntegrity,
                left.ExecutionWorld, left.ScriptScope, left.RevisionScope);
            Assert.True(test.Executor.OwnsNativeMenuFrame(updated, "native-left", false));
        }

        [Fact]
        public async Task RejectsLateProofsAcrossNativeAndCdpChanges()
        {
            var test = await Fixture.CreateAsync("original-attachment");
            var original = test.AddDocument("frame", 11, "first");
            var publish = test.Capture(new[] { "frame" }, new[] { "native-id" });
            var observed = new[] { new[] { "native-id" } };
            publish(observed);
            Assert.True(test.Executor.OwnsNativeMenuFrame(original, "native-id", false));

            test.Executor.NativeMenuNavigation("native-id", main: false, loading: true);
            test.Executor.NativeMenuNavigation("native-id", main: false, loading: false);
            publish(observed);
            Assert.False(test.Executor.OwnsNativeMenuFrame(original, "native-id", false));

            var duringNavigation = test.Capture(new[] { "frame" }, new[] { "native-id" });
            var replacement = test.AddDocument("frame", 11, "second");
            duringNavigation(observed);
            Assert.False(test.Executor.OwnsNativeMenuFrame(replacement, "native-id", false));
            test.Capture(new[] { "frame" }, new[] { "native-id" })(observed);
            Assert.True(test.Executor.OwnsNativeMenuFrame(replacement, "native-id", false));
            Assert.False(test.Executor.OwnsNativeMenuFrame(original, "native-id", false));

            var reattached = await Fixture.CreateAsync("new-attachment");
            var current = reattached.AddDocument("frame", 11, "second");
            reattached.Capture(new[] { "frame" }, new[] { "native-id" })(observed);
            Assert.True(reattached.Executor.OwnsNativeMenuFrame(current, "native-id", false));
            Assert.False(reattached.Executor.OwnsNativeMenuFrame(replacement, "native-id", false));
        }

        private sealed class Fixture
        {
            private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            private static readonly Type ExecutorType = typeof(CefSharpIsolatedExecutor);
            private readonly UserScriptInstallation _script;
            private readonly string _browserSession;
            private readonly Dictionary<string, string> _loaders = new Dictionary<string, string>();

            private Fixture(UserScriptRepository repository, UserScriptInstallation script, string browserSession)
            {
                _script = script;
                _browserSession = browserSession;
                Executor = new CefSharpIsolatedExecutor(null, repository, null, null, browserSession, () => false, null,
                    null, null, CancellationToken.None);
            }

            internal CefSharpIsolatedExecutor Executor { get; }

            internal static async Task<Fixture> CreateAsync(string browserSession)
            {
                var repository = TestRepository.Create();
                var script = await repository.InstallAsync("// ==UserScript==\n// @name isolated menu\n// @match *://*/*\n// @sandbox DOM\n// ==/UserScript==",
                    "test", true, CancellationToken.None);
                return new Fixture(repository, script, browserSession);
            }

            internal DocumentFrame AddDocument(string id, int contextId, string loader)
            {
                _loaders[id] = loader;
                Invoke("FrameNavigated", JsonSerializer.SerializeToElement(new { id, loaderId = loader, parentId = "main", url = "https://example.com/same" }));
                Invoke("RememberMenuContext", id, contextId, JsonSerializer.SerializeToElement(new { uniqueId = id + ":" + loader }));
                var worldType = ExecutorType.GetNestedType("World", BindingFlags.NonPublic);
                var world = Activator.CreateInstance(worldType, Instance, null, new[] { ReadIndex("_frames")[id], _script, "world", _browserSession, "root" }, null);
                worldType.GetField("Ready", Instance).SetValue(world, true);
                var frame = (DocumentFrame)worldType.GetField("Frame", Instance).GetValue(world);
                ReadIndex("_documents").Add(frame.DocumentId, world);
                return frame;
            }

            internal Action<string[][]> Capture(string[] frameIds, string[] nativeIds)
            {
                var candidateType = ExecutorType.GetNestedType("MenuCandidate", BindingFlags.NonPublic);
                var nativeType = ExecutorType.GetNestedType("NativeMenuFrame", BindingFlags.NonPublic);
                var candidates = Array.CreateInstance(candidateType, frameIds.Length);
                for (var index = 0; index < frameIds.Length; index++)
                {
                    var id = frameIds[index];
                    candidates.SetValue(Activator.CreateInstance(candidateType, Instance, null,
                        new[] { ReadIndex("_frames")[id], ReadIndex("_menuContexts")[id] }, null), index);
                }
                var markers = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), nativeType));
                var nativeFrames = ReadIndex("_menuNativeFrames");
                foreach (var id in nativeIds)
                {
                    if (!nativeFrames.Contains(id)) nativeFrames.Add(id, Activator.CreateInstance(nativeType, Instance, null, new object[] { id }, null));
                    markers.Add(id, nativeFrames[id]);
                }
                return matches =>
                {
                    var tree = JsonSerializer.SerializeToElement(new
                    {
                        frame = new { id = "main", loaderId = "main" },
                        childFrames = _loaders.Select(pair => new { frame = new { id = pair.Key, loaderId = pair.Value } })
                    });
                    Invoke("PublishMenuBindings", candidates, markers, matches, tree);
                };
            }

            private void Invoke(string name, params object[] args) => ExecutorType.GetMethod(name, Instance).Invoke(Executor, args);
            private IDictionary ReadIndex(string name) => (IDictionary)ExecutorType.GetField(name, Instance).GetValue(Executor);
        }
    }
}
