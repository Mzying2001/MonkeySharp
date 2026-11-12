using Mzying2001.MonkeySharp.Demo.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Parsing;
using System.Linq;

namespace Mzying2001.MonkeySharp.Demo.ViewModels
{
    public sealed partial class ScriptEditorViewModel : ObservableObject
    {
        public const string Template = "// ==UserScript==\n// @name My userscript\n// @namespace demo.local\n// @version 1.0\n// @description A MonkeySharp demo script\n// @match https://example.com/*\n// @grant none\n// @run-at document-end\n// ==/UserScript==\n\ndocument.documentElement.dataset.monkeySharp = 'ready';\n";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsDirty))]
        private string _source = Template;

        private string _saved = Template;

        public bool IsDirty => Source != _saved;

        public void Load(string source) { _saved = source; Source = source; OnPropertyChanged(nameof(IsDirty)); }

        public MetadataParseResult Parse() => new UserScriptMetadataParser().Parse(Source ?? string.Empty);

        public static string Describe(MetadataParseResult parsed)
        {
            var metadata = parsed.Metadata;
            var diagnostics = string.Join("\n", parsed.Diagnostics.Select(item => item.Severity + " " + item.Code + " L" + item.LineNumber + ": " + item.Message));
            if (metadata == null) return diagnostics;
            return Strings.Format("EditorDescription", metadata.Name, metadata.Version, metadata.Author, metadata.Description,
                string.Join(", ", metadata.Matches), string.Join(", ", metadata.Includes),
                string.Join(", ", metadata.Excludes.Concat(metadata.ExcludeMatches)), string.Join(", ", metadata.DeclaredGrants),
                string.Join(", ", metadata.Connects), string.Join(", ", metadata.Requires.Select(item => item.Url)),
                string.Join(", ", metadata.Resources.Select(item => item.Name + " = " + item.Url)), diagnostics);
        }
    }
}
