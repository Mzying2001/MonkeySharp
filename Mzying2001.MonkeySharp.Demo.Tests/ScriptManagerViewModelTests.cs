using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Parsing;
using Mzying2001.MonkeySharp.Core.Updates;
using Mzying2001.MonkeySharp.Demo.Persistence;
using Mzying2001.MonkeySharp.Demo.Services;
using Mzying2001.MonkeySharp.Demo.ViewModels;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Xunit;

namespace Mzying2001.MonkeySharp.Demo.Tests
{
    public sealed class ScriptManagerViewModelTests
    {
        [Fact]
        public async Task ScriptItemExposesReadableStateAndUpdatesAfterToggle()
        {
            var repository = new InMemoryUserScriptRepository();
            var installation = await repository.InstallAsync(Source, "application://test", true, CancellationToken.None);
            var item = new ScriptItemViewModel(installation);

            Assert.Equal("Manager sample", item.Name);
            Assert.Equal("v1.2.0", item.Version);
            Assert.Equal("A script used by manager tests.", item.Description);
            Assert.Equal("已启用", item.StatusLabel);
            Assert.Equal("停用", item.ToggleLabel);
            Assert.True(item.CanToggle);

            item.Update(await repository.SetEnabledAsync(installation.ScriptKey, false, CancellationToken.None));

            Assert.Equal("已停用", item.StatusLabel);
            Assert.Equal("启用", item.ToggleLabel);
            Assert.True(item.CanToggle);
        }

        [Fact]
        public async Task ScriptItemDisablesToggleWhileBusy()
        {
            var repository = new InMemoryUserScriptRepository();
            var installation = await repository.InstallAsync(Source, "application://test", true, CancellationToken.None);
            var item = new ScriptItemViewModel(installation);

            item.IsBusy = true;

            Assert.False(item.CanToggle);
        }

        [Fact]
        public async Task ScriptItemPrefersThe64PixelIconAndProvidesFallbackText()
        {
            var repository = new InMemoryUserScriptRepository();
            var installation = await repository.InstallAsync(SourceWithIcons, "application://test", true, CancellationToken.None);
            var item = new ScriptItemViewModel(installation);

            Assert.Equal("https://example.com/icon64.png", item.IconUrl);
            Assert.Equal("M", item.IconFallbackText);
        }

        [Fact]
        public void ScriptDetailsGroupsMetadataAndDiagnostics()
        {
            var details = new ScriptDetailsViewModel();
            details.Update(new UserScriptMetadataParser().Parse(SourceWithDetails), "https://example.com/script.user.js");

            Assert.Equal("Details", details.Name);
            Assert.Equal("document-end", details.RunAt);
            Assert.Contains("https://example.com/*", details.Matches);
            Assert.Contains("GM_setValue", details.Grants);
            Assert.Contains("cdn.example.com", details.Connects);
            Assert.Contains("https://example.com/lib.js", details.Requires);
            Assert.Contains("badge = https://example.com/badge.png", details.Resources);
            Assert.Equal("https://example.com/script.user.js", details.SourceOrigin);
            Assert.Contains("Details", details.ToConfirmationText());
        }

        [Fact]
        public async Task ManagerFiltersSearchesAndSortsTheScriptView()
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            using (var content = new HttpContentService(fixture.Paths))
            {
                var enabled = await fixture.Repository.InstallAsync(Source.Replace("Manager sample", "Zeta"), "application://zeta", true, CancellationToken.None);
                await fixture.Repository.InstallAsync(Source.Replace("Manager sample", "Alpha"), "application://alpha", false, CancellationToken.None);
                var manager = new ScriptManagerViewModel(fixture.Repository, content,
                    new UserScriptUpdateService(fixture.Repository, content), _ => true,
                    new DiagnosticsViewModel(Dispatcher.CurrentDispatcher, fixture.Paths.LogsDirectory));
                await manager.RefreshAsync();

                Assert.Equal(2, manager.FilteredCount);
                Assert.True(manager.HasScripts);
                Assert.True(manager.HasFilteredScripts);
                Assert.Equal("Alpha", ((ScriptItemViewModel)manager.FilteredScripts.Cast<object>().First()).Name);

                manager.StatusFilter = "已启用";
                Assert.Equal(1, manager.FilteredCount);
                manager.Search = "does-not-exist";
                Assert.True(manager.ShowNoResults);
                Assert.False(manager.ShowNoScripts);
                manager.Dispose();
            }
        }

        private const string Source = "// ==UserScript==\n" +
            "// @name Manager sample\n" +
            "// @namespace tests\n" +
            "// @version 1.2.0\n" +
            "// @description A script used by manager tests.\n" +
            "// @match https://example.com/*\n" +
            "// @grant none\n" +
            "// ==/UserScript==\n";

        private const string SourceWithIcons = "// ==UserScript==\n" +
            "// @name Manager sample\n" +
            "// @namespace tests\n" +
            "// @version 1.2.0\n" +
            "// @icon https://example.com/icon.png\n" +
            "// @icon64 https://example.com/icon64.png\n" +
            "// @match https://example.com/*\n" +
            "// @grant none\n" +
            "// ==/UserScript==\n";

        private const string SourceWithDetails = "// ==UserScript==\n" +
            "// @name Details\n" +
            "// @namespace tests\n" +
            "// @version 1.0.0\n" +
            "// @description Structured metadata\n" +
            "// @match https://example.com/*\n" +
            "// @grant GM_setValue\n" +
            "// @connect cdn.example.com\n" +
            "// @require https://example.com/lib.js\n" +
            "// @resource badge https://example.com/badge.png\n" +
            "// @run-at document-end\n" +
            "// ==/UserScript==\n";

    }
}
