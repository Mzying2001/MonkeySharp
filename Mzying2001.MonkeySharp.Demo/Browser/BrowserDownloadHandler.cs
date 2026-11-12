using CefSharp;
using Mzying2001.MonkeySharp.Demo.Localization;
using System;

namespace Mzying2001.MonkeySharp.Demo.Browser
{
    internal sealed class BrowserDownloadHandler : IDownloadHandler
    {
        private readonly string _directory;
        private readonly Action<string> _report;
        public BrowserDownloadHandler(string directory, Action<string> report)
        {
            _directory = directory;
            _report = report;
        }
#if CEF_SHARP_HAS_DOWNLOAD_CAN_DOWNLOAD
        public bool CanDownload(IWebBrowser control, IBrowser browser, string url, string requestMethod) => true;
#endif
#if CEF_SHARP_BOOLEAN_DOWNLOAD
        public bool OnBeforeDownload(IWebBrowser control, IBrowser browser, DownloadItem item, IBeforeDownloadCallback callback)
#else
        public void OnBeforeDownload(IWebBrowser control, IBrowser browser, DownloadItem item, IBeforeDownloadCallback callback)
#endif
        {
            using (callback) callback.Continue(System.IO.Path.Combine(_directory, Services.DownloadService.SafeFileName(item.SuggestedFileName)), true);
#if CEF_SHARP_BOOLEAN_DOWNLOAD
            return true;
#endif
        }
        public void OnDownloadUpdated(IWebBrowser control, IBrowser browser, DownloadItem item, IDownloadItemCallback callback)
        { if (item.IsComplete) _report(Strings.Format("BrowserDownloadComplete", item.FullPath)); }
    }
}
