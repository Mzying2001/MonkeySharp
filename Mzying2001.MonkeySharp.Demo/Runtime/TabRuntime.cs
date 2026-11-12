using CefSharp;
using Mzying2001.MonkeySharp.CefSharp;
using Mzying2001.MonkeySharp.Demo.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Demo.Runtime
{
    public sealed class TabRuntime
    {
        private readonly global::CefSharp.Wpf.ChromiumWebBrowser _browser;
        private readonly DownloadService _downloads;
        private readonly Action _closed;
        private Task _disposal;
        public TabRuntime(global::CefSharp.Wpf.ChromiumWebBrowser browser, CefSharpUserScriptHost host, DownloadService downloads, TabContext context, Action closed)
        {
            _browser = browser;
            Host = host;
            _downloads = downloads;
            Context = context;
            _closed = closed;
        }

        public CefSharpUserScriptHost Host { get; }
        public TabContext Context { get; }

        // UI closure and application shutdown may both request disposal of the same tab.
        public Task DisposeAsync() => _disposal ?? (_disposal = DisposeCoreAsync());

        private async Task DisposeCoreAsync()
        {
            await _browser.Dispatcher.InvokeAsync(() =>
            {
                if (!_browser.IsDisposed && _browser.IsBrowserInitialized) _browser.Stop();
            });
            // Keep browser/context resources alive until the host and accepted downloads have drained.
            _downloads.Dispose();
            await Host.DetachAsync(CancellationToken.None);
            Host.Dispose();
            await _downloads.DrainAsync();
            await _browser.Dispatcher.InvokeAsync(() =>
            {
                _browser.Dispose();
                _closed();
            });
        }
    }
}
