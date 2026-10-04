using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Demo.Runtime;
using Mzying2001.MonkeySharp.Demo.Services;
using Mzying2001.MonkeySharp.Demo.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using Xunit;

namespace Mzying2001.MonkeySharp.Demo.Tests
{
    public sealed class HostServiceTests
    {
        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 0)]
        [InlineData(2, 1)]
        public Task ClosingSelectedTabSelectsItsPreviousNeighborWithTwoWayBinding(int closedIndex, int expectedIndex) => OnDispatcher(async () =>
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            {
                var main = new MainWindowViewModel(new DiagnosticsViewModel(Dispatcher.CurrentDispatcher, fixture.Paths.LogsDirectory));
                var tabs = Enumerable.Range(0, 3).Select(index => main.NewTab("https://example.com/" + index)).ToArray();
                var selector = CreateTabSelector(main);
                selector.SelectedItem = tabs[closedIndex];
                Assert.Same(tabs[closedIndex], main.SelectedTab);
                main.CloseTab(tabs[closedIndex]);
                Assert.Same(tabs[expectedIndex], main.SelectedTab);
                Assert.Same(main.SelectedTab, selector.SelectedItem);
                Assert.Equal(tabs[expectedIndex].Address, main.Address);
                main.CloseAll();
            }
        });

        [Fact]
        public Task ClosingBackgroundAndLastTabsPreservesAValidSelection() => OnDispatcher(async () =>
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            {
                var main = new MainWindowViewModel(new DiagnosticsViewModel(Dispatcher.CurrentDispatcher, fixture.Paths.LogsDirectory));
                var background = main.NewTab("https://example.com/background");
                var selected = main.NewTab("https://example.com/selected");
                var selector = CreateTabSelector(main);
                main.CloseTab(background);
                Assert.Same(selected, main.SelectedTab);
                Assert.Same(selected, selector.SelectedItem);
                main.CloseTab(selected);
                Assert.Same(Assert.Single(main.Tabs), main.SelectedTab);
                Assert.Same(main.SelectedTab, selector.SelectedItem);
                Assert.Equal("about:blank", main.Address);
                main.CloseAll();
                Assert.Empty(main.Tabs);
                Assert.Null(selector.SelectedItem);
            }
        });

        private static ListBox CreateTabSelector(MainWindowViewModel main)
        {
            var selector = new ListBox { ItemsSource = main.Tabs };
            selector.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(MainWindowViewModel.SelectedTab))
            {
                Source = main, Mode = BindingMode.TwoWay
            });
            return selector;
        }

        [Fact]
        public Task TabsRespectOriginBackgroundInsertionParentAndUserClosure() => OnDispatcher(async () =>
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            {
                var main = new MainWindowViewModel(new DiagnosticsViewModel(Dispatcher.CurrentDispatcher, fixture.Paths.LogsDirectory));
                var origin = main.NewTab("https://example.com/");
                var other = main.NewTab("https://other.example/");
                var tabs = new WpfTabService(main, origin, Dispatcher.CurrentDispatcher);
                using (var handle = await tabs.OpenAsync(new OpenTabRequest(new Uri("https://child.example/"), false, true, true), CancellationToken.None))
                {
                    Assert.Same(other, main.SelectedTab);
                    var child = main.Tabs[1];
                    Assert.Equal(handle.TabId, child.TabId);
                    Assert.Equal(origin.TabId, child.ParentTabId);
                    var closed = 0;
                    handle.OnClose += (sender, args) => closed++;
                    main.SelectedTab = child;
                    main.CloseTab(child);
                    Assert.True(handle.Closed);
                    Assert.Equal(1, closed);
                    Assert.Same(origin, main.SelectedTab);
                }
                var detached = await tabs.OpenAsync(new OpenTabRequest(new Uri("https://child.example/"), true, false, false), CancellationToken.None);
                detached.Dispose();
                Assert.False(detached.Closed);
                main.CloseAll(); Assert.Empty(main.Tabs);
            }
        });

        [Fact]
        public Task MenuRegistrationHasUserVisibleCommandAndDeterministicRemoval() => OnDispatcher(async () =>
        {
            var tab = new BrowserTabViewModel("about:blank");
            var menu = new WpfMenuService(tab, Dispatcher.CurrentDispatcher);
            var invoked = 0;
            var key = ScriptKey.Parse(Guid.NewGuid().ToString());
            var registration = await menu.RegisterAsync(new MenuCommandRequest(key, "Test command", "t"), () => invoked++, CancellationToken.None);
            Assert.Contains("Test command", Assert.Single(tab.MenuCommands).Label);
            tab.MenuCommands[0].InvokeCommand.Execute(null);
            Assert.Equal(1, invoked);
            registration.Dispose();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Empty(tab.MenuCommands);
        });

        [Theory]
        [InlineData("../../escape.exe", "escape.exe")]
        [InlineData("..\\..\\test.txt", "test.txt")]
        [InlineData("CON", "_CON")]
        [InlineData("LPT1.txt", "_LPT1.txt")]
        [InlineData("..", "download.bin")]
        [InlineData("bad:name.txt", "bad_name.txt")]
        public void DownloadNamesStayWithinDestination(string supplied, string expected)
            => Assert.Equal(expected, DownloadService.SafeFileName(supplied));

        [Fact]
        public async Task DependenciesAreCachedAndContentHasRedirectAndSizeLimits()
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            using (var server = new LocalHttpFixture())
            using (var content = new HttpContentService(fixture.Paths))
            {
                var source = PersistenceTests.Source.Replace("// @grant none", "// @grant none\n// @require " + server.BaseUrl + "/dependency.js");
                var script = await fixture.Repository.InstallAsync(source, "origin", true, CancellationToken.None);
                Assert.Equal("demo-resource", (await content.FetchAsync(server.BaseUrl + "/redirect", CancellationToken.None)).Text);
                await Assert.ThrowsAsync<ArgumentException>(() => content.FetchAsync(server.BaseUrl + "/bad-redirect", CancellationToken.None));
                await Assert.ThrowsAsync<IOException>(() => content.FetchAsync(server.BaseUrl + "/oversize", CancellationToken.None));
                var dependency = script.Definition.Metadata.Requires[0];
                var first = await content.GetScriptAsync(script, dependency, CancellationToken.None);
                server.Dispose();
                Assert.Equal(first.Text, (await content.GetScriptAsync(script, dependency, CancellationToken.None)).Text);
            }
        }

        [Fact]
        public async Task DependencyCacheVerifiesSRIBeforeReturningCachedContent()
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            using (var server = new LocalHttpFixture())
            using (var content = new HttpContentService(fixture.Paths))
            {
                var expectedBytes = Encoding.UTF8.GetBytes("window.__demoDependency = true;");
                string digest;
                using (var sha = SHA256.Create())
                    digest = Convert.ToBase64String(sha.ComputeHash(expectedBytes));
                var source = PersistenceTests.Source.Replace("// @grant none", "// @grant none\n// @require " +
                    server.BaseUrl + "/dependency.js#sha256=" + digest);
                var script = await fixture.Repository.InstallAsync(source, "origin", true, CancellationToken.None);
                var dependency = script.Definition.Metadata.Requires.Single();
                var first = await content.GetScriptAsync(script, dependency, CancellationToken.None);
                Assert.Equal(Encoding.UTF8.GetString(expectedBytes), first.Text);

                var cachePath = Assert.Single(Directory.GetFiles(fixture.Paths.DependenciesDirectory, "*.json"));
                var cachedJson = File.ReadAllText(cachePath, Encoding.UTF8);
                cachedJson = cachedJson.Replace(
                    Convert.ToBase64String(expectedBytes),
                    Convert.ToBase64String(Encoding.UTF8.GetBytes("tampered")));
                File.WriteAllText(cachePath, cachedJson, new UTF8Encoding(false));

                var refreshed = await content.GetScriptAsync(script, dependency, CancellationToken.None);
                Assert.Equal(Encoding.UTF8.GetString(expectedBytes), refreshed.Text);
                using (var document = JsonDocument.Parse(File.ReadAllText(cachePath, Encoding.UTF8)))
                    Assert.Equal(Convert.ToBase64String(expectedBytes), document.RootElement.GetProperty("bytes").GetString());
            }
        }

        [Fact]
        public Task DownloadsReportProgressCompleteAndCancelWithoutLeavingPartialFiles() => OnDispatcher(async () =>
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            using (var server = new LocalHttpFixture())
            {
                var main = new MainWindowViewModel(new DiagnosticsViewModel(Dispatcher.CurrentDispatcher, fixture.Paths.LogsDirectory));
                var key = ScriptKey.Parse(Guid.NewGuid().ToString());
                using (var service = new DownloadService(fixture.Paths.DownloadsDirectory, Dispatcher.CurrentDispatcher, main))
                {
                    var operation = await service.DownloadAsync(new DownloadRequest(
                        key,
                        new Uri(server.BaseUrl + "/download"),
                        "../result.txt",
                        false,
                        new Dictionary<string, string> { ["X-MonkeySharp-Test"] = "download" },
                        "overwrite"), CancellationToken.None);
                    var progress = 0;
                    var completed = 0;
                    operation.Progress += (sender, args) => progress++;
                    operation.Completed += (sender, args) => completed++;
                    await operation.Completion;
                    Assert.True(progress > 0); Assert.Equal(1, completed);
                    var replayed = 0;
                    operation.Completed += (sender, args) => replayed++;
                    Assert.Equal(1, replayed);
                    Assert.Equal(256 * 1024, new FileInfo(Path.Combine(fixture.Paths.DownloadsDirectory, "result.txt")).Length);
                    var second = await service.DownloadAsync(new DownloadRequest(key, new Uri(server.BaseUrl + "/slow"), "cancel.txt", false), CancellationToken.None);
                    second.Abort();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.Completion);
                    await service.DrainAsync();
                    Assert.False(File.Exists(Path.Combine(fixture.Paths.DownloadsDirectory, "cancel.txt")));
                    Assert.Empty(Directory.GetFiles(fixture.Paths.DownloadsDirectory, "*.part"));
                    var timedOut = await service.DownloadAsync(new DownloadRequest(
                        key,
                        new Uri(server.BaseUrl + "/slow"),
                        "timeout.txt",
                        false,
                        null,
                        "uniquify",
                        TimeSpan.FromMilliseconds(20)), CancellationToken.None);
                    var timeoutCallbacks = 0;
                    timedOut.TimedOut += (sender, args) => timeoutCallbacks++;
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => timedOut.Completion);
                    Assert.Equal(1, timeoutCallbacks);
                }
            }
        });

        private static Task OnDispatcher(Func<Task> action)
        {
            var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                dispatcher.BeginInvoke(new Action(async () =>
                {
                    try { await action(); completion.TrySetResult(null); }
                    catch (Exception exception) { completion.TrySetException(exception); }
                    finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
                }));
                Dispatcher.Run();
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            return completion.Task;
        }
    }
}
