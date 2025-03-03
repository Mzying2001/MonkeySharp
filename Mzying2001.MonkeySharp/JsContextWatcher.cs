using CefSharp;
using System;

namespace Mzying2001.MonkeySharp
{
    internal class JsContextWatcher : IRenderProcessMessageHandler
    {
        public event EventHandler<IFrame> ContextCreated;
        public event EventHandler<IFrame> ContextReleased;

        private IWebBrowser _browser;
        private IRenderProcessMessageHandler _handler;

        public JsContextWatcher(IWebBrowser browser)
        {
            _browser = browser;
            _handler = browser.RenderProcessMessageHandler;
            browser.RenderProcessMessageHandler = this;
        }

        public void ReleaseWatcher()
        {
            _browser.RenderProcessMessageHandler = _handler;
            _browser = null;
            _handler = null;
            ContextCreated = null;
            ContextReleased = null;
        }

        public void OnContextCreated(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame)
        {
            _handler?.OnContextCreated(chromiumWebBrowser, browser, frame);
            ContextCreated?.Invoke(this, frame);
        }

        public void OnContextReleased(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame)
        {
            _handler?.OnContextReleased(chromiumWebBrowser, browser, frame);
            ContextReleased?.Invoke(this, frame);
        }

        public void OnFocusedNodeChanged(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IDomNode node)
        {
            _handler?.OnFocusedNodeChanged(chromiumWebBrowser, browser, frame, node);
        }

        public void OnUncaughtException(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, JavascriptException exception)
        {
            _handler?.OnUncaughtException(chromiumWebBrowser, browser, frame, exception);
        }
    }
}
