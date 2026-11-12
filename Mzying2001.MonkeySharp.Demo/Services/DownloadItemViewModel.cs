using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace Mzying2001.MonkeySharp.Demo.Services
{
    public sealed partial class DownloadItemViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _status = "等待下载";

        [ObservableProperty]
        private long _loaded;

        private readonly Action _cancel;

        public DownloadItemViewModel(string name, Action cancel) { Name = name; _cancel = cancel; }

        public string Name { get; }

        [RelayCommand]
        private void Cancel() => _cancel();
    }
}
