using CommunityToolkit.Mvvm.ComponentModel;
using Mzying2001.MonkeySharp.Core.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mzying2001.MonkeySharp.Demo.ViewModels
{
    public sealed class ScriptDetailsViewModel : ObservableObject
    {
        private static readonly IReadOnlyList<string> Empty = new string[0];

        public string MetadataStatus { get; private set; } = "尚未解析脚本元数据。";
        public string Name { get; private set; } = "未识别";
        public string Version { get; private set; } = "未提供";
        public string Namespace { get; private set; } = "未提供";
        public string Author { get; private set; } = "未提供";
        public string Description { get; private set; } = "未提供";
        public string SourceOrigin { get; private set; } = "未提供";
        public string RunAt { get; private set; } = "未提供";
        public IReadOnlyList<string> Matches { get; private set; } = Empty;
        public IReadOnlyList<string> Includes { get; private set; } = Empty;
        public IReadOnlyList<string> Excludes { get; private set; } = Empty;
        public IReadOnlyList<string> Grants { get; private set; } = Empty;
        public IReadOnlyList<string> Connects { get; private set; } = Empty;
        public IReadOnlyList<string> Requires { get; private set; } = Empty;
        public IReadOnlyList<string> Resources { get; private set; } = Empty;
        public IReadOnlyList<string> Diagnostics { get; private set; } = Empty;

        public void Update(MetadataParseResult parsed, string sourceOrigin)
        {
            if (parsed == null) throw new ArgumentNullException(nameof(parsed));
            var metadata = parsed.Metadata;
            MetadataStatus = metadata == null
                ? "未找到有效元数据。"
                : parsed.CanEnable ? "元数据有效，可以启用。" : "元数据包含错误，无法启用。";
            Name = metadata?.Name ?? "未识别";
            Version = metadata?.Version ?? "未提供";
            Namespace = metadata?.Namespace ?? "未提供";
            Author = metadata?.Author ?? "未提供";
            Description = metadata?.Description ?? "未提供";
            SourceOrigin = string.IsNullOrWhiteSpace(sourceOrigin) ? "未提供" : sourceOrigin;
            RunAt = metadata == null ? "未提供" : FormatRunAt(metadata.RunAt);
            Matches = metadata == null ? Empty : ReadList(metadata.Matches);
            Includes = metadata == null ? Empty : ReadList(metadata.Includes);
            Excludes = metadata == null ? Empty : ReadList(metadata.Excludes.Concat(metadata.ExcludeMatches));
            Grants = metadata == null ? Empty : ReadList(metadata.DeclaredGrants);
            Connects = metadata == null ? Empty : ReadList(metadata.Connects);
            Requires = metadata == null ? Empty : ReadList(metadata.Requires.Select(item => item.Url));
            Resources = metadata == null ? Empty : ReadList(metadata.Resources.Select(item => item.Name + " = " + item.Url));
            Diagnostics = ReadList(parsed.Diagnostics.Select(item =>
                item.Severity + " " + item.Code + (item.LineNumber > 0 ? " L" + item.LineNumber : string.Empty) + ": " + item.Message));
            OnPropertyChanged(string.Empty);
        }

        public string ToConfirmationText()
        {
            return Name + "  v" + Version + "\n作者：" + Author + "\n" + Description +
                "\n匹配：" + Join(Matches) + "\nInclude：" + Join(Includes) +
                "\n排除：" + Join(Excludes) + "\n权限：" + Join(Grants) +
                "\nConnect：" + Join(Connects) + "\nRequire：" + Join(Requires) +
                "\nResource：" + Join(Resources) + "\n" + Join(Diagnostics);
        }

        private static IReadOnlyList<string> ReadList(IEnumerable<string> values)
        {
            return values.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        }

        private static string Join(IEnumerable<string> values)
        {
            var list = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            return list.Count == 0 ? "无" : string.Join(", ", list);
        }

        private static string FormatRunAt(UserScriptRunAt runAt)
        {
            switch (runAt)
            {
                case UserScriptRunAt.DocumentStart: return "document-start";
                case UserScriptRunAt.DocumentBody: return "document-body";
                case UserScriptRunAt.DocumentEnd: return "document-end";
                case UserScriptRunAt.DocumentIdle: return "document-idle";
                default: return runAt.ToString();
            }
        }
    }
}
