using CefSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mzying2001.MonkeySharp.CefSharp
{
    public sealed class RenderProcessMessageHandlerMultiplexer : IRenderProcessMessageHandler
    {
        private readonly object _sync = new object();
        private readonly List<IRenderProcessMessageHandler> _handlers = new List<IRenderProcessMessageHandler>();

        public RenderProcessMessageHandlerMultiplexer(params IRenderProcessMessageHandler[] handlers)
        {
            if (handlers == null)
                throw new ArgumentNullException(nameof(handlers));
            foreach (var handler in handlers)
                Add(handler);
        }

        public event EventHandler<RenderHandlerFailedEventArgs> HandlerFailed;

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

        public bool Remove(IRenderProcessMessageHandler handler)
        {
            if (handler == null)
                return false;
            lock (_sync)
                return _handlers.Remove(handler);
        }

        public void OnContextCreated(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame)
        {
            Invoke(handler => handler.OnContextCreated(chromiumWebBrowser, browser, frame));
        }

        public void OnContextReleased(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame)
        {
            Invoke(handler => handler.OnContextReleased(chromiumWebBrowser, browser, frame));
        }

        public void OnFocusedNodeChanged(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IDomNode node)
        {
            Invoke(handler => handler.OnFocusedNodeChanged(chromiumWebBrowser, browser, frame, node));
        }

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

    public sealed class RenderHandlerFailedEventArgs : EventArgs
    {
        public RenderHandlerFailedEventArgs(IRenderProcessMessageHandler handler, Exception exception)
        {
            Handler = handler;
            Exception = exception;
        }

        public IRenderProcessMessageHandler Handler { get; }
        public Exception Exception { get; }
    }
}
