using Mzying2001.MonkeySharp.Core.Bridge;

namespace Mzying2001.MonkeySharp.CefSharp
{
    public sealed class CefSharpHostOptions
    {
        public bool TrustedPageWorld { get; set; }
        public bool RequireGuaranteedDocumentStart { get; set; }
        public BridgeOptions Bridge { get; set; } = new BridgeOptions();
    }
}
