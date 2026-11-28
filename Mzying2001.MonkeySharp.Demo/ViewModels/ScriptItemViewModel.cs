using Mzying2001.MonkeySharp.Demo.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using Mzying2001.MonkeySharp.Core.Domain;
using System;

namespace Mzying2001.MonkeySharp.Demo.ViewModels
{
    public sealed class ScriptItemViewModel : ObservableObject
    {
        private UserScriptInstallation _installation;
        private bool _isBusy;

        public ScriptItemViewModel(UserScriptInstallation installation) { _installation = installation; }
        public UserScriptInstallation Installation => _installation;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (!SetProperty(ref _isBusy, value)) return;
                OnPropertyChanged(nameof(CanToggle));
            }
        }
        public bool CanToggle => !_isBusy;
        public string Name => Installation.Definition.Metadata?.GetLocalizedName(UiLanguage.CurrentCulture.Name) ?? ScriptKey;
        public string Version => string.IsNullOrWhiteSpace(Installation.Definition.Metadata?.Version) ? Strings.Get("NoVersion") : "v" + Installation.Definition.Metadata.Version;
        public string Description
        {
            get
            {
                var description = Installation.Definition.Metadata?.GetLocalizedDescription(UiLanguage.CurrentCulture.Name);
                return string.IsNullOrWhiteSpace(description) ? Strings.Get("NoDescription") : description;
            }
        }
        public string ScriptKey => Installation.ScriptKey.ToString();
        public string SearchText
        {
            get
            {
                var metadata = Installation.Definition.Metadata;
                return string.Join("\n", Name, Description, metadata?.Name, metadata?.Description, metadata?.Namespace, Installation.SourceOrigin);
            }
        }
        public string IconUrl
        {
            get
            {
                var metadata = Installation.Definition.Metadata;
                return !string.IsNullOrWhiteSpace(metadata?.Icon64Url) ? metadata.Icon64Url : metadata?.IconUrl;
            }
        }
        public string IconFallbackText => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Substring(0, 1).ToUpperInvariant();
        public string StatusLabel => !Installation.IsReady ? Strings.Format("UnavailableWithIntent", Strings.Get(Installation.IsEnabled ? "Enabled" : "Disabled")) : Installation.IsEnabled ? Strings.Get("Enabled") : Strings.Get("Disabled");
        public string ToggleLabel => Installation.IsEnabled ? Strings.Get("Disable") : Strings.Get("Enable");
        public string ToggleAutomationName => Installation.IsEnabled ? "Disable script" : "Enable script";
        public string StatusBrush => !Installation.IsReady ? "#B42318" : Installation.IsEnabled ? "#2F855A" : "#94A3B8";
        public string DisplayName => StatusLabel + " " + Name + "  " + Version;

        public void Update(UserScriptInstallation installation)
        {
            _installation = installation ?? throw new ArgumentNullException(nameof(installation));
            OnPropertyChanged(string.Empty);
        }
    }
}
