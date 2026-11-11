using CefSharp;
using CefSharp.Callback;
using CefSharp.WinForms;
using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Permissions;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using Mzying2001.MonkeySharp.Core.Storage;
using System.Text;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Mzying2001.MonkeySharp.CefSharp.SmokeHost
{
    internal static class OopifProbe
    {
        internal static void Run()
        {
            using (var fixture = new LocalHttpFixture())
            {
                fixture.Start();
                foreach (var privateContext in new[] { false, true })
                    using (var context = privateContext ? new RequestContext(new RequestContextSettings()) : null)
                    using (var form = new ProbeForm(fixture, context, privateContext)) Application.Run(form);
            }
        }
        private sealed class ProbeForm : Form
        {
            private readonly LocalHttpFixture _fixture;
            private readonly ChromiumWebBrowser _browser;
            private readonly bool _private;
            private readonly List<object> _events = new List<object>();
            private readonly System.Windows.Forms.Timer _watchdog = new System.Windows.Forms.Timer { Interval = 50000 };
            internal ProbeForm(LocalHttpFixture fixture, IRequestContext context, bool privateContext)
            {
                _private = privateContext;
                _browser = new ChromiumWebBrowser("about:blank", context) { Dock = DockStyle.Fill };
                _fixture = fixture; Controls.Add(_browser);
                Shown += async (_, __) => await ExecuteAsync();
                _watchdog.Tick += (_, __) => { Environment.ExitCode = 1; Console.WriteLine("{\"type\":\"oopif-probe\",\"status\":\"failed\",\"error\":\"watchdog\"}"); Close(); };
                _watchdog.Start();
            }
            private async Task ExecuteAsync()
            {
                CefSharpCdpSession root = null;
                RawSession raw = null;
                string childSession = null;
                var facts = new Dictionary<string, object>();
                try
                {
                    var deadline = DateTime.UtcNow.AddSeconds(8);
                    while (!_browser.IsBrowserInitialized) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Browser initialization"); await Task.Delay(20); }
                    root = new CefSharpCdpSession(_browser.GetBrowser().GetHost());
                    await root.InitializeAsync();
                    root.EventReceived += (_, args) => { if (args.Method.StartsWith("Target.", StringComparison.Ordinal)) lock (_events) _events.Add(new { method = args.Method, parameters = args.Parameters }); };
                    await root.ExecuteAsync("Target.setDiscoverTargets", new { discover = true });
                    await root.ExecuteAsync("Page.enable");
                    await root.ExecuteAsync("Page.navigate", new { url = "http://a.monkeysharp.test:" + _fixture.Port + "/probe/oopif" });
                    JsonElement targets = default(JsonElement), child = default(JsonElement);
                    deadline = DateTime.UtcNow.AddSeconds(8);
                    do
                    {
                        targets = await root.ExecuteAsync("Target.getTargets");
                        child = targets.GetProperty("targetInfos").EnumerateArray().FirstOrDefault(item => item.GetProperty("type").GetString() == "iframe" && item.GetProperty("url").GetString().Contains("/probe/oopif-child"));
                        if (child.ValueKind != JsonValueKind.Undefined) break;
                        await Task.Delay(100);
                    } while (DateTime.UtcNow < deadline);
                    facts["targets"] = targets;
                    facts["childHttpRequests"] = _fixture.RequestCount("/probe/oopif-child");
                    facts["rootFrameTree"] = await root.ExecuteAsync("Page.getFrameTree");
                    var supported = false;
                    if (child.ValueKind != JsonValueKind.Undefined)
                    {
                        var attached = await root.ExecuteAsync("Target.attachToTarget", new { targetId = child.GetProperty("targetId").GetString(), flatten = true });
                        childSession = attached.GetProperty("sessionId").GetString();
                        facts["childTarget"] = child; facts["childSession"] = childSession;
                        raw = new RawSession(_browser.GetBrowser().GetHost(), childSession);
                        await raw.InitializeAsync();
                        try
                        {
                            await raw.ExecuteAsync("Runtime.enable");
                            var tree = await raw.ExecuteAsync("Page.getFrameTree");
                            facts["childFrameTree"] = tree;
                            var frameId = tree.GetProperty("frameTree").GetProperty("frame").GetProperty("id").GetString();
                            var world = await raw.ExecuteAsync("Page.createIsolatedWorld", new { frameId, worldName = "monkeysharp-oopif-probe" });
                            var value = await raw.ExecuteAsync("Runtime.evaluate", new { expression = "({origin:location.origin,privateProof:37})", contextId = world.GetProperty("executionContextId").GetInt32(), returnByValue = true });
                            facts["isolatedEvaluation"] = value;
                            supported = value.GetProperty("result").GetProperty("value").GetProperty("privateProof").GetInt32() == 37;
                        }
                        catch (Exception error) { facts["childRoutingError"] = error.ToString(); }
                        using (var other = new CefSharpCdpSession(_browser.GetBrowser().GetHost()))
                        {
                            await other.InitializeAsync();
                            facts["otherObserverAlive"] = (await other.ExecuteAsync("Browser.getVersion")).GetProperty("product").GetString();
                        }
                        await root.ExecuteAsync("Target.detachFromTarget", new { sessionId = childSession });
                        childSession = null;
                        raw.Dispose(); raw = null;
                        if (supported) await ValidateRuntimeAsync(root, facts);
                    }
                    object[] events;
                    lock (_events) events = _events.ToArray();
                    Console.WriteLine(JsonSerializer.Serialize(new { type = "oopif-probe", status = "passed", assessment = "completed",
                        transportProven = supported, featureImplemented = facts.ContainsKey("runtimeValidated"), privateContext = _private, facts, events,
                        cefSharpVersion = typeof(Cef).Assembly.GetName().Version.ToString(), platform = Environment.Is64BitProcess ? "x64" : "x86",
                        configuration = "fresh profile; site-per-process; test-only DNS mapping; distinct sites monkeysharp.test and monkeysharp-other.test; flat session transport via public SendDevToolsMessage" }));
                }
                catch (Exception error) { Environment.ExitCode = 1; Console.WriteLine(JsonSerializer.Serialize(new { type = "oopif-probe", status = "failed", facts, error = error.ToString() })); }
                finally
                {
                    if (childSession != null && root != null) try { await root.ExecuteAsync("Target.detachFromTarget", new { sessionId = childSession }); } catch (Exception) { }
                    raw?.Dispose(); root?.Dispose(); _watchdog.Stop(); Close(); _browser.Dispose();
                }
            }

            private async Task ValidateRuntimeAsync(CefSharpCdpSession root, Dictionary<string, object> facts)
            {
                using (var values = new InMemoryUserScriptValueStore())
                {
                    var repository = new InMemoryUserScriptRepository(preparer: new UserScriptPreparer(resources: new ProbeResources()));
                    var script = await repository.InstallAsync("// ==UserScript==\n// @name Real OOPIF GM\n// @match http://b.monkeysharp-other.test/*\n// @match http://a.monkeysharp.test/probe/oopif-nested*\n" +
                        "// @sandbox DOM\n// @run-at document-end\n// @grant GM_getValue\n// @grant GM.setValue\n// @grant GM_getResourceText\n// @grant GM.xmlHttpRequest\n" +
                        "// @resource text http://b.monkeysharp-other.test/resource\n// @connect b.monkeysharp-other.test\n// @connect a.monkeysharp.test\n// ==/UserScript==\n" +
                        "(async function(){try {globalThis.__oopifPrivateA=17;var ready=GM_getValue('ready');var resource=GM_getResourceText('text');var visits=GM_getValue('visits',0)+1;await GM.setValue('visits',visits);" +
                        "var cookie=(await GM.xmlHttpRequest({url:location.origin+'/cookie'})).responseText;var denied=false;try{await GM.xmlHttpRequest({url:location.origin+'/probe-denied'});}catch(e){denied=e.code==='MSP005_PERMISSION_DENIED';}" +
                        "var d=document.documentElement;d.dataset.oopifRuns=String(Number(d.dataset.oopifRuns||0)+1);d.dataset.oopifResult=JSON.stringify({ready:ready,resource:resource,cookie:cookie,denied:denied,visits:visits,runs:Number(d.dataset.oopifRuns),world:GM.info.sandboxMode,private:GM.info.isIncognito,alien:typeof __oopifPrivateB});" +
                        "}catch(e){document.documentElement.dataset.oopifError=String(e&&e.stack||e);}})();", "probe://actual-oopif", true, CancellationToken.None);
                    await values.SetAsync(script.ScriptKey.ToString(), "ready", UserScriptValue.FromJson("\"snapshot-ready\""), CancellationToken.None);
                    var otherScript = await repository.InstallAsync("// ==UserScript==\n// @name Other OOPIF script\n// @match http://b.monkeysharp-other.test/*\n// @sandbox DOM\n// @run-at document-end\n// @grant GM_getValue\n// ==/UserScript==\n" +
                        "globalThis.__oopifPrivateB=29;document.documentElement.dataset.oopifOther=JSON.stringify({ready:GM_getValue('ready'),alien:typeof __oopifPrivateA});", "probe://other-script", true, CancellationToken.None);
                    await values.SetAsync(otherScript.ScriptKey.ToString(), "ready", UserScriptValue.FromJson("\"other-script\""), CancellationToken.None);
                    using (var host = new CefSharpUserScriptHostBuilder(repository).UseValueStore(values).UsePermissionPolicy(new ProbePolicy())
                        .Configure(new CefSharpHostOptions { IsIncognito = context => context == null ? (bool?)null : _private }).Build())
                    {
                        var lifecycle = new ConcurrentQueue<DocumentLifecycleEventArgs>();
                        var diagnostics = new ConcurrentQueue<string>();
                        host.DocumentLifecycle += (_, args) => lifecycle.Enqueue(args);
                        host.Diagnostic += (_, args) => diagnostics.Enqueue(args.Code + ":" + args.Message + ":" + args.Exception?.Message);
                        host.Attach(_browser);
                        using (var cookies = new CefSharpCookieService(host))
                            await cookies.SetAsync(new UserScriptCookieMutation(new Uri("http://b.monkeysharp-other.test:" + _fixture.Port + "/"), "oopif-proof", _private ? "private" : "normal"), CancellationToken.None);
                        await host.PrepareAsync();
                        var first = await ReadRuntimeAsync(root, "/probe/oopif-child");
                        Require(first.GetProperty("ready").GetString() == "snapshot-ready" && first.GetProperty("resource").GetString() == "offline-resource", "Child synchronous snapshots");
                        Require(first.GetProperty("cookie").GetString().Contains("oopif-proof=" + (_private ? "private" : "normal")) && first.GetProperty("denied").GetBoolean(), "Child RequestContext and permission dispatch");
                        Require(first.GetProperty("private").GetBoolean() == _private, "Child privacy metadata");
                        Require(first.GetProperty("runs").GetInt32() == 1 && first.GetProperty("visits").GetInt32() == 1, "Child executes once");
                        Require(first.GetProperty("alien").GetString() == "undefined", "Per-script globals are isolated");
                        facts["runtimeFirst"] = first;
                        await EditChildAsync(root, "var f=document.createElement('iframe');f.src='http://a.monkeysharp.test:" + _fixture.Port + "/probe/oopif-nested';document.body.appendChild(f);");
                        var nested = await ReadRuntimeAsync(root, "/probe/oopif-nested");
                        Require(nested.GetProperty("runs").GetInt32() == 1 && nested.GetProperty("visits").GetInt32() == 2 && nested.GetProperty("resource").GetString() == "offline-resource", "Nested target source and storage");
                        facts["runtimeNested"] = nested;
                        await root.ExecuteAsync("Runtime.evaluate", new { expression = "document.querySelector('iframe').src='http://a.monkeysharp.test:" + _fixture.Port + "/probe/oopif-child';true", returnByValue = true });
                        await WaitAsync(async () => (await root.ExecuteAsync("Runtime.evaluate", new { expression = "document.querySelector('iframe').contentWindow.location.hostname==='a.monkeysharp.test'", returnByValue = true })).GetProperty("result").TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.True);
                        await root.ExecuteAsync("Runtime.evaluate", new { expression = "document.querySelector('iframe').src='http://b.monkeysharp-other.test:" + _fixture.Port + "/probe/oopif-child';true", returnByValue = true });
                        var migrated = await ReadRuntimeAsync(root, "/probe/oopif-child");
                        Require(migrated.GetProperty("runs").GetInt32() == 1 && migrated.GetProperty("visits").GetInt32() == 3, "Process migration has no duplicate source");
                        facts["runtimeMigrated"] = migrated;
                        if (_private)
                            using (var global = Cef.GetGlobalRequestContext())
                            using (var http = new CefSharpHttpRequestService(global))
                            {
                                var observer = new ProbeHttpObserver();
                                await http.SendAsync(new UserScriptHttpRequest("GET", new Uri("http://b.monkeysharp-other.test:" + _fixture.Port + "/cookie"),
                                    new Dictionary<string, string>(), null, null, null, (_, __) => Task.FromResult(true)), observer, CancellationToken.None).Completion;
                                Require(observer.Text.Contains("oopif-proof=normal") && !observer.Text.Contains("oopif-proof=private"), "Private OOPIF did not change the normal jar");
                                facts["normalJarUnaffected"] = true;
                            }
                        Require(_fixture.RequestCount("/probe-denied") == 0, "Denied child request has no network effect");
                        await host.DetachAsync(CancellationToken.None);
                        var created = lifecycle.Where(item => item.Kind == DocumentLifecycleKind.ContextCreated && item.Frame.ExecutionWorld == "isolated").Select(item => item.Frame).ToArray();
                        await WaitAsync(() => Task.FromResult(created.All(frame => lifecycle.Count(item => item.Kind == DocumentLifecycleKind.ContextReleased && item.Frame.DocumentId == frame.DocumentId) == 1)));
                        facts["runtimeContextBalance"] = created.Length;
                        facts["runtimeFrameScopes"] = created.Select(frame => frame.FrameId).ToArray();
                        facts["runtimeDiagnostics"] = diagnostics.ToArray();
                        facts["runtimeValidated"] = true;
                    }
                }
            }

            private async Task<JsonElement> ReadRuntimeAsync(CefSharpCdpSession root, string path)
            {
                JsonElement answer = default(JsonElement);
                await WaitAsync(async () => {
                    var targets = await root.ExecuteAsync("Target.getTargets");
                    var target = targets.GetProperty("targetInfos").EnumerateArray().FirstOrDefault(item => item.GetProperty("type").GetString() == "iframe" && item.GetProperty("url").GetString().Contains(path));
                    if (target.ValueKind == JsonValueKind.Undefined) return false;
                    using (var child = await root.AttachTargetAsync(target.GetProperty("targetId").GetString(), CancellationToken.None))
                    {
                        try
                        {
                            var expression = "document.documentElement.dataset.oopifError||document.documentElement.dataset.oopifResult";
                            if (path == "/probe/oopif-child") expression = "document.documentElement.dataset.oopifError||(document.documentElement.dataset.oopifOther&&JSON.parse(document.documentElement.dataset.oopifOther).ready==='other-script'?document.documentElement.dataset.oopifResult:undefined)";
                            var result = await child.ExecuteAsync("Runtime.evaluate", new { expression, returnByValue = true });
                            if (!result.GetProperty("result").TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String) return false;
                            using (var parsed = JsonDocument.Parse(value.GetString())) answer = parsed.RootElement.Clone();
                            return true;
                        }
                        finally { await child.DetachAsync(); }
                    }
                });
                return answer;
            }
            private async Task EditChildAsync(CefSharpCdpSession root, string source)
            {
                var targets = await root.ExecuteAsync("Target.getTargets");
                var target = targets.GetProperty("targetInfos").EnumerateArray().First(item => item.GetProperty("type").GetString() == "iframe" && item.GetProperty("url").GetString().Contains("/probe/oopif-child"));
                using (var child = await root.AttachTargetAsync(target.GetProperty("targetId").GetString(), CancellationToken.None))
                    try { await child.ExecuteAsync("Runtime.evaluate", new { expression = "(function(){" + source + "return true;})()", returnByValue = true }); }
                    finally { await child.DetachAsync(); }
            }
            private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
            private static async Task WaitAsync(Func<Task<bool>> predicate)
            {
                var deadline = DateTime.UtcNow.AddSeconds(12);
                while (!await predicate()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("OOPIF runtime condition timed out."); await Task.Delay(50); }
            }
        }

        private sealed class ProbeResources : IResourceProvider
        {
            public Task<ResourceContent> GetAsync(UserScriptInstallation installation, ResourceDeclaration resource, CancellationToken token) =>
                Task.FromResult(new ResourceContent(Encoding.UTF8.GetBytes("offline-resource"), "text/plain", "offline-resource"));
        }
        private sealed class ProbePolicy : IUserScriptPermissionPolicy
        {
            public Task<PermissionDecision> AuthorizeAsync(ApiAuthorizationRequest request, CancellationToken token) =>
                Task.FromResult(request.Target != null && request.Target.Contains("/probe-denied") ? PermissionDecision.Deny : PermissionDecision.Allow);
        }
        private sealed class ProbeHttpObserver : IUserScriptHttpObserver
        {
            private readonly MemoryStream _body = new MemoryStream();
            internal string Text => Encoding.UTF8.GetString(_body.ToArray());
            public void OnResponseStarted(UserScriptHttpResponse response) { }
            public void OnResponseData(byte[] buffer, int offset, int count) => _body.Write(buffer, offset, count);
            public void OnDownloadProgress(long loaded, long? total) { }
            public void OnUploadProgress(long loaded, long? total) { }
        }
        private sealed class RawSession : IDevToolsMessageObserver, IDisposable
        {
            private readonly IBrowserHost _host;
            private readonly string _session;
            private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new ConcurrentDictionary<int, TaskCompletionSource<JsonElement>>();
            private IRegistration _registration;
            internal RawSession(IBrowserHost host, string session) { _host = host; _session = session; }
            internal Task InitializeAsync() => Cef.UIThreadTaskFactory.StartNew(() => { _registration = _host.AddDevToolsMessageObserver(this); });
            internal async Task<JsonElement> ExecuteAsync(string method, object parameters = null)
            {
                var id = 0;
                var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    await Cef.UIThreadTaskFactory.StartNew(() => {
                        // Reserve a CEF-allocated ID; scoped child IDs cannot collide with another observer's root commands.
                        id = _host.ExecuteDevToolsMethod(0, "Browser.getVersion", "{}");
                        if (id <= 0) throw new InvalidOperationException("CEF did not allocate a command ID.");
                        _pending[id] = completion;
                        if (!_host.SendDevToolsMessage(JsonSerializer.Serialize(new { id, method, sessionId = _session, @params = parameters ?? new object() })))
                            throw new InvalidOperationException("CEF rejected the raw session message.");
                    });
                    if (await Task.WhenAny(completion.Task, Task.Delay(2000)) != completion.Task) throw new TimeoutException("Child session command timed out: " + method);
                    return await completion.Task;
                }
                finally { if (id != 0) _pending.TryRemove(id, out _); }
            }
            public bool OnDevToolsMessage(IBrowser browser, Stream message)
            {
                using (var reader = new StreamReader(message, System.Text.Encoding.UTF8, true, 1024, true))
                using (var document = JsonDocument.Parse(reader.ReadToEnd()))
                {
                    var value = document.RootElement;
                    if (!value.TryGetProperty("sessionId", out var session) || session.GetString() != _session) return false;
                    if (value.TryGetProperty("id", out var id) && _pending.TryRemove(id.GetInt32(), out var completion))
                    {
                        if (value.TryGetProperty("error", out var error)) completion.TrySetException(new InvalidOperationException(error.GetRawText()));
                        else completion.TrySetResult(value.GetProperty("result").Clone());
                    }
                    return true; // Only this observer skips unscoped convenience callbacks; other observers remain registered.
                }
            }
            public void OnDevToolsMethodResult(IBrowser browser, int messageId, bool success, Stream result) { }
            public void OnDevToolsEvent(IBrowser browser, string method, Stream parameters) { }
            public void OnDevToolsAgentAttached(IBrowser browser) { }
            public void OnDevToolsAgentDetached(IBrowser browser) { foreach (var item in _pending.Values) item.TrySetException(new ObjectDisposedException("child session")); }
            public void Dispose() { _registration?.Dispose(); foreach (var item in _pending.Values) item.TrySetCanceled(); _pending.Clear(); }
        }
    }
}
