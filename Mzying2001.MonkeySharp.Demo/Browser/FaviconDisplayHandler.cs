using CefSharp;
using System;
using System.Collections.Generic;

namespace Mzying2001.MonkeySharp.Demo.Browser
{
    internal sealed class FaviconDisplayHandler : IDisplayHandler
    {
        private readonly Action<string> _changed;
        public FaviconDisplayHandler(Action<string> changed) { _changed = changed; }
        public void OnFaviconUrlChange(IWebBrowser chromiumWebBrowser, IBrowser browser, IList<string> urls)
            => _changed(BrowserFavicon.SelectUrl(urls));
        public void OnAddressChanged(IWebBrowser control, AddressChangedEventArgs args) { }
        public bool OnAutoResize(IWebBrowser control, IBrowser browser, global::CefSharp.Structs.Size size) => false;
        public void OnTitleChanged(IWebBrowser control, TitleChangedEventArgs args) { }
        public void OnFullscreenModeChange(IWebBrowser control, IBrowser browser, bool fullscreen) { }
        public void OnLoadingProgressChange(IWebBrowser control, IBrowser browser, double progress) { }
        public bool OnTooltipChanged(IWebBrowser control, ref string text) => false;
        public void OnStatusMessage(IWebBrowser control, StatusMessageEventArgs args) { }
        public bool OnConsoleMessage(IWebBrowser control, ConsoleMessageEventArgs args) => false;
#if CEF_SHARP_HAS_CURSOR_CHANGE
        public bool OnCursorChange(IWebBrowser control, IBrowser browser, IntPtr cursor,
            global::CefSharp.Enums.CursorType type, global::CefSharp.Structs.CursorInfo info) => false;
#endif
    }
}
