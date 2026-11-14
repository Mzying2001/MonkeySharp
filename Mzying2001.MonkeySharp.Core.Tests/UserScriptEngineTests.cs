using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Compatibility;
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
    public sealed class UserScriptEngineTests
    {
        [Fact]
        public async Task DuplicateAntifeaturesDoNotInterruptInjectionOrOtherScripts()
        {
            var repository = new InMemoryUserScriptRepository();
            await repository.InstallAsync(MetadataAndMatchingTests.Script("// @name disclosures\n// @match https://example.com/*\n// @grant none\n// @run-at document-end\n" +
                "// @antifeature tracking first\n// @antifeature tracking second\n// @antifeature:en-US tracking English\n// @antifeature:EN-us tracking ignored"), "test", true, CancellationToken.None);
            await repository.InstallAsync(Script("document-end"), "test", true, CancellationToken.None);
            using var engine = new UserScriptEngine(repository);
            var plan = await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, Frame("disclosures", true)), CancellationToken.None);
            Assert.Equal(2, plan.Invocations.Count);
            using var info = JsonDocument.Parse(plan.Invocations[0].SerializedInfo);
            var descriptions = info.RootElement.GetProperty("script").GetProperty("antifeatures").GetProperty("tracking");
            Assert.Equal("first", descriptions.GetProperty("default").GetString());
            Assert.Equal("English", descriptions.GetProperty("en-US").GetString());
        }

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
        public async Task ContextMenuEntriesAreFilteredAndEachSelectionCreatesANewExecution()
        {
            var repository = new InMemoryUserScriptRepository();
            var installation = await repository.InstallAsync(
                MetadataAndMatchingTests.Script(
                    "// @name context action\n// @match https://example.com/*\n// @grant none\n// @run-at context-menu"),
                "test", true, CancellationToken.None);
            using (var engine = new UserScriptEngine(repository))
            {
                var frame = Frame("context", true);
                var entries = await engine.GetContextMenuEntriesAsync(frame, CancellationToken.None);
                var entry = Assert.Single(entries);
                Assert.Equal("context action", entry.Title);

                var first = await engine.ProcessContextMenuAsync(frame, installation.ScriptKey, CancellationToken.None);
                var second = await engine.ProcessContextMenuAsync(frame, installation.ScriptKey, CancellationToken.None);
                Assert.Single(first.Invocations);
                Assert.Single(second.Invocations);
                Assert.NotEqual(first.Invocations[0].ExecutionId, second.Invocations[0].ExecutionId);
            }
        }

        [Fact]
        public async Task ContextMenuHonorsNoFrames()
        {
            var repository = new InMemoryUserScriptRepository();
            var installation = await repository.InstallAsync(
                MetadataAndMatchingTests.Script(
                    "// @name context main\n// @match https://example.com/*\n// @noframes\n// @grant none\n// @run-at context-menu"),
                "test", true, CancellationToken.None);
            using (var engine = new UserScriptEngine(repository))
            {
                var child = Frame("context-child", false);
                Assert.Empty(await engine.GetContextMenuEntriesAsync(child, CancellationToken.None));
                Assert.Empty((await engine.ProcessContextMenuAsync(child, installation.ScriptKey, CancellationToken.None)).Invocations);
            }
        }

        [Fact]
        public async Task AboutBlankMatchProducesAnInjectionPlanOncePerDocument()
        {
            var repository = new InMemoryUserScriptRepository();
            await repository.InstallAsync(
                MetadataAndMatchingTests.Script(
                    "// @name about blank\n// @match about:blank\n// @grant none\n// @run-at document-start",
                    "window.aboutBlankExecuted = true;"),
                "about-blank", true, CancellationToken.None);
            using (var engine = new UserScriptEngine(repository))
            {
                var frame = Frame("about-blank", true, new Uri("about:blank#section"));
                var first = await engine.ProcessLifecycleAsync(
                    new DocumentLifecycleEventArgs(DocumentLifecycleKind.DocumentStart, frame), CancellationToken.None);
                var duplicate = await engine.ProcessLifecycleAsync(
                    new DocumentLifecycleEventArgs(DocumentLifecycleKind.DocumentStart, frame), CancellationToken.None);

                Assert.Single(first.Invocations);
                Assert.Empty(duplicate.Invocations);
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
        public async Task UnavailableWorldDoesNotFallBackToPageExecution()
        {
            var repository = new InMemoryUserScriptRepository();
            await repository.InstallAsync(
                MetadataAndMatchingTests.Script(
                    "// @name worlds\n// @match https://example.com/*\n// @grant none\n" +
                    "// @inject-into content\n// @run-at document-end"),
                "test", true, CancellationToken.None);
            using (var engine = new UserScriptEngine(repository))
            {
                var diagnostics = new List<UserScriptDiagnostic>();
                engine.Diagnostic += (_, item) => diagnostics.Add(item);
                var plan = await engine.ProcessLifecycleAsync(
                    new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, Frame("worlds", true)),
                    CancellationToken.None);

                Assert.Empty(plan.Invocations);
                Assert.Contains(diagnostics, item => item.Code == "MSR215_EXECUTION_WORLD_UNAVAILABLE" && item.Severity == DiagnosticSeverity.Error);
            }
        }

        [Fact]
        public async Task InvocationContainsTampermonkeyInfoSnapshot()
        {
            var repository = new InMemoryUserScriptRepository(preparer: new UserScriptPreparer(resources: new PreparedScriptRevisionTests.Content()));
            await repository.InstallAsync(
                MetadataAndMatchingTests.Script(
                    "// @name info\n// @namespace tests\n// @version 1.2.3\n" +
                    "// @description details\n// @author author\n// @match https://example.com/*\n" +
                    "// @grant GM.info\n// @connect api.example.com\n// @resource icon https://example.com/icon.png\n// @run-at document-end"),
                "origin", true, CancellationToken.None);
            using (var engine = new UserScriptEngine(repository))
            {
                var plan = await engine.ProcessLifecycleAsync(
                    new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, Frame("info", true)),
                    CancellationToken.None);
                var invocation = Assert.Single(plan.Invocations);
                using (var document = JsonDocument.Parse(invocation.SerializedInfo))
                {
                    var root = document.RootElement;
                    Assert.Equal("MonkeySharp", root.GetProperty("scriptHandler").GetString());
                    Assert.Equal("disabled", root.GetProperty("downloadMode").GetString());
                    Assert.False(root.TryGetProperty("isIncognito", out _));
                    Assert.False(root.TryGetProperty("scriptWillUpdate", out _));
                    Assert.Equal("info", root.GetProperty("script").GetProperty("name").GetString());
                    Assert.Equal("document-end", root.GetProperty("script").GetProperty("run-at").GetString());
                    Assert.Equal("icon", root.GetProperty("script").GetProperty("resources")[0].GetProperty("name").GetString());
                    Assert.Equal("raw", root.GetProperty("sandboxMode").GetString());
                    var compatibility = root.GetProperty("compatibility");
                    Assert.Equal(2, compatibility.GetProperty("contractVersion").GetInt32());
                    Assert.Equal("page", compatibility.GetProperty("executionWorld").GetString());
                    Assert.False(compatibility.GetProperty("isolatedWorld").GetBoolean());
                    Assert.Equal("metadata", compatibility.GetProperty("fields").GetProperty("scriptUpdateURL").GetString());
                    Assert.True(root.GetProperty("script").GetProperty("webRequest").ValueKind == JsonValueKind.Null);
                    Assert.Contains("@name info", root.GetProperty("scriptMetaStr").GetString());
                }
            }
        }

        [Fact]
        public async Task UnsupportedUnwrapRejectsExecutionInsteadOfKeepingAWrapper()
        {
            var repository = new InMemoryUserScriptRepository();
            await repository.InstallAsync(
                MetadataAndMatchingTests.Script(
                    "// @name isolation\n// @match https://example.com/*\n// @grant none\n" +
                    "// @sandbox DOM\n// @unwrap\n// @run-at document-end"),
                "test", true, CancellationToken.None);
            using (var engine = new UserScriptEngine(repository))
            {
                var diagnostics = new List<UserScriptDiagnostic>();
                engine.Diagnostic += (_, item) => diagnostics.Add(item);
                var plan = await engine.ProcessLifecycleAsync(
                    new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, Frame("isolation", true)),
                    CancellationToken.None);

                Assert.Empty(plan.Invocations);
                Assert.Contains(diagnostics, item => item.Code == "MSR215_EXECUTION_WORLD_UNAVAILABLE" && item.Severity == DiagnosticSeverity.Error);
            }
        }

        [Theory]
        [InlineData("", false)]
        [InlineData("// @grant none\n", false)]
        [InlineData("// @grant GM.info\n", false)]
        [InlineData("// @grant GM_info\n", false)]
        [InlineData("// @grant GM.info\n// @grant GM_info\n", false)]
        [InlineData("// @grant GM.getValue\n", true)]
        [InlineData("// @grant GM.info\n// @grant GM.getValue\n", true)]
        [InlineData("// @grant GM_info\n// @grant GM.getValue\n", true)]
        public async Task DefaultBridgePolicyAllowsInfoButStillSuppressesPrivilegedGrants(
            string grantLines, bool requiresBridge)
        {
            var repository = new InMemoryUserScriptRepository();
            var installation = await repository.InstallAsync(
                MetadataAndMatchingTests.Script(
                    "// @name info policy\n// @match https://example.com/*\n" + grantLines +
                    "// @run-at document-end"), "test", true, CancellationToken.None);
            {
                using (var engine = new UserScriptEngine(repository))
                {
                    var diagnostics = new List<UserScriptDiagnostic>();
                    engine.Diagnostic += (_, item) => diagnostics.Add(item);
                    var plan = await engine.ProcessLifecycleAsync(
                        new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, Frame("info-policy", true)),
                        CancellationToken.None);
                    if (requiresBridge)
                    {
                        Assert.Empty(plan.Invocations);
                        Assert.Contains(diagnostics, item => item.Code == "MSR201_BRIDGE_INTEGRITY_REQUIRED");
                    }
                    else
                    {
                        var invocation = Assert.Single(plan.Invocations);
                        Assert.Equal(installation.Definition.Metadata.DeclaredGrants, invocation.DeclaredGrants);
                        Assert.Equal(installation.Definition.Metadata.Capabilities, invocation.Grants);
                        Assert.Equal(installation.Definition.Metadata.GrantDeclarationState, invocation.GrantDeclarationState);
                        Assert.DoesNotContain(diagnostics, item => item.Code == "MSR201_BRIDGE_INTEGRITY_REQUIRED");
                    }
                }
            }
        }

        [Theory]
        [InlineData("native", false, false)]
        [InlineData("browser", true, true)]
        public async Task InfoUsesTheManagerVersionAndActualHostEnvironment(string mode, bool privateContext, bool updates)
        {
            var repository = new InMemoryUserScriptRepository();
            await repository.InstallAsync(MetadataAndMatchingTests.Script(
                "// @name environment\n// @version 1.2.3\n// @match https://example.com/*\n// @run-at document-end"), "test", true, CancellationToken.None);
            using (var engine = new UserScriptEngine(repository, options: new UserScriptEngineOptions
            {
                EnvironmentProvider = frame => new UserScriptExecutionEnvironment(mode, privateContext, updates,
                    capabilityStatus: new Dictionary<string, string> { ["execution.documentStart"] = "best-effort", ["GM.xmlHttpRequest.cookie"] = "unsupported:cef-urlrequest" })
            }))
            {
                var plan = await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, Frame("environment", true)), CancellationToken.None);
                using (var info = JsonDocument.Parse(Assert.Single(plan.Invocations).SerializedInfo))
                {
                    Assert.Equal(typeof(UserScriptEngine).Assembly.GetName().Version.ToString(3), info.RootElement.GetProperty("version").GetString());
                    Assert.Equal("1.2.3", info.RootElement.GetProperty("script").GetProperty("version").GetString());
                    Assert.Equal(mode, info.RootElement.GetProperty("downloadMode").GetString());
                    Assert.Equal(privateContext, info.RootElement.GetProperty("isIncognito").GetBoolean());
                    Assert.Equal(updates, info.RootElement.GetProperty("scriptWillUpdate").GetBoolean());
                    var capabilities = info.RootElement.GetProperty("compatibility").GetProperty("capabilities");
                    Assert.Equal("best-effort", capabilities.GetProperty("execution.documentStart").GetString());
                    Assert.Equal("unsupported:cef-urlrequest", capabilities.GetProperty("GM.xmlHttpRequest.cookie").GetString());
                    Assert.False(info.RootElement.GetProperty("script").TryGetProperty("position", out _));
                    Assert.False(info.RootElement.GetProperty("script").TryGetProperty("blockers", out _));
                }
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
            return Frame(documentId, mainFrame, new Uri("https://example.com/page"));
        }

        private static DocumentFrame Frame(string documentId, bool mainFrame, Uri url)
        {
            return new DocumentFrame(
                "browser",
                documentId,
                mainFrame ? "main" : "sub",
                url,
                mainFrame,
                TimingGuarantee.BestEffortDocumentStart,
                BridgeIntegrityGuarantee.Unverified);
        }

    }
}
