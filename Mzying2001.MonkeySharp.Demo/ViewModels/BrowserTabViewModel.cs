using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;

namespace Mzying2001.MonkeySharp.Demo.ViewModels
{
    public interface IBrowserCommands
    {
        void Load(string address);
        void Back();
        void Forward();
        void Reload();
        void Stop();
        void ShowDevTools();
    }

    public sealed class BrowserTabViewModel : ObservableObject
    {
        private string _title = "新标签页";
        private string _address;
        private bool _isLoading;
        private bool _canGoBack;
        private bool _canGoForward;
        private string _error;
        public BrowserTabViewModel(string address) { Address = address; }
        public string TabId { get; } = Guid.NewGuid().ToString("N");
        public string ParentTabId { get; set; }
        public string Title { get => _title; set => SetProperty(ref _title, value); }
        public string Address { get => _address; set => SetProperty(ref _address, value); }
        public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
        public bool CanGoBack { get => _canGoBack; set => SetProperty(ref _canGoBack, value); }
        public bool CanGoForward { get => _canGoForward; set => SetProperty(ref _canGoForward, value); }
        public string Error { get => _error; set => SetProperty(ref _error, value); }
        public IBrowserCommands Browser { get; set; }
        public ObservableCollection<Services.ScriptMenuCommand> MenuCommands { get; } = new ObservableCollection<Services.ScriptMenuCommand>();
        public bool IsClosed { get; private set; }
        public event EventHandler Closed;
        internal void Close()
        {
            if (IsClosed) return;
            IsClosed = true;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
