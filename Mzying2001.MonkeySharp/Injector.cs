using CefSharp;
using Mzying2001.MonkeySharp.Core;
using Mzying2001.MonkeySharp.Core.Messaging;
using System;

namespace Mzying2001.MonkeySharp
{
    /// <summary>
    /// Injector for <see cref="IWebBrowser"/>.
    /// </summary>
    public class Injector : InjectorBase<IWebBrowser>
    {
        /// <summary>
        /// The attached browser.
        /// </summary>
        private IWebBrowser _browser = null;


        /// <summary>
        /// The callback provided by <see cref="OnAttachBrowser"/>
        /// </summary>
        private Action _callback = null;


        /// <inheritdoc/>
        public override bool IsBrowserAttached => _browser != null;


        /// <inheritdoc/>
        protected override void OnAttachBrowser(IWebBrowser browser, Messenger messenger, Action initCallback)
        {
            if (IsBrowserAttached)
                throw new InvalidOperationException("Injector is already attached to a browser.");

            // Enable WCF to allow register object with non-async mode.
            CefSharpSettings.WcfEnabled = true;

            // Register messenger.
            browser.JavascriptObjectRepository.Settings.LegacyBindingEnabled = true;
            browser.JavascriptObjectRepository.Register("__MonkeySharp_Messenger", messenger, isAsync: false);
            browser.JavascriptObjectRepository.Register("__MonkeySharp_AsyncMessenger", messenger, isAsync: true);

            // Attach browser.
            _browser = browser;
            _callback = initCallback;

            // Call init callback when the main frame starts loading.
            _browser.FrameLoadStart += BrowserFrameLoadStartHandler;
        }


        /// <summary>
        /// Called when the browser frame starts loading.
        /// </summary>
        private void BrowserFrameLoadStartHandler(object sender, FrameLoadStartEventArgs e)
        {
            if (e.Frame.IsMain)
            {
                _callback?.Invoke();
            }
        }


        /// <inheritdoc/>
        protected override void OnDetachBrowser(Messenger messenger)
        {
            if (_browser != null)
            {
                _browser.FrameLoadStart -= BrowserFrameLoadStartHandler;
                _browser.JavascriptObjectRepository.UnRegister("__MonkeySharp_Messenger");
                _browser.JavascriptObjectRepository.UnRegister("__MonkeySharp_AsyncMessenger");
                _browser = null;
                _callback = null;
            }
        }


        /// <inheritdoc/>
        public override void ExecuteScriptAsync(string script)
        {
            try { _browser.ExecuteScriptAsync(script); }
            catch { }
        }


        /// <inheritdoc/>
        public override void ExecuteScriptAsync(string function, params object[] args)
        {
            try { _browser.ExecuteScriptAsync(function, args); }
            catch { }
        }
    }
}
