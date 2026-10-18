using CommunityToolkit.Mvvm.ComponentModel;
using Mzying2001.MonkeySharp.Core.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mzying2001.MonkeySharp.Demo.ViewModels
{
    public sealed class CompatibilityStatusViewModel
    {
        public CompatibilityStatusViewModel(string category, string message, string brush)
        {
            Category = category ?? throw new ArgumentNullException(nameof(category));
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Brush = brush ?? throw new ArgumentNullException(nameof(brush));
        }

        public string Category { get; }
        public string Message { get; }
        public string Brush { get; }
        public string Display => Category + "：" + Message;
    }

    public sealed class ScriptDetailsViewModel : ObservableObject
    {
        private static readonly IReadOnlyList<string> Empty = new string[0];
        private static readonly IReadOnlyList<CompatibilityStatusViewModel> EmptyStatuses =
            new CompatibilityStatusViewModel[0];

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
        public IReadOnlyList<CompatibilityStatusViewModel> CompatibilityStatuses { get; private set; } = EmptyStatuses;

        public void Update(MetadataParseResult parsed, string sourceOrigin)
        {
            Update(parsed, sourceOrigin, null);
        }

        public void Update(MetadataParseResult parsed, string sourceOrigin, string source)
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
            CompatibilityStatuses = BuildCompatibilityStatuses(metadata, source);
            OnPropertyChanged(string.Empty);
        }

        public string ToConfirmationText()
        {
            return Name + "  v" + Version + "\n作者：" + Author + "\n" + Description +
                "\n匹配：" + Join(Matches) + "\nInclude：" + Join(Includes) +
                "\n排除：" + Join(Excludes) + "\n权限：" + Join(Grants) +
                "\nConnect：" + Join(Connects) + "\nRequire：" + Join(Requires) +
                "\nResource：" + Join(Resources) + "\n兼容性：" + Join(CompatibilityStatuses.Select(item => item.Display)) +
                "\n诊断：" + Join(Diagnostics);
        }

        private static IReadOnlyList<CompatibilityStatusViewModel> BuildCompatibilityStatuses(
            UserScriptMetadata metadata, string source)
        {
            if (metadata == null) return EmptyStatuses;

            var statuses = new List<CompatibilityStatusViewModel>
            {
                new CompatibilityStatusViewModel(
                    "Core 已实现",
                    "元数据解析、授权桥和基础 GM 门面已纳入当前运行时契约。",
                    "#2F855A"),
                new CompatibilityStatusViewModel(
                    "与 Tampermonkey 有差异",
                    "脚本在 page world 的 MonkeySharp wrapper 中执行；unsafeWindow 不是隔离安全边界。",
                    "#B7791F")
            };

            var hostCapabilities = new[]
            {
                "GM.getResourceText", "GM.getResourceURL", "GM.xmlHttpRequest", "GM.registerMenuCommand",
                "GM.unregisterMenuCommand", "GM.notification", "GM.setClipboard", "GM.openInTab", "GM.download",
                "GM.getTab", "GM.saveTab", "GM.getTabs", "GM.cookie", "GM.webRequest", "window.close", "window.focus"
            };
            var requestedHostCapabilities = metadata.Grants.Where(item => hostCapabilities.Contains(item, StringComparer.Ordinal)).ToList();
            if (metadata.Requires.Count != 0 || metadata.Resources.Count != 0 || requestedHostCapabilities.Count != 0)
            {
                var details = requestedHostCapabilities.Count == 0
                    ? "@require/@resource 需要宿主提供依赖或资源服务。"
                    : "以下能力取决于宿主 service：" + string.Join(", ", requestedHostCapabilities) + "。";
                statuses.Insert(1, new CompatibilityStatusViewModel("需要宿主 service", details, "#2B6CB0"));
            }

            var unsupported = new List<string>();
            if (!string.IsNullOrWhiteSpace(metadata.Sandbox) &&
                !string.Equals(metadata.Sandbox, "raw", StringComparison.OrdinalIgnoreCase))
                unsupported.Add("@sandbox 非 raw 不创建隔离世界");
            if (metadata.Unwrap) unsupported.Add("@unwrap 不移除 MonkeySharp wrapper");
            if (!string.IsNullOrWhiteSpace(metadata.InjectInto) &&
                !string.Equals(metadata.InjectInto, "page", StringComparison.OrdinalIgnoreCase))
                unsupported.Add("@inject-into 非 page 不可用");
            if (!string.IsNullOrWhiteSpace(metadata.RunIn)) unsupported.Add("@run-in 未实现");
            if (!string.IsNullOrWhiteSpace(source))
            {
                var sourceLower = source.ToLowerInvariant();
                var requestOptions = new[] { "proxy", "cookiepartition", "responsexml", "binary", "synchronous" }
                    .Where(option => sourceLower.IndexOf(option, StringComparison.Ordinal) >= 0).ToList();
                if (requestOptions.Count != 0)
                    unsupported.Add("XHR 输入包含当前不支持或语义不同的选项：" + string.Join(", ", requestOptions));
            }
            if (unsupported.Count != 0)
            {
                statuses.Insert(1, new CompatibilityStatusViewModel(
                    "有意降级/不支持", string.Join("；", unsupported) + "。", "#B42318"));
            }

            if (metadata.WebRequest.Count != 0)
            {
                var difference = statuses.First(item => item.Category == "与 Tampermonkey 有差异");
                statuses.Remove(difference);
                statuses.Add(new CompatibilityStatusViewModel(
                    difference.Category,
                    difference.Message + "静态 @webRequest 仅写入 GM.info，不会自动安装网络规则。",
                    difference.Brush));
            }
            return statuses;
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
