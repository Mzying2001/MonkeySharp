using CefSharp;

namespace Mzying2001.MonkeySharp.CefSharp
{
    /// <summary>Holds the request context of the browser currently attached to a host.</summary>
    internal sealed class CefSharpRequestContextAccessor
    {
        private volatile IRequestContext _current;
        private volatile bool _attached;

        public IRequestContext Current
        {
            get
            {
                var current = _current;
                if (current != null || !_attached)
                    return current;

                // IWebBrowser.RequestContext is null when the browser uses the
                // process-wide context. Resolve that context lazily because the
                // global CEF context may not exist when Attach is called.
                try { return Cef.GetGlobalRequestContext(); }
                catch { return null; }
            }
            set { _current = value; }
        }

        public bool IsAttached
        {
            get { return _attached; }
            set { _attached = value; }
        }
    }
}
