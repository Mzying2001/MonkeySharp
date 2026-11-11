using CefSharp;
using CefSharp.WinForms;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Storage;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Mzying2001.MonkeySharp.CefSharp.SmokeHost
{
    internal static class BfcacheProbe
    {
        internal static async Task RunAsync()
        {
            using (var fixture = new LocalHttpFixture())
            {
                var origin = fixture.Start();
                foreach (var attached in new[] { false, true })
                using (var values = new InMemoryUserScriptValueStore())
                {
                    var repository = new InMemoryUserScriptRepository();
                    foreach (var world in new[] { "raw", "isolated" })
                    {
                        var script = await repository.InstallAsync("// ==UserScript==\n// @name BFCache " + world +
                            "\n// @match http://127.0.0.1/probe/bfcache-a\n// @grant GM_getValue\n// @run-at document-end\n" +
                            (world == "isolated" ? "// @sandbox DOM\n" : "") + "// ==/UserScript==\n" +
                            "document.documentElement.dataset." + world + "Ready=GM_getValue('ready');" +
                            "globalThis.__cacheGmClosure=function(){return GM_getValue('ready');};", "probe://bfcache", true, CancellationToken.None);
                        await values.SetAsync(script.ScriptKey.ToString(), "ready", UserScriptValue.FromJson("\"ready\""), CancellationToken.None);
                    }
                    using (var host = new CefSharpUserScriptHostBuilder(repository).UseValueStore(values)
                        .Configure(new CefSharpHostOptions { TrustedPageWorld = true }).Build())
                    using (var form = new ProbeForm(origin, attached ? host : null)) Application.Run(form);
                }
            }
        }

        private sealed class ProbeForm : Form
        {
            private readonly ChromiumWebBrowser _browser = new ChromiumWebBrowser("about:blank") { Dock = DockStyle.Fill };
            private readonly CefSharpUserScriptHost _host;
            private readonly Uri _origin;
            private readonly System.Windows.Forms.Timer _watchdog = new System.Windows.Forms.Timer { Interval = 45000 };
            internal ProbeForm(Uri origin, CefSharpUserScriptHost host)
            {
                _host = host; _origin = origin;
                if (host != null) host.Attach(_browser);
                Controls.Add(_browser);
                Shown += async (_, __) => await ExecuteAsync();
                _watchdog.Tick += (_, __) => { Environment.ExitCode = 1; Console.WriteLine("{\"type\":\"bfcache-probe\",\"status\":\"failed\",\"error\":\"watchdog\"}"); Close(); };
                _watchdog.Start();
            }
            private async Task ExecuteAsync()
            {
                try
                {
                    await WaitAsync(() => Task.FromResult(_browser.IsBrowserInitialized));
                    if (_host != null) await _host.PrepareAsync();
                    await NavigateAsync("/probe/bfcache-a");
                    if (_host != null) await WaitAsync(async () => await ReadAsync("document.documentElement.dataset.rawReady==='ready'&&document.documentElement.dataset.isolatedReady==='ready'") == "true");
                    var before = await SnapshotAsync();
                    await NavigateAsync("/probe/bfcache-b");
                    _browser.GetBrowser().GoBack();
                    await WaitAsync(async () => await ReadAsync("location.pathname==='/probe/bfcache-a'&&typeof __cacheProbe!=='undefined'&&__cacheProbe.events.some(e=>e.type==='show')") == "true");
                    var after = await SnapshotAsync();
                    var restored = false;
                    foreach (var item in after.GetProperty("events").EnumerateArray())
                        if (item.GetProperty("type").GetString() == "show" && item.GetProperty("persisted").GetBoolean()) restored = true;
                    Console.WriteLine(JsonSerializer.Serialize(new { type = "bfcache-probe", status = "passed", assessment = "completed",
                        capability = restored ? "cache-hit-requires-runtime-evaluation" : "not-observed;restoration-unproven",
                        runtimeAttached = _host != null, cefSharpVersion = typeof(Cef).Assembly.GetName().Version.ToString(),
                        platform = Environment.Is64BitProcess ? "x64" : "x86", before, after,
                        sameClosure = before.GetProperty("nonce").GetString() == after.GetProperty("nonce").GetString(), persisted = restored,
                        enableBackForwardCacheFlag = Program.EnableBackForwardCacheProbe,
                        configuration = "fresh profile; cacheable A/B; no unload/beforeunload; control has no CDP attachment; runtime has raw and isolated GM" }));
                }
                catch (Exception error) { Environment.ExitCode = 1; Console.WriteLine(JsonSerializer.Serialize(new { type = "bfcache-probe", status = "failed", runtimeAttached = _host != null, error = error.ToString() })); }
                finally { if (_host != null) await _host.DetachAsync(CancellationToken.None); _watchdog.Stop(); Close(); _browser.Dispose(); }
            }
            private async Task NavigateAsync(string path)
            {
                if (_host == null) _browser.Load(new Uri(_origin, path).AbsoluteUri); else await _host.NavigateAsync(new Uri(_origin, path).AbsoluteUri);
                await WaitAsync(async () => await ReadAsync("location.pathname===" + JsonSerializer.Serialize(path) + "&&typeof __cacheProbe!=='undefined'&&__cacheProbe.events.some(e=>e.type==='show')") == "true");
            }
            private async Task<JsonElement> SnapshotAsync()
            {
                var text = await ReadAsync("JSON.stringify({nonce:__cacheProbe.nonce,events:__cacheProbe.events,rawReady:document.documentElement.dataset.rawReady,isolatedReady:document.documentElement.dataset.isolatedReady,gmClosure:typeof __cacheGmClosure==='function'?(()=>{try{return __cacheGmClosure();}catch(e){return String(e);}})():'absent'})");
                using (var document = JsonDocument.Parse(text)) return document.RootElement.Clone();
            }
            private async Task<string> ReadAsync(string expression)
            {
                try
                {
                using (var frame = _browser.GetMainFrame())
                {
                    var response = await frame.EvaluateScriptAsync(expression, timeout: TimeSpan.FromSeconds(2));
                    if (!response.Success) return null;
                    return response.Result is bool value ? value ? "true" : "false" : Convert.ToString(response.Result, System.Globalization.CultureInfo.InvariantCulture);
                }
                }
                catch (TaskCanceledException) { return null; } // Navigation can replace the polled context; the outer deadline still applies.
            }
            private static async Task WaitAsync(Func<Task<bool>> predicate)
            {
                var deadline = DateTime.UtcNow.AddSeconds(8);
                while (!await predicate()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("BFCache probe condition timed out."); await Task.Delay(25); }
            }
        }
    }
}
