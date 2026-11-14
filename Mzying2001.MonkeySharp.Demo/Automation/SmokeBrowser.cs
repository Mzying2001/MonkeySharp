using Mzying2001.MonkeySharp.Demo.ViewModels;
using Mzying2001.MonkeySharp.Demo.Views;
using System;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Demo.Runtime
{
    internal static class SmokeBrowser
    {
        internal static async Task WaitAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(25));
            while (DateTime.UtcNow < deadline)
            {
                try { if (await condition()) return; }
                // A reload can destroy the V8 context between the ready check and evaluation.
                // Only polling retries cancellation; direct evaluations and assertion errors still fail.
                catch (TaskCanceledException) { }
                await Task.Delay(150);
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
