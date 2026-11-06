using CefSharp;
using CefSharp.WinForms;
using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Storage;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Mzying2001.MonkeySharp.CefSharp.SmokeHost
{
    internal static class CdpProbe
    {
        internal static async Task RunAsync()
        {
            using (var fixture = new LocalHttpFixture())
            using (var store = new InMemoryUserScriptValueStore())
            {
                var start = fixture.Start();
                var repository = new InMemoryUserScriptRepository();
                var script = await repository.InstallAsync("// ==UserScript==\n// @name GM first inline probe\n// @match http://127.0.0.1/*\n// @run-at document-start\n// @grant GM_getValue\n// @grant GM_getResourceText\n// @grant GM.xmlHttpRequest\n// @resource text " + start + "\n// @connect 127.0.0.1\n// ==/UserScript==\n" +
                    "document.documentElement.dataset.gmStart = GM_getValue('ready') + ':' + GM_getResourceText('text');\n" +
                    "GM.xmlHttpRequest({url:location.origin+'/xhr'}).then(function(r){document.documentElement.dataset.gmXhr=r.responseText;});", "probe://timing", true, CancellationToken.None);
                await store.SetAsync(script.ScriptKey.ToString(), "ready", UserScriptValue.FromJson("\"value\""), CancellationToken.None);
                using (var host = new CefSharpUserScriptHostBuilder(repository).UseValueStore(store).UseResourceProvider(new ProbeResources())
                    .Configure(new CefSharpHostOptions { TrustedPageWorld = true }).Build())
                using (var form = new ProbeForm(host, new Uri(start, "/probe/start")))
                    Application.Run(form);
            }
        }

        private sealed class ProbeResources : IResourceProvider
        {
            public Task<ResourceContent> GetAsync(UserScriptInstallation installation, ResourceDeclaration resource, CancellationToken cancellationToken)
                => Task.FromResult(new ResourceContent(Encoding.UTF8.GetBytes("resource"), "text/plain", "resource"));
        }

        private sealed class ProbeForm : Form
        {
            private readonly CefSharpUserScriptHost _host;
            private readonly ChromiumWebBrowser _browser;
            private readonly Uri _url;
            private readonly ConcurrentDictionary<int, JsonElement> _contexts = new ConcurrentDictionary<int, JsonElement>();
            private readonly List<object> _events = new List<object>();
            private readonly System.Windows.Forms.Timer _watchdog = new System.Windows.Forms.Timer { Interval = 60000 };
            private int _bindingContext;
            private int _cleared;

            internal ProbeForm(CefSharpUserScriptHost host, Uri url)
            {
                _host = host; _url = url;
                _browser = new ChromiumWebBrowser("about:blank") { Dock = DockStyle.Fill };
                _host.Attach(_browser);
                // Start waiting before control initialization, to identify prepared navigation.
                var prepared = _host.PrepareAsync();
                Controls.Add(_browser);
                Shown += async (_, __) => await ExecuteAsync(prepared);
                _watchdog.Tick += (_, __) => { Environment.ExitCode = 1; Console.WriteLine("{\"type\":\"cdp-probe\",\"status\":\"failed\",\"error\":\"watchdog\"}"); Close(); };
                _watchdog.Start();
            }

            private async Task ExecuteAsync(Task prepared)
            {
                var checks = new Dictionary<string, bool>();
                var scripts = new List<string>();
                CefSharpCdpSession session = null;
                try
                {
                    await prepared;
                    session = await _host.GetCdpSessionAsync(CancellationToken.None);
                    session.EventReceived += EventReceived;
                    await session.ExecuteAsync("Runtime.enable");
                    await session.ExecuteAsync("Page.enable");
                    var version = await session.ExecuteAsync("Browser.getVersion");
                    scripts.Add((await session.ExecuteAsync("Page.addScriptToEvaluateOnNewDocument", new { source = "window.__pagePrepared = true;" })).GetProperty("identifier").GetString());
                    foreach (var name in new[] { "A", "B" })
                    {
                        var source = "globalThis.__privateProbe='" + name + "'; document.addEventListener('probe-first-inline',function(){document.documentElement.dataset.isolated" + name + "=globalThis.__privateProbe;});";
                        scripts.Add((await session.ExecuteAsync("Page.addScriptToEvaluateOnNewDocument", new { source, worldName = "monkeysharp-probe-" + name })).GetProperty("identifier").GetString());
                    }
                    await _host.NavigateAsync(_url.AbsoluteUri);
                    await WaitAsync(async () => (await EvaluateAsync(session, "typeof __firstInline !== 'undefined' && document.documentElement.dataset.gmXhr === 'legacy xhr ok'")).GetBoolean());
                    var first = await EvaluateAsync(session, "__firstInline");
                    checks["preparedNavigation"] = !_host.IsLatePreparation;
                    checks["pageBeforeFirstInline"] = first.GetProperty("page").GetBoolean();
                    checks["isolatedBeforeFirstInline"] = first.GetProperty("isolatedA").GetString() == "A" && first.GetProperty("isolatedB").GetString() == "B";
                    checks["realGmEventuallyReady"] = (await EvaluateAsync(session, "document.documentElement.dataset.gmStart")).GetString() == "value:resource";
                    var a = FindContext("monkeysharp-probe-A");
                    var b = FindContext("monkeysharp-probe-B");
                    checks["nativeFrameIdentity"] = a.Value.GetProperty("auxData").GetProperty("frameId").GetString() == b.Value.GetProperty("auxData").GetProperty("frameId").GetString();
                    checks["perScriptWorld"] = (await EvaluateAsync(session, "__privateProbe", a.Key)).GetString() == "A" &&
                        (await EvaluateAsync(session, "__privateProbe", b.Key)).GetString() == "B" &&
                        (await EvaluateAsync(session, "typeof __privateProbe")).GetString() == "undefined";
                    await session.ExecuteAsync("Runtime.addBinding", new { name = "__monkeysharpProbeBinding", executionContextId = a.Key });
                    await EvaluateAsync(session, "__monkeysharpProbeBinding('context-proof')", a.Key);
                    await WaitAsync(() => Task.FromResult(_bindingContext == a.Key));
                    checks["contextIdBinding"] = _bindingContext == a.Key && (await EvaluateAsync(session, "typeof __monkeysharpProbeBinding", b.Key)).GetString() == "undefined";
                    await session.ExecuteAsync("Runtime.removeBinding", new { name = "__monkeysharpProbeBinding" });
                    var cookie = await session.ExecuteAsync("Network.setCookie", new { url = _url.AbsoluteUri, name = "cdp-probe", value = "actual", sameSite = "Lax" });
                    checks["cookieEngineAccepted"] = !cookie.TryGetProperty("success", out var accepted) || accepted.GetBoolean();
                    var cookies = await session.ExecuteAsync("Network.getCookies", new { urls = new[] { _url.AbsoluteUri } });
                    checks["cookieReadback"] = cookies.GetProperty("cookies").EnumerateArray().Any(item => item.GetProperty("name").GetString() == "cdp-probe" && item.GetProperty("value").GetString() == "actual" && item.GetProperty("sameSite").GetString() == "Lax");
                    checks["cookieHttpEffect"] = (await EvaluateAsync(session, "fetch('/cookie').then(r=>r.text())", awaitPromise: true)).GetString().Contains("cdp-probe=actual");
                    await session.ExecuteAsync("Network.deleteCookies", new { name = "cdp-probe", domain = _url.Host, path = "/" });
                    checks["cookieDeleted"] = !(await EvaluateAsync(session, "fetch('/cookie').then(r=>r.text())", awaitPromise: true)).GetString().Contains("cdp-probe=");
                    using (var other = new CefSharpCdpSession(_browser.GetBrowser().GetHost()))
                    {
                        await other.InitializeAsync();
                        await Task.WhenAll(other.ExecuteAsync("Browser.getVersion"), session.ExecuteAsync("Browser.getVersion"));
                    }
                    checks["observerCoexistence"] = (await session.ExecuteAsync("Browser.getVersion")).GetProperty("product").GetString() == version.GetProperty("product").GetString();
                    try { await session.ExecuteAsync("Runtime.evaluate", new { expression = "new Promise(()=>{})", awaitPromise = true }, timeout: TimeSpan.FromMilliseconds(40)); }
                    catch (TimeoutException) { checks["commandTimeout"] = true; }
                    using (var cancellation = new CancellationTokenSource(40))
                    {
                        try { await session.ExecuteAsync("Runtime.evaluate", new { expression = "new Promise(()=>{})", awaitPromise = true }, cancellation.Token); }
                        catch (OperationCanceledException) { checks["commandCancellation"] = true; }
                    }
                    await _host.NavigateAsync(_url.AbsoluteUri + "?next");
                    await WaitAsync(() => Task.FromResult(_cleared > 0 && !_contexts.ContainsKey(a.Key)));
                    checks["contextRevocation"] = !_contexts.ContainsKey(a.Key);
                    var strictGm = first.GetProperty("gm").ValueKind == JsonValueKind.String;
                    object[] events;
                    lock (_events) events = _events.ToArray();
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        type = "cdp-probe", status = checks.Values.All(value => value) ? "passed" : "failed",
                        cefSharpVersion = typeof(Cef).Assembly.GetName().Version.ToString(),
                        platform = Environment.Is64BitProcess ? "x64" : "x86", browser = version,
                        checks, firstInline = first,
                        strictGm = strictGm ? "observed-in-this-run-only" : "blocked",
                        timingReason = "current-host-bootstrap-asynchronous",
                        limitation = "Preinjection works; the current asynchronous GM bootstrap does not guarantee readiness before the first inline. No isolated privileged executor is enabled.",
                        contexts = _contexts.ToDictionary(item => item.Key.ToString(), item => item.Value), events
                    }));
                    if (checks.Values.Any(value => !value)) Environment.ExitCode = 1;
                }
                catch (Exception error)
                {
                    Environment.ExitCode = 1;
                    Console.WriteLine(JsonSerializer.Serialize(new { type = "cdp-probe", status = "failed", checks, error = error.ToString() }));
                }
                finally
                {
                    if (session != null)
                    {
                        foreach (var identifier in scripts)
                        {
                            try { await session.ExecuteAsync("Page.removeScriptToEvaluateOnNewDocument", new { identifier }); }
                            catch { }
                        }
                        session.EventReceived -= EventReceived;
                    }
                    await _host.DetachAsync(CancellationToken.None);
                    _watchdog.Stop();
                    Close();
                    _browser.Dispose();
                }
            }

            private void EventReceived(object sender, CdpEventArgs args)
            {
                if (args.Method == "Runtime.executionContextCreated")
                {
                    var context = args.Parameters.GetProperty("context");
                    _contexts[context.GetProperty("id").GetInt32()] = context;
                }
                if (args.Method == "Runtime.executionContextDestroyed") _contexts.TryRemove(args.Parameters.GetProperty("executionContextId").GetInt32(), out _);
                if (args.Method == "Runtime.executionContextsCleared") { _contexts.Clear(); Interlocked.Increment(ref _cleared); }
                if (args.Method == "Runtime.bindingCalled") _bindingContext = args.Parameters.GetProperty("executionContextId").GetInt32();
                lock (_events) _events.Add(new { method = args.Method, parameters = args.Parameters });
            }

            private KeyValuePair<int, JsonElement> FindContext(string name) => _contexts.Single(item => item.Value.GetProperty("name").GetString() == name);

            private static async Task<JsonElement> EvaluateAsync(CefSharpCdpSession session, string expression, int? contextId = null, bool awaitPromise = false)
            {
                var parameters = new Dictionary<string, object> { ["expression"] = expression, ["returnByValue"] = true, ["awaitPromise"] = awaitPromise };
                if (contextId.HasValue) parameters["contextId"] = contextId.Value;
                var response = await session.ExecuteAsync("Runtime.evaluate", parameters);
                if (response.TryGetProperty("exceptionDetails", out var error)) throw new InvalidOperationException(error.GetRawText());
                var result = response.GetProperty("result");
                return result.TryGetProperty("value", out var value) ? value : default(JsonElement);
            }

            private static async Task WaitAsync(Func<Task<bool>> predicate)
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!await predicate())
                {
                    if (DateTime.UtcNow > deadline) throw new TimeoutException("CDP probe condition did not become true.");
                    await Task.Delay(20);
                }
            }
        }
    }
}
