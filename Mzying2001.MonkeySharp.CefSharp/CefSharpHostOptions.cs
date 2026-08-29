using Mzying2001.MonkeySharp.Core.Bridge;

namespace Mzying2001.MonkeySharp.CefSharp
{
    /// <summary>
    /// Configures bridge security, document-start requirements, and protocol limits for a CefSharp host.
    /// </summary>
    public sealed class CefSharpHostOptions
    {
        /// <summary>
        /// Gets or sets whether pages are trusted enough to expose the bridge in the page JavaScript world.
        /// </summary>
        public bool TrustedPageWorld { get; set; }

        /// <summary>Gets or sets whether document-start userscripts require guaranteed pre-page execution.</summary>
        public bool RequireGuaranteedDocumentStart { get; set; }

        /// <summary>Gets or sets bridge payload, resource, concurrency, and timeout limits.</summary>
        public BridgeOptions Bridge { get; set; } = new BridgeOptions();
    }
}
