using CefSharp;
using CefSharp.WinForms;
using Mzying2001.MonkeySharp.Core.Repository;
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
        private readonly object _sync = new object();
        private readonly Dictionary<string, DocumentFrame> _frames = new Dictionary<string, DocumentFrame>();
        private readonly List<DocumentLifecycleEventArgs> _events = new List<DocumentLifecycleEventArgs>();
        private Dictionary<string, bool> _checks;
        private string _lastState;
        private DocumentFrame _context;

        public CompatibilitySuite(CefSharpUserScriptHost host, ChromiumWebBrowser browser,
            LocalHttpFixture fixture, SmokeHttpDownloadService downloads)
        {
            _host = host; _browser = browser; _fixture = fixture; _downloads = downloads;
            host.DocumentLifecycle += Lifecycle;
        }

        public static async Task InstallAsync(InMemoryUserScriptRepository repository, int port, int otherPort)
        {
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
            results["frames"] = await RunCaseAsync("frames", FramesAsync);
            results["spaUrl"] = await RunCaseAsync("spaUrl", SpaAsync);
            results["download"] = await RunCaseAsync("download", DownloadAsync);
            results["cookie"] = await RunCaseAsync("cookie", () => ScriptCaseAsync("cookie"));
            results["webRequest"] = await RunCaseAsync("webRequest", WebRequestAsync);
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

        private void Lifecycle(object sender, DocumentLifecycleEventArgs args)
        {
            lock (_sync)
            {
                _events.Add(args);
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
                Check(path + ":noframes", result.GetProperty("noFrames").GetInt32() == (frame.IsMainFrame ? 1 : 0));
            }
            var old = FindFrame("/compat/frames/same");
            await ReadAsync("(document.getElementById('same').src = '/compat/frames/same-next', true)");
            await WaitAsync(() => Task.FromResult(FindFrame("/compat/frames/same-next") != null && EventCount(DocumentLifecycleKind.ContextReleased, old.DocumentId) > 0));
            var next = FindFrame("/compat/frames/same-next");
            var nextResult = await WaitResultAsync("globalThis.__compatFrame || null", next);
            Check("childNavigation", next.DocumentId != old.DocumentId && nextResult.GetProperty("runs").GetInt32() == 1 && nextResult.GetProperty("value").GetString() == next.Url.AbsoluteUri);
            Check("childNavigation:bulkStorage", nextResult.GetProperty("bulkStorage").GetBoolean());
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
                ? new[] { "promise", "handle", "completed", "canceled", "timeout", "failure", "singleTerminal" }
                : name == "cookie"
                    ? new[] { "setList", "legacy", "context", "listener", "removed", "deleted" }
                    : new[] { "cancel", "redirect", "remove", "restored", "noCallbackAfterRemove" };
            foreach (var check in required) Check(check, result.GetProperty("checks").GetProperty(check).GetBoolean());
        }

        private async Task DownloadAsync()
        {
            await ScriptCaseAsync("download");
            await _downloads.DrainAsync();
            foreach (var name in new[] { "modern", "legacy" })
                Check(name + "Bytes", File.ReadAllBytes(Path.Combine(_downloads.DirectoryPath, name + ".bin")).SequenceEqual(LocalHttpFixture.DownloadBytes));
            Check("partialCleanup", !Directory.GetFiles(_downloads.DirectoryPath, "*.part").Any() &&
                new[] { "cancel", "timeout", "failure" }.All(name => !File.Exists(Path.Combine(_downloads.DirectoryPath, name + ".bin"))));
            Check("realRequests", _fixture.RequestCount("/compat/files/bytes") == 2 && _fixture.RequestCount("/compat/files/slow") == 1 &&
                _fixture.RequestCount("/compat/files/stalled") == 1 && _fixture.RequestCount("/compat/files/missing") == 1);
        }

        private async Task WebRequestAsync()
        {
            await ScriptCaseAsync("webrequest");
            Check("serverEffects", _fixture.RequestCount("/compat/net/blocked") == 1 && _fixture.RequestCount("/compat/net/original") == 0 &&
                _fixture.RequestCount("/compat/net/target") == 1);
            var old = FindFrame("/compat/webrequest");
            await NavigateAsync("/compat/cleanup");
            await WaitAsync(() => Task.FromResult(EventCount(DocumentLifecycleKind.ContextReleased, old.DocumentId) > 0));
            await ReadAsync("(globalThis.__cleanup = null, fetch('/compat/net/cleanup', { cache: 'no-store' }).then(function (response) { return response.text(); }).then(function (text) { globalThis.__cleanup = text; }), true)");
            await WaitAsync(async () => (await ReadAsync("globalThis.__cleanup || null"))?.GetString() == "cleanup");
            Check("navigationCleanup", _fixture.RequestCount("/compat/net/cleanup") == 1);
        }

        public void Dispose() => _host.DocumentLifecycle -= Lifecycle;
    }
}
