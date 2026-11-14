using CefSharp;
using CefSharp.Wpf;
using Mzying2001.MonkeySharp.Demo.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Demo.Browser
{
    internal sealed class BrowserFavicon : IDisposable
    {
        internal const string ProbeScript = "(function(){var icon=document.querySelector('link[rel~=\"icon\"],link[rel=\"shortcut icon\"]');return icon ? icon.href : null;})()";
        private readonly ChromiumWebBrowser _browser;
        private readonly BrowserTabViewModel _tab;
        private readonly Action<Action> _post;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private bool _disposed;

        public BrowserFavicon(ChromiumWebBrowser browser, BrowserTabViewModel tab, Action<Action> post)
        {
            _browser = browser;
            _tab = tab;
            _post = post;
        }

        internal static string SelectUrl(IList<string> urls)
        {
            if (urls == null) return null;
            foreach (var url in urls)
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                    !string.IsNullOrEmpty(uri.UserInfo)) continue;
                return uri.AbsoluteUri;
            }
            return null;
        }

        private static string DefaultFaviconUrl(string address)
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(uri.UserInfo)) return null;
            var builder = new UriBuilder(uri) { Path = "/favicon.ico", Query = string.Empty, Fragment = string.Empty };
            return builder.Uri.AbsoluteUri;
        }

        public async Task RefreshAsync()
        {
            if (_tab.IsIncognito || _disposed) return;
            var token = _lifetime.Token;
            var address = _tab.Address;
            try
            {
                await _browser.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                await Task.Delay(100, token);
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    token.ThrowIfCancellationRequested();
                    if (!_browser.IsBrowserInitialized || !_browser.CanExecuteJavascriptInMainFrame)
                    {
                        await Task.Delay(200, token);
                        continue;
                    }
                    var response = await _browser.EvaluateScriptAsync(
                        ProbeScript,
                        timeout: TimeSpan.FromSeconds(5));
                    if (!response.Success)
                    {
                        await Task.Delay(200, token);
                        continue;
                    }
                    var iconUrl = SelectUrl(new[] { response.Result as string }) ?? DefaultFaviconUrl(address);
                    _post(() =>
                    {
                        if (string.Equals(_tab.Address, address, StringComparison.OrdinalIgnoreCase)) _tab.IconUrl = iconUrl;
                    });
                    return;
                }
                _post(() =>
                {
                    if (string.Equals(_tab.Address, address, StringComparison.OrdinalIgnoreCase))
                        _tab.IconUrl = DefaultFaviconUrl(address);
                });
            }
            // Navigation and tab disposal can invalidate a pending favicon evaluation.
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
