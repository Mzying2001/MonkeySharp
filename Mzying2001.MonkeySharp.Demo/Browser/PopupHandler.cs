using CefSharp;
using System;

namespace Mzying2001.MonkeySharp.Demo.Browser
{
    internal sealed class PopupHandler : ILifeSpanHandler
    {
        private readonly Action<string, bool> _open;
        public PopupHandler(Action<string, bool> open) { _open = open; }
        public bool OnBeforePopup(IWebBrowser control, IBrowser browser, IFrame frame,
            string targetUrl, string targetFrameName, WindowOpenDisposition disposition, bool userGesture,
            IPopupFeatures popupFeatures, IWindowInfo windowInfo, IBrowserSettings settings,
            ref bool noJavascriptAccess, out IWebBrowser newBrowser)
        {
            newBrowser = null;
            _open(targetUrl, disposition != WindowOpenDisposition.NewBackgroundTab);
            return true;
        }
        public void OnAfterCreated(IWebBrowser control, IBrowser browser) { }
        public bool DoClose(IWebBrowser control, IBrowser browser) => !browser.IsPopup;
        public void OnBeforeClose(IWebBrowser control, IBrowser browser) { }
    }
}
