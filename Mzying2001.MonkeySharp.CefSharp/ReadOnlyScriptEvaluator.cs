using CefSharp;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.CefSharp
{
    internal static class ReadOnlyScriptEvaluator
    {
        internal static async Task<JavascriptResponse> EvaluateAsync(
            Func<TimeSpan, Task<JavascriptResponse>> evaluate,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            // CefSharp represents an evaluation deadline as task cancellation.
            // Only read-only probes may be replayed; caller cancellation remains terminal.
            cancellationToken.ThrowIfCancellationRequested();
            var timer = Stopwatch.StartNew();
            TaskCanceledException lastCancellation = null;
            while (timer.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = timeout - timer.Elapsed;
                if (remaining <= TimeSpan.Zero) break;
                var attemptTimeout = remaining < TimeSpan.FromSeconds(2) ? remaining : TimeSpan.FromSeconds(2);
                try
                {
                    var response = await evaluate(attemptTimeout).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    return response;
                }
                catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
                {
                    lastCancellation = exception;
                }
                remaining = timeout - timer.Elapsed;
                if (remaining <= TimeSpan.Zero) break;
                await Task.Delay(remaining < TimeSpan.FromMilliseconds(50) ? remaining : TimeSpan.FromMilliseconds(50),
                    cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("The read-only CefSharp evaluation did not complete within " + timeout + ".", lastCancellation);
        }
    }
}
