using CefSharp;
using CefSharp.WinForms;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Mzying2001.MonkeySharp.CefSharp.SmokeHost
{
    internal static class HistoryNavigationProbe
    {
        internal static async Task RunAsync()
        {
            using (var http = new LocalHttpFixture())
            {
                var origin = http.Start();
                var first = new UriBuilder(origin) { Host = "localhost", Path = "/probe/bfcache-a" }.Uri;
                var second = new Uri(origin, "/probe/bfcache-b");
                var repository = new UserScriptRepository(new InMemoryUserScriptPersistence());
                await repository.InitializeAsync(CancellationToken.None);
                await repository.InstallAsync("// ==UserScript==\n// @name History navigation marker\n" +
                    "// @match http://127.0.0.1/probe/bfcache-b\n// @grant none\n// @run-at document-end\n" +
                    "// ==/UserScript==\nwindow.__monkeyHistoryUrl=location.href;console.log('MONKEY-HISTORY '+location.href);",
                    "probe://history-navigation", true, CancellationToken.None);
                using (var host = new CefSharpUserScriptHostBuilder(repository).Build())
                using (var form = new ProbeForm(host, first, second))
                    Application.Run(form);
            }
        }

        private sealed class ProbeForm : Form
        {
            private const int Rounds = 20;
            private const string LogPrefix = "MONKEY-HISTORY ";
            private readonly ChromiumWebBrowser _browser = new ChromiumWebBrowser("about:blank") { Dock = DockStyle.Fill };
            private readonly ConcurrentQueue<string> _executions = new ConcurrentQueue<string>();
            private readonly ConcurrentQueue<string> _urlChanges = new ConcurrentQueue<string>();
            private readonly ConcurrentQueue<string> _diagnostics = new ConcurrentQueue<string>();

            internal ProbeForm(CefSharpUserScriptHost host, Uri first, Uri second)
            {
                _browser.ConsoleMessage += (_, args) =>
                {
                    if (args.Message.StartsWith(LogPrefix, StringComparison.Ordinal))
                        _executions.Enqueue(args.Message.Substring(LogPrefix.Length));
                };
                host.DocumentLifecycle += (_, args) =>
                {
                    if (args.Frame.IsMainFrame && args.Kind == DocumentLifecycleKind.UrlChanged)
                        _urlChanges.Enqueue(args.Frame.Url.AbsoluteUri);
                };
                host.Diagnostic += (_, args) => _diagnostics.Enqueue(args.Code + " " + args.Message + " " + args.Exception?.Message);
                host.Attach(_browser);
                Controls.Add(_browser);
                Shown += async (_, __) =>
                {
                    try
                    {
                        await host.PrepareAsync();
                        for (var round = 0; round < Rounds; round++)
                        {
                            await host.NavigateAsync(first.AbsoluteUri);
                            await WaitForPageAsync(first, false);
                            await host.NavigateAsync(second.AbsoluteUri);
                            await WaitForPageAsync(second, true);
                            await WaitAsync(() => Task.FromResult(_executions.Count >= round + 1), "initial script log");
                            AssertExecutions(round + 1, second);
                            _browser.GetBrowser().GoBack();
                            await WaitForPageAsync(first, false);
                            AssertExecutions(round + 1, second);
                        }

                        _browser.GetBrowser().GoForward();
                        await WaitForPageAsync(second, true);
                        await WaitAsync(() => Task.FromResult(_executions.Count >= Rounds + 1), "forward script log");
                        AssertExecutions(Rounds + 1, second);
                        _browser.GetBrowser().Reload();
                        await WaitForPageAsync(second, true);
                        await WaitAsync(() => Task.FromResult(_executions.Count >= Rounds + 2), "reload script log");
                        AssertExecutions(Rounds + 2, second);
                        var changed = new Uri(second, "#same-document");
                        await EvaluateAsync("history.pushState(null, '', '#same-document')");
                        await WaitAsync(() => Task.FromResult(_urlChanges.Contains(changed.AbsoluteUri)), "same-document URL change");
                        AssertExecutions(Rounds + 2, second);
                        Console.WriteLine(JsonSerializer.Serialize(new { type = "history-navigation-probe", status = "passed",
                            cefSharpVersion = typeof(Cef).Assembly.GetName().Version.ToString(),
                            platform = Environment.Is64BitProcess ? "x64" : "x86", rounds = Rounds }));
                    }
                    catch (Exception error)
                    {
                        Environment.ExitCode = 1;
                        Console.WriteLine(JsonSerializer.Serialize(new { type = "history-navigation-probe", status = "failed",
                            error = error.ToString(), diagnostics = _diagnostics.ToArray() }));
                    }
                    finally
                    {
                        await host.DetachAsync(CancellationToken.None);
                        Close();
                    }
                };
            }

            private void AssertExecutions(int expected, Uri matched)
            {
                var observed = _executions.ToArray();
                if (observed.Length != expected || observed.Any(url => url != matched.AbsoluteUri))
                    throw new InvalidOperationException("Unexpected userscript execution: " + string.Join(", ", observed));
            }

            private Task WaitForPageAsync(Uri url, bool matched)
            {
                var address = JsonSerializer.Serialize(url.AbsoluteUri);
                return WaitAsync(async () =>
                {
                    if (_browser.IsLoading) return false;
                    var state = await EvaluateAsync("location.href===" + address +
                        "&&document.readyState==='complete'?[true,window.__monkeyHistoryUrl||null]:null") as System.Collections.Generic.List<object>;
                    if (state == null) return false;
                    if (!matched && state[1] != null)
                        throw new InvalidOperationException("Userscript marked the unmatched page: " + state[1]);
                    return !matched || state[1] as string == url.AbsoluteUri;
                }, url.AbsoluteUri);
            }

            private async Task<object> EvaluateAsync(string source)
            {
                using (var frame = _browser.GetMainFrame())
                {
                    var response = await NativeFrameProbe.EvaluateAsync(frame, source);
                    return response?.Success == true ? response.Result : null;
                }
            }

            private static async Task WaitAsync(Func<Task<bool>> condition, string description)
            {
                var deadline = DateTime.UtcNow.AddSeconds(8);
                while (!await condition())
                {
                    if (DateTime.UtcNow >= deadline) throw new TimeoutException(description);
                    await Task.Delay(25);
                }
            }
        }
    }
}
