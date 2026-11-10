using CefSharp;
using CefSharp.WinForms;
using Mzying2001.MonkeySharp.CefSharp;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--corpus") return MonkeySharp.Compatibility.CorpusHost.Run(args[1], args[2]);
        var report = Path.GetFullPath(args[0]);
        var expected = new Version(args[1]);
        var corpus = args.Length > 2 ? Path.GetFullPath(args[2]) : null;
        var checks = new Dictionary<string, bool>();
        var initialized = false;
        var success = false;
        string failure = null;
        try
        {
            CefSharpSettings.WcfEnabled = false;
            CefSharpSettings.ConcurrentTaskExecution = true;
            using (var settings = new CefSettings { RootCachePath = Path.Combine(Path.GetDirectoryName(report), "profile-" + Guid.NewGuid().ToString("N")) })
                initialized = Cef.Initialize(settings);
            if (!initialized) throw new InvalidOperationException("CEF initialization failed.");
            var actual = typeof(Cef).Assembly.GetName().Version;
            checks["cefVersion"] = actual.Major == expected.Major && actual.Minor == expected.Minor && actual.Build == expected.Build;
            checks["coreAnyCpu"] = typeof(InMemoryUserScriptRepository).Assembly.GetName().ProcessorArchitecture == System.Reflection.ProcessorArchitecture.MSIL;
            using (var privateContext = CefSharpRequestContexts.CreatePrivate())
                checks["privateContextFactory"] = !privateContext.IsGlobal && privateContext.CachePath == string.Empty;
            using (var fixture = new Fixture())
            using (var values = new InMemoryUserScriptValueStore())
            {
                var repository = new InMemoryUserScriptRepository();
                repository.InstallAsync(Source.Replace("__BASE__", fixture.Url).Replace("__PARTITIONS__", expected.Major == 84 ? "false" : "true"), "consumer://public-api", true, CancellationToken.None).GetAwaiter().GetResult();
                string corpusKey = null;
                if (corpus != null)
                    corpusKey = repository.InstallAsync(File.ReadAllText(corpus, Encoding.UTF8), "consumer://original-local-source", true, CancellationToken.None).GetAwaiter().GetResult().ScriptKey.ToString();
                using (var host = new CefSharpUserScriptHostBuilder(repository).UseValueStore(values)
                    .Configure(new CefSharpHostOptions { TrustedPageWorld = true }).Build())
                using (var form = new Form { Width = 1024, Height = 768, Text = "MonkeySharp packaged consumer" })
                using (var browser = new ChromiumWebBrowser("about:blank") { Dock = DockStyle.Fill })
                {
                    host.Attach(browser);
                    form.Controls.Add(browser);
                    form.Shown += async (_, __) =>
                    {
                        try
                        {
                            await host.NavigateAsync(fixture.Url + "/consumer");
                            await Wait(browser, "window.__packageResult && window.__packageResult.done");
                            foreach (var name in new[] { "storage", "cookie", "audio", "partition" })
                                checks[name] = await Boolean(browser, "window.__packageResult." + name + " === true");
                            if (corpusKey != null)
                            {
                                await host.NavigateAsync("about:blank");
                                await Wait(browser, "document.querySelector('.ms-search-page') && document.querySelector('[data-engine=bing][aria-pressed=true]')");
                                checks["originalCorpusDom"] = true;
                                await browser.EvaluateScriptAsync("document.querySelector('[data-engine=baidu]').click();true;");
                                var deadline = DateTime.UtcNow.AddSeconds(10);
                                while ((await values.GetAsync(corpusKey, "search-engine", CancellationToken.None)).Value?.EncodedValue != "sbaidu" && DateTime.UtcNow < deadline) await Task.Delay(30);
                                checks["originalCorpusStorage"] = (await values.GetAsync(corpusKey, "search-engine", CancellationToken.None)).Value?.EncodedValue == "sbaidu";
                                await browser.EvaluateScriptAsync("document.querySelector('.ms-form').dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));true;");
                                checks["originalCorpusEmptySubmit"] = await Boolean(browser, "location.href === 'about:blank' && document.querySelector('.ms-status').textContent === 'Please enter a search query'");
                                await host.NavigateAsync(fixture.Url + "/reset");
                                await Wait(browser, "location.pathname === '/reset' && document.readyState === 'complete'");
                                await host.NavigateAsync("about:blank");
                                await Wait(browser, "document.querySelector('[data-engine=baidu][aria-pressed=true]')");
                                checks["originalCorpusNavigation"] = true;
                            }
                            success = checks.Values.All(value => value);
                        }
                        catch (Exception error) { failure = error.ToString(); }
                        finally
                        {
                            try { await host.DetachAsync(CancellationToken.None); checks["detached"] = !host.IsAttached; }
                            catch (Exception error) { failure = error.ToString(); success = false; }
                            form.Close();
                        }
                    };
                    Application.Run(form);
                }
            }
        }
        catch (Exception error) { failure = error.ToString(); }
        finally { if (initialized) Cef.Shutdown(); }
        var result = new { type = "package-consumer", success = success && failure == null && checks.Values.All(value => value), checks,
            error = failure, cefSharpVersion = typeof(Cef).Assembly.GetName().Version.ToString(), platform = Environment.Is64BitProcess ? "x64" : "x86",
            corpusSha256 = corpus == null ? null : Hash(File.ReadAllBytes(corpus)) };
        File.WriteAllText(report, JsonSerializer.Serialize(result));
        Console.WriteLine(JsonSerializer.Serialize(result));
        return result.success ? 0 : 1;
    }
    private static string Hash(byte[] bytes) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    private static async Task<bool> Boolean(ChromiumWebBrowser browser, string expression)
    {
        if (!browser.IsBrowserInitialized || !browser.CanExecuteJavascriptInMainFrame) return false;
        try { var result = await browser.EvaluateScriptAsync("Boolean(" + expression + ")"); return result.Success && result.Result is bool && (bool)result.Result; }
        catch (OperationCanceledException) { return false; } // Navigation can retire the evaluated context before its response.
    }
    private static async Task Wait(ChromiumWebBrowser browser, string expression)
    { var until = DateTime.UtcNow.AddSeconds(15); while (DateTime.UtcNow < until) { if (await Boolean(browser, expression)) return; await Task.Delay(50); } throw new TimeoutException(expression); }
    private sealed class Fixture : IDisposable
    {
        private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
        public Fixture() { _listener.Start(); Url = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port; Accept(); }
        public string Url { get; }
        private async void Accept()
        {
            try { while (true) Serve(await _listener.AcceptTcpClientAsync()); }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        }
        private static async void Serve(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
                {
                    string line; do { line = await reader.ReadLineAsync(); } while (!string.IsNullOrEmpty(line));
                    var body = Encoding.UTF8.GetBytes("<!doctype html><html><body>Packaged consumer fixture</body></html>");
                    var headers = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(headers, 0, headers.Length); await stream.WriteAsync(body, 0, body.Length);
                }
            }
            catch (IOException) { }
        }
        public void Dispose() => _listener.Stop();
    }
    private const string Source = @"// ==UserScript==
