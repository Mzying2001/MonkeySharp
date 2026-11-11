using CefSharp;
using CefSharp.WinForms;
using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Storage;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Mzying2001.MonkeySharp.CefSharp.SmokeHost
{
    internal static class StartCspProbe
    {
        internal const string ResourceSource = "document.documentElement.dataset.resourceEffect='ran';";
        internal static async Task RunAsync()
        {
            using (var fixture = new LocalHttpFixture())
            {
                var origin = fixture.Start();
                foreach (var privateContext in new[] { false, true })
                using (var context = privateContext ? CefSharpRequestContexts.CreatePrivate() : null)
                using (var values = new InMemoryUserScriptValueStore())
                {
                    var content = new Content();
                    var repository = new InMemoryUserScriptRepository(preparer: new UserScriptPreparer(content, content));
                    foreach (var world in new[] { "raw", "isolated" })
                    {
                        var header = "// ==UserScript==\n// @name Full GM " + world + "\n// @match http://127.0.0.1/probe/start-full*\n// @match http://b.monkeysharp-other.test/probe/start-full*\n// @run-at document-start\n" +
                            (world == "isolated" ? "// @sandbox DOM\n" : "") + "// @grant GM_getValue\n// @grant GM_getResourceText\n// @grant GM.xmlHttpRequest\n// @require " + origin + "require.js\n// @resource text " + origin + "text\n// @connect 127.0.0.1\n// @connect b.monkeysharp-other.test\n// ==/UserScript==\n";
                        var script = await repository.InstallAsync(header + "var key=new URL(location.href).searchParams.get('probe')+'" + world + "';" +
                            "var report={world:'" + world + "',nonce:new URL(location.href).searchParams.get('probe'),ready:GM_getValue('ready'),resource:GM_getResourceText('text'),required:requiredProbe,private:GM.info.isIncognito,readyAt:performance.now()};globalThis.__probeStartReport=report;" +
                            "var publish=function(){if(document.documentElement)document.documentElement.setAttribute('data-start-" + world + "',JSON.stringify(report));};publish();if(!document.documentElement){var observer=new MutationObserver(function(){publish();if(document.documentElement)observer.disconnect();});observer.observe(document,{childList:true,subtree:true});}" +
                            "var request=GM.xmlHttpRequest({url:location.origin+'/xhr',onloadstart:function(){console.log('MONKEY_START_ACK:'+JSON.stringify(report));}});report.bridgeShape=typeof request.then;console.log('MONKEY_START_READY:'+JSON.stringify(report));try{localStorage.setItem(key,JSON.stringify(report));}catch(e){report.markerError=String(e);}publish();" +
                            "request.then(function(r){report.bridgeResult=r.responseText;report.doneAt=performance.now();publish();console.log('MONKEY_START_DONE:'+JSON.stringify(report));try{localStorage.setItem(key+'done',JSON.stringify(report));}catch(e){report.markerError=String(e);}});", "probe://full-gm-start", true, CancellationToken.None);
                        await values.SetAsync(script.ScriptKey.ToString(), "ready", UserScriptValue.FromJson("\"snapshot-ready\""), CancellationToken.None);
                        var cspHeader = header.Replace("Full GM " + world, "CSP effects " + world).Replace("/probe/start-full*", "/probe/csp-effects").Replace("document-start", "document-end")
                            .Replace("// @grant GM_getValue", "// @grant GM_getValue\n// @grant GM_addElement\n// @grant GM_addStyle\n// @grant GM_getResourceURL\n// @resource script " + origin + "script.js");
                        await repository.InstallAsync(cspHeader + "(async function(){var style=GM_addStyle('body{margin-left:123px!important}');var node=GM_addElement('span',{id:'csp-" + world + "',textContent:'node'});" +
                            "GM_addElement('script',{textContent:\"document.documentElement.dataset.cspInjectedInline='ran';\"});GM_addElement('script',{src:location.origin+'/probe/csp-external.js'});" +
                            "GM_addElement('link',{rel:'stylesheet',href:location.origin+'/probe/csp.css'});var data=GM_getResourceURL('script');GM_addElement('script',{src:data});" +
                            "var response=await GM.xmlHttpRequest({url:location.origin+'/xhr'});document.documentElement.setAttribute('data-csp-" + world + "',JSON.stringify({node:!!node,styleNode:!!style,resourceBytes:atob(data.split(',')[1]),bridge:response.responseText,required:requiredProbe}));})();", "probe://csp-effects", true, CancellationToken.None);
                    }
                    using (var form = new ProbeForm(origin, repository, values, context, privateContext)) Application.Run(form);
                }
            }
        }
        private sealed class Content : IUserScriptDependencyProvider, IResourceProvider
        {
            public Task<ResourceContent> GetScriptAsync(UserScriptInstallation installation, UserScriptDependencyDeclaration dependency, CancellationToken token) =>
                Task.FromResult(new ResourceContent(Encoding.UTF8.GetBytes("var requiredProbe='required';"), "application/javascript"));
            public Task<ResourceContent> GetAsync(UserScriptInstallation installation, ResourceDeclaration resource, CancellationToken token) =>
                Task.FromResult(new ResourceContent(Encoding.UTF8.GetBytes(resource.Name == "script" ? ResourceSource : "offline-resource"), resource.Name == "script" ? "application/javascript" : "text/plain"));
        }
        private sealed class ProbeForm : Form
        {
            private readonly Uri _origin;
            private readonly InMemoryUserScriptRepository _repository;
            private readonly InMemoryUserScriptValueStore _values;
            private readonly IRequestContext _context;
            private readonly bool _private;
            private ChromiumWebBrowser _browser;
            private CefSharpUserScriptHost _host;
            private bool _readChild;
            private string _phase = "create";
            private readonly System.Collections.Generic.Dictionary<string, object> _observations = new System.Collections.Generic.Dictionary<string, object>();
            private readonly System.Windows.Forms.Timer _watchdog = new System.Windows.Forms.Timer { Interval = 60000 };
            internal ProbeForm(Uri origin, InMemoryUserScriptRepository repository, InMemoryUserScriptValueStore values, IRequestContext context, bool privateContext)
            {
                _origin = origin; _repository = repository; _values = values; _context = context; _private = privateContext;
                Shown += async (_, __) => await ExecuteAsync();
                _watchdog.Tick += (_, __) => { Environment.ExitCode = 1; Console.WriteLine("{\"type\":\"start-csp-probe\",\"status\":\"failed\",\"error\":\"watchdog\"}"); Close(); };
                _watchdog.Start();
            }
            private async Task CreateAsync(bool strict, ConcurrentQueue<string> diagnostics)
            {
                _browser = new ChromiumWebBrowser("about:blank", _context) { Dock = DockStyle.Fill };
                _host = new CefSharpUserScriptHostBuilder(_repository).UseValueStore(_values)
                    .Configure(new CefSharpHostOptions { TrustedPageWorld = true, RequireGuaranteedDocumentStart = strict, IsIncognito = context => context == null ? (bool?)null : _private }).Build();
                _host.Diagnostic += (_, args) => diagnostics.Enqueue(args.Code);
                _host.Attach(_browser); Controls.Add(_browser); await _host.PrepareAsync();
            }
            private async Task ReleaseAsync()
            {
                if (_host != null) { await _host.DetachAsync(CancellationToken.None); _host.Dispose(); _host = null; }
                if (_browser != null) { Controls.Remove(_browser); _browser.Dispose(); _browser = null; }
            }
            private async Task ExecuteAsync()
            {
                try
                {
                    var diagnostics = new ConcurrentQueue<string>();
                    await CreateAsync(false, diagnostics);
                    var nonce = Guid.NewGuid().ToString("N");
                    _phase = "main-first-inline";
                    await _host.NavigateAsync(new Uri(_origin, "/probe/start-full?probe=" + nonce).AbsoluteUri);
                    await WaitAsync(async () => await ReadAsync("!!document.documentElement.getAttribute('data-first-inline')") == "true");
                    var first = await JsonAsync("document.documentElement.getAttribute('data-first-inline')");
                    _observations["firstInline"] = first;
                    _phase = "main-eventual";
                    await WaitAsync(async () => await ReadAsync("!!document.documentElement.getAttribute('data-start-raw')&&!!document.documentElement.getAttribute('data-start-isolated')&&!!JSON.parse(document.documentElement.getAttribute('data-start-raw')).bridgeResult&&!!JSON.parse(document.documentElement.getAttribute('data-start-isolated')).bridgeResult") == "true");
                    var eventual = await JsonAsync("JSON.stringify({raw:JSON.parse(document.documentElement.getAttribute('data-start-raw')),isolated:JSON.parse(document.documentElement.getAttribute('data-start-isolated'))})");
                    foreach (var world in new[] { "raw", "isolated" })
                    {
                        var value = eventual.GetProperty(world);
                        Require(value.GetProperty("ready").GetString() == "snapshot-ready" && value.GetProperty("resource").GetString() == "offline-resource" &&
                            value.GetProperty("required").GetString() == "required" && value.GetProperty("bridgeResult").GetString() == "legacy xhr ok" && value.GetProperty("private").GetBoolean() == _private, "Prepared full GM eventually ready: " + world);
                    }
                    _phase = "paused-candidate";
                    var pausedCandidate = await PausedCandidateAsync();
                    _observations["pausedCandidate"] = pausedCandidate;
                    var childNonce = Guid.NewGuid().ToString("N");
                    _readChild = true;
                    _phase = "child-first-inline";
                    await _host.NavigateAsync(new Uri(_origin, "/probe/start-frame-parent?probe=" + childNonce).AbsoluteUri);
                    await WaitAsync(async () => await ReadAsync("new URL(location.href).searchParams.get('probe')==='" + childNonce + "'&&!!document.documentElement.getAttribute('data-first-inline')") == "true");
                    var childFirst = await JsonAsync("document.documentElement.getAttribute('data-first-inline')");
                    _phase = "child-eventual";
                    await WaitAsync(async () => await ReadAsync("!!document.documentElement.getAttribute('data-start-isolated')&&!!JSON.parse(document.documentElement.getAttribute('data-start-isolated')).bridgeResult") == "true");
                    var childEventual = await JsonAsync("JSON.stringify({raw:JSON.parse(document.documentElement.getAttribute('data-start-raw')||'null'),isolated:JSON.parse(document.documentElement.getAttribute('data-start-isolated'))})");
                    Require(childEventual.GetProperty("isolated").GetProperty("bridgeResult").GetString() == "legacy xhr ok", "OOPIF full isolated GM eventually ready");
                    await ReleaseAsync();
                    _readChild = false;
                    var strictDiagnostics = new ConcurrentQueue<string>();
                    _phase = "strict-main";
                    await CreateAsync(true, strictDiagnostics);
                    var strictNonce = Guid.NewGuid().ToString("N");
                    await _host.NavigateAsync(new Uri(_origin, "/probe/start-full?probe=" + strictNonce).AbsoluteUri);
                    await WaitAsync(() => Task.FromResult(strictDiagnostics.Count(code => code == "MSR101_DOCUMENT_START_UNAVAILABLE") >= 2));
                    var strict = await JsonAsync("JSON.stringify({raw:localStorage.getItem('" + strictNonce + "raw'),isolated:localStorage.getItem('" + strictNonce + "isolated')})");
                    Require(strict.GetProperty("raw").ValueKind == JsonValueKind.Null && strict.GetProperty("isolated").ValueKind == JsonValueKind.Null, "Strict start rejects source before effects");
                    _readChild = true;
                    _phase = "strict-child";
                    var childStrictNonce = Guid.NewGuid().ToString("N");
                    await _host.NavigateAsync(new Uri(_origin, "/probe/start-frame-parent?probe=" + childStrictNonce).AbsoluteUri);
                    await WaitAsync(() => Task.FromResult(strictDiagnostics.Count(code => code == "MSR101_DOCUMENT_START_UNAVAILABLE") >= 3));
                    await WaitAsync(async () => await ReadAsync("new URL(location.href).searchParams.get('probe')==='" + childStrictNonce + "'&&!!document.documentElement.getAttribute('data-first-inline')") == "true");
                    var childStrict = await JsonAsync("JSON.stringify({raw:localStorage.getItem('" + childStrictNonce + "raw'),isolated:localStorage.getItem('" + childStrictNonce + "isolated')})");
                    Require(childStrict.GetProperty("raw").ValueKind == JsonValueKind.Null && childStrict.GetProperty("isolated").ValueKind == JsonValueKind.Null, "Strict OOPIF start rejects before effects");
                    _readChild = false;
                    _phase = "csp-main";
                    await _host.NavigateAsync(new Uri(_origin, "/probe/csp-effects").AbsoluteUri);
                    await WaitAsync(async () => await ReadAsync("!!document.documentElement.getAttribute('data-csp-isolated')&&document.documentElement.dataset.cspExternal==='ran'") == "true");
                    var csp = await JsonAsync("JSON.stringify({isolated:JSON.parse(document.documentElement.getAttribute('data-csp-isolated')),raw:document.documentElement.getAttribute('data-csp-raw'),pageInline:typeof document.documentElement.dataset.cspPageInline,injectedInline:typeof document.documentElement.dataset.cspInjectedInline,resourceEffect:document.documentElement.dataset.resourceEffect||null,margin:getComputedStyle(document.body).marginLeft,outline:getComputedStyle(document.body).outlineWidth,outlineStyle:getComputedStyle(document.body).outlineStyle,external:document.documentElement.dataset.cspExternal})");
                    _observations["csp"] = csp;
                    Require(csp.GetProperty("raw").ValueKind == JsonValueKind.Null && csp.GetProperty("pageInline").GetString() == "undefined" && csp.GetProperty("injectedInline").GetString() == "undefined", "Page and raw CSP source boundaries");
                    var isolated = csp.GetProperty("isolated");
                    Require(isolated.GetProperty("node").GetBoolean() && isolated.GetProperty("styleNode").GetBoolean() && isolated.GetProperty("resourceBytes").GetString() == ResourceSource && isolated.GetProperty("bridge").GetString() == "legacy xhr ok", "Isolated source, nodes, resource bytes and bridge");
                    Require(csp.GetProperty("margin").GetString() != "123px" && csp.GetProperty("outlineStyle").GetString() == "none" && csp.GetProperty("resourceEffect").ValueKind == JsonValueKind.Null, "Styles and data script remain blocked");
                    _readChild = true;
                    _phase = "csp-child";
                    await _host.NavigateAsync(new Uri(_origin, "/probe/csp-frame-parent").AbsoluteUri);
                    await WaitAsync(async () => await ReadAsync("!!document.documentElement.getAttribute('data-csp-isolated')&&document.documentElement.dataset.cspExternal==='ran'") == "true");
                    var childCsp = await JsonAsync("JSON.stringify({isolated:JSON.parse(document.documentElement.getAttribute('data-csp-isolated')),pageInline:typeof document.documentElement.dataset.cspPageInline,injectedInline:typeof document.documentElement.dataset.cspInjectedInline,resourceEffect:document.documentElement.dataset.resourceEffect||null,margin:getComputedStyle(document.body).marginLeft})");
                    Require(childCsp.GetProperty("isolated").GetProperty("bridge").GetString() == "legacy xhr ok" && childCsp.GetProperty("isolated").GetProperty("resourceBytes").GetString() == ResourceSource &&
                        childCsp.GetProperty("pageInline").GetString() == "undefined" && childCsp.GetProperty("injectedInline").GetString() == "undefined" && childCsp.GetProperty("margin").GetString() != "123px" && childCsp.GetProperty("resourceEffect").ValueKind == JsonValueKind.Null, "OOPIF CSP and resource boundaries");
                    Console.WriteLine(JsonSerializer.Serialize(new { type = "start-csp-probe", status = "passed", assessment = "completed", fullGmStartGuaranteed = false,
                        firstInline = first, eventual, pausedCandidate, childFirstInline = childFirst, childEventual, strictRejected = true, strict, childStrict, csp, childCsp, privateContext = _private, diagnostics = diagnostics.ToArray(), strictDiagnostics = strictDiagnostics.ToArray(),
                        cefSharpVersion = typeof(Cef).Assembly.GetName().Version.ToString(), platform = Environment.Is64BitProcess ? "x64" : "x86",
                        configuration = "prepared offline revision; require; sync value/resource; actual native GM request; unique localStorage marker; first inline; strict rejection; unchanged CSP header; no bypass" }));
                }
                catch (Exception error) { Environment.ExitCode = 1; Console.WriteLine(JsonSerializer.Serialize(new { type = "start-csp-probe", status = "failed", phase = _phase, privateContext = _private, observations = _observations, error = error.ToString() })); }
                finally { await ReleaseAsync(); _watchdog.Stop(); Close(); }
            }
            private async Task<JsonElement> PausedCandidateAsync()
            {
                var session = await _host.GetCdpSessionAsync(CancellationToken.None);
                var paused = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler<CdpEventArgs> observe = (_, args) => { if (args.Method == "Debugger.paused") paused.TrySetResult(true); };
                string identifier = null;
                JsonElement beforeResume = default(JsonElement);
                string error = null;
                var nonce = Guid.NewGuid().ToString("N");
                var markers = new ConcurrentDictionary<string, JsonElement>();
                System.Collections.Generic.Dictionary<string, JsonElement> beforeMarkers = null;
                EventHandler<CdpEventArgs> console = (_, args) =>
                {
                    if (args.Method != "Runtime.consoleAPICalled") return;
                    foreach (var argument in args.Parameters.GetProperty("args").EnumerateArray())
                    {
                        if (!argument.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String) continue;
                        var text = value.GetString();
                        if (!text.StartsWith("MONKEY_START_", StringComparison.Ordinal)) continue;
                        var separator = text.IndexOf(':');
                        using (var document = JsonDocument.Parse(text.Substring(separator + 1)))
                            if (document.RootElement.GetProperty("nonce").GetString() == nonce)
                                markers[text.Substring(0, separator) + ":" + document.RootElement.GetProperty("world").GetString()] = document.RootElement.Clone();
                    }
                };
                session.EventReceived += observe;
                session.EventReceived += console;
                try
                {
                    await session.ExecuteAsync("Debugger.enable");
                    identifier = (await session.ExecuteAsync("Page.addScriptToEvaluateOnNewDocument", new { source = "debugger;\n//# sourceURL=monkeysharp-owned-start-experiment" })).GetProperty("identifier").GetString();
                    await _host.NavigateAsync(new Uri(_origin, "/probe/start-full?probe=" + nonce).AbsoluteUri);
                    if (await Task.WhenAny(paused.Task, Task.Delay(3000)) != paused.Task) throw new TimeoutException("The candidate did not observe its parser pause.");
                    var deadline = DateTime.UtcNow.AddSeconds(2);
                    do
                    {
                        var result = await session.ExecuteAsync("Runtime.evaluate", new { expression = "JSON.stringify({rawReady:JSON.parse(localStorage.getItem('" + nonce + "raw')||'null'),isolatedReady:JSON.parse(localStorage.getItem('" + nonce + "isolated')||'null'),rawAck:localStorage.getItem('" + nonce + "rawack'),isolatedAck:localStorage.getItem('" + nonce + "isolatedack'),raw:JSON.parse(localStorage.getItem('" + nonce + "rawdone')||'null'),isolated:JSON.parse(localStorage.getItem('" + nonce + "isolateddone')||'null'),pageRan:typeof __startCanary!=='undefined'})", returnByValue = true });
                        using (var document = JsonDocument.Parse(result.GetProperty("result").GetProperty("value").GetString())) beforeResume = document.RootElement.Clone();
                        if (beforeResume.GetProperty("raw").ValueKind == JsonValueKind.Object && beforeResume.GetProperty("isolated").ValueKind == JsonValueKind.Object) break;
                        await Task.Delay(25);
                    } while (DateTime.UtcNow < deadline);
                    beforeMarkers = markers.ToDictionary(item => item.Key, item => item.Value);
                }
                catch (Exception exception) { error = exception.Message; }
                finally
                {
                    if (paused.Task.IsCompleted) try { await session.ExecuteAsync("Debugger.resume"); } catch (Exception) { }
                    if (identifier != null) try { await session.ExecuteAsync("Page.removeScriptToEvaluateOnNewDocument", new { identifier }); } catch (Exception) { }
                    try { await session.ExecuteAsync("Debugger.disable"); } catch (Exception) { }
                    session.EventReceived -= observe;
                    session.EventReceived -= console;
                }
                return JsonSerializer.SerializeToElement(new { parserPauseObserved = paused.Task.IsCompleted,
                    beforeResume = beforeResume.ValueKind == JsonValueKind.Undefined ? (JsonElement?)null : beforeResume,
                    beforeMarkers, error, integrated = false, scope = "single owned debugger pause; readiness/ack/completion observed separately during a bounded two-second window; no production timing guarantee" });
            }
            private async Task<string> ReadAsync(string expression)
            {
                if (_browser == null || !_browser.IsBrowserInitialized) return null;
                try
                {
                    if (_readChild)
                    {
                        var session = await _host.GetCdpSessionAsync(CancellationToken.None);
                        var parent = (await session.ExecuteAsync("Page.getFrameTree")).GetProperty("frameTree").GetProperty("frame").GetProperty("id").GetString();
                        var targets = await session.ExecuteAsync("Target.getTargets");
                        foreach (var target in targets.GetProperty("targetInfos").EnumerateArray())
                        {
                            if (target.GetProperty("type").GetString() != "iframe" || !target.GetProperty("url").GetString().Contains("b.monkeysharp-other.test")) continue;
                            using (var child = await session.AttachTargetAsync(target.GetProperty("targetId").GetString(), CancellationToken.None))
                            {
                                try
                                {
                                    var frame = (await child.ExecuteAsync("Page.getFrameTree")).GetProperty("frameTree").GetProperty("frame");
                                    if (!frame.TryGetProperty("parentId", out var owner) || owner.GetString() != parent) continue;
                                    var result = await child.ExecuteAsync("Runtime.evaluate", new { expression, returnByValue = true });
                                    if (result.TryGetProperty("exceptionDetails", out _) || !result.GetProperty("result").TryGetProperty("value", out var value)) return null;
                                    if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False) return value.GetBoolean() ? "true" : "false";
                                    return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
                                }
                                finally { await child.DetachAsync(); }
                            }
                        }
                        return null;
                    }
                    IFrame selected = null;
                    selected = _browser.GetMainFrame();
                    if (selected == null) return null;
                    using (var frame = selected)
                    {
                        var result = await frame.EvaluateScriptAsync(expression, timeout: TimeSpan.FromSeconds(2));
                        if (!result.Success) return null;
                        return result.Result is bool value ? value ? "true" : "false" : Convert.ToString(result.Result, System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
                catch (TaskCanceledException) { return null; }
            }
            private async Task<JsonElement> JsonAsync(string expression)
            { using (var document = JsonDocument.Parse(await ReadAsync(expression))) return document.RootElement.Clone(); }
            private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
            private static async Task WaitAsync(Func<Task<bool>> predicate)
            {
                var deadline = DateTime.UtcNow.AddSeconds(12);
                while (!await predicate()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Start/CSP probe condition timed out."); await Task.Delay(25); }
            }
        }
    }
}
