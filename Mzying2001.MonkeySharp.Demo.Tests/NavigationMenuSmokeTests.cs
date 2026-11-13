using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Demo.Runtime;
using Mzying2001.MonkeySharp.Demo.Services;
using Mzying2001.MonkeySharp.Demo.ViewModels;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Demo.Tests
{
    public sealed class NavigationMenuSmokeTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MenuChangedDuringReadinessEvaluationIsRechecked(bool replaced)
        {
            const string url = "http://example.com/menu-page";
            var tab = new BrowserTabViewModel(url);
            var old = Command("example.com");
            var current = Command("example.com");
            tab.MenuCommands.Add(old);
            var attempts = 0;

            var result = await NavigationMenuSmokeTest.CurrentMenuAsync(tab, url, null, async () =>
            {
                await Task.Yield();
                tab.MenuCommands.Clear();
                if (++attempts == 1)
                {
                    if (replaced) tab.MenuCommands.Add(Command("other.example"));
                }
                else tab.MenuCommands.Add(current);
                return true;
            });

            Assert.Same(current, result);
            Assert.Equal(2, attempts);
        }

        private static ScriptMenuCommand Command(string host) => new ScriptMenuCommand(
            new MenuCommandRequest(ScriptKey.Parse("9e9be9b3-a637-453e-a70d-f61d0deba59a"), "Navigation menu " + host, null), "Navigation smoke");
    }
}
