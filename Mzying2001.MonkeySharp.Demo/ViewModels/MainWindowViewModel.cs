using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace Mzying2001.MonkeySharp.Demo.ViewModels
{
    public sealed partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        private BrowserTabViewModel _selectedTab;

        [ObservableProperty]
        private string _address = "about:blank";

        [ObservableProperty]
        private bool _showDiagnostics;

        private bool _closing;

        public ObservableCollection<BrowserTabViewModel> Tabs { get; } = new ObservableCollection<BrowserTabViewModel>();
        public ObservableCollection<Services.DownloadItemViewModel> Downloads { get; } = new ObservableCollection<Services.DownloadItemViewModel>();
        public DiagnosticsViewModel Diagnostics { get; }

        [RelayCommand(CanExecute = nameof(CanGoBack))]
        private void Back() => SelectedTab?.Browser?.Back();

        [RelayCommand(CanExecute = nameof(CanGoForward))]
        private void Forward() => SelectedTab?.Browser?.Forward();

        [RelayCommand]
        private void Reload() => SelectedTab?.Browser?.Reload();

        [RelayCommand]
        private void Stop() => SelectedTab?.Browser?.Stop();

        [RelayCommand]
        private void DevTools() => SelectedTab?.Browser?.ShowDevTools();

        [RelayCommand]
        private void ScriptManager() => ScriptManagerRequested?.Invoke(this, EventArgs.Empty);

        [RelayCommand]
        private void ToggleDiagnostics() => ShowDiagnostics = !ShowDiagnostics;

        public IRelayCommand NewTabCommand => CreateNewTabCommand;

        public IRelayCommand DiagnosticsCommand => ToggleDiagnosticsCommand;

        public event EventHandler ScriptManagerRequested;
        public event EventHandler<BrowserTabViewModel> TabClosed;

        public MainWindowViewModel(DiagnosticsViewModel diagnostics)
        {
            Diagnostics = diagnostics;
        }

        partial void OnSelectedTabChanged(BrowserTabViewModel value)
        {
            Address = value?.Address ?? "about:blank";
            UpdateNavigation();
        }

        private bool CanGoBack() => SelectedTab?.CanGoBack == true;

        private bool CanGoForward() => SelectedTab?.CanGoForward == true;

        [RelayCommand]
        private void CreateNewTab() => NewTab();

        public BrowserTabViewModel NewTab(string address = "about:blank", bool active = true,
            BrowserTabViewModel origin = null, bool insert = false, bool setParent = false)
        {
            if (_closing) throw new InvalidOperationException("The browser is closing.");
            var tab = new BrowserTabViewModel(address) { ParentTabId = setParent ? origin?.TabId : null };
            tab.PropertyChanged += TabPropertyChanged;
            if (insert && origin != null && Tabs.Contains(origin)) Tabs.Insert(Tabs.IndexOf(origin) + 1, tab);
            else Tabs.Add(tab);
            if (active || SelectedTab == null) SelectedTab = tab;
            return tab;
        }

        [RelayCommand]
        public void CloseTab(BrowserTabViewModel tab)
        {
            if (tab == null || !Tabs.Contains(tab)) return;
            var index = Tabs.IndexOf(tab);
            if (SelectedTab == tab)
                SelectedTab = Tabs.FirstOrDefault(item => item != tab && item.TabId == tab.ParentTabId) ??
                    (index > 0 ? Tabs[index - 1] : Tabs.Skip(1).FirstOrDefault());
            tab.PropertyChanged -= TabPropertyChanged;
            tab.Close();
            Tabs.Remove(tab);
            TabClosed?.Invoke(this, tab);
            if (Tabs.Count == 0 && !_closing) NewTab();
        }

        public void CloseAll()
        {
            _closing = true;
            foreach (var tab in Tabs.ToArray()) CloseTab(tab);
        }

        [RelayCommand]
        private void Navigate()
        {
            try
            {
                var address = NormalizeAddress(Address);
                if (SelectedTab == null) NewTab(address);
                else { SelectedTab.Address = address; SelectedTab.Browser?.Load(address); }
            }
            catch (Exception exception) { Diagnostics.Report(exception.Message); ShowDiagnostics = true; }
        }

        public static string NormalizeAddress(string address)
        {
            var text = (address ?? string.Empty).Trim();
            if (text == "about:blank") return text;
            if (!text.Contains("://")) text = "https://" + text;
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") || string.IsNullOrEmpty(uri.Host))
                throw new ArgumentException("请输入 HTTP(S) 地址，或 about:blank。");
            return uri.AbsoluteUri;
        }

        private void TabPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            if (sender != SelectedTab) return;
            if (args.PropertyName == nameof(BrowserTabViewModel.Address)) Address = SelectedTab.Address;
            UpdateNavigation();
        }

        private void UpdateNavigation() { BackCommand.NotifyCanExecuteChanged(); ForwardCommand.NotifyCanExecuteChanged(); }
    }
}
