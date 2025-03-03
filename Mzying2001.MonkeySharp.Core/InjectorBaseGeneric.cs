using Mzying2001.MonkeySharp.Core.Internal;
using Mzying2001.MonkeySharp.Core.Messaging;
using System;

namespace Mzying2001.MonkeySharp.Core
{
    /// <summary>
    /// Generic version of <see cref="InjectorBase"/>.
    /// </summary>
    public abstract class InjectorBase<TBrowser> : InjectorBase
        where TBrowser : class
    {
        /// <inheritdoc/>
        protected override void OnAttachBrowser(object browser, Messenger messenger, Action initCallback)
        {
            ThrowHelper.ThrowIfArgumentNotOfType<TBrowser>(browser, nameof(browser));
            OnAttachBrowser((TBrowser)browser, messenger, initCallback);
        }


        /// <summary>
        /// Called when attaching the browser.
        /// The messenger should be registered as __MonkeySharp_Messenger and __MonkeySharp_AsyncMessenger (for async messages)
        /// to the browser for javascript and C# communication.
        /// When the javascript context of the main frame is created, the initCallback should be called.
        /// </summary>
        /// <param name="browser">The browser object.</param>
        /// <param name="messenger">Proxy object for javascript and C# communication.</param>
        /// <param name="initCallback">When the javascript context of the main frame is created, call this callback.</param>
        protected abstract void OnAttachBrowser(TBrowser browser, Messenger messenger, Action initCallback);
    }
}
