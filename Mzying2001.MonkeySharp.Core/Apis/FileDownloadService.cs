using Mzying2001.MonkeySharp.Core.Domain;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Apis
{
    /// <summary>A destination selected by trusted host UI or its download-directory policy.</summary>
    public sealed class DownloadDestination
    {
        /// <summary>Initializes an absolute destination and its confirmed overwrite policy.</summary>
        public DownloadDestination(string path, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathRooted(path)) throw new ArgumentException("An absolute destination is required.", nameof(path));
            Path = System.IO.Path.GetFullPath(path); Overwrite = overwrite;
        }
        /// <summary>Gets the host-selected absolute path.</summary>
        public string Path { get; }
        /// <summary>Gets whether replacing that exact file was approved by host policy or UI.</summary>
        public bool Overwrite { get; }
    }

    /// <summary>Transfers through the supplied browser network service and atomically commits files.</summary>
    public sealed class FileDownloadService : IDownloadService, IDisposable
    {
        private readonly IHttpRequestService _http;
        private readonly Func<DownloadRequest, CancellationToken, Task<DownloadDestination>> _destination;
        private readonly ConcurrentDictionary<string, Operation> _operations = new ConcurrentDictionary<string, Operation>();
        private readonly object _sync = new object();
        private bool _disposed;

        /// <summary>Initializes the shared network/file pipeline. The caller owns the HTTP service.</summary>
        public FileDownloadService(IHttpRequestService http,
            Func<DownloadRequest, CancellationToken, Task<DownloadDestination>> destination,
            string downloadMode = "browser")
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _destination = destination ?? throw new ArgumentNullException(nameof(destination));
            if (downloadMode != "native" && downloadMode != "browser") throw new ArgumentException("A working backend mode is required.", nameof(downloadMode));
            DownloadMode = downloadMode;
        }

        /// <inheritdoc />
        public string DownloadMode { get; }

        /// <inheritdoc />
        public Task<IDownloadOperation> CreateAsync(DownloadRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (DownloadMode != "browser" && (request.SaveAs || request.ConflictAction != "uniquify"))
                throw new UnsupportedApiException("Destination options require browser download mode.");
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(FileDownloadService));
                var operation = new Operation(this, request, cancellationToken);
                _operations.TryAdd(operation.DownloadId, operation);
                return Task.FromResult<IDownloadOperation>(operation);
            }
        }

        /// <summary>Waits for accepted downloads and cleanup without propagating their individual failures.</summary>
        public Task DrainAsync() => Task.WhenAll(_operations.Values.Select(operation => operation.Completion.ContinueWith(task => { var error = task.Exception; }, TaskScheduler.Default)));

        /// <summary>Cancels this service's operations. Drain before shutting down the network backend.</summary>
        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                foreach (var operation in _operations.Values) operation.Dispose();
            }
        }

        private sealed class Operation : IDownloadOperation, IUserScriptHttpObserver
        {
            private readonly FileDownloadService _owner;
            private readonly DownloadRequest _request;
            private readonly CancellationTokenSource _deadline = new CancellationTokenSource();
            private readonly CancellationTokenSource _cancellation;
            private readonly TaskCompletionSource<object> _completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly object _sync = new object();
            private FileStream _output;
            private Exception _responseError;
            private long _loaded;
            private long? _total;
            private int _started;
            private bool _finished;
            private bool _committed;

            internal Operation(FileDownloadService owner, DownloadRequest request, CancellationToken cancellationToken)
            {
                _owner = owner; _request = request;
                _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _deadline.Token);
            }
            public string DownloadId { get; } = Guid.NewGuid().ToString("N");
            public DownloadResult Result { get; private set; }
            public Task Completion => _completion.Task;
            public event EventHandler<UserScriptDownloadProgress> Progress;
            public event EventHandler Completed;
            public event EventHandler<UserScriptDownloadFailure> Failed;
            public event EventHandler Aborted;
            public event EventHandler TimedOut;

            public void Start()
            {
                if (Interlocked.Exchange(ref _started, 1) != 0) return;
                _deadline.CancelAfter(_request.Timeout ?? TimeSpan.FromMinutes(10));
                _ = Task.Run(RunAsync);
            }

            public void Abort()
            {
                lock (_sync) if (!_finished && !_committed) _cancellation.Cancel();
            }

            public void Dispose()
            {
                Abort();
                // An unstarted operation still needs to reach a terminal state on disposal.
                Start();
            }

            private async Task RunAsync()
            {
                string partial = null;
                Exception failure = null;
                var terminal = 0;
                try
                {
                    _cancellation.Token.ThrowIfCancellationRequested();
                    var destination = await _owner._destination(_request, _cancellation.Token).ConfigureAwait(false);
                    if (destination == null) throw new OperationCanceledException("Destination selection was canceled.");
                    _cancellation.Token.ThrowIfCancellationRequested();
                    partial = destination.Path + "." + DownloadId + ".part";
                    _output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024);
                    var request = new UserScriptHttpRequest("GET", _request.Url,
                        _request.Headers.ToDictionary(item => item.Key, item => item.Value), null, null, null,
                        _request.RedirectAllowed, new UserScriptHttpRequestOptions { Anonymous = _request.Anonymous });
                    var network = _owner._http.SendAsync(request, this, _cancellation.Token);
                    var response = await network.Completion.ConfigureAwait(false);
                    if (_responseError != null) throw _responseError;
                    if (response.Status < 200 || response.Status >= 400) throw new IOException("Download HTTP status " + response.Status + ".");
                    _output.Flush(true);
                    _output.Dispose(); _output = null;
                    lock (_sync)
                    {
                        _cancellation.Token.ThrowIfCancellationRequested();
                        // File commit is the completion/abort linearization boundary.
                        var path = Commit(partial, destination);
                        _committed = true;
                        Result = new DownloadResult(DownloadId, response.FinalUrl, Path.GetFileName(path), _loaded);
                    }
                    terminal = 1;
                }
                catch (OperationCanceledException error) { failure = error; terminal = _deadline.IsCancellationRequested ? 4 : 3; }
                catch (Exception error) { failure = error; terminal = _deadline.IsCancellationRequested ? 4 : _cancellation.IsCancellationRequested ? 3 : 2; }
                finally
                {
                    try
                    {
                        _output?.Dispose(); _output = null;
                        if (partial != null && File.Exists(partial)) File.Delete(partial);
                    }
                    catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                    { failure = error; terminal = 2; }
                    lock (_sync) _finished = true;
                    if (terminal == 1) Raise(() => Completed?.Invoke(this, EventArgs.Empty));
                    else if (terminal == 4) Raise(() => TimedOut?.Invoke(this, EventArgs.Empty));
                    else if (terminal == 3) Raise(() => Aborted?.Invoke(this, EventArgs.Empty));
                    else Raise(() => Failed?.Invoke(this, new UserScriptDownloadFailure(failure)));
                    if (terminal == 1) _completion.TrySetResult(null);
                    else if (terminal == 3) _completion.TrySetCanceled();
                    else _completion.TrySetException(terminal == 4 ? new TimeoutException("The download timed out.", failure) : failure ?? new IOException("The download failed."));
                    _owner._operations.TryRemove(DownloadId, out _);
                    _cancellation.Dispose(); _deadline.Dispose();
                }
            }

            private static string Commit(string partial, DownloadDestination destination)
            {
                if (destination.Overwrite)
                {
                    if (File.Exists(destination.Path)) File.Replace(partial, destination.Path, null);
                    else File.Move(partial, destination.Path);
                    return destination.Path;
                }
                for (var suffix = 0; suffix < 10000; suffix++)
                {
                    var candidate = suffix == 0 ? destination.Path : Path.Combine(Path.GetDirectoryName(destination.Path),
                        Path.GetFileNameWithoutExtension(destination.Path) + " (" + suffix + ")" + Path.GetExtension(destination.Path));
                    try { File.Move(partial, candidate); return candidate; }
                    catch (IOException) when (File.Exists(candidate)) { }
                }
                throw new IOException("No unused download file name was available.");
            }

            public void OnResponseStarted(UserScriptHttpResponse response)
            {
                if (response.Status < 200 || response.Status >= 400) _responseError = new IOException("Download HTTP status " + response.Status + ".");
                if (response.Headers.TryGetValue("Content-Length", out var length) && long.TryParse(length, NumberStyles.None, CultureInfo.InvariantCulture, out var total)) _total = total;
            }
            public void OnResponseData(byte[] buffer, int offset, int count)
            {
                _cancellation.Token.ThrowIfCancellationRequested();
                if (_responseError != null) throw _responseError;
                _output.Write(buffer, offset, count);
                _loaded += count;
                Raise(() => Progress?.Invoke(this, new UserScriptDownloadProgress(_loaded, _total)));
            }
            public void OnUploadProgress(long loaded, long? total) { }
            public void OnDownloadProgress(long loaded, long? total) { }
            private static void Raise(Action action)
            {
                try { action(); }
                catch (Exception error) { System.Diagnostics.Trace.TraceError(error.ToString()); }
            }
        }
    }
}
