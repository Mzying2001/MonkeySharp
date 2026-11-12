using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class ExecutionWorldTests
    {
        [Fact]
        public async Task IsolatedNativeScopeSelectsOneInstallationAndRevisionWithoutPageFallback()
        {
            var repository = new InMemoryUserScriptRepository();
            var first = await repository.InstallAsync(Source("DOM"), "first", true, CancellationToken.None);
            await repository.InstallAsync(Source("DOM"), "second", true, CancellationToken.None);
            using (var engine = new UserScriptEngine(repository, options: new UserScriptEngineOptions { SupportsIsolatedWorlds = true }))
            {
                Assert.Empty((await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded,
                    Frame("page", "page", null, null)), CancellationToken.None)).Invocations);
                var frame = Frame("native", "isolated", first.ScriptKey, first.RevisionId);
                var plan = await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, frame), CancellationToken.None);
                Assert.Equal(first.ScriptKey, Assert.Single(plan.Invocations).ScriptKey);
                using (var info = JsonDocument.Parse(plan.Invocations[0].SerializedInfo))
                {
                    Assert.Equal("isolated", info.RootElement.GetProperty("compatibility").GetProperty("executionWorld").GetString());
                    Assert.True(info.RootElement.GetProperty("compatibility").GetProperty("isolatedWorld").GetBoolean());
                    Assert.Equal("DOM", info.RootElement.GetProperty("sandboxMode").GetString());
                }
                Assert.Empty((await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded,
                    Frame("stale", "isolated", first.ScriptKey, Guid.NewGuid())), CancellationToken.None)).Invocations);
            }
        }

        [Theory]
        [InlineData("// @sandbox DOM\n// @grant unsafeWindow")]
        [InlineData("// @sandbox raw\n// @unwrap")]
        [InlineData("// @sandbox unknown")]
        [InlineData("// @sandbox raw\n// @inject-into content")]
        public async Task IncompatibleRequirementsAreRejectedBeforeSourceRuns(string requirements)
        {
            var repository = new InMemoryUserScriptRepository();
            await repository.InstallAsync(MetadataAndMatchingTests.Script("// @name rejected\n// @match https://example.com/*\n// @run-at document-end\n" + requirements), "test", true, CancellationToken.None);
            using (var engine = new UserScriptEngine(repository, options: new UserScriptEngineOptions { SupportsIsolatedWorlds = true }))
            {
                var diagnostics = new List<UserScriptDiagnostic>();
                engine.Diagnostic += (_, value) => diagnostics.Add(value);
                Assert.Empty((await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded,
                    Frame("doc", "page", null, null)), CancellationToken.None)).Invocations);
                Assert.Contains(diagnostics, value => value.Code == "MSR215_EXECUTION_WORLD_UNAVAILABLE" && value.Severity == DiagnosticSeverity.Error);
            }
        }

        [Fact]
        public async Task GuaranteedStartPolicyDoesNotTreatIsolatedTransportRegistrationAsStrictTiming()
        {
            var repository = new InMemoryUserScriptRepository();
            var script = await repository.InstallAsync(Source("DOM").Replace("document-end", "document-start"), "test", true, CancellationToken.None);
            using (var engine = new UserScriptEngine(repository, options: new UserScriptEngineOptions { SupportsIsolatedWorlds = true, RequireGuaranteedDocumentStart = true }))
                Assert.Empty((await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.DocumentStart,
                    Frame("doc", "isolated", script.ScriptKey, script.RevisionId)), CancellationToken.None)).Invocations);
        }

        private static string Source(string sandbox) => MetadataAndMatchingTests.Script("// @name world\n// @match https://example.com/*\n// @sandbox " + sandbox + "\n// @grant GM_getValue\n// @run-at document-end");
        private static DocumentFrame Frame(string document, string world, ScriptKey? key, Guid? revision) =>
            new DocumentFrame("browser", document, "native-frame", new Uri("https://example.com/"), true,
                TimingGuarantee.BestEffortDocumentStart, BridgeIntegrityGuarantee.Verified, world, key, revision);
    }
}
