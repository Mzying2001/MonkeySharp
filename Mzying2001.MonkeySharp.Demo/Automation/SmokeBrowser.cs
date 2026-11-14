using Mzying2001.MonkeySharp.Demo.ViewModels;
using Mzying2001.MonkeySharp.Demo.Views;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Demo.Runtime
{
    internal static class SmokeBrowser
    {
        internal static async Task WaitAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
        {
            var budget = timeout ?? TimeSpan.FromSeconds(25);
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < budget)
            {
                try
                {
                    var pending = condition();
                    using (var deadline = new CancellationTokenSource())
                    {
                        var remaining = budget - timer.Elapsed;
                        if (remaining <= TimeSpan.Zero) break;
                        if (await Task.WhenAny(pending, Task.Delay(remaining, deadline.Token)) != pending) break;
                        deadline.Cancel();
                        if (await pending) return;
                    }
                }
                // A reload can destroy the V8 context between the ready check and evaluation.
                // Only polling retries cancellation; direct evaluations and assertion errors still fail.
                catch (TaskCanceledException) { }
                var pause = budget - timer.Elapsed;
                if (pause > TimeSpan.Zero) await Task.Delay(pause < TimeSpan.FromMilliseconds(150) ? pause : TimeSpan.FromMilliseconds(150));
            }
            throw new TimeoutException("A WPF/Chromium smoke assertion timed out. " + SmokeTrace.Describe());
        }
        internal static async Task<bool> EvaluateBoolean(BrowserTabViewModel tab, string script)
        {
            var result = await Evaluate(tab, "Boolean(" + script + ")");
            return result is bool value && value;
        }
        internal static async Task<object> Evaluate(BrowserTabViewModel tab, string script)
        {
            if (!(tab.Browser is BrowserTabView view) || !view.IsJavaScriptReady)
            {
                SmokeTrace.Observe(script, "Browser not ready: " + tab.Address);
                return null;
            }
            var result = await view.EvaluateAsync(script);
            SmokeTrace.Observe(script, new { tab = tab.TabId, tab.Address, result.Success, result.Result, result.Message });
            return result.Success ? result.Result : null;
        }

    }
}
