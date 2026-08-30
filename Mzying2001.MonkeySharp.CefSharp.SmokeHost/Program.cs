using CefSharp;
using CefSharp.WinForms;
using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.CefSharp;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
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
        private static void Main()
        {
            CefSharpSettings.WcfEnabled = false;
            CefSharpSettings.ConcurrentTaskExecution = true;
            var settings = new CefSettings
            {
                RootCachePath = Path.Combine(Path.GetTempPath(), "MonkeySharpSmoke", Guid.NewGuid().ToString("N"))
            };
            if (!Cef.Initialize(settings))
                throw new InvalidOperationException("CefSharp initialization failed.");

            try
            {
                RunAsync().GetAwaiter().GetResult();
            }
            finally
            {
                Cef.Shutdown();
            }
        }

        private static async Task RunAsync()
        {
            using (var fixture = new LocalHttpFixture())
            {
                var startUri = fixture.Start();
                var repository = new InMemoryUserScriptRepository();
                await repository.InstallAsync(
                    LegacyFixture.Replace("__PORT__", fixture.Port.ToString()),
                    "smoke://fixture",
                    true,
                    CancellationToken.None);

                {
                    var host = new CefSharpUserScriptHostBuilder(repository)
                        .UseNotificationService(new SmokeNotificationService())
                        .UseTabService(new SmokeTabService())
                        .UseDownloadService(new SmokeDownloadService())
                        .Configure(new CefSharpHostOptions
                        {
                            TrustedPageWorld = true
                        })
                        .LogTo(entry => Console.WriteLine("GM.log: " + entry.JsonValue))
                        .Build();
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

                    using (var form = new SmokeForm(host, startUri))
                        Application.Run(form);
                    host.Dispose();
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        type = "final-disposal",
                        disposed = true,
                        platform = Environment.Is64BitProcess ? "x64" : "x86",
                        cefSharpVersion = typeof(Cef).Assembly.GetName().Version.ToString()
                    }));
                }
            }
        }

        private const string LegacyFixture =
            "/* Licensed compatibility smoke fixture. */\n" +
            "// ==UserScript==\n" +
            "// @name MonkeySharp compatibility smoke\n" +
            "// @match http://127.0.0.1:__PORT__/*\n" +
            "// @grant GM_getValue\n" +
            "// @grant GM_setValue\n" +
            "// @grant GM_xmlhttpRequest\n" +
            "// @grant GM.notification\n" +
            "// @grant GM.openInTab\n" +
            "// @grant GM.download\n" +
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
            "    smoke.cookie = await GM.cookie.set({ url: location.href, name: 'smoke-cookie', value: 'ready' });\n" +
            "    smoke.cookieList = (await GM.cookie.list({ url: location.href })).length;\n" +
            "    GM.cookie.addListener({ url: location.href }, function (cookie) { smoke.cookieChanged = cookie && cookie.value === 'changed'; });\n" +
            "    await new Promise(function (resolve) { setTimeout(resolve, 80); });\n" +
            "    await GM.cookie.set({ url: location.href, name: 'smoke-cookie', value: 'changed' });\n" +
            "    smoke.webRule = await GM.webRequest.addRule({ id: 'smoke-header', phase: 'OnBeforeRequest', priority: 10, filter: { urlPatterns: [location.origin + '/*'] }, action: { kind: 'ModifyRequestHeaders', headers: { 'X-MonkeySharp-Smoke': '1' } } });\n" +
            "    GM.webRequest.addListener({ urlPatterns: [location.origin + '/*'] }, function (event) { smoke.webRequestEvents = (smoke.webRequestEvents || 0) + 1; });\n" +
            "    var beacon = document.createElement('img'); beacon.src = location.origin + '/webrequest-beacon'; document.body.appendChild(beacon);\n" +
            "    await GM.notification({ title: 'MonkeySharp smoke', text: 'notification', onclick: function () { smoke.notificationClicked = true; }, ondone: function () { smoke.notificationDone = true; } });\n" +
            "    smoke.tab = await GM.openInTab(location.href, { active: false });\n" +
            "    var legacyTab = GM_openInTab(location.href, { active: false }); setTimeout(function () { legacyTab.close(); }, 25);\n" +
            "    GM_download({ url: location.href, name: 'smoke.txt', onprogress: function () { smoke.downloadProgress = true; }, onload: function () { smoke.downloadCompleted = true; } });\n" +
            "    GM_xmlhttpRequest({ url: location.origin + '/xhr', onprogress: function (progress) { smoke.xhr.push('progress'); }, onload: function (response) { smoke.xhr.push('load'); smoke.responseText = response.responseText; } });\n" +
            "    await new Promise(function (resolve) { setTimeout(resolve, 350); });\n" +
            "    smoke.apis.notification = smoke.notificationDone === true;\n" +
            "    smoke.apis.tab = Boolean(smoke.tab && smoke.tab.id);\n" +
            "    smoke.apis.download = smoke.downloadProgress === true && smoke.downloadCompleted === true;\n" +
            "    smoke.apis.cookie = smoke.cookieList === 1 && smoke.cookieChanged === true;\n" +
            "    smoke.apis.webRequest = Boolean(smoke.webRule) && (smoke.webRequestEvents || 0) > 0;\n" +
            "    window.__monkeySharpSmokeResult = smoke; document.documentElement.dataset.monkeySharpSmoke = JSON.stringify(smoke);\n" +
            "  })().catch(function (error) { smoke.error = String(error && error.message || error); window.__monkeySharpSmokeResult = smoke; });\n" +
            "} else if (mainFrame) {\n" +
            "  GM_xmlhttpRequest({ url: location.origin + '/xhr', onprogress: function (progress) { smoke.xhr.push('progress'); }, onload: function (response) { smoke.xhr.push('load'); smoke.responseText = response.responseText; window.__monkeySharpSmokeResult = smoke; } });\n" +
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
        private bool _notificationVerified;
        private bool _tabVerified;
        private bool _downloadVerified;
        private bool _cookieVerified;
        private bool _webRequestVerified;
        private readonly System.Windows.Forms.Timer _timeoutTimer;

        public SmokeForm(CefSharpUserScriptHost host, Uri startUri)
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
            _browser.FrameLoadEnd += BrowserFrameLoadEnd;
            Controls.Add(_browser);
            FormClosing += FormOnClosing;
            FormClosed += (_, __) => _browser.Dispose();
            _timeoutTimer = new System.Windows.Forms.Timer { Interval = 30000 };
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
            _ = VerifyFixtureAsync(loadNumber == 1 ? "initial" : "navigation");
        }

        private async Task VerifyFixtureAsync(string phase)
        {
            // The legacy callbacks and host lifecycle notifications are asynchronous.
            await Task.Delay(1500).ConfigureAwait(true);
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
                response = await frame.EvaluateScriptAsync(
                    "JSON.stringify(window.__monkeySharpSmokeResult || null)",
                    timeout: TimeSpan.FromSeconds(5));
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
                        return _notificationVerified && _tabVerified && _downloadVerified &&
                            _cookieVerified && _webRequestVerified;
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
            Close();
        }

        private async Task CloseAfterVerificationAsync()
        {
            if (_closing)
                return;
            _closing = true;
            _timeoutTimer.Stop();
            await _host.DetachAsync(CancellationToken.None).ConfigureAwait(true);
            object fixture = null;
            if (!string.IsNullOrEmpty(_initialFixtureJson))
            {
                using (var document = JsonDocument.Parse(_initialFixtureJson))
                    fixture = document.RootElement.Clone();
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                type = "smoke-summary",
                success = _initialFixturePassed && _mainFrameLoads >= 2 && _childFrameLoads >= 1 && _contextReleased >= 1,
                fixtureResult = fixture,
                storageMirror = _storageMirrorVerified,
                xhr = _xhrVerified,
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
            if (_initialFixtureJson == null || !_initialFixturePassed || _mainFrameLoads < 2 || _childFrameLoads < 1 || _contextReleased < 1)
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
                message = "The browser did not complete the fixture checks within 30 seconds."
            }));
            Environment.ExitCode = 1;
            Close();
        }
    }

    internal sealed class LocalHttpFixture : IDisposable
    {
        private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
        private CancellationTokenSource _cancellation;
        private Task _acceptLoop;

        public int Port { get; private set; }

        public Uri Start()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _cancellation = new CancellationTokenSource();
            _acceptLoop = AcceptLoopAsync();
            return new Uri("http://127.0.0.1:" + Port + "/initial");
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
            _cancellation?.Dispose();
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
                _ = ServeAsync(client);
            }
        }

        private static async Task ServeAsync(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var requestBuffer = new byte[4096];
                try
                {
                    await stream.ReadAsync(requestBuffer, 0, requestBuffer.Length).ConfigureAwait(false);
                    var requestText = Encoding.ASCII.GetString(requestBuffer);
                    var isXhr = requestText.StartsWith("GET /xhr", StringComparison.OrdinalIgnoreCase);
                    var body = isXhr
                        ? Encoding.UTF8.GetBytes("legacy xhr ok")
                        : Encoding.UTF8.GetBytes(
                            "<!doctype html><html><head><title>MonkeySharp smoke</title></head>" +
                            "<body><main id='smoke-fixture'>loopback fixture</main></body></html>");
                    var header = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\nContent-Type: " + (isXhr ? "text/plain" : "text/html") + "; charset=utf-8\r\n" +
                        "Content-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
                    await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
                }
                catch (IOException)
                {
                }
                catch (SocketException)
                {
                }
            }
        }
    }

    internal sealed class SmokeNotificationService : INotificationService
    {
        public INotificationHandle ShowAsync(UserScriptNotificationRequest request, CancellationToken cancellationToken)
        {
            return new SmokeNotificationHandle();
        }
    }

    internal sealed class SmokeNotificationHandle : INotificationHandle
    {
        private readonly TaskCompletionSource<bool> _completion = new TaskCompletionSource<bool>();
        private int _disposed;

        public SmokeNotificationHandle()
        {
            _ = CompleteAsync();
        }

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

    internal sealed class SmokeDownloadService : IDownloadService
    {
        private int _nextId;

        public Task<IDownloadOperation> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IDownloadOperation>(
                new SmokeDownloadOperation("smoke-download-" + Interlocked.Increment(ref _nextId)));
        }
    }

#pragma warning disable CS0067
    internal sealed class SmokeDownloadOperation : IDownloadOperation
    {
        private readonly TaskCompletionSource<bool> _completion = new TaskCompletionSource<bool>();
        private int _state;

        public SmokeDownloadOperation(string downloadId)
        {
            DownloadId = downloadId;
            _ = CompleteAsync();
        }

        public string DownloadId { get; }
        public Task Completion => _completion.Task;
        public event EventHandler<UserScriptDownloadProgress> Progress;
        public event EventHandler Completed;
        public event EventHandler<UserScriptDownloadFailure> Failed;
        public event EventHandler Aborted;

        public void Abort()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
            {
                Aborted?.Invoke(this, EventArgs.Empty);
                _completion.TrySetResult(true);
            }
        }

        public void Dispose() { Abort(); }

        private async Task CompleteAsync()
        {
            await Task.Delay(90).ConfigureAwait(false);
            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0) return;
            Progress?.Invoke(this, new UserScriptDownloadProgress(1, 1));
            Completed?.Invoke(this, EventArgs.Empty);
            _completion.TrySetResult(true);
        }
    }
#pragma warning restore CS0067

}
