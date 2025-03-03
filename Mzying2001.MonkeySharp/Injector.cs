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

            // Call init callback when javascript context is created.
            new JsContextWatcher(browser).ContextCreated += (_, frame) =>
            {
                if (frame.IsMain)
                    initCallback?.Invoke();
            };

            // Attach browser.
            _browser = browser;
        }


        /// <inheritdoc/>
        protected override void OnDetachBrowser(Messenger messenger)
        {
            if (_browser != null)
            {
                if (_browser.RenderProcessMessageHandler is JsContextWatcher watcher)
                {
                    watcher.ReleaseWatcher();
                }
                _browser.JavascriptObjectRepository.UnRegister("__MonkeySharp_Messenger");
                _browser.JavascriptObjectRepository.UnRegister("__MonkeySharp_AsyncMessenger");
                _browser = null;
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
