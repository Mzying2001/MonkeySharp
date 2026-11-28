using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Permissions;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using Mzying2001.MonkeySharp.Core.Updates;
using Mzying2001.MonkeySharp.Demo.Localization;
using Mzying2001.MonkeySharp.Demo.Services;
using Mzying2001.MonkeySharp.Demo.ViewModels;
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using Xunit;

namespace Mzying2001.MonkeySharp.Demo.Tests
{
    [CollectionDefinition("UI language", DisableParallelization = true)]
    public sealed class UiLanguageCollection { }

    [Collection("UI language")]
    public sealed class LocalizationTests
    {
        [Fact]
        public async Task DefaultIsEnglishAndPreferencesAndOverridesHaveExplicitPrecedence()
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            {
                var directory = fixture.Paths.BaseDirectory;
                Assert.Equal("en", UiLanguage.Resolve(new string[0], directory));
                UiLanguage.Save(directory, "zh-cn");
                Assert.Equal("zh-CN", UiLanguage.Resolve(new string[0], directory));
                Assert.Equal("en", UiLanguage.Resolve(new[] { "--lang", "EN" }, directory));
                Assert.Equal("zh-CN", UiLanguage.Resolve(new string[0], directory));
                UiLanguage.Save(directory, "en");
                Assert.Equal("en", UiLanguage.Resolve(new string[0], directory));
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("fr-FR")]
        [InlineData("corrupted preference")]
        public async Task UnknownSavedLanguagesFallBackToEnglish(string saved)
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            {
                File.WriteAllText(Path.Combine(fixture.Paths.DataDirectory, UiLanguage.PreferenceFileName), saved);
                Assert.Equal("en", UiLanguage.Resolve(new string[0], fixture.Paths.BaseDirectory));
            }
        }

        [Fact]
        public void InvalidCommandLineLanguagesAreRejected()
        {
            foreach (var arguments in new[] {
                new[] { "--lang" }, new[] { "--lang", "fr" }, new[] { "--lang", "--smoke" },
                new[] { "--lang", "en", "--lang", "zh-CN" } })
                Assert.Throws<ArgumentException>(() => UiLanguage.Resolve(arguments, Path.GetTempPath()));
        }

        [Fact]
        public void MissingPreferencesDoNotCreateAProfile()
        {
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Assert.Equal("en", UiLanguage.Resolve(new string[0], directory));
            Assert.False(Directory.Exists(directory));
            Assert.Throws<ArgumentException>(() => UiLanguage.Save(directory, "fr"));
            Assert.False(Directory.Exists(directory));
        }

