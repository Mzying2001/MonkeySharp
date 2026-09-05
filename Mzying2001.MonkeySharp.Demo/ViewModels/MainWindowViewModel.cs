using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace Mzying2001.MonkeySharp.Demo.ViewModels
{
    public sealed class MainWindowViewModel : ObservableObject
    {
        private BrowserTabViewModel _selectedTab;
        private string _address = "about:blank";
        private bool _showDiagnostics;
        private bool _closing;
        public MainWindowViewModel(DiagnosticsViewModel diagnostics)
        {
            Diagnostics = diagnostics;
            NewTabCommand = new RelayCommand(() => NewTab());
            CloseTabCommand = new RelayCommand<BrowserTabViewModel>(tab => CloseTab(tab ?? SelectedTab));
            SelectTabCommand = new RelayCommand<BrowserTabViewModel>(tab => SelectedTab = tab);
            NavigateCommand = new RelayCommand(Navigate);
            BackCommand = new RelayCommand(() => SelectedTab?.Browser?.Back(), () => SelectedTab?.CanGoBack == true);
            ForwardCommand = new RelayCommand(() => SelectedTab?.Browser?.Forward(), () => SelectedTab?.CanGoForward == true);
            ReloadCommand = new RelayCommand(() => SelectedTab?.Browser?.Reload());
            StopCommand = new RelayCommand(() => SelectedTab?.Browser?.Stop());
            DevToolsCommand = new RelayCommand(() => SelectedTab?.Browser?.ShowDevTools());
            ScriptManagerCommand = new RelayCommand(() => ScriptManagerRequested?.Invoke(this, EventArgs.Empty));
            DiagnosticsCommand = new RelayCommand(() => ShowDiagnostics = !ShowDiagnostics);
        }

        public ObservableCollection<BrowserTabViewModel> Tabs { get; } = new ObservableCollection<BrowserTabViewModel>();
        public ObservableCollection<Services.DownloadItemViewModel> Downloads { get; } = new ObservableCollection<Services.DownloadItemViewModel>();
        public DiagnosticsViewModel Diagnostics { get; }
        public bool ShowDiagnostics { get => _showDiagnostics; set => SetProperty(ref _showDiagnostics, value); }
        public string Address { get => _address; set => SetProperty(ref _address, value); }
        public BrowserTabViewModel SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (!SetProperty(ref _selectedTab, value)) return;
                Address = value?.Address ?? "about:blank";
                UpdateNavigation();
            }
        }

        public IRelayCommand NewTabCommand { get; }
        public IRelayCommand<BrowserTabViewModel> CloseTabCommand { get; }
        public IRelayCommand<BrowserTabViewModel> SelectTabCommand { get; }
        public IRelayCommand NavigateCommand { get; }
        public IRelayCommand BackCommand { get; }
        public IRelayCommand ForwardCommand { get; }
        public IRelayCommand ReloadCommand { get; }
        public IRelayCommand StopCommand { get; }
        public IRelayCommand DevToolsCommand { get; }
        public IRelayCommand ScriptManagerCommand { get; }
        public IRelayCommand DiagnosticsCommand { get; }
        public event EventHandler ScriptManagerRequested;
        public event EventHandler<BrowserTabViewModel> TabClosed;

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
