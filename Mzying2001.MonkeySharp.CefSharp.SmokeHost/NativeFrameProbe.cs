using CefSharp;
using System;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.CefSharp.SmokeHost
{
    internal static class NativeFrameProbe
    {
        internal static async Task<JavascriptResponse> EvaluateAsync(IFrame frame, string expression)
        {
            if (frame == null || frame.IsDisposed || !frame.IsValid) return null;
            try
            {
                return await frame.EvaluateScriptAsync(expression, timeout: TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (TaskCanceledException) { return null; }
            // CEF may retire the wrapper between IsValid and EvaluateScriptAsync,
            // throwing an ordinary Exception. Only confirmed retirement is retryable.
            catch (Exception) when (frame.IsDisposed || !frame.IsValid) { return null; }
        }
    }
}
