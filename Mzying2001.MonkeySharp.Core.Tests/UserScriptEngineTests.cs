using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Compatibility;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class UserScriptEngineTests
    {
        [Fact]
        public async Task ARunAtExecutesOnlyOncePerDocumentAndScript()
        {
            var repository = new InMemoryUserScriptRepository();
            await repository.InstallAsync(Script("document-start"), "test", true, CancellationToken.None);
            using (var engine = new UserScriptEngine(repository))
            {
                var frame = Frame("doc", true);
                var first = await engine.ProcessLifecycleAsync(
                    new DocumentLifecycleEventArgs(DocumentLifecycleKind.DocumentStart, frame), CancellationToken.None);
                var duplicate = await engine.ProcessLifecycleAsync(
                    new DocumentLifecycleEventArgs(DocumentLifecycleKind.DocumentStart, frame), CancellationToken.None);

                Assert.Single(first.Invocations);
                Assert.Empty(duplicate.Invocations);
                Assert.Equal(43, first.Invocations[0].Capability.Length);
            }
        }

        [Fact]
        public async Task NoFramesSkipsChildFrame()
        {
            var repository = new InMemoryUserScriptRepository();
            var source = MetadataAndMatchingTests.Script(
                "// @name noframes\n// @match https://example.com/*\n// @noframes\n// @run-at document-end");
            await repository.InstallAsync(source, "test", true, CancellationToken.None);
            using (var engine = new UserScriptEngine(repository))
            {
                var plan = await engine.ProcessLifecycleAsync(
                    new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, Frame("child", false)),
                    CancellationToken.None);

                Assert.Empty(plan.Invocations);
            }
        }

        [Fact]
        public async Task BestEffortDocumentStartProducesDiagnostic()
        {
            var repository = new InMemoryUserScriptRepository();
            await repository.InstallAsync(Script("document-start"), "test", true, CancellationToken.None);
            using (var engine = new UserScriptEngine(repository))
            {
                var diagnostics = new List<UserScriptDiagnostic>();
                engine.Diagnostic += (_, item) => diagnostics.Add(item);

                await engine.ProcessLifecycleAsync(
                    new DocumentLifecycleEventArgs(DocumentLifecycleKind.DocumentStart, Frame("doc", true)),
                    CancellationToken.None);

                Assert.Contains(diagnostics, item => item.Code == "MSR100_DOCUMENT_START_BEST_EFFORT");
            }
        }

        [Fact]
        public async Task RequiredGuaranteedStartSkipsBestEffortHost()
        {
            var repository = new InMemoryUserScriptRepository();
            await repository.InstallAsync(Script("document-start"), "test", true, CancellationToken.None);
            using (var engine = new UserScriptEngine(
                repository,
                options: new UserScriptEngineOptions { RequireGuaranteedDocumentStart = true }))
            {
                var plan = await engine.ProcessLifecycleAsync(
                    new DocumentLifecycleEventArgs(DocumentLifecycleKind.DocumentStart, Frame("doc", true)),
                    CancellationToken.None);

                Assert.Empty(plan.Invocations);
            }
        }

        [Fact]
        public async Task DependencyResolutionFailureSkipsInvocationAndProducesDiagnostic()
        {
            var repository = new InMemoryUserScriptRepository();
            var source = MetadataAndMatchingTests.Script(
                "// @name dependencies\n// @match https://example.com/*\n// @grant none\n" +
                "// @require https://cdn.example/dependency.js\n// @run-at document-end");
            await repository.InstallAsync(source, "test", true, CancellationToken.None);
            using (var engine = new UserScriptEngine(repository, sourceResolver: new FailingSourceResolver()))
            {
                var diagnostics = new List<UserScriptDiagnostic>();
                engine.Diagnostic += (_, item) => diagnostics.Add(item);

                var plan = await engine.ProcessLifecycleAsync(
                    new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, Frame("doc", true)),
                    CancellationToken.None);

                Assert.Empty(plan.Invocations);
                Assert.Contains(diagnostics, item =>
                    item.Code == "MSR400_DEPENDENCY_RESOLUTION_FAILED" && item.Exception is InvalidOperationException);
            }
        }

        [Fact]
        public async Task MissingDependencyProviderSkipsLegacyInvocation()
        {
            var repository = new InMemoryUserScriptRepository();
            await repository.InstallAsync(CompatibilityFixtures.MissingDependencyProvider, "test", true, CancellationToken.None);
            using (var engine = new UserScriptEngine(repository))
            {
                var diagnostics = new List<UserScriptDiagnostic>();
                engine.Diagnostic += (_, item) => diagnostics.Add(item);

                var plan = await engine.ProcessLifecycleAsync(
                    new DocumentLifecycleEventArgs(DocumentLifecycleKind.Load, Frame("missing", true)),
                    CancellationToken.None);

                Assert.Empty(plan.Invocations);
                Assert.Contains(diagnostics, item => item.Code == "MSR401_DEPENDENCY_PROVIDER_UNAVAILABLE");
            }
        }

        [Fact]
        public async Task UnsupportedWorldMetadataProducesWarningsButDoesNotChangePageExecution()
        {
            var repository = new InMemoryUserScriptRepository();
            await repository.InstallAsync(
                MetadataAndMatchingTests.Script(
                    "// @name worlds\n// @match https://example.com/*\n// @grant none\n" +
                    "// @inject-into content\n// @run-in content\n// @run-at document-end"),
                "test", true, CancellationToken.None);
            using (var engine = new UserScriptEngine(repository))
            {
                var diagnostics = new List<UserScriptDiagnostic>();
                engine.Diagnostic += (_, item) => diagnostics.Add(item);
                var plan = await engine.ProcessLifecycleAsync(
                    new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, Frame("worlds", true)),
                    CancellationToken.None);

                Assert.Single(plan.Invocations);
                Assert.Contains(diagnostics, item => item.Code == "MSR210_UNSUPPORTED_INJECT_WORLD");
                Assert.Contains(diagnostics, item => item.Code == "MSR211_UNSUPPORTED_RUN_IN");
            }
        }

        private static string Script(string runAt)
        {
            return MetadataAndMatchingTests.Script(
                "// @name engine\n// @match https://example.com/*\n// @grant none\n// @run-at " + runAt,
                "window.test = true;");
        }

        private static DocumentFrame Frame(string documentId, bool mainFrame)
        {
            return new DocumentFrame(
                "browser",
                documentId,
                mainFrame ? "main" : "sub",
                new Uri("https://example.com/page"),
                mainFrame,
                TimingGuarantee.BestEffortDocumentStart,
                BridgeIntegrityGuarantee.Unverified);
        }

        private sealed class FailingSourceResolver : IUserScriptSourceResolver
        {
            public Task<string> ResolveSourceAsync(
                UserScriptInstallation installation,
                CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("Dependency fixture failure.");
            }
        }
    }
}
