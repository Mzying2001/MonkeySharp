using CefSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mzying2001.MonkeySharp.CefSharp
{
    /// <summary>
    /// Forwards CefSharp render-process callbacks to multiple handlers without allowing one handler failure to stop the others.
    /// </summary>
    public sealed class RenderProcessMessageHandlerMultiplexer : IRenderProcessMessageHandler
    {
        private readonly object _sync = new object();
        private readonly List<IRenderProcessMessageHandler> _handlers = new List<IRenderProcessMessageHandler>();

        /// <summary>Initializes a multiplexer with an optional set of handlers.</summary>
        /// <param name="handlers">The handlers to register in callback order.</param>
        public RenderProcessMessageHandlerMultiplexer(params IRenderProcessMessageHandler[] handlers)
        {
            if (handlers == null)
                throw new ArgumentNullException(nameof(handlers));
            foreach (var handler in handlers)
                Add(handler);
        }

        /// <summary>Occurs when a registered handler throws while processing a callback.</summary>
        public event EventHandler<RenderHandlerFailedEventArgs> HandlerFailed;

        /// <summary>Adds a handler to the end of the callback sequence.</summary>
        /// <param name="handler">The handler to add.</param>
        public void Add(IRenderProcessMessageHandler handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            lock (_sync)
            {
                if (_handlers.Contains(handler))
                    throw new InvalidOperationException("The render process message handler is already registered.");
                _handlers.Add(handler);
            }
        }

        /// <summary>Removes a registered handler.</summary>
        /// <param name="handler">The handler to remove.</param>
        /// <returns><see langword="true"/> when the handler was registered and removed.</returns>
        public bool Remove(IRenderProcessMessageHandler handler)
        {
            if (handler == null)
                return false;
            lock (_sync)
                return _handlers.Remove(handler);
        }

        /// <inheritdoc />
        public void OnContextCreated(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame)
        {
            Invoke(handler => handler.OnContextCreated(chromiumWebBrowser, browser, frame));
        }

        /// <inheritdoc />
        public void OnContextReleased(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame)
        {
            Invoke(handler => handler.OnContextReleased(chromiumWebBrowser, browser, frame));
        }

        /// <inheritdoc />
        public void OnFocusedNodeChanged(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IDomNode node)
        {
            Invoke(handler => handler.OnFocusedNodeChanged(chromiumWebBrowser, browser, frame, node));
        }

        /// <inheritdoc />
        public void OnUncaughtException(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, JavascriptException exception)
        {
            Invoke(handler => handler.OnUncaughtException(chromiumWebBrowser, browser, frame, exception));
        }

        private void Invoke(Action<IRenderProcessMessageHandler> action)
        {
            IRenderProcessMessageHandler[] snapshot;
            lock (_sync)
                snapshot = _handlers.ToArray();
            foreach (var handler in snapshot)
            {
                try
                {
                    action(handler);
                }
                catch (Exception exception)
                {
                    HandlerFailed?.Invoke(this, new RenderHandlerFailedEventArgs(handler, exception));
                }
            }
        }
    }

    /// <summary>
    /// Identifies a render-process message handler that failed during callback dispatch.
    /// </summary>
    public sealed class RenderHandlerFailedEventArgs : EventArgs
    {
        /// <summary>Initializes handler failure event data.</summary>
        /// <param name="handler">The handler that threw.</param>
        /// <param name="exception">The exception thrown by the handler.</param>
        public RenderHandlerFailedEventArgs(IRenderProcessMessageHandler handler, Exception exception)
        {
            Handler = handler;
            Exception = exception;
        }

        /// <summary>Gets the handler that threw.</summary>
        public IRenderProcessMessageHandler Handler { get; }

        /// <summary>Gets the exception thrown by the handler.</summary>
        public Exception Exception { get; }
    }
}
