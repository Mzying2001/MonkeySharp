using Mzying2001.MonkeySharp.Core.Apis;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.CefSharp.SmokeHost
{
    // The production file/network pipeline with a local-fixture destination policy.
    internal sealed class SmokeHttpDownloadService : IDownloadService, IDisposable
    {
        private readonly FileDownloadService _pipeline;
        private readonly int _port;
        public string DownloadMode => "native";
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "MonkeySharpDownloads", Guid.NewGuid().ToString("N"));

        public SmokeHttpDownloadService(int port, IHttpRequestService http)
        {
            _port = port; Directory.CreateDirectory(DirectoryPath);
            _pipeline = new FileDownloadService(http, (request, token) => Task.FromResult(
                new DownloadDestination(Path.Combine(DirectoryPath, request.Name == "disk-failure.bin" ? "missing/file.bin" : request.Name), false)), "native");
        }
        public Task<IDownloadOperation> CreateAsync(DownloadRequest request, CancellationToken cancellationToken)
        {
            if ((request.Body == null && (request.Url.Scheme != "http" || request.Url.Host != "127.0.0.1" || request.Url.Port != _port)) ||
                string.IsNullOrEmpty(request.Name) || Path.GetFileName(request.Name) != request.Name || request.SaveAs)
                throw new InvalidOperationException("Smoke downloads require a named local file without UI.");
            return _pipeline.CreateAsync(request, cancellationToken);
        }
        public Task DrainAsync() => _pipeline.DrainAsync();
        public void Dispose()
        {
            _pipeline.Dispose();
            _pipeline.DrainAsync().GetAwaiter().GetResult();
            if (!Directory.Exists(DirectoryPath)) return;
            foreach (var file in Directory.GetFiles(DirectoryPath)) File.Delete(file);
            Directory.Delete(DirectoryPath);
        }
    }
}