        [Fact]
        public void SatelliteHasEveryNeutralKeyAndMatchingFormatArguments()
        {
            var neutral = Strings.Resources.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, false)
                .Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value);
            var chinese = Strings.Resources.GetResourceSet(CultureInfo.GetCultureInfo("zh-CN"), true, false)
                .Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value);
            Assert.Equal(neutral.Keys.OrderBy(key => key), chinese.Keys.OrderBy(key => key));
            foreach (var key in neutral.Keys)
            {
                Assert.False(string.IsNullOrWhiteSpace(chinese[key]), key);
                Assert.Equal(FormatArguments(neutral[key]), FormatArguments(chinese[key]));
                var arguments = Enumerable.Range(0, 13).Select(value => (object)("argument-" + value)).ToArray();
                string.Format(neutral[key], arguments);
                string.Format(chinese[key], arguments);
            }
            Assert.Equal("Script manager", Strings.Get("ScriptManager", CultureInfo.GetCultureInfo("fr-FR")));
            Assert.Equal("脚本管理器", Strings.Get("ScriptManager", CultureInfo.GetCultureInfo("zh-CN")));
            Assert.Throws<ArgumentException>(() => Strings.Get("MissingResource"));
        }

        [Theory]
        [InlineData("en", "Enabled", "Disable", "Private ", "Author:")]
        [InlineData("zh-CN", "已启用", "停用", "私密 ", "作者：")]
        public async Task ViewModelsAndFilteringWorkInBothLanguages(string language, string enabled, string disable, string privacy, string author)
        {
            var previous = UiLanguage.CurrentCulture;
            UiLanguage.Apply(language);
            try
            {
                using (var fixture = await PersistenceTests.Fixture.Create())
                using (var content = new HttpContentService())
                {
                    var repository = TestRepository.Create();
                    const string source = "// ==UserScript==\n// @name Localization sample\n// @match https://example.com/*\n// @grant none\n// ==/UserScript==\n";
                    await repository.InstallAsync(source, "fixture://enabled", true, CancellationToken.None);
                    await repository.InstallAsync(source.Replace("sample", "disabled"), "fixture://disabled", false, CancellationToken.None);
                    using (var manager = new ScriptManagerViewModel(repository, content,
                        new UserScriptUpdateService(repository, content), _ => true,
                        new DiagnosticsViewModel(Dispatcher.CurrentDispatcher, fixture.Paths.LogsDirectory)))
                    {
                        await manager.RefreshAsync();
                        manager.StatusFilter = ScriptStatusFilter.Enabled;
                        var item = Assert.Single(manager.FilteredScripts.Cast<ScriptItemViewModel>());
                        Assert.Equal(enabled, item.StatusLabel);
                        Assert.Equal(disable, item.ToggleLabel);
                        Assert.Equal("Disable script", item.ToggleAutomationName);
                        Assert.Equal(enabled, manager.StatusFilters.Single(option => option.Value == ScriptStatusFilter.Enabled).Label);
                        manager.SelectedScript = item;
                        Assert.Contains(author, manager.Details.ToConfirmationText());
                        Assert.Contains(author, ScriptEditorViewModel.Describe(item.Installation.Definition.ParseResult));
                        Assert.Equal(privacy, new BrowserTabViewModel("about:blank", true).PrivacyLabel);
                        manager.StatusFilter = ScriptStatusFilter.Disabled;
                        Assert.Single(manager.FilteredScripts.Cast<ScriptItemViewModel>());
                        Assert.Equal(Strings.Get("WaitingDownload"), new DownloadItemViewModel("file", () => { }).Status);
                    }
                }
            }
            finally { UiLanguage.Apply(previous.Name); }
        }

        [Fact]
        public void CallbackThreadCultureDoesNotOverrideTheApplicationLanguage()
        {
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
                Assert.Equal("en", UiLanguage.CurrentCulture.Name);
                Assert.Equal("Save userscript download", DownloadFilePicker.Title);
            }
            finally { CultureInfo.CurrentUICulture = previous; }
        }

        [Theory]
        [InlineData("en", "Alpha English name", "Regional English description")]
        [InlineData("zh-CN", "Alpha 中文名称", "繁體說明")]
        public async Task ScriptManagerDisplaysAndSearchesLocalizedMetadata(string language, string name, string description)
        {
            var previous = UiLanguage.CurrentCulture;
            UiLanguage.Apply(language);
            try
            {
                const string source = "// ==UserScript==\n" +
                    "// @name Zulu default name\n// @name:en Alpha English name\n// @name:zh-CN Alpha 中文名称\n" +
                    "// @description Default description\n// @description:en-US Regional English description\n// @description:zh-TW 繁體說明\n" +
                    "// @match https://example.com/*\n// @grant none\n// ==/UserScript==\n";
                const string otherSource = "// ==UserScript==\n// @name Bravo fallback\n// @name:ja 他の名前\n" +
                    "// @description:ja 説明\n// @match https://example.com/*\n// @grant none\n// ==/UserScript==\n";
                using (var fixture = await PersistenceTests.Fixture.Create())
                using (var content = new HttpContentService())
                {
                    var repository = TestRepository.Create();
                    await repository.InstallAsync(otherSource, "fixture://other", true, CancellationToken.None);
                    var installation = await repository.InstallAsync(source, "fixture://localized", true, CancellationToken.None);
                    string confirmation = null;
                    using (var manager = new ScriptManagerViewModel(repository, content, new UserScriptUpdateService(repository, content),
                        message => { confirmation = message; return false; },
                        new DiagnosticsViewModel(Dispatcher.CurrentDispatcher, fixture.Paths.LogsDirectory)))
                    {
                        await manager.RefreshAsync();
                        var sorted = manager.FilteredScripts.Cast<ScriptItemViewModel>().ToArray();
                        Assert.Equal(new[] { name, "Bravo fallback" }, sorted.Select(item => item.Name));
                        Assert.Equal(description, sorted[0].Description);
                        Assert.Equal(Strings.Get("NoDescription"), sorted[1].Description);
                        foreach (var query in new[] { name, description, "Zulu default name", "Default description" })
                        {
                            manager.Search = query;
                            Assert.Same(sorted[0], Assert.Single(manager.FilteredScripts.Cast<ScriptItemViewModel>()));
                        }
                        manager.Search = string.Empty;
                        manager.SelectedScript = sorted[0];
                        Assert.Equal(name, manager.Details.Name);
                        Assert.Equal(description, manager.Details.Description);
                        var summary = ScriptEditorViewModel.Describe(installation.Definition.ParseResult);
                        Assert.Contains(name, summary);
                        Assert.Contains(description, summary);

                        manager.NewCommand.Execute(null);
                        manager.Editor.Source = source;
                        await manager.SaveCommand.ExecuteAsync(null);
                        Assert.Contains(name, confirmation);
                        Assert.Contains(description, confirmation);
                    }
                }
            }
            finally { UiLanguage.Apply(previous.Name); }
        }

        [Theory]
        [InlineData("en", "Regional name")]
        [InlineData("zh-CN", "繁體名稱")]
        public Task ScriptMenuAndPermissionPromptUseTheApplicationLanguage(string language, string name) => HostServiceTests.OnDispatcher(async () =>
        {
            var previous = UiLanguage.CurrentCulture;
            UiLanguage.Apply(language);
            try
            {
                const string source = "// ==UserScript==\n// @name Default name\n// @name:en-US Regional name\n// @name:zh-TW 繁體名稱\n" +
                    "// @match https://example.com/*\n// @grant GM.download\n// ==/UserScript==\n";
                var repository = TestRepository.Create();
                var script = await repository.InstallAsync(source, "fixture://localized", true, CancellationToken.None);
                var tab = new BrowserTabViewModel("about:blank");
                var menu = new WpfMenuService(tab, Dispatcher.CurrentDispatcher, repository);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
                using (await menu.RegisterAsync(new MenuCommandRequest(script.ScriptKey, "Run script", "r"), _ => { }, CancellationToken.None))
                {
                    Assert.Equal(new[] { name, "Run script" }, tab.MenuRows.Select(row => row.Label));
                    Assert.Equal(script.ScriptKey, tab.MenuRows[0].ScriptKey);
                }

                var frame = new DocumentFrame("browser", "document", "main", new Uri("https://example.com/"), true, TimingGuarantee.BestEffortDocumentStart);
                var request = new ApiAuthorizationRequest(script, frame, "GM.download", "https://example.com/file", "summary", new[] { "GM.download" });
                var prompt = new WpfPermissionPrompt(Dispatcher.CurrentDispatcher);
                string heading = null;
                prompt.Opened += dialog =>
                {
                    heading = ((StackPanel)dialog.Content).Children.OfType<TextBlock>().First().Text;
                    dialog.Close();
                };
                await prompt.PromptAsync(request, UserScriptPermissionScope.From(request), CancellationToken.None);
                Assert.Equal(name, heading);
            }
            finally { UiLanguage.Apply(previous.Name); }
        });

        private static string[] FormatArguments(string value) => Regex.Matches(value, @"\{\d+\}")
            .Cast<Match>().Select(match => match.Value).OrderBy(item => item).ToArray();
    }
}
