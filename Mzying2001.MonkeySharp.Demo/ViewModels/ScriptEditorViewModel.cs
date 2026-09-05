using CommunityToolkit.Mvvm.ComponentModel;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Parsing;
using System.Linq;

namespace Mzying2001.MonkeySharp.Demo.ViewModels
{
    public sealed class ScriptEditorViewModel : ObservableObject
    {
        public const string Template = "// ==UserScript==\n// @name My userscript\n// @namespace demo.local\n// @version 1.0\n// @description A MonkeySharp demo script\n// @match https://example.com/*\n// @grant none\n// @run-at document-end\n// ==/UserScript==\n\ndocument.documentElement.dataset.monkeySharp = 'ready';\n";
        private string _source = Template;
        private string _saved = Template;
        public string Source
        {
            get => _source;
            set { if (SetProperty(ref _source, value)) OnPropertyChanged(nameof(IsDirty)); }
        }
        public bool IsDirty => _source != _saved;
        public void Load(string source) { _saved = source; Source = source; OnPropertyChanged(nameof(IsDirty)); }
        public MetadataParseResult Parse() => new UserScriptMetadataParser().Parse(Source ?? string.Empty);
        public static string Describe(MetadataParseResult parsed)
        {
            var metadata = parsed.Metadata;
            var diagnostics = string.Join("\n", parsed.Diagnostics.Select(item => item.Severity + " " + item.Code + " L" + item.LineNumber + ": " + item.Message));
            if (metadata == null) return diagnostics;
            return metadata.Name + "  v" + metadata.Version + "\n作者：" + metadata.Author + "\n" + metadata.Description +
                "\n匹配：" + string.Join(", ", metadata.Matches) + "\nInclude：" + string.Join(", ", metadata.Includes) +
                "\n排除：" + string.Join(", ", metadata.Excludes.Concat(metadata.ExcludeMatches)) +
                "\n权限：" + string.Join(", ", metadata.DeclaredGrants) + "\nConnect：" + string.Join(", ", metadata.Connects) +
                "\nRequire：" + string.Join(", ", metadata.Requires) +
                "\nResource：" + string.Join(", ", metadata.Resources.Select(item => item.Name + " = " + item.Url)) + "\n" + diagnostics;
        }
    }
}
