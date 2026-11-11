using Mzying2001.MonkeySharp.Core.Bridge;
using System;

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

        /// <summary>
        /// Gets or sets whether the CefSharp adapter creates its built-in HTTP,
        /// webRequest, and cookie services when no explicit service is supplied.
        /// </summary>
        public bool EnableDefaultNetworkServices { get; set; } = true;

        /// <summary>Gets or sets whether the adapter supplies its native mute-only audio service.</summary>
        public bool EnableDefaultAudioService { get; set; } = true;

        /// <summary>Gets or sets whether document-start userscripts require guaranteed pre-page execution.</summary>
        public bool RequireGuaranteedDocumentStart { get; set; }

        /// <summary>Gets or sets bridge payload, resource, concurrency, and timeout limits.</summary>
        public BridgeOptions Bridge { get; set; } = new BridgeOptions();

        /// <summary>Gets or sets a resolver for actual private-context state; null leaves it unknown.</summary>
        public Func<global::CefSharp.IRequestContext, bool?> IsIncognito { get; set; }

        /// <summary>Gets or sets whether scripts update automatically; null leaves policy unknown.</summary>
        public bool? AutomaticallyUpdatesScripts { get; set; }
    }
}
