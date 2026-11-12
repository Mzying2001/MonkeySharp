using CefSharp;
using CefSharp.Handler;
using System;

namespace Mzying2001.MonkeySharp.Demo.Browser
{
    internal sealed class NavigationHandler : RequestHandler
    {
        private readonly Action<string, bool> _open;
        public NavigationHandler(Action<string, bool> open) { _open = open; }
        protected override bool OnOpenUrlFromTab(IWebBrowser control, IBrowser browser, IFrame frame,
            string targetUrl, WindowOpenDisposition disposition, bool userGesture)
        {
            _open(targetUrl, disposition != WindowOpenDisposition.NewBackgroundTab);
            return true;
        }
    }
}
