using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Demo.ViewModels;
using System.Threading;
using System.Threading.Tasks;
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

        private const string Source = "// ==UserScript==\n" +
            "// @name Manager sample\n" +
            "// @namespace tests\n" +
            "// @version 1.2.0\n" +
            "// @description A script used by manager tests.\n" +
            "// @match https://example.com/*\n" +
            "// @grant none\n" +
            "// ==/UserScript==\n";

    }
}
