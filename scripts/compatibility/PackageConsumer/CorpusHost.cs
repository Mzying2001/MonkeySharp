using CefSharp;
using CefSharp.WinForms;
using Mzying2001.MonkeySharp.CefSharp;
using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Permissions;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MonkeySharp.Compatibility
{
    // Shared by the source SmokeHost and the NuGet-only consumer. Only public runtime APIs are used.
    internal static class CorpusHost
    {
        public static int Run(string inputPath, string outputPath)
        {
            var input = JsonDocument.Parse(File.ReadAllText(inputPath));
            var observations = new Dictionary<string, object>();
            var checks = new Dictionary<string, bool>();
            var diagnostics = new List<string>();
            var events = new List<object>();
            object failedAction = null;
            var services = new List<Session>();
            var output = input.RootElement.GetProperty("downloads").GetString();
            Directory.CreateDirectory(output);
            string error = null;
            var initialized = false;
            try
            {
                CefSharpSettings.WcfEnabled = false;
                CefSharpSettings.ConcurrentTaskExecution = true;
                using (var settings = new CefSettings { RootCachePath = Path.Combine(Path.GetDirectoryName(outputPath), "profile-" + Guid.NewGuid().ToString("N")), Locale = "en-US" })
                    initialized = Cef.Initialize(settings);
                if (!initialized) throw new InvalidOperationException("CEF initialization failed.");
                using (var store = new InMemoryUserScriptValueStore())
                using (var form = new Form { Width = 1100, Height = 800, Text = "MonkeySharp corpus" })
                {
                    var repository = new InMemoryUserScriptRepository();
                    repository.InstallAsync(File.ReadAllText(input.RootElement.GetProperty("source").GetString(), Encoding.UTF8), "corpus://unmodified-upstream", true, CancellationToken.None).GetAwaiter().GetResult();
                    var active = 0;
                    form.Shown += async (_, __) =>
                    {
                        try
                        {
                            foreach (var action in input.RootElement.GetProperty("actions").EnumerateArray())
                            {
                                failedAction = action.Clone();
                                var kind = action.GetProperty("kind").GetString();
                                if (kind == "open")
                                {
                                    var session = new Session(form, repository, store, output,
                                        input.RootElement.TryGetProperty("cancelDownloads", out var cancel) && cancel.GetBoolean(),
                                        input.RootElement.TryGetProperty("denyNetwork", out var deny) && deny.GetBoolean());
                                    services.Add(session); active = services.Count - 1;
                                    session.Host.Diagnostic += (s, d) => { lock (diagnostics) diagnostics.Add(d.Code + ": " + d.Message); };
                                    session.Browser.ConsoleMessage += (s, e) => { if (e.Message.Contains("replaceAll")) lock (diagnostics) diagnostics.Add("console: " + e.Message); };
                                    await Until(() => Task.FromResult(session.Browser.IsBrowserInitialized));
                                    var cookies = session.Browser.GetBrowser().GetHost().RequestContext.GetCookieManager(null);
                                    foreach (var host in new[] { "127.0.0.1", "localhost" })
                                        if (!await cookies.SetCookieAsync("http://" + host, new Cookie { Name = "corpus-auth", Value = "fixture-only", Path = "/" }))
                                            throw new InvalidOperationException("Cannot seed test cookie jar.");
                                    await session.Host.NavigateAsync(action.GetProperty("url").GetString());
                                    var pageUrl = action.GetProperty("url").GetString();
                                    await Until(async () => session.Browser.CanExecuteJavascriptInMainFrame && (await Evaluate(session.Browser, "location.href === " + JsonSerializer.Serialize(pageUrl) + " && document.readyState === 'complete'")).ValueKind == JsonValueKind.True);
                                    observations["engineFeatures"] = await Evaluate(session.Browser, "({replaceAll:typeof String.prototype.replaceAll,compressionStream:typeof CompressionStream,saveFilePicker:typeof window.showSaveFilePicker})");
                                }
                                else if (kind == "switch") active = action.GetProperty("index").GetInt32();
                                else if (kind == "navigate")
                                {
                                    var url = action.GetProperty("url").GetString();
                                    await services[active].Host.NavigateAsync(url);
                                    await Until(async () => (await Evaluate(services[active].Browser, "location.href === " + JsonSerializer.Serialize(url) + " && document.readyState === 'complete'")).ValueKind == JsonValueKind.True);
                                }
                                else if (kind == "wait") await Task.Delay(action.GetProperty("milliseconds").GetInt32());
                                else if (kind == "menu")
                                {
                                    var caption = action.GetProperty("caption").GetString();
                                    await Until(() => Task.FromResult(services[active].Menus.Has(caption)));
                                    services[active].Menus.Click(caption);
                                }
                                else if (kind == "assertMenu")
                                {
                                    var caption = action.GetProperty("caption").GetString();
                                    var expected = action.GetProperty("present").GetBoolean();
                                    await Until(() => Task.FromResult(services[active].Menus.Has(caption) == expected));
                                    checks[action.GetProperty("name").GetString()] = true;
                                }
                                else if (kind == "files")
                                {
                                    var count = action.GetProperty("minimum").GetInt32();
                                    await Until(() => Task.FromResult(Directory.GetFiles(output).Count(file => !file.EndsWith(".part") && !file.EndsWith(".crdownload")) >= count && !services[active].BrowserDownloads.HasPending));
                                    if (action.TryGetProperty("maximum", out var maximum) && Directory.GetFiles(output).Length > maximum.GetInt32())
                                        throw new InvalidOperationException("Canceled corpus download left a file.");
                                    checks[action.GetProperty("name").GetString()] = true;
                                }
                                else if (kind == "assert")
                                {
                                    var expression = action.GetProperty("expression").GetString();
                                    await Until(async () => (await Evaluate(services[active].Browser, "Boolean(" + expression + ")")).ValueKind == JsonValueKind.True);
                                    checks[action.GetProperty("name").GetString()] = true;
                                }
                                else if (kind == "evaluate")
                                {
                                    var value = await Evaluate(services[active].Browser, action.GetProperty("expression").GetString());
                                    if (action.TryGetProperty("name", out var name)) observations[name.GetString()] = value.Clone();
                                }
                                else throw new InvalidOperationException("Unknown corpus action: " + kind);
                                events.Add(new { kind, name = action.TryGetProperty("name", out var actionName) ? actionName.GetString() : null });
                                failedAction = null;
                            }
                        }
                        catch (Exception failure) { error = failure.ToString(); }
                        finally
                        {
                            foreach (var session in services)
                            {
                                try { await session.Downloads.DrainAsync(); await Until(() => Task.FromResult(!session.BrowserDownloads.HasPending)); await session.Host.DetachAsync(CancellationToken.None); }
                                catch (Exception failure) { error = error ?? failure.ToString(); }
                                observations["downloadTransitions"] = services.SelectMany(item => item.DownloadEvents).ToArray();
                                session.Dispose();
                            }
                            form.Close();
                        }
                    };
                    Application.Run(form);
                }
            }
            catch (Exception failure) { error = failure.ToString(); }
            finally
            {
                if (initialized) Cef.Shutdown();
                File.WriteAllText(outputPath, JsonSerializer.Serialize(new
                {
                    status = error == null ? "passed" : "failed", error, checks, observations, diagnostics, events, failedAction,
                    environment = new { manager = "MonkeySharp", managerVersion = typeof(InMemoryUserScriptRepository).Assembly.GetName().Version.ToString(3), chromiumVersion = Cef.ChromiumVersion, manifestVersion = (int?)null,
                        cefSharpVersion = typeof(Cef).Assembly.GetName().Version.ToString(), platform = Environment.Is64BitProcess ? "x64" : "x86", menuInput = "WinForms ToolStripMenuItem.PerformClick; no trusted DOM activation claimed" }
                }));
                input.Dispose();
            }
            return error == null ? 0 : 1;
        }

        private static async Task<JsonElement> Evaluate(ChromiumWebBrowser browser, string expression)
        {
            var result = await browser.EvaluateScriptAsync("JSON.stringify((" + expression + "))");
            if (!result.Success) throw new InvalidOperationException(result.Message);
            using (var json = JsonDocument.Parse(result.Result as string ?? "null")) return json.RootElement.Clone();
        }
        private static async Task Until(Func<Task<bool>> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(12);
            Exception last = null;
            do
            {
                try { if (await condition()) return; } catch (Exception error) { last = error; }
                await Task.Delay(50);
            } while (DateTime.UtcNow < deadline);
            throw new TimeoutException("Corpus action did not meet its functional assertion.", last);
        }

        private sealed class Session : IDisposable
        {
            public ChromiumWebBrowser Browser;
            public CefSharpUserScriptHost Host;
            public Menus Menus;
            public FileDownloadService Downloads;
            public BrowserDownloads BrowserDownloads;
            public readonly List<object> DownloadEvents = new List<object>();
            public Session(Form form, InMemoryUserScriptRepository repository, InMemoryUserScriptValueStore store, string output, bool cancel, bool deny)
            {
                Menus = new Menus(form);
                Host = new CefSharpUserScriptHostBuilder(repository).UseValueStore(store).UseMenuService(Menus)
                    .UsePermissionPolicy(new CorpusPolicy(deny))
                    .UseDownloadServiceFactory(http =>
                    {
                        Downloads = new FileDownloadService(http, (request, token) =>
                            Task.FromResult(cancel ? null : new DownloadDestination(Path.Combine(output, Path.GetFileName(request.Name ?? "download.bin")), false)));
                        return new RecordedDownloads(Downloads, DownloadEvents);
                    })
                    .Configure(new CefSharpHostOptions { TrustedPageWorld = true }).Build();
                Browser = new ChromiumWebBrowser("about:blank") { Dock = DockStyle.Fill };
                Browser.DownloadHandler = BrowserDownloads = new BrowserDownloads(output, cancel);
                form.Controls.Add(Browser); Browser.BringToFront(); Host.Attach(Browser);
            }
            public void Dispose() { Host.Dispose(); Browser.Dispose(); Menus.Dispose(); }
        }

        private sealed class Menus : IMenuService, IDisposable
        {
            private readonly Form _form;
            private readonly MenuStrip _strip = new MenuStrip();
            private readonly Dictionary<ToolStripMenuItem, string> _items = new Dictionary<ToolStripMenuItem, string>();
            public Menus(Form form) { _form = form; form.Controls.Add(_strip); _strip.BringToFront(); }
            private void Ui(Action action) { if (_form.InvokeRequired) _form.Invoke(action); else action(); }
            public bool Has(string caption) => _items.Values.Any(value => value.EndsWith(caption, StringComparison.OrdinalIgnoreCase));
            public void Click(string caption) => _items.First(item => item.Value.IndexOf(caption, StringComparison.OrdinalIgnoreCase) >= 0).Key.PerformClick();
            public Task<IMenuRegistration> RegisterAsync(MenuCommandRequest request, Action invoked, CancellationToken cancellationToken)
            {
                ToolStripMenuItem item = null;
                Ui(() => { item = new ToolStripMenuItem(request.Name); item.Click += (_, __) => invoked(); _items.Add(item, request.Name); _strip.Items.Add(item); });
                return Task.FromResult<IMenuRegistration>(new Registration(() => Ui(() => { _items.Remove(item); _strip.Items.Remove(item); item.Dispose(); })));
            }
            public void Dispose() { foreach (var item in _items.Keys.ToArray()) item.Dispose(); _items.Clear(); _strip.Dispose(); }
        }
        private sealed class Registration : IMenuRegistration
        {
            private Action _dispose;
            public Registration(Action dispose) { _dispose = dispose; }
            public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }

        private sealed class CorpusPolicy : IUserScriptPermissionPolicy
        {
            private readonly bool _deny;
            public CorpusPolicy(bool deny) { _deny = deny; }
            public Task<PermissionDecision> AuthorizeAsync(ApiAuthorizationRequest request, CancellationToken token)
                => Task.FromResult(_deny && request.Target != null && request.Target.Contains("/protected/") ? PermissionDecision.Deny : PermissionDecision.Allow);
        }

        private sealed class RecordedDownloads : IDownloadService, IDisposable
        {
            private readonly FileDownloadService _downloads;
            private readonly List<object> _events;
            public RecordedDownloads(FileDownloadService downloads, List<object> events) { _downloads = downloads; _events = events; }
            public string DownloadMode => _downloads.DownloadMode;
            public void Dispose() => _downloads.Dispose();
            public async Task<IDownloadOperation> CreateAsync(DownloadRequest request, CancellationToken token)
            {
                var operation = await _downloads.CreateAsync(request, token);
                Action<string> record = state => { lock (_events) _events.Add(new { url = request.Url.AbsoluteUri, state }); };
                record("created");
                operation.Completed += (_, __) => record("completed");
                operation.Aborted += (_, __) => record("aborted");
                operation.Failed += (_, __) => record("failed");
                operation.TimedOut += (_, __) => record("timeout");
                return operation;
            }
        }

        internal sealed class BrowserDownloads : IDownloadHandler
        {
            private readonly string _directory;
            private readonly bool _cancel;
            private readonly HashSet<string> _pending = new HashSet<string>();
            public bool HasPending { get { lock (_pending) return _pending.Count != 0; } }
            public BrowserDownloads(string directory, bool cancel) { _directory = directory; _cancel = cancel; }
            public bool CanDownload(IWebBrowser control, IBrowser browser, string url, string method) => !_cancel;
#if CEF_CORPUS_NEW_DOWNLOAD_RETURN
            public bool OnBeforeDownload(IWebBrowser control, IBrowser browser, DownloadItem item, IBeforeDownloadCallback callback)
#else
            public void OnBeforeDownload(IWebBrowser control, IBrowser browser, DownloadItem item, IBeforeDownloadCallback callback)
#endif
            {
                if (!_cancel) lock (_pending) _pending.Add(item.Id.ToString());
                using (callback) if (!_cancel) callback.Continue(Path.Combine(_directory, Path.GetFileName(item.SuggestedFileName)), false);
#if CEF_CORPUS_NEW_DOWNLOAD_RETURN
                return true;
#endif
            }
            public void OnDownloadUpdated(IWebBrowser control, IBrowser browser, DownloadItem item, IDownloadItemCallback callback)
            {
                if (item.IsComplete || item.IsCancelled) lock (_pending) _pending.Remove(item.Id.ToString());
                using (callback) if (_cancel && item.IsInProgress) callback.Cancel();
            }
        }
    }
}
