using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;

namespace Mzying2001.MonkeySharp.Demo.ViewModels
{
    public sealed partial class BrowserTabViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _title = "新标签页";

        [ObservableProperty]
        private string _address;

        [ObservableProperty]
        private string _iconUrl;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private bool _canGoBack;

        [ObservableProperty]
        private bool _canGoForward;

        [ObservableProperty]
        private string _error;

        public string TabId { get; } = Guid.NewGuid().ToString("N");
        public bool IsIncognito { get; }
        public string PrivacyLabel => IsIncognito ? "私密 " : string.Empty;
        public string ParentTabId { get; set; }
        public IBrowserCommands Browser { get; set; }
        public ObservableCollection<Services.ScriptMenuCommand> MenuCommands { get; } = new ObservableCollection<Services.ScriptMenuCommand>();
        public bool IsClosed { get; private set; }
        public event EventHandler Closed;

        public BrowserTabViewModel(string address, bool isIncognito = false)
        {
            Address = address;
            IsIncognito = isIncognito;
        }

        internal void Close()
        {
            if (IsClosed) return;
            IsClosed = true;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
