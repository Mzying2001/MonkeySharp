using CefSharp;
using System;
using System.Globalization;
using System.Reflection;

namespace Mzying2001.MonkeySharp.CefSharp
{
    public sealed partial class CefSharpUserScriptHost
    {
        // IBrowser.GetFrame(long) was renamed to GetFrameByIdentifier(string)
        // in newer CefSharp releases. Resolve either shape at runtime so the
        // adapter can be compiled against the host application's CefSharp.
        internal static IFrame GetFrameByIdentifier(IBrowser browser, string frameId)
        {
            if (browser == null || frameId == null)
                return null;

            var byString = typeof(IBrowser).GetMethod(
                "GetFrameByIdentifier",
                new[] { typeof(string) });
            if (byString != null)
                return byString.Invoke(browser, new object[] { frameId }) as IFrame;

            if (long.TryParse(frameId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var identifier))
            {
                var byLong = typeof(IBrowser).GetMethod(
                    "GetFrame",
                    new[] { typeof(long) });
                if (byLong != null)
                    return byLong.Invoke(browser, new object[] { identifier }) as IFrame;
            }

            // Some transitional releases expose only the string overload
            // under the original GetFrame name.
            var legacyByString = typeof(IBrowser).GetMethod(
                "GetFrame",
                new[] { typeof(string) });
            return legacyByString?.Invoke(browser, new object[] { frameId }) as IFrame;
        }

        private static void EnsureCompatibleCefSharp()
        {
            var identifier = typeof(IFrame).GetProperty("Identifier");
            if (identifier == null ||
                (identifier.PropertyType != typeof(long) && identifier.PropertyType != typeof(string)))
            {
                throw new NotSupportedException(
                    "The loaded CefSharp version exposes an unsupported IFrame.Identifier type. " +
                    "Build MonkeySharp against the CefSharp version used by the host.");
            }

            var browser = typeof(IBrowser);
            var hasFrameLookup = browser.GetMethod("GetFrameByIdentifier", new[] { typeof(string) }) != null ||
                browser.GetMethod("GetFrame", new[] { typeof(string) }) != null ||
                browser.GetMethod("GetFrame", new[] { typeof(long) }) != null;
            if (!hasFrameLookup)
            {
                throw new NotSupportedException(
                    "The loaded CefSharp version does not expose a supported frame lookup API. " +
                    "Build MonkeySharp against the CefSharp version used by the host.");
            }
        }

        // IFrame.Identifier changed from Int64 to String in newer CefSharp
        // releases. Reading it through reflection keeps lifecycle code source
        // compatible when the adapter is compiled against either interface shape.
        private static string GetFrameIdentifier(IFrame frame)
        {
            if (frame == null)
                return string.Empty;
            var property = typeof(IFrame).GetProperty("Identifier");
            var value = property?.GetValue(frame, null);
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static PropertyInfo GetContextMenuHandlerProperty(IWebBrowser browser)
        {
            if (browser == null)
                return null;
            var flags = BindingFlags.Instance | BindingFlags.Public;
            return browser.GetType().GetProperty("MenuHandler", flags) ??
                browser.GetType().GetProperty("ContextMenuHandler", flags);
        }

    }
}
