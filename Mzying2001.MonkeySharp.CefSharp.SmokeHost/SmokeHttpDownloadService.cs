using Mzying2001.MonkeySharp.Core.Apis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.CefSharp.SmokeHost
{
    // Deliberately a fixture service: no UI, redirects or non-fixture destinations.
    internal sealed class SmokeHttpDownloadService : IDownloadService, IDisposable
    {
        public string DownloadMode => "native";
        private readonly HttpClient _client = new HttpClient(new HttpClientHandler
        { AllowAutoRedirect = false, UseCookies = false, UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
        private readonly List<Operation> _operations = new List<Operation>();
        private readonly int _port;
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "MonkeySharpDownloads", Guid.NewGuid().ToString("N"));

        public SmokeHttpDownloadService(int port)
        {
            _port = port;
            Directory.CreateDirectory(DirectoryPath);
        }

        public Task<IDownloadOperation> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Url.Scheme != "http" || request.Url.Host != "127.0.0.1" || request.Url.Port != _port ||
                string.IsNullOrEmpty(request.Name) || Path.GetFileName(request.Name) != request.Name || request.SaveAs)
                throw new InvalidOperationException("Smoke downloads require a named file from the local fixture without UI.");
            var operation = new Operation(_client, request, Path.Combine(DirectoryPath, request.Name), cancellationToken);
            lock (_operations) _operations.Add(operation);
            return Task.FromResult<IDownloadOperation>(operation);
        }

        public Task DrainAsync()
        {
            lock (_operations) return Task.WhenAll(_operations.Select(operation => operation.Work));
        }

        public void Dispose()
        {
            Operation[] operations;
            lock (_operations) operations = _operations.ToArray();
            foreach (var operation in operations) operation.Abort();
            Task.WhenAll(operations.Select(operation => operation.Work)).GetAwaiter().GetResult();
            _client.Dispose();
            // This directory is generated above; no caller-supplied deletion target.
            Directory.Delete(DirectoryPath, true);
        }

        private sealed class Operation : IDownloadOperation
        {
            private readonly object _sync = new object();
            private readonly CancellationTokenSource _timeout = new CancellationTokenSource();
            private readonly CancellationTokenSource _cancellation;
            private int _terminal;
            private Exception _error;
            private UserScriptDownloadProgress _lastProgress;
            private EventHandler<UserScriptDownloadProgress> _progress;
            private EventHandler _completed, _aborted, _timedOut;
            private EventHandler<UserScriptDownloadFailure> _failed;

            public Operation(HttpClient client, DownloadRequest request, string path, CancellationToken token)
            {
                _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _timeout.Token);
                _timeout.CancelAfter(request.Timeout ?? TimeSpan.FromSeconds(20));
                Work = Task.Run(() => RunAsync(client, request, path));
            }

            public string DownloadId { get; } = Guid.NewGuid().ToString("N");
            public Task Work { get; }
            public Task Completion => Work;
            public event EventHandler<UserScriptDownloadProgress> Progress
            {
                add { lock (_sync) { _progress += value; if (_lastProgress != null) value(this, _lastProgress); } }
                remove { lock (_sync) _progress -= value; }
            }
            public event EventHandler Completed
            {
                add { lock (_sync) { _completed += value; if (_terminal == 1) value(this, EventArgs.Empty); } }
                remove { lock (_sync) _completed -= value; }
            }
            public event EventHandler<UserScriptDownloadFailure> Failed
            {
                add { lock (_sync) { _failed += value; if (_terminal == 2) value(this, new UserScriptDownloadFailure(_error)); } }
                remove { lock (_sync) _failed -= value; }
            }
            public event EventHandler Aborted
            {
                add { lock (_sync) { _aborted += value; if (_terminal == 3) value(this, EventArgs.Empty); } }
                remove { lock (_sync) _aborted -= value; }
            }
            public event EventHandler TimedOut
            {
                add { lock (_sync) { _timedOut += value; if (_terminal == 4) value(this, EventArgs.Empty); } }
                remove { lock (_sync) _timedOut -= value; }
            }

            public void Abort()
            {
                lock (_sync) if (_terminal == 0) _cancellation.Cancel();
            }
            public void Dispose() => Abort();

            private async Task RunAsync(HttpClient client, DownloadRequest request, string path)
            {
                var partial = path + ".part";
                var terminal = 1;
                Exception error = null;
                try
                {
                    using (var message = new HttpRequestMessage(HttpMethod.Get, request.Url))
                    {
                        foreach (var header in request.Headers) message.Headers.TryAddWithoutValidation(header.Key, header.Value);
                        using (var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, _cancellation.Token).ConfigureAwait(false))
                        {
                            response.EnsureSuccessStatusCode();
                            using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
                            // Disposing the response also interrupts .NET Framework stream reads on cancellation.
                            using (_cancellation.Token.Register(response.Dispose))
                            {
                                var buffer = new byte[4096];
                                long loaded = 0;
                                int count;
                                while ((count = await input.ReadAsync(buffer, 0, buffer.Length, _cancellation.Token).ConfigureAwait(false)) > 0)
                                {
                                    await output.WriteAsync(buffer, 0, count, _cancellation.Token).ConfigureAwait(false);
                                    loaded += count;
                                    lock (_sync)
                                    {
                                        _lastProgress = new UserScriptDownloadProgress(loaded, response.Content.Headers.ContentLength);
                                        _progress?.Invoke(this, _lastProgress);
                                    }
                                }
                            }
                        }
                    }
                    _cancellation.Token.ThrowIfCancellationRequested();
                    File.Move(partial, path);
                }
                catch (Exception exception)
                {
                    error = exception;
                    terminal = _cancellation.IsCancellationRequested ? (_timeout.IsCancellationRequested ? 4 : 3) : 2;
                }
                finally
                {
                    if (File.Exists(partial)) File.Delete(partial);
                }
                lock (_sync)
                {
                    _terminal = terminal;
                    _error = error;
                    _timeout.Dispose();
                    _cancellation.Dispose();
                    if (terminal == 1) _completed?.Invoke(this, EventArgs.Empty);
                    else if (terminal == 2) _failed?.Invoke(this, new UserScriptDownloadFailure(error));
                    else if (terminal == 3) _aborted?.Invoke(this, EventArgs.Empty);
                    else _timedOut?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }
}
