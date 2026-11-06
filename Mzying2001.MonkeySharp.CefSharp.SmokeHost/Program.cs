using CefSharp;
using CefSharp.WinForms;
using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.CefSharp;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Mzying2001.MonkeySharp.CefSharp.SmokeHost
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            CefSharpSettings.WcfEnabled = false;
            CefSharpSettings.ConcurrentTaskExecution = true;
            var cachePath = Path.Combine(Path.GetTempPath(), "MonkeySharpSmoke", Guid.NewGuid().ToString("N"));
            var settings = new CefSettings
            {
                RootCachePath = cachePath
            };
            // Test-only self-signed loopback TLS; never applied by the production adapter.
            settings.CefCommandLineArgs.Add("ignore-certificate-errors", "1");
            settings.CefCommandLineArgs.Add("host-resolver-rules", "MAP cookie.monkeysharp.test 127.0.0.1");
            if (!Cef.Initialize(settings))
                throw new InvalidOperationException("CefSharp initialization failed.");

            try
            {
                if (args.Contains("--cdp-probe")) CdpProbe.RunAsync().GetAwaiter().GetResult();
                else RunAsync().GetAwaiter().GetResult();
            }
            finally
            {
                Cef.Shutdown();
            }
        }

        private static async Task RunAsync()
        {
            using (var fixture = new LocalHttpFixture())
            using (var otherFixture = new LocalHttpFixture())
            using (var values = new PreparedValueStore())
            {
                var startUri = fixture.Start();
                otherFixture.Start();
                fixture.OtherPort = otherFixture.Port;
                var preparedContent = new PreparedContent();
                var repository = new InMemoryUserScriptRepository(preparer: new UserScriptPreparer(preparedContent, preparedContent));
                await repository.InstallAsync(
                    LegacyFixture.Replace("__PORT__", fixture.Port.ToString()),
                    "smoke://fixture",
                    true,
                    CancellationToken.None);
                await CompatibilitySuite.InstallAsync(repository, fixture.Port, otherFixture.Port);
                preparedContent.Offline = true;
                foreach (var script in await repository.GetSnapshotAsync(CancellationToken.None))
                    if (script.Definition.Metadata.Name.StartsWith("Replay ", StringComparison.Ordinal)) values.SnapshotDelays[script.ScriptKey.ToString()] = TimeSpan.FromMilliseconds(500);
                var preparedScript = (await repository.GetSnapshotAsync(CancellationToken.None)).Single(script => script.Definition.Metadata.Name == "Prepared content");
                await values.SetAsync(preparedScript.ScriptKey.ToString(), "large", Mzying2001.MonkeySharp.Core.Storage.UserScriptValue.FromEncoded("s" + new string('v', 600000)), CancellationToken.None);

                SmokeHttpDownloadService downloads = null;
                {
                    using (var host = new CefSharpUserScriptHostBuilder(repository)
                        .UseValueStore(values)
                        .UseNotificationService(new SmokeNotificationService())
                        .UseTabService(new SmokeTabService())
                        .UseDownloadServiceFactory(http => downloads = new SmokeHttpDownloadService(fixture.Port, http))
                        .Configure(new CefSharpHostOptions
                        {
                            TrustedPageWorld = true,
                            IsIncognito = context => context != null && context.IsGlobal ? (bool?)false : null
                        })
                        .LogTo(entry => Console.WriteLine("GM.log: " + entry.JsonValue))
                        .Build())
                    {
                        host.Diagnostic += (_, diagnostic) =>
                            Console.WriteLine(JsonSerializer.Serialize(new
                            {
                                type = "diagnostic",
                                code = diagnostic.Code,
                                severity = diagnostic.Severity.ToString(),
                                message = diagnostic.Message,
                                documentId = diagnostic.DocumentId,
                                frameId = diagnostic.FrameId,
                                requestId = diagnostic.RequestId
                            }));

                        using (var form = new SmokeForm(host, startUri, fixture, downloads))
                            Application.Run(form);
                    }
                }
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                type = "final-disposal", disposed = true,
                platform = Environment.Is64BitProcess ? "x64" : "x86",
                cefSharpVersion = typeof(Cef).Assembly.GetName().Version.ToString()
            }));
        }

        private const string LegacyFixture =
            "/* Licensed compatibility smoke fixture. */\n" +
            "// ==UserScript==\n" +
            "// @name MonkeySharp compatibility smoke\n" +
            "// @match http://127.0.0.1:__PORT__/initial*\n" +
            "// @match http://127.0.0.1:__PORT__/navigation*\n" +
            "// @grant GM_getValue\n" +
            "// @grant GM_setValue\n" +
            "// @grant GM_xmlhttpRequest\n" +
            "// @grant GM.notification\n" +
            "// @grant GM.openInTab\n" +
            "// @grant GM_openInTab\n" +
            "// @grant GM.download\n" +
            "// @grant GM_download\n" +
            "// @grant GM.cookie\n" +
            "// @grant GM.webRequest\n" +
            "// @connect 127.0.0.1\n" +
            "// @run-at document-end\n" +
            "// ==/UserScript==\n" +
            "var mainFrame = window.top === window;\n" +
            "var count = GM_getValue('smoke-count', 0);\n" +
            "if (mainFrame) GM_setValue('smoke-count', count + 1);\n" +
            "var smoke = { mainFrame: mainFrame, storageBefore: count, storageAfter: GM_getValue('smoke-count', 0), xhr: [] };\n" +
            "if (mainFrame && location.pathname.indexOf('/initial') >= 0) {\n" +
            "  (async function () {\n" +
            "    smoke.apis = {};\n" +
            "    await GM.cookie.set({ url: location.href, name: 'smoke-cookie', value: 'ready' });\n" +
            "    smoke.cookieList = (await GM.cookie.list({ url: location.href })).length;\n" +
            "    await GM.cookie.list({ url: location.href });\n" +
            "    await GM.cookie.set({ url: location.href, name: 'smoke-cookie', value: 'changed' });\n" +
            "    await GM.cookie.set({ url: location.href, name: 'smoke-cookie', value: 'changed' }); smoke.cookieReadback = (await GM.cookie.list({url:location.href,name:'smoke-cookie'}))[0].value === 'changed';\n" +
            "    smoke.webRule = GM.webRequest([{ selector: location.origin + '/__monkeysharp_block__', action: 'cancel' }], function (info, message, details) { smoke.webRequestEvents = (smoke.webRequestEvents || 0) + (message === 'cancel' ? 1 : 0); });\n" +
            "    var beacon = document.createElement('img'); beacon.src = location.origin + '/webrequest-beacon'; document.body.appendChild(beacon);\n" +
            "    await GM.notification({ title: 'MonkeySharp smoke', text: 'notification', onclick: function () { smoke.notificationClicked = true; }, ondone: function () { smoke.notificationDone = true; } });\n" +
            "    smoke.tab = await GM.openInTab(location.href, { active: false });\n" +
            "    var legacyTab = GM_openInTab(location.href, { active: false }); setTimeout(function () { legacyTab.close(); }, 25);\n" +
            "    GM_download({ url: location.href, name: 'smoke.txt', onprogress: function () { smoke.downloadProgress = true; }, onload: function () { smoke.downloadCompleted = true; } });\n" +
            "    GM_xmlhttpRequest({ url: location.origin + '/xhr', onprogress: function (progress) { smoke.xhr.push('progress'); }, onload: function (response) { smoke.xhr.push('load'); smoke.responseText = response.responseText; } });\n" +
            "    smoke.extended = {};\n" +
            "    var echo = await GM.xmlHttpRequest({ url: location.origin + '/echo', method: 'PROPFIND', data: new Uint8Array([65, 0, 66]), responseType: 'arraybuffer', headers: { 'X-Smoke': 'echo' } });\n" +
            "    var echoBytes = new Uint8Array(echo.response); smoke.extended.echo = echo.status === 200 && echoBytes.length === 3 && echoBytes[0] === 65 && echoBytes[1] === 0 && echoBytes[2] === 66 && echo.responseHeaders.indexOf('X-Echo-Method: PROPFIND') >= 0;\n" +
            "    var cookieXhr = await GM.xmlHttpRequest({ url: location.origin + '/cookie' });\n" +
            "    var anonymousXhr = await GM.xmlHttpRequest({ url: location.origin + '/cookie', anonymous: true });\n" +
            "    smoke.extended.cookies = cookieXhr.responseText.indexOf('smoke-cookie=changed') >= 0 && anonymousXhr.responseText.indexOf('smoke-cookie') < 0;\n" +
            "    var auth = await GM.xmlHttpRequest({ url: location.origin + '/auth', user: 'alice', password: 'secret' }); smoke.extended.auth = auth.status === 200 && auth.responseText === 'auth ok';\n" +
            "    var redirect = await GM.xmlHttpRequest({ url: location.origin + '/redirect' }); smoke.extended.redirect = redirect.status === 200 && /redirect-target$/.test(redirect.finalUrl) && redirect.responseText === 'redirect ok';\n" +
            "    var binary = await GM.xmlHttpRequest({ url: location.origin + '/binary', responseType: 'arraybuffer' }); var binaryBytes = new Uint8Array(binary.response); smoke.extended.binary = binaryBytes.length === 4 && binaryBytes[3] === 255;\n" +
            "    var large = await GM.xmlHttpRequest({ url: location.origin + '/large' }); smoke.extended.large = large.responseText.length === 1200000;\n" +
            "    var duplicate = await GM.xmlHttpRequest({ url: location.origin + '/headers' }); smoke.extended.headers = duplicate.responseHeaders.indexOf('X-Duplicate: one') >= 0 && duplicate.responseHeaders.indexOf('X-Duplicate: two') >= 0;\n" +
            "    var streamed = await GM.xmlHttpRequest({ url: location.origin + '/stream', responseType: 'stream' }); var reader = streamed.response.getReader(); var streamText = ''; while (true) { var part = await reader.read(); if (part.done) break; streamText += new TextDecoder().decode(part.value); } smoke.extended.stream = streamText === 'stream body';\n" +
            "    try { await GM.xmlHttpRequest({ url: location.origin + '/slow', timeout: 50, ontimeout: function () { smoke.extended.timeout = true; } }); } catch (ignore) {}\n" +
            "    await new Promise(function (resolve) { var pending = GM.xmlHttpRequest({ url: location.origin + '/slow', onabort: function () { smoke.extended.abort = true; resolve(); }, onerror: resolve }); pending.catch(function () {}); setTimeout(function () { pending.abort(); }, 20); });\n" +
            "    smoke.apis.xmlhttpExtended = Object.keys(smoke.extended).length === 10 && Object.keys(smoke.extended).every(function (key) { return smoke.extended[key] === true; });\n" +
            "    while (!smoke.notificationDone || !smoke.downloadProgress || !smoke.downloadCompleted || !smoke.cookieReadback || !smoke.tab.id) await new Promise(function (resolve) { setTimeout(resolve, 25); });\n" +
            "    smoke.apis.notification = smoke.notificationDone === true;\n" +
            "    smoke.apis.tab = Boolean(smoke.tab && smoke.tab.id);\n" +
            "    smoke.apis.download = smoke.downloadProgress === true && smoke.downloadCompleted === true;\n" +
            "    smoke.apis.cookie = smoke.cookieList === 1 && smoke.cookieReadback === true;\n" +
            "    smoke.apis.webRequest = Boolean(smoke.webRule) && typeof smoke.webRule.remove === 'function';\n" +
            "    smoke.done = true;\n" +
            "    window.__monkeySharpSmokeResult = smoke; document.documentElement.dataset.monkeySharpSmoke = JSON.stringify(smoke);\n" +
            "  })().catch(function (error) { smoke.error = String(error && error.message || error); window.__monkeySharpSmokeResult = smoke; });\n" +
            "} else if (mainFrame) {\n" +
            "  GM_xmlhttpRequest({ url: location.origin + '/xhr', onprogress: function (progress) { smoke.xhr.push('progress'); }, onload: function (response) { smoke.xhr.push('load'); smoke.responseText = response.responseText; smoke.done = true; window.__monkeySharpSmokeResult = smoke; } });\n" +
            "}\n" +
            "window.__monkeySharpSmokeResult = smoke;\n" +
            "if (window.top === window) { var child = document.createElement('iframe'); child.src = location.href + '#child'; document.body.appendChild(child); }";
    }

    internal sealed class SmokeForm : Form
    {
        private readonly CefSharpUserScriptHost _host;
        private readonly ChromiumWebBrowser _browser;
        private readonly Uri _startUri;
        private int _mainFrameLoads;
        private int _childFrameLoads;
        private int _contextCreated;
        private int _contextReleased;
        private bool _navigationRequested;
        private bool _closing;
        private bool _initialFixturePassed;
        private string _initialFixtureJson;
        private bool _storageMirrorVerified;
        private bool _xhrVerified;
        private bool _xhrExtendedVerified;
        private bool _notificationVerified;
        private bool _tabVerified;
        private bool _downloadVerified;
        private bool _cookieVerified;
        private bool _webRequestVerified;
        private readonly CompatibilitySuite _compatibility;
        private Dictionary<string, bool> _compatibilityResults;
        private readonly System.Windows.Forms.Timer _timeoutTimer;

        public SmokeForm(CefSharpUserScriptHost host, Uri startUri, LocalHttpFixture fixture, SmokeHttpDownloadService downloads)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _startUri = startUri ?? throw new ArgumentNullException(nameof(startUri));
            Text = "MonkeySharp compatibility smoke host";
            Width = 1024;
            Height = 768;
            _browser = new ChromiumWebBrowser(
                _startUri.AbsoluteUri)
            {
                Dock = DockStyle.Fill
            };
            // CefSharp 121 creates the native browser when the control handle is
            // created. Attach before adding the control so the host sees the
            // uninitialized browser and can register its bridge first.
            _host.Attach(_browser);
            _host.DocumentLifecycle += HostDocumentLifecycle;
            _compatibility = new CompatibilitySuite(host, _browser, fixture, downloads);
            _browser.FrameLoadEnd += BrowserFrameLoadEnd;
            _browser.ConsoleMessage += (_, message) => Console.WriteLine(JsonSerializer.Serialize(new
            { type = "console", level = message.Level.ToString(), message = message.Message, source = message.Source, line = message.Line }));
            Controls.Add(_browser);
            FormClosing += FormOnClosing;
            FormClosed += (_, __) => { _compatibility.Dispose(); _browser.Dispose(); };
            _timeoutTimer = new System.Windows.Forms.Timer { Interval = 90000 };
            _timeoutTimer.Tick += SmokeTimeout;
            _timeoutTimer.Start();
        }

        private void HostDocumentLifecycle(object sender, DocumentLifecycleEventArgs args)
        {
            if (args.Kind == DocumentLifecycleKind.ContextCreated)
                Interlocked.Increment(ref _contextCreated);
            else if (args.Kind == DocumentLifecycleKind.ContextReleased)
                Interlocked.Increment(ref _contextReleased);

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                type = "lifecycle",
                kind = args.Kind.ToString(),
                frameId = args.Frame.FrameId,
                documentId = args.Frame.DocumentId,
                mainFrame = args.Frame.IsMainFrame,
                url = args.Frame.Url.AbsoluteUri
            }));
        }

        private void BrowserFrameLoadEnd(object sender, FrameLoadEndEventArgs args)
        {
            if (!args.Frame.IsMain)
            {
                Interlocked.Increment(ref _childFrameLoads);
                return;
            }

            var loadNumber = Interlocked.Increment(ref _mainFrameLoads);
            var path = new Uri(args.Url).AbsolutePath;
            if (path == "/initial" || path == "/navigation")
                _ = VerifyFixtureSafelyAsync(loadNumber == 1 ? "initial" : "navigation");
        }

        private async Task VerifyFixtureSafelyAsync(string phase)
        {
            try { await VerifyFixtureAsync(phase); }
            catch (Exception exception) { FailSmoke(phase, exception.ToString()); }
        }

        private async Task VerifyFixtureAsync(string phase)
        {
            var browser = _browser.GetBrowser();
            var frame = browser?.MainFrame;
            if (frame == null || frame.IsDisposed)
            {
                FailSmoke(phase, "The browser main frame is unavailable.");
                return;
            }
            JavascriptResponse response;
            try
            {
                var timer = Stopwatch.StartNew();
                while (true)
                {
                    response = await frame.EvaluateScriptAsync(
                        "JSON.stringify(window.__monkeySharpSmokeResult || null)", timeout: TimeSpan.FromSeconds(2));
                    if (response.Success && response.Result is string json && json != "null")
                    {
                        using (var document = JsonDocument.Parse(json))
                            if (document.RootElement.TryGetProperty("done", out _) || document.RootElement.TryGetProperty("error", out _)) break;
                    }
                    if (timer.Elapsed > TimeSpan.FromSeconds(15))
                        throw new TimeoutException("Legacy fixture did not finish: " + response.Result);
                    await Task.Delay(50);
                }
            }
            catch (Exception exception)
            {
                FailSmoke(phase, exception.Message);
                return;
            }

            var passed = ValidateFixtureResult(response, phase);
            if (phase == "initial" && response.Success && response.Result is string initialJson)
                _initialFixtureJson = initialJson;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                type = "fixture-result",
                phase,
                success = passed,
                result = response.Result,
                message = response.Message
            }));

            frame.Dispose();
            if (!passed)
            {
                Environment.ExitCode = 1;
                Close();
                return;
            }

            if (phase == "initial" && !_navigationRequested)
            {
                _navigationRequested = true;
                _browser.Load(new Uri(_startUri, "navigation").AbsoluteUri);
                _initialFixturePassed = true;
            }
            else if (phase == "navigation")
            {
                _compatibilityResults = await _compatibility.RunAsync();
                await CloseAfterVerificationAsync().ConfigureAwait(true);
            }
        }

        private bool ValidateFixtureResult(JavascriptResponse response, string phase)
        {
            if (!response.Success || !(response.Result is string json))
                return false;
            try
            {
                using (var document = JsonDocument.Parse(json))
                {
                    var root = document.RootElement;
                    var expectedBefore = phase == "initial" ? 0 : 1;
                    var expectedAfter = phase == "initial" ? 1 : 2;
                    _storageMirrorVerified = root.GetProperty("mainFrame").GetBoolean() &&
                        root.GetProperty("storageBefore").GetInt32() == expectedBefore &&
                        root.GetProperty("storageAfter").GetInt32() == expectedAfter;
                    if (!_storageMirrorVerified)
                        return false;
                    var callbacks = root.GetProperty("xhr").EnumerateArray()
                        .Select(item => item.GetString()).ToList();
                    _xhrVerified = callbacks.Count >= 2 && callbacks.Last() == "load" &&
                        callbacks.Take(callbacks.Count - 1).All(item => item == "progress") &&
                        root.GetProperty("responseText").GetString() == "legacy xhr ok";
                    if (!_xhrVerified)
                        return false;
                    if (phase == "initial")
                    {
                        if (root.TryGetProperty("error", out _))
                            return false;
                        var apis = root.GetProperty("apis");
                        _notificationVerified = apis.GetProperty("notification").GetBoolean();
                        _tabVerified = apis.GetProperty("tab").GetBoolean();
                        _downloadVerified = apis.GetProperty("download").GetBoolean();
                        _cookieVerified = apis.GetProperty("cookie").GetBoolean();
                        _webRequestVerified = apis.GetProperty("webRequest").GetBoolean();
                        _xhrExtendedVerified = apis.GetProperty("xmlhttpExtended").GetBoolean();
                        return _notificationVerified && _tabVerified && _downloadVerified &&
                            _cookieVerified && _webRequestVerified && _xhrExtendedVerified;
                    }
                    return true;
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    type = "fixture-validation-error",
                    phase,
                    error = exception.Message
                }));
                return false;
            }
        }

        private void FailSmoke(string phase, string message)
        {
            Environment.ExitCode = 1;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                type = "fixture-result",
                phase,
                success = false,
                error = message
            }));
            BeginInvoke((Action)Close);
        }

        private async Task CloseAfterVerificationAsync()
        {
            if (_closing)
                return;
            _closing = true;
            _timeoutTimer.Stop();
            await _host.DetachAsync(CancellationToken.None).ConfigureAwait(true);
            var success = _initialFixturePassed && _mainFrameLoads >= 2 && _childFrameLoads >= 1 && _contextReleased >= 1 &&
                !_host.IsAttached && _compatibilityResults != null && _compatibilityResults.Count == 10 && _compatibilityResults.Values.All(value => value);
            object fixture = null;
            if (!string.IsNullOrEmpty(_initialFixtureJson))
            {
                using (var document = JsonDocument.Parse(_initialFixtureJson))
                    fixture = document.RootElement.Clone();
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                type = "smoke-summary",
                success,
                compatibility = _compatibilityResults,
                fixtureResult = fixture,
                storageMirror = _storageMirrorVerified,
                xhr = _xhrVerified,
                xhrExtended = _xhrExtendedVerified,
                notification = _notificationVerified,
                tab = _tabVerified,
                download = _downloadVerified,
                cookie = _cookieVerified,
                webRequest = _webRequestVerified,
                mainFrameLoads = _mainFrameLoads,
                childFrameLoads = _childFrameLoads,
                navigation = _navigationRequested,
                contextCreated = _contextCreated,
                contextReleased = _contextReleased,
                hostAttachedAfterDetach = _host.IsAttached
            }));
            if (!success)
                Environment.ExitCode = 1;
            BeginInvoke((Action)Close);
        }

        private void FormOnClosing(object sender, FormClosingEventArgs args)
        {
            if (_closing)
                return;
            _closing = true;
            _timeoutTimer.Stop();
            _host.DetachAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        private void SmokeTimeout(object sender, EventArgs args)
        {
            _timeoutTimer.Stop();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                type = "smoke-timeout",
                message = "The browser did not complete the fixture checks within 90 seconds."
            }));
            Environment.ExitCode = 1;
            Close();
        }
    }

    internal sealed class LocalHttpFixture : IDisposable
    {
        private readonly System.Security.Cryptography.X509Certificates.X509Certificate2 _certificate;
        public LocalHttpFixture(bool tls = false)
        {
            if (!tls) return;
            var assembly = typeof(LocalHttpFixture).Assembly;
            using (var input = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith("loopback.pfx"))))
            using (var bytes = new MemoryStream())
            {
                input.CopyTo(bytes);
                _certificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(bytes.ToArray(), "fixture");
            }
        }
        private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
        private CancellationTokenSource _cancellation;
        private Task _acceptLoop;
        private readonly ConcurrentDictionary<string, int> _requests = new ConcurrentDictionary<string, int>();
        private readonly ConcurrentDictionary<TcpClient, byte> _clients = new ConcurrentDictionary<TcpClient, byte>();
        private readonly ConcurrentBag<Task> _serving = new ConcurrentBag<Task>();

        public int Port { get; private set; }
        public int OtherPort { get; set; }
        public static readonly byte[] DownloadBytes = Enumerable.Range(0, 32768).Select(index => (byte)(index % 251)).ToArray();
        public int RequestCount(string path) => _requests.TryGetValue(path, out var count) ? count : 0;

        public Uri Start()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _cancellation = new CancellationTokenSource();
            _acceptLoop = AcceptLoopAsync();
            return new Uri((_certificate == null ? "http" : "https") + "://127.0.0.1:" + Port + "/initial");
        }

        public void Dispose()
        {
            _cancellation?.Cancel();
            _listener.Stop();
            try
            {
                _acceptLoop?.GetAwaiter().GetResult();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
            foreach (var client in _clients.Keys) client.Dispose();
            Task.WhenAll(_serving).GetAwaiter().GetResult();
            _cancellation?.Dispose();
            _certificate?.Dispose();
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cancellation.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    break;
                }
                _clients.TryAdd(client, 0);
                _serving.Add(ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            using (var stream = await OpenStreamAsync(client).ConfigureAwait(false))
            {
                try
                {
                    var request = await ReadRequestAsync(stream).ConfigureAwait(false);
                    if (request == null) return;
                    _requests.AddOrUpdate(request.Path, 1, (_, count) => count + 1);
                    if (request.Path == "/compat/files/bytes" || request.Path == "/compat/files/slow")
                    {
                        await WriteDownloadAsync(stream, request.Path.EndsWith("slow"), _cancellation.Token).ConfigureAwait(false);
                        return;
                    }
                    if (request.Path == "/compat/replay-image") await Task.Delay(3000, _cancellation.Token).ConfigureAwait(false);
                    if (request.Path == "/compat/replay-defer") await Task.Delay(200, _cancellation.Token).ConfigureAwait(false);
                    if (request.Path == "/compat/files/stalled") await Task.Delay(1500, _cancellation.Token).ConfigureAwait(false);
                    if (request.Path == "/stream")
                    {
                        await WriteChunkedAsync(stream).ConfigureAwait(false);
                        return;
                    }
                    if (request.Path == "/slow") await Task.Delay(500).ConfigureAwait(false);

                    var status = 200;
                    var statusText = "OK";
                    var contentType = "text/plain; charset=utf-8";
                    var headers = new List<KeyValuePair<string, string>>();
                    byte[] body;
                    switch (request.Path)
                    {
                        case "/compat/files/auth":
                            request.Headers.TryGetValue("Cookie", out var downloadCookie);
                            if (downloadCookie != null && downloadCookie.Contains("download-login=ready")) body = DownloadBytes;
                            else { status = 401; statusText = "Unauthorized"; body = Encoding.UTF8.GetBytes("Login required"); }
                            break;
                        case "/compat/files/auth-redirect":
                            status = 302; statusText = "Found";
                            headers.Add(new KeyValuePair<string, string>("Location", "/compat/files/auth"));
                            body = new byte[0];
                            break;
                        case "/compat/files/empty":
                            body = new byte[0];
                            break;
                        case "/probe/start":
                            contentType = "text/html; charset=utf-8";
                            body = Encoding.UTF8.GetBytes("<!doctype html><html><head><script>" +
                                "document.dispatchEvent(new Event('probe-first-inline'));" +
                                "window.__firstInline = {page:window.__pagePrepared === true, isolatedA:document.documentElement.dataset.isolatedA || null, isolatedB:document.documentElement.dataset.isolatedB || null, gm:document.documentElement.dataset.gmStart || null};" +
                                "</script></head><body>First inline timing probe</body></html>");
                            break;
                        case "/compat/frames":
                            contentType = "text/html; charset=utf-8";
                            body = Encoding.UTF8.GetBytes("<!doctype html><html><body><iframe id='same' src='/compat/frames/same'></iframe>" +
                                "<iframe src='http://127.0.0.1:" + OtherPort + "/compat/frames/cross'></iframe></body></html>");
                            break;
                        case "/compat/replay":
                            contentType = "text/html; charset=utf-8";
                            body = Encoding.UTF8.GetBytes("<!doctype html><html><head><script defer src='/compat/replay-defer'></script></head><body><img src='/compat/replay-image'></body></html>");
                            break;
                        case "/compat/csp":
                            contentType = "text/html; charset=utf-8";
                            headers.Add(new KeyValuePair<string, string>("Content-Security-Policy", "default-src 'none';script-src 'none';style-src 'none'"));
                            body = Encoding.UTF8.GetBytes("<!doctype html><html><head><script>window.pageCspBypassed=true;</script></head><body>CSP fixture</body></html>");
                            break;
                        case "/compat/replay-defer":
                            contentType = "text/javascript";
                            body = Encoding.UTF8.GetBytes("window.replayDeferred=true;");
                            break;
                        case "/compat/replay-image":
                            contentType = "image/png";
                            body = new byte[] { 0, 1, 2 };
                            break;
                        case "/compat/isolated":
                        case "/compat/isolated-child":
                            contentType = "text/html; charset=utf-8";
                            body = Encoding.UTF8.GetBytes("<!doctype html><html><head><script>window.pageOnlyCanary='page';window.isolatedAtFirstInline=document.documentElement.getAttribute('data-isolated-a');</script></head><body>Isolated execution fixture</body></html>");
                            break;
                        case "/compat/cookie-echo":
                            request.Headers.TryGetValue("Cookie", out var compatCookie);
                            body = Encoding.UTF8.GetBytes(compatCookie ?? string.Empty);
                            break;
                        case "/compat/net/blocked":
                        case "/compat/net/original":
                        case "/compat/net/target":
                        case "/compat/net/cleanup":
                            body = Encoding.UTF8.GetBytes(request.Path.Substring(request.Path.LastIndexOf('/') + 1));
                            break;
                        case "/compat/files/missing":
                            status = 404;
                            statusText = "Not Found";
                            body = Encoding.UTF8.GetBytes("missing fixture file");
                            break;
                        case "/xhr":
                            body = Encoding.UTF8.GetBytes("legacy xhr ok");
                            break;
                        case "/echo":
                            body = request.Body;
                            headers.Add(new KeyValuePair<string, string>("X-Echo-Method", request.Method));
                            break;
                        case "/cookie":
                            request.Headers.TryGetValue("Cookie", out var cookie);
                            body = Encoding.UTF8.GetBytes(cookie ?? string.Empty);
                            break;
                        case "/auth":
                            request.Headers.TryGetValue("Authorization", out var authorization);
                            if (authorization != "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:secret")))
                            {
                                status = 401;
                                statusText = "Unauthorized";
                                headers.Add(new KeyValuePair<string, string>("WWW-Authenticate", "Basic realm=\"MonkeySharp\""));
                                body = Encoding.UTF8.GetBytes("auth required");
                            }
                            else body = Encoding.UTF8.GetBytes("auth ok");
                            break;
                        case "/redirect":
                            status = 302;
                            statusText = "Found";
                            headers.Add(new KeyValuePair<string, string>("Location", "/redirect-target"));
                            body = new byte[0];
                            break;
                        case "/redirect-target":
                            body = Encoding.UTF8.GetBytes("redirect ok");
                            break;
                        case "/binary":
                            contentType = "application/octet-stream";
                            body = new byte[] { 0, 1, 2, 255 };
                            break;
                        case "/large":
                            body = Enumerable.Repeat((byte)'x', 1200000).ToArray();
                            break;
                        case "/headers":
                            headers.Add(new KeyValuePair<string, string>("X-Duplicate", "one"));
                            headers.Add(new KeyValuePair<string, string>("X-Duplicate", "two"));
                            body = Encoding.UTF8.GetBytes("headers");
                            break;
                        case "/slow":
                            body = Encoding.UTF8.GetBytes("too slow");
                            break;
                        default:
                            contentType = "text/html; charset=utf-8";
                            body = Encoding.UTF8.GetBytes(
                                "<!doctype html><html><head><title>MonkeySharp smoke</title></head>" +
                                "<body><main id='smoke-fixture'>loopback fixture</main></body></html>");
                            break;
                    }
                    await WriteResponseAsync(stream, status, statusText, contentType, headers, body)
                        .ConfigureAwait(false);
                }
                catch (IOException)
                {
                }
                catch (System.Security.Authentication.AuthenticationException) { }
                catch (SocketException)
                {
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) when (_cancellation.IsCancellationRequested) { }
                finally { _clients.TryRemove(client, out _); }
            }
        }

        private async Task<Stream> OpenStreamAsync(TcpClient client)
        {
            if (_certificate == null) return client.GetStream();
            var stream = new System.Net.Security.SslStream(client.GetStream(), false);
            try
            {
                await stream.AuthenticateAsServerAsync(_certificate, false, System.Security.Authentication.SslProtocols.Tls12, false).ConfigureAwait(false);
                return stream;
            }
            catch (Exception error) when (error is IOException || error is System.Security.Authentication.AuthenticationException)
            { stream.Dispose(); return Stream.Null; }
        }

        private static async Task<HttpRequestData> ReadRequestAsync(Stream stream)
        {
            var buffer = new byte[4096];
            using (var received = new MemoryStream())
            {
                var headerEnd = -1;
                while (headerEnd < 0)
                {
                    var count = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (count == 0) return null;
                    received.Write(buffer, 0, count);
                    if (received.Length > 64 * 1024) throw new IOException("HTTP request headers are too large.");
                    headerEnd = FindHeaderEnd(received.GetBuffer(), (int)received.Length);
                }
                var all = received.ToArray();
                var headerText = Encoding.ASCII.GetString(all, 0, headerEnd);
                var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
                var requestLine = lines[0].Split(' ');
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in lines.Skip(1))
                {
                    var separator = line.IndexOf(':');
                    if (separator > 0)
                        headers[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
                }
                var contentLength = 0;
                if (headers.TryGetValue("Content-Length", out var lengthText)) int.TryParse(lengthText, out contentLength);
                var body = new byte[contentLength];
                var available = Math.Min(contentLength, all.Length - headerEnd - 4);
                if (available > 0) Buffer.BlockCopy(all, headerEnd + 4, body, 0, available);
                var offset = available;
                while (offset < contentLength)
                {
                    var count = await stream.ReadAsync(body, offset, contentLength - offset).ConfigureAwait(false);
                    if (count == 0) break;
                    offset += count;
                }
                return new HttpRequestData(requestLine[0], requestLine[1].Split('?')[0], headers, body);
            }
        }

        private static int FindHeaderEnd(byte[] buffer, int length)
        {
            for (var index = 0; index <= length - 4; index++)
            {
                if (buffer[index] == 13 && buffer[index + 1] == 10 &&
                    buffer[index + 2] == 13 && buffer[index + 3] == 10)
                    return index;
            }
            return -1;
        }

        private static async Task WriteResponseAsync(
            Stream stream,
            int status,
            string statusText,
            string contentType,
            IEnumerable<KeyValuePair<string, string>> headers,
            byte[] body)
        {
            var builder = new StringBuilder()
                .Append("HTTP/1.1 ").Append(status).Append(' ').Append(statusText).Append("\r\n")
                .Append("Content-Type: ").Append(contentType).Append("\r\n")
                .Append("Content-Length: ").Append(body.Length).Append("\r\nCache-Control: no-store\r\n");
            foreach (var header in headers)
                builder.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
            builder.Append("Connection: close\r\n\r\n");
            var headerBytes = Encoding.ASCII.GetBytes(builder.ToString());
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(false);
            await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
        }

        private static async Task WriteChunkedAsync(Stream stream)
        {
            var header = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\n" +
                "Transfer-Encoding: chunked\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
            foreach (var text in new[] { "stream ", "body" })
            {
                var body = Encoding.UTF8.GetBytes(text);
                var prefix = Encoding.ASCII.GetBytes(body.Length.ToString("X") + "\r\n");
                await stream.WriteAsync(prefix, 0, prefix.Length).ConfigureAwait(false);
                await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
                await stream.WriteAsync(new byte[] { 13, 10 }, 0, 2).ConfigureAwait(false);
                await Task.Delay(40).ConfigureAwait(false);
            }
            var end = Encoding.ASCII.GetBytes("0\r\n\r\n");
            await stream.WriteAsync(end, 0, end.Length).ConfigureAwait(false);
        }

        private static async Task WriteDownloadAsync(Stream stream, bool slow, CancellationToken token)
        {
            var header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: " +
                DownloadBytes.Length + "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, 0, header.Length, token).ConfigureAwait(false);
            for (var offset = 0; offset < DownloadBytes.Length; offset += 4096)
            {
                await stream.WriteAsync(DownloadBytes, offset, 4096, token).ConfigureAwait(false);
                await Task.Delay(slow ? 200 : 10, token).ConfigureAwait(false);
            }
        }

        private sealed class HttpRequestData
        {
            public HttpRequestData(string method, string path, IDictionary<string, string> headers, byte[] body)
            {
                Method = method;
                Path = path;
                Headers = headers;
                Body = body;
            }

            public string Method { get; }
            public string Path { get; }
            public IDictionary<string, string> Headers { get; }
            public byte[] Body { get; }
        }
    }

    internal sealed class SmokeNotificationService : INotificationService
    {
        public INotificationHandle Create(UserScriptNotificationRequest request, CancellationToken cancellationToken)
        {
            return new SmokeNotificationHandle();
        }
    }

    internal sealed class SmokeNotificationHandle : INotificationHandle
    {
        public void Start() { _ = CompleteAsync(); }
        public void ResolveClick(bool preventDefault) { }
        private readonly TaskCompletionSource<bool> _completion = new TaskCompletionSource<bool>();
        private int _disposed;


        public Task Completion => _completion.Task;
        public event EventHandler Clicked;
        public event EventHandler Closed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _completion.TrySetResult(true);
        }

        private async Task CompleteAsync()
        {
            await Task.Delay(80).ConfigureAwait(false);
            if (Volatile.Read(ref _disposed) != 0) return;
            Clicked?.Invoke(this, EventArgs.Empty);
            await Task.Delay(80).ConfigureAwait(false);
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Closed?.Invoke(this, EventArgs.Empty);
                _completion.TrySetResult(true);
            }
        }
    }

    internal sealed class SmokeTabService : ITabService
    {
        private int _nextId;

        public Task<ITabHandle> OpenAsync(OpenTabRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handle = new SmokeTabHandle("smoke-tab-" + Interlocked.Increment(ref _nextId));
            return Task.FromResult<ITabHandle>(handle);
        }
    }

    internal sealed class SmokeTabHandle : ITabHandle
    {
        private readonly TaskCompletionSource<bool> _closed = new TaskCompletionSource<bool>();
        private int _isClosed;

        public SmokeTabHandle(string tabId)
        {
            TabId = tabId;
            _ = AutoCloseAsync();
        }

        public string TabId { get; }
        public bool Closed => Volatile.Read(ref _isClosed) != 0;
        public event EventHandler OnClose;
        public Task CloseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CloseCore();
            return _closed.Task;
        }

        public void Dispose() { CloseCore(); }

        private async Task AutoCloseAsync()
        {
            await Task.Delay(220).ConfigureAwait(false);
            CloseCore();
        }

        private void CloseCore()
        {
            if (Interlocked.Exchange(ref _isClosed, 1) != 0) return;
            OnClose?.Invoke(this, EventArgs.Empty);
            _closed.TrySetResult(true);
        }
    }

}
