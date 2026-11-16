using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Permissions;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class ReadinessLifecycleTests
    {
        [Fact]
        public async Task FailedReloadRevokesExecutionsMenusAndStaticRulesWithoutDisablingIntent()
        {
            var token = CancellationToken.None;
            var storage = new PersistentRepositoryTests.Storage();
            var repository = new UserScriptRepository(storage);
            await repository.InitializeAsync(token);
            var source = MetadataAndMatchingTests.Script("// @name ready\n// @match https://example.com/*\n// @run-at document-end\n// @webRequest [{\"selector\":\"*\",\"action\":{\"cancel\":true}}]");
            var script = await repository.InstallAsync(source, null, true, token);
            var menu = await repository.InstallAsync(source.Replace("@run-at document-end", "@run-at context-menu"), null, true, token);
            var good = await repository.InstallAsync(source.Replace("@name ready", "@name good").Replace("// @webRequest [{\"selector\":\"*\",\"action\":{\"cancel\":true}}]", ""), null, true, token);
            using var engine = new UserScriptEngine(repository);
            using var service = new InMemoryWebRequestService();
            using var rules = new StaticWebRequestRules(repository, service, new AllowDeclaredPermissionsPolicy(), "browser", () => false);
            await rules.InitializeAsync(token);
            var frame = RepositoryExecutionLifecycleTests.Frame("old");
            Assert.Equal(2, (await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, frame), token)).Invocations.Count);
            await storage.Change(script.ScriptKey, "invalid");
            await storage.Change(menu.ScriptKey, "invalid");
            await repository.ReloadAsync(script.ScriptKey, token);
            await repository.ReloadAsync(menu.ScriptKey, token);
            await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.UrlChanged, frame), token);
            Assert.Empty(engine.GetExecutionsForScript(script.ScriptKey));
            Assert.True((await repository.GetAsync(script.ScriptKey, token)).IsEnabled);
            var request = new WebRequestEvent(WebRequestPhase.OnBeforeRequest, 1, "https://example.com/a", resourceType: "script", frame: frame);
            Assert.Equal(WebRequestActionKind.Allow, (await service.EvaluateAsync(request, token)).Kind);
            var next = await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, RepositoryExecutionLifecycleTests.Frame("new")), token);
            Assert.Equal(good.ScriptKey, Assert.Single(next.Invocations).ScriptKey);
        }

        [Fact]
        public async Task SuccessfulReloadDoesNotRepeatAnExecutedDocument()
        {
            var token = CancellationToken.None; var storage = new PersistentRepositoryTests.Storage();
            var repository = new UserScriptRepository(storage); await repository.InitializeAsync(token);
            var source = RepositoryExecutionLifecycleTests.Source("reload");
            var script = await repository.InstallAsync(source, null, true, token);
            using var engine = new UserScriptEngine(repository);
            var frame = RepositoryExecutionLifecycleTests.Frame("same");
            Assert.Single((await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, frame), token)).Invocations);
            await storage.Change(script.ScriptKey, source + "newCode();");
            await repository.ReloadAsync(script.ScriptKey, token);
            Assert.Empty((await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, frame), token)).Invocations);
            Assert.Single((await engine.ProcessLifecycleAsync(new DocumentLifecycleEventArgs(DocumentLifecycleKind.DomContentLoaded, RepositoryExecutionLifecycleTests.Frame("next")), token)).Invocations);
        }
    }
}
