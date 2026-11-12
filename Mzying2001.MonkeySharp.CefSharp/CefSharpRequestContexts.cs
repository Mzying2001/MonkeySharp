using CefSharp;

namespace Mzying2001.MonkeySharp.CefSharp
{
    /// <summary>Creates native request contexts with explicit host-owned lifetimes.</summary>
    public static class CefSharpRequestContexts
    {
        /// <summary>Creates a fresh memory-only Cookie/cache/preferences context; the caller owns its disposal.</summary>
        public static IRequestContext CreatePrivate()
        {
            var settings = new RequestContextSettings { CachePath = string.Empty, PersistSessionCookies = false };
            // This optional setting was removed in newer SDKs. Empty CachePath remains
            // the native memory-only boundary, independent of the managed property.
            var persistence = typeof(RequestContextSettings).GetProperty("PersistUserPreferences");
            if (persistence != null && persistence.CanWrite) persistence.SetValue(settings, false, null);
            return new RequestContext(settings);
        }
    }
}
