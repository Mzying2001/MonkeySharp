using CefSharp;
using CefSharp.WinForms;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.CefSharp.SmokeHost
{
    internal sealed class CompatibilitySuite : IDisposable
    {
        private readonly CefSharpUserScriptHost _host;
        private readonly ChromiumWebBrowser _browser;
        private readonly LocalHttpFixture _fixture;
        private readonly SmokeHttpDownloadService _downloads;
        private readonly LocalHttpFixture _otherFixture;
        private readonly SmokePermissionPolicy _permissions;
        private readonly InMemoryUserScriptRepository _repository;
        private readonly object _sync = new object();
        private readonly Dictionary<string, DocumentFrame> _frames = new Dictionary<string, DocumentFrame>();
        private readonly Dictionary<string, DocumentFrame> _isolatedFrames = new Dictionary<string, DocumentFrame>();
        private readonly List<DocumentLifecycleEventArgs> _events = new List<DocumentLifecycleEventArgs>();
        private Dictionary<string, bool> _checks;
        private string _lastState;
        private DocumentFrame _context;

        public CompatibilitySuite(CefSharpUserScriptHost host, ChromiumWebBrowser browser,
            LocalHttpFixture fixture, SmokeHttpDownloadService downloads, LocalHttpFixture otherFixture, SmokePermissionPolicy permissions, InMemoryUserScriptRepository repository)
        {
            _host = host; _browser = browser; _fixture = fixture; _downloads = downloads;
            _otherFixture = otherFixture; _permissions = permissions;
            _repository = repository;
            host.DocumentLifecycle += Lifecycle;
        }

        public static async Task InstallAsync(InMemoryUserScriptRepository repository, int port, int otherPort)
        {
            await repository.InstallAsync("// ==UserScript==\n// @name Prepared content\n// @match http://127.0.0.1/compat/prepared*\n" +
                "// @require https://content.example/one.js\n// @require https://content.example/two.js\n" +
                "// @resource binary https://content.example/binary\n// @grant GM_getResourceText\n// @grant GM_getResourceURL\n// @grant GM_getValue\n// @run-at document-end\n// ==/UserScript==\n" +
                "var binary = atob(GM_getResourceURL('binary').split(',')[1]);\n" +
                "globalThis.__preparedResult = {done:true, shared:shared, accented:accented, strict:(function(){return this;})()===undefined, " +
                "length:binary.length, bytes:binary.charCodeAt(0)===0 && binary.charCodeAt(1)===255 && binary.charCodeAt(2)===1, " +
                "text:GM_getResourceText('binary').length, storageLength:GM_getValue('large').length};", "smoke://prepared", true, CancellationToken.None);
            var assembly = typeof(CompatibilitySuite).Assembly;
            foreach (var name in assembly.GetManifestResourceNames().Where(name => name.EndsWith(".user.js", StringComparison.Ordinal)).OrderBy(name => name, StringComparer.Ordinal))
            {
                using (var reader = new StreamReader(assembly.GetManifestResourceStream(name)))
                    await repository.InstallAsync(reader.ReadToEnd().Replace("__PORT__", port.ToString(CultureInfo.InvariantCulture))
                        .Replace("__OTHER_PORT__", otherPort.ToString(CultureInfo.InvariantCulture)), "smoke://" + name, true, CancellationToken.None);
            }
        }

        public async Task<Dictionary<string, bool>> RunAsync()
        {
            var results = new Dictionary<string, bool>();
            if (Program.CompatibilityCase == "webRequest")
            {
                results["webRequest"] = await RunCaseAsync("webRequest", WebRequestAsync);
                return results;
            }
            results["prepared"] = await RunCaseAsync("prepared", PreparedAsync);
            results["conditions"] = await RunCaseAsync("conditions", ConditionsAsync);
            results["isolated"] = await RunCaseAsync("isolated", IsolatedAsync);
            results["replay"] = await RunCaseAsync("replay", ReplayAsync);
            results["csp"] = await RunCaseAsync("csp", CspAsync);
            results["pageHelpers"] = await RunCaseAsync("pageHelpers", PageHelpersAsync);
            results["binary"] = await RunCaseAsync("binary", BinaryAsync);
            results["permissions"] = await RunCaseAsync("permissions", PermissionsAsync);
            results["frames"] = await RunCaseAsync("frames", FramesAsync);
            results["spaUrl"] = await RunCaseAsync("spaUrl", SpaAsync);
            results["download"] = await RunCaseAsync("download", DownloadAsync);
            results["cookie"] = await RunCaseAsync("cookie", CookieAsync);
            results["webRequest"] = await RunCaseAsync("webRequest", WebRequestAsync);
            results["audio"] = await RunCaseAsync("audio", AudioAsync);
            results["partitions"] = await RunCaseAsync("partitions", PartitionsAsync);
            return results;
        }

        private async Task<bool> RunCaseAsync(string id, Func<Task> action)
        {
            _checks = new Dictionary<string, bool>();
            _lastState = null;
            _context = null;
            string error = null;
            try { await action(); }
            catch (Exception exception) { error = exception.ToString(); }
            var success = error == null && _checks.Count > 0 && _checks.Values.All(value => value);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                type = "compatibility-case", caseId = id, success, checks = _checks, error,
                frameId = _context?.FrameId, documentId = _context?.DocumentId,
                url = _context?.Url.AbsoluteUri, state = _lastState
            }));
            return success;
        }

        private async Task PageHelpersAsync()
        {
            await NavigateAsync("/compat/page-helpers");
            var result = await WaitResultAsync("globalThis.__pageHelpers || null");
            foreach (var check in result.EnumerateObject().Where(value => value.Name != "done")) Check(check.Name, check.Value.GetBoolean());
        }

        private async Task BinaryAsync()
        {
            await NavigateAsync("/compat/binary");
            var result = await WaitResultAsync("globalThis.__binary || null");
            foreach (var check in result.EnumerateObject().Where(value => value.Name != "done")) Check(check.Name, check.Value.GetBoolean());
            foreach (var name in new[] { "blob-bytes.bin", "file-bytes.bin", "url-bytes.bin" })
                Check(name, File.ReadAllBytes(Path.Combine(_downloads.DirectoryPath, name)).SequenceEqual(Enumerable.Range(0, 350003).Select(value => (byte)(value % 251))));
            Check("noPartialFiles", Directory.GetFiles(_downloads.DirectoryPath, "*.part").Length == 0);
        }

        private async Task PreparedAsync()
        {
            foreach (var path in new[] { "/compat/prepared", "/compat/prepared-again" })
            {
                await NavigateAsync(path);
                var result = await WaitResultAsync("globalThis.__preparedResult || null");
                Check(path + ":scope", result.GetProperty("shared").GetInt32() == 2 && result.GetProperty("strict").GetBoolean());
                Check(path + ":charset", result.GetProperty("accented").GetString() == "café");
                Check(path + ":binary", result.GetProperty("length").GetInt32() == 600003 && result.GetProperty("bytes").GetBoolean());
                Check(path + ":syncText", result.GetProperty("text").GetInt32() == 600003);
                Check(path + ":storageSnapshot", result.GetProperty("storageLength").GetInt32() == 600000);
            }
            await ReadAsync("(function(){var f=document.createElement('iframe'); f.src='/compat/prepared-frame'; document.body.appendChild(f);return true;})()");
            await WaitAsync(() => Task.FromResult(FindFrame("/compat/prepared-frame") != null));
            var child = await WaitResultAsync("globalThis.__preparedResult || null", FindFrame("/compat/prepared-frame"));
            Check("frameResources", child.GetProperty("length").GetInt32() == 600003 && child.GetProperty("shared").GetInt32() == 2);
            Check("frameStorageSnapshot", child.GetProperty("storageLength").GetInt32() == 600000);
        }

        private async Task ConditionsAsync()
        {
            await NavigateAsync("/compat/conditions");
            await WaitAsync(async () => (await ReadAsync("document.documentElement.dataset.normalCondition === 'ran' && document.documentElement.dataset.matchCondition === 'ran'"))?.GetBoolean() == true);
            Check("normalContext", true);
            Check("privateContextRejected", (await ReadAsync("document.documentElement.dataset.privateCondition === undefined")).Value.GetBoolean());
            Check("manualDoesNotAutoRun", (await ReadAsync("document.documentElement.dataset.manualCondition === undefined")).Value.GetBoolean());
            var frame = FindFrame("/compat/conditions");
            var entry = (await _host.Engine.GetContextMenuEntriesAsync(frame, CancellationToken.None)).Single(value => value.Title == "Manual metadata condition");
            var plan = await _host.Engine.ProcessContextMenuAsync(frame, entry.ScriptKey, CancellationToken.None);
            await _host.ExecuteAsync(frame, Mzying2001.MonkeySharp.Core.Bridge.BridgeScriptBuilder.BuildInjection(plan), CancellationToken.None);
            await WaitAsync(async () => (await ReadAsync("document.documentElement.dataset.manualCondition === 'ran'"))?.GetBoolean() == true);
            Check("manualIgnoresIncludeExclude", true);
            await NavigateAsync("/compat/conditions-blocked");
            await WaitAsync(async () => (await ReadAsync("document.documentElement.dataset.normalCondition === 'ran'"))?.GetBoolean() == true);
            Check("exclusion", (await ReadAsync("document.documentElement.dataset.matchCondition === undefined")).Value.GetBoolean());
        }

        private async Task IsolatedAsync()
        {
            await NavigateAsync("/compat/isolated");
            const string readA = "JSON.parse(document.documentElement.getAttribute('data-isolated-a') || 'null')";
            const string readB = "JSON.parse(document.documentElement.getAttribute('data-isolated-b') || 'null')";
            var a = await WaitResultAsync(readA);
            var b = await WaitResultAsync(readB);
            Check("world", a.GetProperty("world").GetString() == "isolated" && b.GetProperty("world").GetString() == "isolated");
            Check("perScriptGlobals", a.GetProperty("privateValue").GetString() == "A" && b.GetProperty("value").GetString() == "B");
            Check("pageIsolation", a.GetProperty("page").GetString() == "undefined" && b.GetProperty("page").GetString() == "undefined" &&
                (await ReadAsync("typeof privateWorldValue === 'undefined' && typeof __MonkeySharpIsolatedTransport === 'undefined'")).Value.GetBoolean());
            Check("unsafeWindow", a.GetProperty("unsafe").GetString() == "undefined");
            Check("synchronousData", a.GetProperty("resource").GetString() == "ready" && a.GetProperty("synchronousValue").GetInt32() == 41 && a.GetProperty("committedValue").GetInt32() == 42);
            Check("xhr", a.GetProperty("xhr").GetString() == "legacy xhr ok");
            Check("nativeProofScope", a.GetProperty("forgeryDenied").GetBoolean());
            Check("rawRunsOnce", (await ReadAsync("window.__rawControlCount === 1")).Value.GetBoolean());
            DocumentFrame[] main;
            await WaitAsync(() => { lock (_sync) return Task.FromResult(_isolatedFrames.Values.Count(frame => frame.IsMainFrame && frame.Url.AbsolutePath == "/compat/isolated") == 3); });
            lock (_sync) main = _isolatedFrames.Values.Where(frame => frame.IsMainFrame && frame.Url.AbsolutePath == "/compat/isolated").ToArray();
            Check("nativeIdentity", main.Length == 3 && main.All(frame => frame.FrameId.StartsWith("cdp:") && frame.ScriptScope.HasValue) && main.Select(frame => frame.DocumentId).Distinct().Count() == 3);
            await ReadAsync("(history.pushState({},'', '/compat/isolated-spa'), true)");
            await WaitAsync(async () => (await ReadAsync(readA + ".changes.length === 1"))?.GetBoolean() == true);
            Check("urlchange", true);
            await ReadAsync("(history.replaceState({},'', '/compat/isolated-spa-next'), true)");
            await WaitAsync(async () => (await ReadAsync(readA + ".tail === 2"))?.GetBoolean() == true);
            var urlState = (await ReadAsync(readA)).Value;
            Check("isolatedUrlOptions", urlState.GetProperty("once").GetInt32() == 1 && urlState.GetProperty("aborted").GetInt32() == 0 && urlState.GetProperty("changes").GetArrayLength() == 2);
            await ReadAsync("(function(){var f=document.createElement('iframe');f.src='/compat/isolated-child';document.body.appendChild(f);return true;})()");
            await WaitAsync(() => Task.FromResult(FindFrame("/compat/isolated-child") != null));
            var child = await WaitResultAsync(readA, FindFrame("/compat/isolated-child"));
            Check("childFrame", child.GetProperty("privateValue").GetString() == "A" && child.GetProperty("committedValue").GetInt32() == 42);
            Check("childRawRunsOnce", (await ReadAsync("window.__rawControlCount === 1", FindFrame("/compat/isolated-child"))).Value.GetBoolean());
            CefSharpMenuTarget capturedMenu = null;
            foreach (var identifier in _browser.GetBrowser().GetFrameIdentifiers())
                using (var native = CefSharpUserScriptHost.GetFrameByIdentifier(_browser.GetBrowser(), identifier.ToString()))
                    if (native != null && native.Url.Contains("/compat/isolated-child"))
                    {
                        if (typeof(Cef).Assembly.GetName().Version.Major != 84)
                        {
                            Check("childMenuNativeProofUnavailable", !_host.GetContextMenuEntries(native).Any(item => item.Entry.Title == "Isolated manual frame"));
                            continue;
                        }
                        await WaitAsync(() => Task.FromResult(_host.GetContextMenuEntries(native).Any(item => item.Entry.Title == "Isolated manual frame")));
                        capturedMenu = _host.GetContextMenuEntries(native).Single(item => item.Entry.Title == "Isolated manual frame");
                        Check("clickedFrameAttestation", ReferenceEquals(capturedMenu.Frame, _host.ResolveMenuFrame(native,
                            new MenuCommandRequest(capturedMenu.Entry.ScriptKey, "frame", null, originFrame: capturedMenu.Frame, clickedFrame: true))));
                        _host.ExecuteContextMenu(native, capturedMenu);
                    }
            if (capturedMenu != null)
            {
                Check("nativeChildMenuMapping", true);
                await WaitAsync(async () => (await ReadAsync("document.documentElement.dataset.manualFrameCount === '1' && document.documentElement.dataset.manualFrameValue === 'ready'", FindFrame("/compat/isolated-child")))?.GetBoolean() == true);
                Check("isolatedChildManualExecution", true);
                await ReadAsync("(document.querySelector('iframe').src='/compat/isolated-child?replacement', true)");
                await WaitAsync(async () => (await ReadAsync("location.search === '?replacement' && !document.documentElement.dataset.manualFrameCount", FindFrame("/compat/isolated-child")))?.GetBoolean() == true);
                foreach (var identifier in _browser.GetBrowser().GetFrameIdentifiers())
                    using (var native = CefSharpUserScriptHost.GetFrameByIdentifier(_browser.GetBrowser(), identifier.ToString()))
                        if (native != null && native.Url.Contains("/compat/isolated-child"))
                        {
                            Check("staleClickedFrameRejected", _host.ResolveMenuFrame(native,
                                new MenuCommandRequest(capturedMenu.Entry.ScriptKey, "frame", null, originFrame: capturedMenu.Frame, clickedFrame: true)) == null);
                            _host.ExecuteContextMenu(native, capturedMenu);
                        }
                await Task.Delay(100);
                Check("staleChildMenuRejected", (await ReadAsync("!document.documentElement.dataset.manualFrameCount", FindFrame("/compat/isolated-child"))).Value.GetBoolean());
            }
            await ReadAsync("(function(){var f=document.createElement('iframe');f.id='isolated-blank';document.body.appendChild(f);var s=document.createElement('iframe');s.srcdoc='<html><body>srcdoc</body></html>';document.body.appendChild(s);return true;})()");
            await WaitAsync(() => Task.FromResult(FindFrame("blank") != null && FindFrame("srcdoc") != null));
            foreach (var path in new[] { "blank", "srcdoc" })
            {
                await WaitAsync(async () => (await ReadAsync("document.documentElement.getAttribute('data-about-isolated') === 'about:" + path + "|isolated'", FindFrame(path)))?.GetBoolean() == true);
                Check("about:" + path, true);
            }
            var documents = main.Select(frame => frame.DocumentId).ToArray();
            await NavigateAsync("/compat/cleanup");
            await WaitAsync(() => Task.FromResult(documents.All(id => EventCount(DocumentLifecycleKind.ContextReleased, id) == 1)));
            Check("contextRevocation", true);
        }

        private async Task ReplayAsync()
        {
            await NavigateAsync("/compat/replay");
            foreach (var world in new[] { "raw", "isolated" })
            {
                var result = await WaitResultAsync("JSON.parse(document.documentElement.getAttribute('data-replay-" + world + "') || 'null')");
                var events = result.GetProperty("events").EnumerateArray().ToArray();
                Check(world + ":order", events.Length == 2 && events[0].GetProperty("type").GetString() == "DOMContentLoaded" && events[1].GetProperty("type").GetString() == "load");
                Check(world + ":cachedDOMContentLoaded", !events[0].GetProperty("trusted").GetBoolean());
            }
            Check("idleBeforeLoad", (await ReadAsync("document.documentElement.dataset.idleBeforeLoad === 'true'")).Value.GetBoolean());
        }

        private async Task CspAsync()
        {
            await NavigateAsync("/compat/csp");
            var isolated = await WaitResultAsync("JSON.parse(document.documentElement.getAttribute('data-csp-isolated') || 'null')");
            Check("isolatedSource", isolated.GetProperty("world").GetString() == "isolated");
            Check("isolatedDom", isolated.GetProperty("dom").GetBoolean());
            Check("pagePolicyIntact", (await ReadAsync("typeof pageCspBypassed === 'undefined'")).Value.GetBoolean());
            Check("stylePolicyIntact", isolated.GetProperty("color").GetString() != "rgb(11, 22, 33)");
            Console.WriteLine(JsonSerializer.Serialize(new { type = "csp-observation", isolatedColor = isolated.GetProperty("color").GetString(),
                rawSource = (await ReadAsync("document.documentElement.dataset.cspRaw === 'executed'")).Value.GetBoolean() }));
        }

        private async Task PermissionsAsync()
        {
            var before = _permissions.Prompts;
            await NavigateAsync("/compat/permissions");
            var result = await WaitResultAsync("globalThis.__permissions || null");
            foreach (var name in new[] { "coalesced", "once", "redirectDenied", "rememberedDeny", "declarationDenied" }) Check(name, result.GetProperty(name).GetBoolean());
            Check("promptScope", _permissions.Prompts - before == 4);
            Check("noDeniedNetworkEffect", _otherFixture.RequestCount("/compat/permission-denied") == 0);
            Check("allowedNetworkEffect", _fixture.RequestCount("/compat/permission-ok") == 3);
        }

        private void Lifecycle(object sender, DocumentLifecycleEventArgs args)
        {
            lock (_sync)
            {
                _events.Add(args);
                if (args.Frame.ExecutionWorld == "isolated")
                {
                    if (args.Kind == DocumentLifecycleKind.ContextReleased) _isolatedFrames.Remove(args.Frame.DocumentId);
                    else _isolatedFrames[args.Frame.DocumentId] = args.Frame;
                    return;
                }
                if (args.Kind == DocumentLifecycleKind.ContextReleased || args.Kind == DocumentLifecycleKind.NavigationSuperseded)
                {
                    if (_frames.TryGetValue(args.Frame.FrameId, out var current) && current.DocumentId == args.Frame.DocumentId)
                        _frames.Remove(args.Frame.FrameId);
                }
                else _frames[args.Frame.FrameId] = args.Frame;
            }
        }

        private DocumentFrame FindFrame(string path)
        {
            lock (_sync) return _frames.Values.FirstOrDefault(frame => frame.Url.AbsolutePath == path);
        }

        private int EventCount(DocumentLifecycleKind kind, string documentId)
        {
            lock (_sync) return _events.Count(item => item.Kind == kind && item.Frame.DocumentId == documentId);
        }

        private bool HasDocumentEnded(string documentId)
        {
            lock (_sync) return SmokeLifecycleAssertions.HasDocumentEnded(_events, documentId);
        }

        private void Check(string name, bool passed)
        {
            _checks[name] = passed;
            if (!passed) throw new InvalidOperationException("Compatibility assertion failed: " + name);
        }

        private async Task WaitAsync(Func<Task<bool>> predicate)
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(15))
            {
                if (await predicate()) return;
                await Task.Delay(50);
            }
            throw new TimeoutException("Compatibility condition timed out. Last state: " + _lastState);
        }

        private static IFrame GetFrame(IBrowser browser, string id)
        {
            // CefSharp 151 uses string identifiers; 84/121 use Int64 identifiers.
            var method = typeof(IBrowser).GetMethod("GetFrameByIdentifier", new[] { typeof(string) });
            if (method != null) return (IFrame)method.Invoke(browser, new object[] { id });
            method = typeof(IBrowser).GetMethod("GetFrame", new[] { typeof(long) });
            if (method != null) return (IFrame)method.Invoke(browser, new object[] { long.Parse(id, CultureInfo.InvariantCulture) });
            return (IFrame)typeof(IBrowser).GetMethod("GetFrame", new[] { typeof(string) }).Invoke(browser, new object[] { id });
        }

        private async Task<JsonElement?> ReadAsync(string expression, DocumentFrame context = null)
        {
            _context = context ?? FindMainFrame();
            using (var frame = context == null ? _browser.GetBrowser()?.MainFrame : GetFrame(_browser.GetBrowser(), context.FrameId))
            {
                if (frame == null || frame.IsDisposed) { _lastState = "Frame unavailable: " + _context?.FrameId; return null; }
                var response = await frame.EvaluateScriptAsync("JSON.stringify(" + expression + ")", timeout: TimeSpan.FromSeconds(2));
                _lastState = response.Result as string ?? response.Message;
                if (!response.Success || string.IsNullOrEmpty(response.Result as string)) return null;
                using (var document = JsonDocument.Parse((string)response.Result))
                {
                    var root = document.RootElement.Clone();
                    if (root.ValueKind == JsonValueKind.Null) return null;
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error))
                        throw new InvalidOperationException("Userscript failed: " + error.GetString());
                    return root;
                }
            }
        }

        private DocumentFrame FindMainFrame()
        {
            lock (_sync) return _frames.Values.FirstOrDefault(frame => frame.IsMainFrame);
        }

        private async Task NavigateAsync(string path)
        {
            _browser.Load("http://127.0.0.1:" + _fixture.Port + path);
            await WaitAsync(() => Task.FromResult(FindFrame(path)?.IsMainFrame == true));
        }

        private async Task<JsonElement> WaitResultAsync(string expression, DocumentFrame frame = null)
        {
            JsonElement? result = null;
            await WaitAsync(async () =>
            {
                result = await ReadAsync(expression, frame);
                return result.HasValue && result.Value.TryGetProperty("done", out var done) && done.GetBoolean();
            });
            return result.Value;
        }

        private async Task FramesAsync()
        {
            await NavigateAsync("/compat/frames");
            foreach (var path in new[] { "/compat/frames", "/compat/frames/same", "/compat/frames/cross" })
            {
                await WaitAsync(() => Task.FromResult(FindFrame(path) != null));
                var frame = FindFrame(path);
                var result = await WaitResultAsync("globalThis.__compatFrame || null", frame);
                Check(path + ":execution", result.GetProperty("runs").GetInt32() == 1 && result.GetProperty("value").GetString() == frame.Url.AbsoluteUri);
                Check(path + ":bulkStorage", result.GetProperty("bulkStorage").GetBoolean());
                Check(path + ":storageCompatibility", result.GetProperty("storageCompatibility").GetBoolean());
                Check(path + ":noframes", result.GetProperty("noFrames").GetInt32() == (frame.IsMainFrame ? 1 : 0));
            }
            await ReadAsync("(globalThis.__compatPublishUndefined(), true)", FindFrame("/compat/frames/cross"));
            foreach (var path in new[] { "/compat/frames", "/compat/frames/same", "/compat/frames/cross" })
            {
                var frame = FindFrame(path);
                await WaitAsync(async () =>
                {
                    var value = await ReadAsync("globalThis.__compatFrame.sharedEvents.length === 1", frame);
                    return value.HasValue && value.Value.GetBoolean();
                });
                var remote = path != "/compat/frames/cross" ? "true" : "false";
                var result = await ReadAsync("globalThis.__compatFrame.sharedEvents.every(e => e.oldUndefined && e.newUndefined && e.remote === " + remote + ")", frame);
                Check(path + ":undefinedListener", result.Value.GetBoolean());
            }
            var old = FindFrame("/compat/frames/same");
            await ReadAsync("(document.getElementById('same').src = '/compat/frames/same-next', true)");
            await WaitAsync(() => Task.FromResult(FindFrame("/compat/frames/same-next") != null && HasDocumentEnded(old.DocumentId)));
            var next = FindFrame("/compat/frames/same-next");
            var nextResult = await WaitResultAsync("globalThis.__compatFrame || null", next);
            Check("childNavigation", next.DocumentId != old.DocumentId && nextResult.GetProperty("runs").GetInt32() == 1 && nextResult.GetProperty("value").GetString() == next.Url.AbsoluteUri);
            Check("childNavigation:bulkStorage", nextResult.GetProperty("bulkStorage").GetBoolean());
            Check("childNavigation:storageCompatibility", nextResult.GetProperty("storageCompatibility").GetBoolean());
            Check("childNavigation:undefinedSnapshot", nextResult.GetProperty("persistedUndefined").GetBoolean());
            Check("crossOrigin", FindFrame("/compat/frames/cross").Url.Port != _fixture.Port);
        }

        private async Task SpaAsync()
        {
            await NavigateAsync("/compat/spa");
            await WaitResultAsync("globalThis.__compatSpa || null");
            var original = FindFrame("/compat/spa");
            var url = original.Url.AbsoluteUri;
            var actions = new[] { "history.pushState({}, '', '?pushed')", "history.replaceState({}, '', '?replaced')", "location.hash = 'hash'", "history.back()" };
            var urls = new[] { url + "?pushed", url + "?replaced", url + "?replaced#hash", url + "?replaced" };
            var previous = url;
            for (var index = 0; index < actions.Length; index++)
            {
                await ReadAsync("(" + actions[index] + ", true)");
                JsonElement? result = null;
                var count = index + 1;
                await WaitAsync(async () =>
                {
                    result = await ReadAsync("globalThis.__compatSpa || null");
                    return result.HasValue && result.Value.GetProperty("events").GetArrayLength() >= count &&
                        EventCount(DocumentLifecycleKind.UrlChanged, original.DocumentId) >= count;
                });
                var events = result.Value.GetProperty("events");
                var listeners = result.Value.GetProperty("listeners");
                Check("urlStep" + count, events.GetArrayLength() == count && listeners.GetArrayLength() == count &&
                    events[index].GetProperty("url").GetString() == urls[index] && events[index].GetProperty("oldURL").GetString() == previous &&
                    events[index].GetRawText() == listeners[index].GetRawText());
                previous = urls[index];
            }
            var state = await ReadAsync("({ runs: globalThis.__compatSpa.runs, denied: globalThis.__compatSpaDenied })");
            Check("rawUrlOptions", (await ReadAsync("__compatSpa.once === 1 && __compatSpa.aborted === 0 && __compatSpa.tail === 4")).Value.GetBoolean());
            var denied = state.Value.GetProperty("denied");
            Check("grantBoundary", denied.GetProperty("hidden").GetBoolean() && denied.GetProperty("events").GetInt32() == 0);
            Check("sameDocument", FindFrame("/compat/spa").DocumentId == original.DocumentId && state.Value.GetProperty("runs").GetInt32() == 1 &&
                EventCount(DocumentLifecycleKind.UrlChanged, original.DocumentId) == 4);
        }

        private async Task ScriptCaseAsync(string name)
        {
            await NavigateAsync("/compat/" + name);
            var result = await WaitResultAsync("globalThis.__compatCase || null");
            var required = name == "download"
                ? new[] { "promise", "handle", "completed", "canceled", "timeout", "failure", "singleTerminal", "loginRedirect", "anonymous", "empty", "earlyCancel", "diskFailure" }
                : name == "cookie"
                    ? new[] { "setList", "legacy", "context", "noListeners", "preciseDelete", "deleted", "setUndefined", "unsupportedFirstParty", "explicitCookieRejected", "actualJarRetained" }
                    : new[] { "staticFirstScript", "voidReturn", "cancel", "replacement", "redirect", "capture", "restored", "noCallbackAfterRevoke" };
            foreach (var check in required) Check(check, result.GetProperty("checks").GetProperty(check).GetBoolean());
        }

        private async Task DownloadAsync()
        {
            await ScriptCaseAsync("download");
            await _downloads.DrainAsync();
            foreach (var name in new[] { "modern", "legacy", "authenticated" })
                Check(name + "Bytes", File.ReadAllBytes(Path.Combine(_downloads.DirectoryPath, name + ".bin")).SequenceEqual(LocalHttpFixture.DownloadBytes));
            Check("emptyFile", new FileInfo(Path.Combine(_downloads.DirectoryPath, "empty.bin")).Length == 0);
            Check("partialCleanup", !Directory.GetFiles(_downloads.DirectoryPath, "*.part").Any() &&
                new[] { "cancel", "timeout", "failure", "anonymous", "early", "disk-failure" }.All(name => !File.Exists(Path.Combine(_downloads.DirectoryPath, name + ".bin"))));
            Check("realRequests", _fixture.RequestCount("/compat/files/bytes") == 2 && _fixture.RequestCount("/compat/files/slow") == 1 &&
                _fixture.RequestCount("/compat/files/stalled") == 1 && _fixture.RequestCount("/compat/files/missing") == 1);
            Check("credentialAndEarlyFailureRequests", _fixture.RequestCount("/compat/files/auth") == 2 &&
                _fixture.RequestCount("/compat/files/auth-redirect") == 1 && _fixture.RequestCount("/compat/files/early-cancel") == 0 &&
                _fixture.RequestCount("/compat/files/disk-failure") == 0);
            await ReadAsync("(globalThis.__startNavigationDownload(), true)");
            await WaitAsync(() => Task.FromResult(_fixture.RequestCount("/compat/files/slow") == 2));
            await NavigateAsync("/compat/cleanup");
            await _downloads.DrainAsync();
            Check("navigationTaskContinues", File.ReadAllBytes(Path.Combine(_downloads.DirectoryPath, "navigation.bin")).SequenceEqual(LocalHttpFixture.DownloadBytes));
            using (var hash = System.Security.Cryptography.SHA256.Create())
                Console.WriteLine(JsonSerializer.Serialize(new { type = "download-file-evidence", sha256 = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(Path.Combine(_downloads.DirectoryPath, "authenticated.bin")))).Replace("-", "").ToLowerInvariant(), bytes = LocalHttpFixture.DownloadBytes.Length }));
        }

        private async Task CookieAsync()
        {
            var echoRequests = _fixture.RequestCount("/compat/cookie-echo");
            await ScriptCaseAsync("cookie");
            // The existing authenticated/anonymous checks and the new jar probe send three requests.
            Check("explicitCookiesHaveNoNetworkEffect", _fixture.RequestCount("/compat/cookie-echo") == echoRequests + 3);
            Console.WriteLine(JsonSerializer.Serialize(new { type = "explicit-cookie-probe", effects = await ReadAsync("globalThis.__compatCase.explicitCookies") }));
            using (var tls = new LocalHttpFixture(true))
            {
                var url = tls.Start();
                await ReadAsync("(globalThis.__httpsCookieProbe(" + JsonSerializer.Serialize(url.GetLeftPart(UriPartial.Authority)) + "), true)");
                await WaitAsync(async () => (await ReadAsync("globalThis.__compatCase.httpsDone || false"))?.GetBoolean() == true);
                var result = (await ReadAsync("globalThis.__compatCase")).Value;
                if (result.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.GetString());
                foreach (var check in new[] { "httpsState", "httpsSend", "expirySameSite", "httpsDeleted", "engineRejected", "domainCookie" })
                    Check(check, result.GetProperty("checks").GetProperty(check).GetBoolean());
                Check("realTlsRequest", tls.RequestCount("/cookie") == 1);
                Console.WriteLine("{\"type\":\"cookie-probe\",\"phase\":\"tls-verified\"}");
            }
            Console.WriteLine("{\"type\":\"cookie-probe\",\"phase\":\"tls-disposed\"}");
            await OnUiAsync(ContextIsolationAsync).ConfigureAwait(false);
        }

        private async Task ContextIsolationAsync()
        {
            using (var primary = new CefSharpCookieService(_host))
            using (var context = CookiePhase("context", () => new RequestContext(new RequestContextSettings())))
            using (var otherHost = new CefSharpUserScriptHostBuilder(new InMemoryUserScriptRepository()).Build())
            using (var other = CookiePhase("browser", () => new ChromiumWebBrowser("about:blank", context)))
            {
                Console.WriteLine("{\"type\":\"cookie-probe\",\"phase\":\"contexts-created\"}");
                var url = new Uri("http://127.0.0.1:" + _fixture.Port + "/cookie");
                await primary.SetAsync(new UserScriptCookieMutation(url, "context-proof", "primary"), CancellationToken.None);
                Console.WriteLine("{\"type\":\"cookie-probe\",\"phase\":\"primary-written\"}");
                otherHost.Attach(other);
                other.Size = new System.Drawing.Size(2, 2);
                _browser.FindForm().Controls.Add(other);
                await otherHost.PrepareAsync();
                Console.WriteLine("{\"type\":\"cookie-probe\",\"phase\":\"secondary-prepared\"}");
                using (var secondary = new CefSharpCookieService(otherHost))
                using (var http = new CefSharpHttpRequestService(context))
                {
                    Check("contextInitiallyIsolated", (await secondary.ListAsync(new UserScriptCookieQuery(url, "context-proof"), CancellationToken.None)).Count == 0);
                    await secondary.SetAsync(new UserScriptCookieMutation(url, "context-proof", "secondary"), CancellationToken.None);
                    Check("contextWritesIsolated", (await primary.ListAsync(new UserScriptCookieQuery(url, "context-proof"), CancellationToken.None)).Single().Value == "primary");
                    var observer = new CookieHttpObserver();
                    await http.SendAsync(new UserScriptHttpRequest("GET", url, new Dictionary<string, string>(), null, null, null, (_, __) => Task.FromResult(true)), observer, CancellationToken.None).Completion;
                    Check("contextHttpEffect", observer.Text.Contains("context-proof=secondary") && !observer.Text.Contains("context-proof=primary"));
                    await secondary.DeleteAsync(new UserScriptCookieMutation(url, "context-proof"), CancellationToken.None);
                }
                await primary.DeleteAsync(new UserScriptCookieMutation(url, "context-proof"), CancellationToken.None);
                await otherHost.DetachAsync(CancellationToken.None);
                _browser.FindForm().Controls.Remove(other);
            }
        }

        private Task OnUiAsync(Func<Task> action)
        {
            var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            _browser.BeginInvoke(new Action(async () =>
            {
                try { await action(); completion.TrySetResult(null); }
                catch (Exception error) { completion.TrySetException(error); }
            }));
            return completion.Task;
        }

        private sealed class CookieHttpObserver : IUserScriptHttpObserver
        {
            private readonly MemoryStream _bytes = new MemoryStream();
            public string Text => System.Text.Encoding.UTF8.GetString(_bytes.ToArray());
            public void OnResponseStarted(UserScriptHttpResponse response) { }
            public void OnResponseData(byte[] bytes, int offset, int count) => _bytes.Write(bytes, offset, count);
            public void OnDownloadProgress(long loaded, long? total) { }
            public void OnUploadProgress(long loaded, long? total) { }
        }

        private static T CookiePhase<T>(string phase, Func<T> create)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { type = "cookie-probe", phase = "creating-" + phase }));
            var value = create();
            Console.WriteLine(JsonSerializer.Serialize(new { type = "cookie-probe", phase = "created-" + phase }));
            return value;
        }

        private async Task WebRequestAsync()
        {
            await ScriptCaseAsync("webrequest");
            Check("serverEffects", _fixture.RequestCount("/compat/net/blocked") == 1 && _fixture.RequestCount("/compat/net/original") == 1 &&
                _fixture.RequestCount("/compat/net/target") == 1);
            Check("staticServerEffects", _fixture.RequestCount("/compat/net/static-blocked.js") == 0 &&
                _fixture.RequestCount("/compat/net/static-old.js") == 0 && _fixture.RequestCount("/compat/net/static-target.js") == 1);
            Console.WriteLine(JsonSerializer.Serialize(new { type = "webrequest-websocket-probe",
                requests = _fixture.RequestCount("/compat/net/websocket"),
                status = _fixture.RequestCount("/compat/net/websocket") == 0 ? "passed" : "expected-engine-limitation" }));
            var old = FindFrame("/compat/webrequest");
            await NavigateAsync("/compat/cleanup");
            await WaitAsync(() => Task.FromResult(HasDocumentEnded(old.DocumentId)));
            await ReadAsync("(globalThis.__cleanup = null, fetch('/compat/net/cleanup', { cache: 'no-store' }).then(function (response) { return response.text(); }).then(function (text) { globalThis.__cleanup = text; }), true)");
            await WaitAsync(async () => (await ReadAsync("globalThis.__cleanup || null"))?.GetString() == "cleanup");
            Check("navigationCleanup", _fixture.RequestCount("/compat/net/cleanup") == 1);
            var installation = (await _repository.GetSnapshotAsync(CancellationToken.None)).Single(script => script.Definition.Metadata.Name == "Compatibility request effects");
            await _repository.SetEnabledAsync(installation.ScriptKey, false, CancellationToken.None);
            await NavigateAsync("/compat/webrequest");
            await WaitAsync(async () => (await ReadAsync("document.readyState === 'complete'"))?.GetBoolean() == true);
            Check("disabledRevokesStatic", _fixture.RequestCount("/compat/net/static-blocked.js") == 1 &&
                _fixture.RequestCount("/compat/net/static-old.js") == 1 && _fixture.RequestCount("/compat/net/static-target.js") == 1);
        }

        private async Task PartitionsAsync()
        {
            await NavigateAsync("/compat/partitions");
            await WaitAsync(async () => (await ReadAsync("typeof globalThis.__runPartitionProbe === 'function'"))?.GetBoolean() == true);
            var session = await _host.GetCdpSessionAsync(CancellationToken.None);
            var version = (await session.ExecuteAsync("Browser.getVersion")).GetProperty("product").GetString();
            var major = int.Parse(version.Substring(version.LastIndexOf('/') + 1).Split('.')[0], CultureInfo.InvariantCulture);
            var protocol = new CookieProtocolProfile(major);
            using (var tls = new LocalHttpFixture(true))
            {
                var url = tls.Start();
                await ReadAsync("(globalThis.__runPartitionProbe(" + JsonSerializer.Serialize(url.GetLeftPart(UriPartial.Authority)) + "," +
                    (protocol.SupportsPartitions ? "true" : "false") + "," + (protocol.SupportsAncestors ? "true" : "false") + "),true)");
                var result = await WaitResultAsync("globalThis.__partitionCase");
                if (result.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.GetString());
                foreach (var check in result.GetProperty("checks").EnumerateObject()) Check(check.Name, check.Value.GetBoolean());
                var checks = result.GetProperty("checks");
                if (protocol.SupportsPartitions)
                {
                    Check("partitionCrudExecuted", checks.GetProperty("partitionA").GetBoolean() && checks.GetProperty("partitionB").GetBoolean() &&
                        checks.GetProperty("partitionDeleteIsExact").GetBoolean() && checks.GetProperty("cleaned").GetBoolean());
                    if (protocol.SupportsAncestors) Check("ancestorCrudExecuted", checks.GetProperty("ancestorIdentities").GetBoolean());
                }
                Check("unsupportedRequestHasNoNetworkEffect", tls.RequestCount("/partition-receive") == 0);
                if (protocol.SupportsPartitions)
                {
                    using (var cookies = new CefSharpCookieService(_host))
                    {
                        var target = new Uri(url, "/partition-receive");
                        await cookies.SetAsync(new UserScriptCookieMutation(target, "native-partition-proof", "plain", path:"/", secure:true, sameSite:"no_restriction"), CancellationToken.None);
                        await cookies.SetAsync(new UserScriptCookieMutation(target, "native-partition-proof", "a", path:"/", secure:true, sameSite:"no_restriction",
                            partitionKey:new UserScriptCookiePartitionKey("https://top-a.test")), CancellationToken.None);
                        var request = await PartitionRequestProbe.SendAsync(_browser.RequestContext, target, "https://top-a.test");
                        var received = await cookies.ListAsync(new UserScriptCookieQuery(url, "partition-received", partitionKey:new UserScriptCookiePartitionKey()), CancellationToken.None);
                        Console.WriteLine(JsonSerializer.Serialize(new { type = "partition-request-probe", engine = version,
                            requestedTopLevelSite = "https://top-a.test", selectorAvailable = request.SelectorAvailable, sent = request.Text,
                            received = received.Select(cookie => new { cookie.Name, topLevelSite = cookie.PartitionKey?.TopLevelSite, ancestor = cookie.PartitionKey?.HasCrossSiteAncestor }).ToArray(),
                            status = request.SelectorAvailable && request.Text.Contains("native-partition-proof=a") && received.Any(cookie => cookie.PartitionKey?.TopLevelSite == "https://top-a.test") ? "passed" : "expected-engine-limitation" }));
                    }
                }
            }
        }

        private async Task AudioAsync()
        {
            await NavigateAsync("/compat/audio");
            var result = await WaitResultAsync("globalThis.__audioCase");
            if (result.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.GetString());
            foreach (var check in result.GetProperty("checks").EnumerateObject()) Check(check.Name, check.Value.GetBoolean());
            await Cef.UIThreadTaskFactory.StartNew(() => _browser.GetBrowser().GetHost().SetAudioMuted(true));
            await WaitAsync(async () => (await ReadAsync("globalThis.__audioCase.external.some(event => event.muted === 'unknown')"))?.GetBoolean() == true);
            Check("otherHostMute", true);
            var before = await Cef.UIThreadTaskFactory.StartNew(() => _browser.GetBrowser().GetHost().IsAudioMuted);
            await NavigateAsync("/compat/audio-next");
            result = await WaitResultAsync("globalThis.__audioCase");
            var after = await Cef.UIThreadTaskFactory.StartNew(() => _browser.GetBrowser().GetHost().IsAudioMuted);
            Check("navigationMatchesNative", result.GetProperty("checks").GetProperty("navigation").GetBoolean() &&
                result.GetProperty("nativeNavigationMute").GetBoolean() == after);
            Console.WriteLine(JsonSerializer.Serialize(new { type = "audio-navigation-probe", nativeBefore = before,
                nativeAfter = after, status = before && after ? "passed" : "expected-engine-limitation" }));
            await Cef.UIThreadTaskFactory.StartNew(() => _browser.GetBrowser().GetHost().SetAudioMuted(false));
        }

        public void Dispose() => _host.DocumentLifecycle -= Lifecycle;
    }
}
