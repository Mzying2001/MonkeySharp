using Mzying2001.MonkeySharp.Demo.Localization;
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
        private static readonly IReadOnlyList<CompatibilityStatusViewModel> EmptyStatuses =
            new CompatibilityStatusViewModel[0];

        public string MetadataStatus { get; private set; } = Strings.Get("MetadataNotParsed");
        public string Name { get; private set; } = Strings.Get("Unrecognized");
        public string Version { get; private set; } = Strings.Get("NotProvided");
        public string Namespace { get; private set; } = Strings.Get("NotProvided");
        public string Author { get; private set; } = Strings.Get("NotProvided");
        public string Description { get; private set; } = Strings.Get("NotProvided");
        public string SourceOrigin { get; private set; } = Strings.Get("NotProvided");
        public string RunAt { get; private set; } = Strings.Get("NotProvided");
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
                ? Strings.Get("NoMetadata")
                : parsed.CanEnable ? Strings.Get("ValidMetadata") : Strings.Get("MetadataErrors");
            Name = metadata?.Name ?? Strings.Get("Unrecognized");
            Version = metadata?.Version ?? Strings.Get("NotProvided");
            Namespace = metadata?.Namespace ?? Strings.Get("NotProvided");
            Author = metadata?.Author ?? Strings.Get("NotProvided");
            Description = metadata?.Description ?? Strings.Get("NotProvided");
            SourceOrigin = string.IsNullOrWhiteSpace(sourceOrigin) ? Strings.Get("NotProvided") : sourceOrigin;
            RunAt = metadata == null ? Strings.Get("NotProvided") : FormatRunAt(metadata.RunAt);
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
            return Strings.Format("ConfirmationDetails", Name, Version, Author, Description,
                Join(Matches), Join(Includes), Join(Excludes), Join(Grants), Join(Connects),
                Join(Requires), Join(Resources), Join(CompatibilityStatuses.Select(item => item.Display)), Join(Diagnostics));
        }

        private static IReadOnlyList<CompatibilityStatusViewModel> BuildCompatibilityStatuses(
            UserScriptMetadata metadata, string source)
        {
            if (metadata == null) return EmptyStatuses;

            var difference = new CompatibilityStatusViewModel(
                Strings.Get("TampermonkeyDifferences"), Strings.Get("TampermonkeyDifferencesHint"), "#B7791F");
            var statuses = new List<CompatibilityStatusViewModel>
            {
                new CompatibilityStatusViewModel(
                    Strings.Get("CoreImplemented"),
                    Strings.Get("CoreImplementedHint"),
                    "#2F855A"),
                difference
            };

            var hostCapabilities = new[]
            {
                "GM.getResourceText", "GM.getResourceUrl", "GM.xmlHttpRequest", "GM.registerMenuCommand",
                "GM.unregisterMenuCommand", "GM.notification", "GM.setClipboard", "GM.openInTab", "GM.download",
                "GM.getTab", "GM.saveTab", "GM.getTabs", "GM.cookie", "GM.webRequest", "GM.audio", "window.close", "window.focus"
            };
            var requestedHostCapabilities = metadata.Grants.Where(item => hostCapabilities.Contains(item, StringComparer.Ordinal)).ToList();
            if (metadata.Requires.Count != 0 || metadata.Resources.Count != 0 || requestedHostCapabilities.Count != 0)
            {
                var details = requestedHostCapabilities.Count == 0
                    ? Strings.Get("HostResourcesHint")
                    : Strings.Format("HostCapabilitiesHint", string.Join(", ", requestedHostCapabilities));
                statuses.Insert(1, new CompatibilityStatusViewModel(Strings.Get("HostServicesRequired"), details, "#2B6CB0"));
            }

            var unsupported = new List<string>();
            if (!string.IsNullOrWhiteSpace(metadata.Sandbox) && !new[] { "raw", "JavaScript", "DOM" }.Contains(metadata.Sandbox, StringComparer.OrdinalIgnoreCase))
                unsupported.Add(Strings.Get("SandboxUnsupported"));
            if (metadata.Unwrap) unsupported.Add(Strings.Get("UnwrapUnsupported"));
            if (!string.IsNullOrWhiteSpace(metadata.InjectInto) && !new[] { "page", "content", "auto" }.Contains(metadata.InjectInto, StringComparer.Ordinal))
                unsupported.Add(Strings.Get("InjectIntoUnsupported"));
            if (metadata.RunIn.Any(value => value.StartsWith("container-id-", StringComparison.Ordinal))) unsupported.Add(Strings.Get("RunInUnsupported"));
            if (!string.IsNullOrWhiteSpace(source))
            {
                var sourceLower = source.ToLowerInvariant();
                var requestOptions = new[] { "proxy", "cookiepartition", "responsexml", "binary", "synchronous" }
                    .Where(option => sourceLower.IndexOf(option, StringComparison.Ordinal) >= 0).ToList();
                if (requestOptions.Count != 0)
                    unsupported.Add(Strings.Format("XhrUnsupported", string.Join(", ", requestOptions)));
            }
            if (unsupported.Count != 0)
            {
                statuses.Insert(1, new CompatibilityStatusViewModel(
                    Strings.Get("Unsupported"), string.Join("; ", unsupported), "#B42318"));
            }

            if (metadata.WebRequest.Count != 0)
            {
                statuses.Remove(difference);
                statuses.Add(new CompatibilityStatusViewModel(
                    difference.Category,
                    difference.Message + Strings.Get("StaticWebRequestHint"),
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
            return list.Count == 0 ? Strings.Get("None") : string.Join(", ", list);
        }

        private static string FormatRunAt(UserScriptRunAt runAt)
        {
            switch (runAt)
            {
                case UserScriptRunAt.DocumentStart: return "document-start";
                case UserScriptRunAt.DocumentBody: return "document-body";
                case UserScriptRunAt.DocumentEnd: return "document-end";
                case UserScriptRunAt.DocumentIdle: return "document-idle";
                case UserScriptRunAt.ContextMenu: return "context-menu";
                default: return runAt.ToString();
            }
        }
    }
}
