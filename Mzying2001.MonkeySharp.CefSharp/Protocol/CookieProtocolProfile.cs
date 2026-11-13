namespace Mzying2001.MonkeySharp.CefSharp
{
    /// <summary>Chromium's partition Cookie protocol generations, shared by the backend and capability reports.</summary>
    internal sealed class CookieProtocolProfile
    {
        internal CookieProtocolProfile(int major) { Major = major; }

        internal int Major { get; }
        internal bool SupportsPartitions => Major >= 114;
        // Chromium 127 changed the CDP key from a string to an object. Its ancestor
        // dimension remained disabled by default until 128; wire shape alone is insufficient.
        internal bool UsesObjectKey => Major >= 127;
        internal bool SupportsAncestors => Major >= 128;
        internal string CapabilityStatus => !SupportsPartitions ? "unsupported:engine-version" :
            SupportsAncestors ? "available:serializable-keys" : "available:without-ancestor-dimension";
    }
}
