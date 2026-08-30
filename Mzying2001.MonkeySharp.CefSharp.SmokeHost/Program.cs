using CefSharp;
using CefSharp.WinForms;
using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.CefSharp;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Generic;
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
            if (!Cef.Initialize(new CefSettings()))
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

                var host = new CefSharpUserScriptHostBuilder(repository)
                    .UseHttpRequestService(new FixtureHttpRequestService())
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
                Console.WriteLine("{\"type\":\"host-disposed\"}");
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
            "// @connect api.example.com\n" +
            "// @run-at document-end\n" +
            "// ==/UserScript==\n" +
            "var mainFrame = window.top === window;\n" +
            "var count = GM_getValue('smoke-count', 0);\n" +
            "if (mainFrame) GM_setValue('smoke-count', count + 1);\n" +
            "var smoke = { mainFrame: mainFrame, storageBefore: count, storageAfter: GM_getValue('smoke-count', 0), xhr: [] };\n" +
            "if (mainFrame) GM_xmlhttpRequest({ url: 'https://api.example.com/smoke', onprogress: function (progress) { smoke.xhr.push('progress'); }, onload: function (response) { smoke.xhr.push('load'); smoke.responseText = response.responseText; window.__monkeySharpSmokeResult = smoke; document.documentElement.dataset.monkeySharpSmoke = JSON.stringify(smoke); } });\n" +
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
            // The legacy XHR callback and child-frame creation are asynchronous.
            await Task.Delay(500).ConfigureAwait(true);
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
                    if (!root.GetProperty("mainFrame").GetBoolean() ||
                        root.GetProperty("storageBefore").GetInt32() != expectedBefore ||
                        root.GetProperty("storageAfter").GetInt32() != expectedAfter)
                        return false;
                    var callbacks = root.GetProperty("xhr").EnumerateArray()
                        .Select(item => item.GetString()).ToList();
                    return callbacks.SequenceEqual(new[] { "progress", "load" }) &&
                        root.GetProperty("responseText").GetString() == "legacy xhr ok";
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
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                type = "smoke-summary",
                mainFrameLoads = _mainFrameLoads,
                childFrameLoads = _childFrameLoads,
                contextCreated = _contextCreated,
                contextReleased = _contextReleased,
                hostAttachedAfterDetach = _host.IsAttached
            }));
            if (!_initialFixturePassed || _mainFrameLoads < 2 || _childFrameLoads < 1 || _contextReleased < 1)
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
                    var body = Encoding.UTF8.GetBytes(
                        "<!doctype html><html><head><title>MonkeySharp smoke</title></head>" +
                        "<body><main id='smoke-fixture'>loopback fixture</main></body></html>");
                    var header = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
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

    internal sealed class FixtureHttpRequestService : IHttpRequestService
    {
        public IHttpRequestOperation SendAsync(
            UserScriptHttpRequest request,
            CancellationToken cancellationToken)
        {
            var body = Encoding.UTF8.GetBytes("legacy xhr ok");
            return new FixtureHttpOperation(new UserScriptHttpResponse(
                    200,
                    "OK",
                    request.Url,
                    new Dictionary<string, string> { ["Content-Type"] = "text/plain" },
                    body,
                    "legacy xhr ok"));
        }
    }

    internal sealed class FixtureHttpOperation : IHttpRequestOperation
    {
        private readonly UserScriptHttpResponse _response;
        public FixtureHttpOperation(UserScriptHttpResponse response) { _response = response; }
        public IProgress<UserScriptHttpProgress> Progress { get; set; }
        public Task<UserScriptHttpResponse> Completion => CompleteAsync();
        private async Task<UserScriptHttpResponse> CompleteAsync()
        {
            await Task.Delay(50).ConfigureAwait(false);
            Progress?.Report(new UserScriptHttpProgress(1, 1));
            return _response;
        }
        public void Abort() { }
    }
}
