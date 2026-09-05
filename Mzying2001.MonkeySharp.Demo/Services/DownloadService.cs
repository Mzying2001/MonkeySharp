using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Demo.ViewModels;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Mzying2001.MonkeySharp.Demo.Services
{
    public sealed class DownloadItemViewModel : ObservableObject
    {
        private string _status = "等待下载";
        private long _loaded;
        public DownloadItemViewModel(string name, Action cancel) { Name = name; CancelCommand = new RelayCommand(cancel); }
        public string Name { get; }
        public string Status { get => _status; set => SetProperty(ref _status, value); }
        public long Loaded { get => _loaded; set => SetProperty(ref _loaded, value); }
        public IRelayCommand CancelCommand { get; }
    }

    public sealed class DownloadService : IDownloadService, IDisposable
    {
        private readonly HttpClient _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = Timeout.InfiniteTimeSpan };
        private readonly string _directory;
        private readonly Dispatcher _dispatcher;
        private readonly MainWindowViewModel _main;
        private readonly ConcurrentDictionary<string, DownloadOperation> _operations = new ConcurrentDictionary<string, DownloadOperation>();
        private bool _disposed;
        public DownloadService(string directory, Dispatcher dispatcher, MainWindowViewModel main)
        { _directory = directory; _dispatcher = dispatcher; _main = main; }

        public async Task<IDownloadOperation> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DownloadService));
            HttpContentService.RequireHttp(request.Url.AbsoluteUri);
            var name = SafeFileName(string.IsNullOrWhiteSpace(request.Name) ? Path.GetFileName(request.Url.AbsolutePath) : request.Name);
            var path = Path.Combine(_directory, name);
            if (request.SaveAs)
            {
                path = await _dispatcher.InvokeAsync(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var dialog = new SaveFileDialog { FileName = name, InitialDirectory = _directory, OverwritePrompt = true };
                    return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
                });
                if (path == null) throw new OperationCanceledException("Download destination selection was canceled.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed) throw new ObjectDisposedException(nameof(DownloadService));
            var operation = new DownloadOperation(_client, request.Url, path, request.SaveAs, cancellationToken);
            _operations.TryAdd(operation.DownloadId, operation);
            var item = new DownloadItemViewModel(name, operation.Abort);
            Post(() => _main.Downloads.Add(item));
            operation.Progress += (sender, args) => Post(() => { item.Loaded = args.Loaded; item.Status = "下载中"; });
            operation.Completed += (sender, args) => Post(() => item.Status = "完成");
            operation.Aborted += (sender, args) => Post(() => item.Status = "已取消");
            operation.Failed += (sender, args) => Post(() => { item.Status = "失败"; _main.Diagnostics.Report(args.Error.Message); });
            operation.Start();
            _ = operation.Work.ContinueWith(completed =>
            {
                var observedError = operation.Completion.Exception;
                _operations.TryRemove(operation.DownloadId, out var removed);
            }, TaskScheduler.Default);
            return operation;
        }

        private void Post(Action action) { if (!_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(action); }
        public void Dispose()
        {
            _disposed = true;
            foreach (var operation in _operations.Values) operation.Abort();
            _client.Dispose();
        }
        public Task DrainAsync() => Task.WhenAll(_operations.Values.Select(operation => operation.Work.ContinueWith(completed => { var error = completed.Exception; })));

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

        private sealed class DownloadOperation : IDownloadOperation
        {
            private readonly HttpClient _client;
            private readonly Uri _url;
            private readonly string _path;
            private readonly bool _overwrite;
            private readonly CancellationTokenSource _cancellation;
            private readonly TaskCompletionSource<object> _completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly object _sync = new object();
            private bool _finished;
            private int _terminal;
            private Exception _error;
            private EventHandler _completed;
            private EventHandler<UserScriptDownloadFailure> _failed;
            private EventHandler _aborted;
            public DownloadOperation(HttpClient client, Uri url, string path, bool overwrite, CancellationToken token)
            {
                _client = client; _url = url; _path = path; _overwrite = overwrite;
                _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                _cancellation.CancelAfter(TimeSpan.FromMinutes(10));
            }
            public string DownloadId { get; } = Guid.NewGuid().ToString("N");
            public Task Completion => _completion.Task;
            public Task Work { get; private set; } = Task.CompletedTask;
            public event EventHandler<UserScriptDownloadProgress> Progress;
            public event EventHandler Completed
            {
                add { bool replay; lock (_sync) { _completed += value; replay = _terminal == 1; } if (replay) Raise(() => value(this, EventArgs.Empty)); }
                remove { lock (_sync) _completed -= value; }
            }
            public event EventHandler<UserScriptDownloadFailure> Failed
            {
                add { Exception error; lock (_sync) { _failed += value; error = _terminal == 2 ? _error : null; } if (error != null) Raise(() => value(this, new UserScriptDownloadFailure(error))); }
                remove { lock (_sync) _failed -= value; }
            }
            public event EventHandler Aborted
            {
                add { bool replay; lock (_sync) { _aborted += value; replay = _terminal == 3; } if (replay) Raise(() => value(this, EventArgs.Empty)); }
                remove { lock (_sync) _aborted -= value; }
            }
            public void Start() { Work = Task.Run(RunAsync); }
            public void Abort() { lock (_sync) if (!_finished) _cancellation.Cancel(); }
            public void Dispose() => Abort();

            private async Task RunAsync()
            {
                var partial = _path + "." + DownloadId + ".part";
                try
                {
                    using (var response = await HttpContentService.GetResponseAsync(_client, _url, _cancellation.Token).ConfigureAwait(false))
                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                    {
                        var buffer = new byte[65536];
                        long loaded = 0;
                        int count;
                        while ((count = await input.ReadAsync(buffer, 0, buffer.Length, _cancellation.Token).ConfigureAwait(false)) != 0)
                        {
                            await output.WriteAsync(buffer, 0, count, _cancellation.Token).ConfigureAwait(false);
                            loaded += count;
                            Raise(() => Progress?.Invoke(this, new UserScriptDownloadProgress(loaded, response.Content.Headers.ContentLength)));
                        }
                        output.Flush(true);
                    }
                    _cancellation.Token.ThrowIfCancellationRequested();
                    if (_overwrite && File.Exists(_path)) File.Replace(partial, _path, null);
                    else
                    {
                        var destination = _path;
                        for (var suffix = 1; ; suffix++)
                        {
                            try { File.Move(partial, destination); break; }
                            catch (IOException) when (!_overwrite && File.Exists(destination))
                            { destination = Path.Combine(Path.GetDirectoryName(_path), Path.GetFileNameWithoutExtension(_path) + " (" + suffix + ")" + Path.GetExtension(_path)); }
                        }
                    }
                    Finish(1, null);
                    _completion.TrySetResult(null);
                }
                catch (OperationCanceledException)
                {
                    Finish(3, null);
                    _completion.TrySetCanceled();
                }
                catch (Exception exception)
                {
                    Finish(2, exception);
                    _completion.TrySetException(exception);
                }
                finally
                {
                    lock (_sync) { _finished = true; _cancellation.Dispose(); }
                    try { if (File.Exists(partial)) File.Delete(partial); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }

            private void Finish(int terminal, Exception error)
            {
                EventHandler completed;
                EventHandler aborted;
                EventHandler<UserScriptDownloadFailure> failed;
                lock (_sync)
                {
                    _terminal = terminal; _error = error;
                    completed = _completed; aborted = _aborted; failed = _failed;
                }
                if (terminal == 1) Raise(() => completed?.Invoke(this, EventArgs.Empty));
                else if (terminal == 2) Raise(() => failed?.Invoke(this, new UserScriptDownloadFailure(error)));
                else Raise(() => aborted?.Invoke(this, EventArgs.Empty));
            }

            private static void Raise(Action callback)
            {
                try { callback(); } catch (Exception exception) { System.Diagnostics.Trace.TraceError(exception.ToString()); }
            }
        }
    }
}
