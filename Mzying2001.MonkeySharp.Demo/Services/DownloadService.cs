using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Demo.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

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

    public sealed class DownloadService : IDownloadService, IDisposable
    {
        private readonly FileDownloadService _pipeline;
        private readonly string _directory;
        private readonly Dispatcher _dispatcher;
        private readonly MainWindowViewModel _main;
        private bool _disposed;

        public DownloadService(string directory, Dispatcher dispatcher, MainWindowViewModel main,
            IHttpRequestService http,
            Func<DownloadRequest, CancellationToken, Task<DownloadDestination>> destination = null)
        {
            _directory = Path.GetFullPath(directory); _dispatcher = dispatcher; _main = main;
            Directory.CreateDirectory(_directory);
            _pipeline = new FileDownloadService(http, destination ?? SelectDestinationAsync);
        }
        public string DownloadMode => _pipeline.DownloadMode;

        public async Task<IDownloadOperation> CreateAsync(DownloadRequest request, CancellationToken cancellationToken)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DownloadService));
            var operation = await _pipeline.CreateAsync(request, cancellationToken).ConfigureAwait(false);
            var name = SafeFileName(string.IsNullOrWhiteSpace(request.Name) ? Path.GetFileName(request.Url.AbsolutePath) : request.Name);
            var item = new DownloadItemViewModel(name, operation.Abort);
            Post(() => _main.Downloads.Add(item));
            operation.Progress += (_, progress) => Post(() => { item.Loaded = progress.Loaded; item.Status = "\u4e0b\u8f7d\u4e2d"; });
            operation.Completed += (_, __) => Post(() => item.Status = "\u5b8c\u6210");
            operation.Aborted += (_, __) => Post(() => item.Status = "\u5df2\u53d6\u6d88");
            operation.TimedOut += (_, __) => Post(() => item.Status = "\u8d85\u65f6");
            operation.Failed += (_, failure) => Post(() => { item.Status = "\u5931\u8d25"; _main.Diagnostics.Report(failure.Error.Message); });
            return operation;
        }

        private async Task<DownloadDestination> SelectDestinationAsync(DownloadRequest request, CancellationToken cancellationToken)
        {
            var name = SafeFileName(string.IsNullOrWhiteSpace(request.Name) ? Path.GetFileName(request.Url.AbsolutePath) : request.Name);
            var path = Path.Combine(_directory, name);
            var prompt = request.SaveAs || request.ConflictAction == "prompt";
            if (prompt)
            {
                path = await _dispatcher.InvokeAsync(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return DownloadFilePicker.Select(name, _directory, cancellationToken);
                });
            }
            cancellationToken.ThrowIfCancellationRequested();
            return path == null ? null : new DownloadDestination(path, prompt || request.ConflictAction == "overwrite");
        }

        private void Post(Action action) { if (!_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(action); }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; _pipeline.Dispose();
        }
        public Task DrainAsync() => _pipeline.DrainAsync();

        public static string SafeFileName(string suggested)
        {
            var name = (suggested ?? string.Empty).Replace('\\', '/');
            name = name.Substring(name.LastIndexOf('/') + 1);
            name = new string(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character).ToArray()).Trim().TrimEnd('.');
            if (name.Length > 150) name = name.Substring(0, 150).TrimEnd('.');
            var stem = name.Split('.')[0].ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
                Enumerable.Range(1, 9).Any(index => stem == "COM" + index || stem == "LPT" + index)) name = "_" + name;
            return string.IsNullOrEmpty(name) ? "download.bin" : name;
        }

    }
}