// @name Packaged public API validation
// @match __BASE__/consumer
// @match https://127.0.0.1/*
// @grant GM_getValue
// @grant GM_setValue
// @grant GM_cookie
// @grant GM_audio
// @run-at document-end
// ==/UserScript==
(async function () {
    const r = window.__packageResult = {};
    await GM.setValue('package-proof', 42); r.storage = GM_getValue('package-proof') === 42;
    await GM.cookie.set({name:'package-cookie',value:'ready',path:'/'});
    r.cookie = (await GM.cookie.list({name:'package-cookie'}))[0].value === 'ready';
    await GM.cookie.delete({name:'package-cookie'});
    await GM.audio.setMute({isMuted:true}); r.audio = (await GM.audio.getState()).isMuted === true;
    await GM.audio.setMute({isMuted:false});
    const details={url:'__BASE__'.replace(/^http:/,'https:')+'/',name:'package-partition',value:'ready',secure:true,path:'/',sameSite:'no_restriction',partitionKey:{topLevelSite:'https://top-a.test'}};
    try { await GM.cookie.set(details); r.partition = __PARTITIONS__ && (await GM.cookie.list(details))[0].value === 'ready'; await GM.cookie.delete(details); }
    catch(error) { r.partition = !__PARTITIONS__ && error.code === 'MSP006_NOT_SUPPORTED'; }
    r.done = true;
})().catch(error => {window.__packageError=String(error);});";
}
