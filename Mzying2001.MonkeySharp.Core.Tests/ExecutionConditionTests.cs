using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Matching;
using Mzying2001.MonkeySharp.Core.Parsing;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class ExecutionConditionTests
    {
        [Fact]
        public async Task PreparedRulesMatchEveryPinnedTampermonkeyUrlCase()
        {
            using (var evidence = JsonDocument.Parse(Read("matching.reference")))
            {
                var cases = evidence.RootElement.GetProperty("effects").GetProperty("cases").EnumerateArray().ToArray();
                var source = Read("matching.source").Replace("__PORT__", new Uri(cases[0].GetProperty("url").GetString()).Port.ToString())
                    .Replace("__IPV6_PORT__", new Uri(cases.Single(item => item.GetProperty("id").GetString() == "ipv6").GetProperty("url").GetString()).Port.ToString());
                var installation = await new InMemoryUserScriptRepository().InstallAsync(source, "reference", true, CancellationToken.None);
                Assert.Same(installation.PreparedRevision.UrlRules, UserScriptUrlRules.Compile(installation.Definition.Metadata));
                Assert.Contains(installation.Definition.ParseResult.Diagnostics, value => value.Code == "MSM084_UNSUPPORTED_IDN_MATCH");
                foreach (var item in cases)
                    Assert.Equal(item.GetProperty("matched").GetBoolean(), installation.PreparedRevision.UrlRules.IsMatch(new Uri(item.GetProperty("url").GetString())));
            }
        }

        [Theory]
        [InlineData("normal-tabs", false, true)]
        [InlineData("normal-tabs", true, false)]
        [InlineData("normal-tabs", null, false)]
        [InlineData("incognito-tabs", true, true)]
        [InlineData("incognito-tabs", false, false)]
        [InlineData("incognito-tabs", null, false)]
        [InlineData("normal-tabs;incognito-tabs", null, true)]
        [InlineData("container-id-2", false, false)]
        [InlineData("container-id-2;normal-tabs", false, true)]
        public async Task RunInUsesActualPrivacyContextAndRejectsUnknownRequirements(string selectors, bool? privateContext, bool allowed)
        {
            var repository = new InMemoryUserScriptRepository();
            var installation = await repository.InstallAsync(Source("// @run-in " + selectors.Replace(";", "\n// @run-in ")), "test", true, CancellationToken.None);
            Assert.Equal(selectors.Split(';'), installation.Definition.Metadata.RunIn);
            using (var engine = new UserScriptEngine(repository, options: new UserScriptEngineOptions {
                EnvironmentProvider = _ => new UserScriptExecutionEnvironment(isIncognito: privateContext) }))
            {
                var diagnostics = new List<UserScriptDiagnostic>();
                engine.Diagnostic += (_, value) => diagnostics.Add(value);
                var plan = await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, Frame()), CancellationToken.None);
                Assert.Equal(allowed ? 1 : 0, plan.Invocations.Count);
                if (!allowed) Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "MSR211_RUN_IN_UNAVAILABLE");
            }
        }

        [Fact]
        public async Task ContextMenuIgnoresIncludeAndExcludeWhileRetainingMatchAndContextConditions()
        {
            var repository = new InMemoryUserScriptRepository();
            var script = await repository.InstallAsync(MetadataAndMatchingTests.Script(
                "// @name menu\n// @include https://elsewhere.test/*\n// @exclude *\n// @grant none\n// @run-at context-menu"), "test", true, CancellationToken.None);
            using (var engine = new UserScriptEngine(repository))
            {
                Assert.Single(await engine.GetContextMenuEntriesAsync(Frame(), CancellationToken.None));
                Assert.Single((await engine.ProcessContextMenuAsync(Frame(), script.ScriptKey, CancellationToken.None)).Invocations);
                await repository.UpdateAsync(script.ScriptKey, MetadataAndMatchingTests.Script(
                    "// @name menu\n// @match https://elsewhere.test/*\n// @grant none\n// @run-at context-menu"), "test", CancellationToken.None);
                Assert.Empty(await engine.GetContextMenuEntriesAsync(Frame(), CancellationToken.None));
            }
        }

        [Theory]
        [InlineData("/(?i)abc/")]
        [InlineData("/(?<=a)b/")]
        [InlineData("/\\p{L}+/")]
        [InlineData("/abc/i")]
        [InlineData("/\\Aabc/")]
        public void UnsupportedRegexConstructsAreInstallationDiagnostics(string expression)
        {
            var parsed = new UserScriptMetadataParser().Parse(Source("// @include " + expression));
            Assert.False(parsed.CanEnable);
            Assert.Contains(parsed.Diagnostics, diagnostic => diagnostic.Code == "MSM021_INVALID_URL_REGEX");
        }

        [Fact]
        public async Task ExclusionTimeoutFailsClosedAndDoesNotBlockOtherScripts()
        {
            var repository = new InMemoryUserScriptRepository();
            await repository.InstallAsync(Source("// @exclude /(a+)+$/"), "test", true, CancellationToken.None);
            await repository.InstallAsync(Source(), "test", true, CancellationToken.None);
            using (var engine = new UserScriptEngine(repository))
            {
                var diagnostics = new List<UserScriptDiagnostic>();
                engine.Diagnostic += (_, item) => diagnostics.Add(item);
                var frame = new DocumentFrame("browser", "doc", "main", new Uri("https://example.com/" + new string('a', 10000) + "!"), true,
                    TimingGuarantee.BestEffortDocumentStart, BridgeIntegrityGuarantee.Verified);
                Assert.Single((await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, frame), CancellationToken.None)).Invocations);
                Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "MSR214_URL_RULE_TIMEOUT");
            }
        }

        [Theory]
        [InlineData("http://[::1]:8080/*", "http://[0:0:0:0:0:0:0:1]:8080/path", true)]
        [InlineData("http://[::1]:*/*", "http://[::1]:9191/path", true)]
        [InlineData("http://[::1]:8080/*", "http://[::1]:8081/path", false)]
        [InlineData("https://example.com:*/*", "https://EXAMPLE.COM:123/path", true)]
        [InlineData("https://example.com:443/*", "https://example.com/path", true)]
        [InlineData("https://example.com/encoded/%41?%61=*", "https://example.com/x/../encoded/%41?%61=one#ignored", true)]
        [InlineData("https://example.com/encoded/%41?%61=*", "https://example.com/encoded/A?a=one", false)]
        public void MatchPatternsHandleCanonicalHostsAndPorts(string pattern, string url, bool expected)
            => Assert.Equal(expected, MatchPatternCompiler.Compile(pattern).IsMatch(new Uri(url)));

        [Fact]
        public void InvalidRunInAndUnsupportedAllUrlsCannotInstall()
        {
            Assert.False(new UserScriptMetadataParser().Parse(Source("// @run-in content")).CanEnable);
            Assert.False(new UserScriptMetadataParser().Parse(MetadataAndMatchingTests.Script("// @name all\n// @match <all_urls>")).CanEnable);
            Assert.False(new UserScriptMatcher().IsMatch(new UserScriptMetadataParser().Parse(MetadataAndMatchingTests.Script("// @name no selector")).Metadata, new Uri("https://example.com/")));
        }

        private static string Source(string extra = "") => MetadataAndMatchingTests.Script("// @name condition\n// @match https://example.com/*\n// @grant none\n// @run-at document-end\n" + extra);
        private static DocumentFrame Frame() => new DocumentFrame("browser", "doc", "main", new Uri("https://example.com/"), true, TimingGuarantee.BestEffortDocumentStart, BridgeIntegrityGuarantee.Verified);
        private static string Read(string name)
        { using (var reader = new StreamReader(typeof(ExecutionConditionTests).Assembly.GetManifestResourceStream(name))) return reader.ReadToEnd(); }
    }
}
