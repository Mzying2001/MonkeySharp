using CefSharp;
using CefSharp.Callback;
using CefSharp.WinForms;
using Mzying2001.MonkeySharp.CefSharp;
using Mzying2001.MonkeySharp.Core.Apis;
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
            {
                settings.CefCommandLineArgs.Add("site-per-process", "1");
                settings.CefCommandLineArgs.Add("host-resolver-rules", "MAP *.monkeysharp.test 127.0.0.1, MAP *.monkeysharp-other.test 127.0.0.1");
                settings.CefCommandLineArgs.Add("no-proxy-server", "1");
                initialized = Cef.Initialize(settings);
            }
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
                var childScript = repository.InstallAsync(OopifSource, "consumer://oopif-public-api", true, CancellationToken.None).GetAwaiter().GetResult();
                values.SetAsync(childScript.ScriptKey.ToString(), "snapshot", UserScriptValue.FromJson("\"package-ready\""), CancellationToken.None).GetAwaiter().GetResult();
                string corpusKey = null;
                if (corpus != null)
                    corpusKey = repository.InstallAsync(File.ReadAllText(corpus, Encoding.UTF8), "consumer://original-local-source", true, CancellationToken.None).GetAwaiter().GetResult().ScriptKey.ToString();
                var menu = new ConsumerMenuService();
                using (var host = new CefSharpUserScriptHostBuilder(repository).UseValueStore(values).UseMenuService(menu)
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
                            foreach (var name in new[] { "storage", "cookie", "audio", "partition", "streamUpload", "textProgress" })
                                checks[name] = await Boolean(browser, "window.__packageResult." + name + " === true");
                            menu.Click();
                            await Wait(browser, "window.__packageResult.menuInput === true");
                            checks["menuInput"] = true;
                            await host.NavigateAsync(fixture.Url.Replace("127.0.0.1", "parent.monkeysharp.test") + "/consumer-oopif");
                            await Wait(browser, "window.__consumerOopif && window.__consumerOopif.ready==='package-ready' && window.__consumerOopif.body==='package-ready' && window.__consumerOopif.world==='DOM'");
                            checks["oopifRuntime"] = true;
                            using (var observer = new ConsumerTargetObserver(browser.GetBrowser().GetHost()))
                                checks["oopifNativeTarget"] = await observer.HasChildAsync();
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
        if (!browser.IsBrowserInitialized) return false;
        try
        {
            // The browser-wide CanExecute flag can change between its check and EvaluateScriptAsync.
            // Poll the captured native frame directly; navigation failure remains bounded by Wait.
            using (var frame = browser.GetMainFrame())
            {
                if (frame == null || frame.IsDisposed) return false;
                var result = await frame.EvaluateScriptAsync("Boolean(" + expression + ")", timeout: TimeSpan.FromSeconds(2));
                return result.Success && result.Result is bool && (bool)result.Result;
            }
        }
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
                {
                    var header = new List<byte>();
                    var one = new byte[1];
                    while (header.Count < 65536)
                    {
                        if (await stream.ReadAsync(one, 0, 1) == 0) return;
                        header.Add(one[0]);
                        var n = header.Count;
                        if (n >= 4 && header[n - 4] == 13 && header[n - 3] == 10 && header[n - 2] == 13 && header[n - 1] == 10) break;
                    }
                    var lines = Encoding.ASCII.GetString(header.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
                    var path = lines[0].Split(' ')[1].Split('?')[0];
                    var lengthLine = lines.FirstOrDefault(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                    var length = lengthLine == null ? 0 : int.Parse(lengthLine.Substring(lengthLine.IndexOf(':') + 1));
                    if (length < 0 || length > 1024 * 1024) return;
                    var requestBody = new byte[length];
                    for (var offset = 0; offset < length;)
                    { var read = await stream.ReadAsync(requestBody, offset, length - offset); if (read == 0) return; offset += read; }
                    if (path == "/progress")
                    {
                        var start = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/plain;charset=utf-8\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n3\r\n");
                        await stream.WriteAsync(start, 0, start.Length); await stream.WriteAsync(new byte[] { 97, 228, 189, 13, 10 }, 0, 5); await stream.FlushAsync();
                        await Task.Delay(350);
                        var end = new byte[] { 52, 13, 10, 160, 229, 165, 189, 13, 10, 48, 13, 10, 13, 10 };
                        await stream.WriteAsync(end, 0, end.Length); return;
                    }
                    var port = ((IPEndPoint)client.Client.LocalEndPoint).Port;
                    var page = path == "/consumer-oopif" ? "<!doctype html><body><script>addEventListener('message',function(e){if(e.origin==='http://child.monkeysharp-other.test:" + port + "')window.__consumerOopif=e.data;});</script><iframe src='http://child.monkeysharp-other.test:" + port + "/consumer-child'></iframe></body>" : "<!doctype html><html><body>Packaged consumer fixture</body></html>";
                    var body = path == "/echo" ? requestBody : Encoding.UTF8.GetBytes(page);
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
// @grant GM.xmlHttpRequest
// @grant GM.registerMenuCommand
// @connect 127.0.0.1
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
    const bytes=Uint8Array.from({length:350003},(_,i)=>i%251); let sent=false;
    const uploaded=await GM.xmlHttpRequest({url:'__BASE__/echo',method:'POST',responseType:'arraybuffer',data:new ReadableStream({pull(c){if(sent)c.close();else{sent=true;c.enqueue(bytes);}}},{highWaterMark:0})});
    r.streamUpload=uploaded.response.byteLength===bytes.length&&new Uint8Array(uploaded.response).every((b,i)=>b===bytes[i]);
    const prefixes=[];const text=await GM.xmlHttpRequest({url:'__BASE__/progress',onprogress(value){prefixes.push(value.responseText);}});
    r.textProgress=prefixes.includes('a')&&text.responseText==='a\u4f60\u597d'&&!prefixes.some(value=>value.includes('\ufffd'));
    await GM.registerMenuCommand('Public input',function(event){r.menuInput=event.type==='keydown'&&event.key==='p'&&event.ctrlKey&&!event.isTrusted;});
    r.done = true;
})().catch(error => {window.__packageError=String(error);});";

    private const string OopifSource = @"// ==UserScript==
// @name Packaged OOPIF validation
// @match http://child.monkeysharp-other.test/consumer-child
// @sandbox DOM
// @grant GM_getValue
// @grant GM.xmlHttpRequest
// @connect child.monkeysharp-other.test
// @run-at document-end
// ==/UserScript==
(async function(){const ready=GM_getValue('snapshot');const response=await GM.xmlHttpRequest({url:location.origin+'/echo',method:'POST',data:ready});
parent.postMessage({ready,body:response.responseText,world:GM.info.sandboxMode},'http://parent.monkeysharp.test:'+location.port);})();";

    private sealed class ConsumerMenuService : IMenuService
    {
        private Action<MenuCommandInvocation> _callback;
        public Task<IMenuRegistration> RegisterAsync(MenuCommandRequest request, Action<MenuCommandInvocation> invoked, CancellationToken token)
        { _callback = invoked; return Task.FromResult<IMenuRegistration>(new Registration()); }
        internal void Click() => _callback(new MenuCommandInvocation { Kind = "keyboard", Key = "p", Code = "KeyP", CtrlKey = true });
        private sealed class Registration : IMenuRegistration { public void Dispose() { } }
    }

    private sealed class ConsumerTargetObserver : IDevToolsMessageObserver, IDisposable
    {
        private readonly IBrowserHost _host;
        private readonly TaskCompletionSource<JsonElement> _result = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        private IRegistration _registration;
        private int _id;
        private bool _submitting;
        internal ConsumerTargetObserver(IBrowserHost host) { _host = host; }
        internal async Task<bool> HasChildAsync()
        {
            await Cef.UIThreadTaskFactory.StartNew(() => {
                _registration = _host.AddDevToolsMessageObserver(this);
                _submitting = true;
                try { _id = _host.ExecuteDevToolsMethod(0, "Target.getTargets", "{}"); }
                finally { _submitting = false; }
            });
            if (await Task.WhenAny(_result.Task, Task.Delay(3000)) != _result.Task) throw new TimeoutException("Packaged child target evidence");
            return (await _result.Task).GetProperty("targetInfos").EnumerateArray().Any(item => item.GetProperty("type").GetString() == "iframe" && item.GetProperty("url").GetString().Contains("child.monkeysharp-other.test"));
        }
        public bool OnDevToolsMessage(IBrowser browser, Stream message) => false;
        public void OnDevToolsMethodResult(IBrowser browser, int messageId, bool success, Stream result)
        {
            if (!_submitting && messageId != _id) return;
            using (var reader = new StreamReader(result, Encoding.UTF8, true, 1024, true))
            using (var document = JsonDocument.Parse(reader.ReadToEnd()))
                if (success) _result.TrySetResult(document.RootElement.Clone()); else _result.TrySetException(new InvalidOperationException("Target.getTargets failed"));
        }
        public void OnDevToolsEvent(IBrowser browser, string method, Stream parameters) { }
        public void OnDevToolsAgentAttached(IBrowser browser) { }
        public void OnDevToolsAgentDetached(IBrowser browser) => _result.TrySetException(new ObjectDisposedException("consumer observer"));
        public void Dispose() => _registration?.Dispose();
    }
}
