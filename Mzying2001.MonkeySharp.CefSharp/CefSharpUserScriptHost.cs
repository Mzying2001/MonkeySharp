using CefSharp;
using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.CefSharp
{
    /// <summary>
    /// Connects the MonkeySharp runtime to the lifecycle and JavaScript binding facilities of one CefSharp browser.
    /// </summary>
    public sealed class CefSharpUserScriptHost : IUserScriptHost, IDisposable
    {
        /// <summary>The JavaScript binding name reserved for the MonkeySharp bridge.</summary>
        public const string BridgeObjectName = "__MonkeySharpBridge";

        private readonly object _sync = new object();
        private readonly UserScriptEngine _engine;
        private readonly UserScriptBridgeGateway _gateway;
        private readonly CefSharpHostOptions _options;
        private readonly IDisposable _ownedStore;
        private readonly RenderObserver _renderObserver;
        private readonly Dictionary<string, FrameSession> _sessions =
            new Dictionary<string, FrameSession>(StringComparer.Ordinal);
        private readonly HashSet<Task> _backgroundTasks = new HashSet<Task>();
        private IUserScriptBridge _bridge;
        private IWebBrowser _browser;
        private RenderProcessMessageHandlerMultiplexer _multiplexer;
        private bool _ownsMultiplexer;
        private CancellationTokenSource _attachmentCancellation;
        private string _browserSessionId;
        private bool _disposed;

        internal CefSharpUserScriptHost(
            UserScriptEngine engine,
            UserScriptBridgeGateway gateway,
            CefSharpHostOptions options,
            IDisposable ownedStore)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _ownedStore = ownedStore;
            _bridge = gateway;
            _renderObserver = new RenderObserver(this);
            _engine.Diagnostic += ForwardDiagnostic;
            _gateway.Diagnostic += ForwardDiagnostic;
            _gateway.Notification += GatewayNotification;
        }

        /// <inheritdoc />
        public event EventHandler<DocumentLifecycleEventArgs> DocumentLifecycle;

        /// <inheritdoc />
        public event EventHandler<BridgeRequestEventArgs> BridgeRequest;

        /// <summary>Occurs when the runtime, bridge, or CefSharp adapter produces a diagnostic.</summary>
        public event EventHandler<UserScriptDiagnostic> Diagnostic;

        /// <summary>Gets the userscript execution engine owned by this host.</summary>
        public UserScriptEngine Engine => _engine;

        /// <summary>Gets the bridge gateway owned by this host.</summary>
        public UserScriptBridgeGateway Gateway => _gateway;

        /// <summary>Gets whether the host is currently attached to a browser.</summary>
        public bool IsAttached
        {
            get { lock (_sync) return _browser != null; }
        }

        /// <summary>Gets the bridge integrity guarantee provided by the configured JavaScript world.</summary>
        public BridgeIntegrityGuarantee BridgeIntegrity => _options.TrustedPageWorld
            ? BridgeIntegrityGuarantee.TrustedPageWorld
            : BridgeIntegrityGuarantee.Unverified;

        /// <summary>Attaches the host before the CefSharp browser is initialized.</summary>
        /// <param name="browser">The uninitialized browser to attach.</param>
        /// <exception cref="InvalidOperationException">The host is already attached or the browser cannot accept the bridge handlers.</exception>
        public void Attach(IWebBrowser browser)
        {
            if (browser == null)
                throw new ArgumentNullException(nameof(browser));
            lock (_sync)
            {
                ThrowIfDisposed();
                if (_browser != null)
                    throw new InvalidOperationException("The host is already attached to a browser.");
            }
            if (browser.IsDisposed)
                throw new ObjectDisposedException(nameof(browser));
            if (browser.IsBrowserInitialized)
                throw new InvalidOperationException("MonkeySharp must be attached before the CefSharp browser is initialized.");
            if (browser.JavascriptObjectRepository.IsBound(BridgeObjectName))
                throw new InvalidOperationException("The MonkeySharp bridge name is already registered.");

            var multiplexer = browser.RenderProcessMessageHandler as RenderProcessMessageHandlerMultiplexer;
            var ownsMultiplexer = false;
            if (multiplexer == null)
            {
                if (browser.RenderProcessMessageHandler != null)
                {
                    throw new InvalidOperationException(
                        "The browser already has a render process message handler. " +
                        "Assign a RenderProcessMessageHandlerMultiplexer before attaching MonkeySharp.");
                }
                multiplexer = new RenderProcessMessageHandlerMultiplexer();
                browser.RenderProcessMessageHandler = multiplexer;
                ownsMultiplexer = true;
            }

            var boundBridge = new BoundBridge(this);
            try
            {
                multiplexer.Add(_renderObserver);
                multiplexer.HandlerFailed += MultiplexerHandlerFailed;
                browser.JavascriptObjectRepository.Register(
                    BridgeObjectName,
                    boundBridge,
                    isAsync: true,
                    options: BindingOptions.DefaultBinder);
                browser.FrameLoadEnd += BrowserFrameLoadEnd;
                lock (_sync)
                {
                    _browser = browser;
                    _multiplexer = multiplexer;
                    _ownsMultiplexer = ownsMultiplexer;
                    _attachmentCancellation = new CancellationTokenSource();
                    _browserSessionId = Guid.NewGuid().ToString("D");
                }
            }
            catch (Exception exception)
            {
                EmitDiagnostic(
                    "MSC200_BRIDGE_REGISTRATION_FAILED",
                    DiagnosticSeverity.Error,
                    "The CefSharp bridge could not be registered.",
                    exception);
                multiplexer.HandlerFailed -= MultiplexerHandlerFailed;
                multiplexer.Remove(_renderObserver);
                if (browser.JavascriptObjectRepository.IsBound(BridgeObjectName))
                    browser.JavascriptObjectRepository.UnRegister(BridgeObjectName);
                if (ownsMultiplexer && ReferenceEquals(browser.RenderProcessMessageHandler, multiplexer))
                    browser.RenderProcessMessageHandler = null;
                throw;
            }
        }

        /// <summary>Detaches the current browser and releases all active document sessions.</summary>
        /// <param name="cancellationToken">A token checked before detachment begins.</param>
        /// <returns>A task that completes after pending background work is drained.</returns>
        public async Task DetachAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IWebBrowser browser;
            RenderProcessMessageHandlerMultiplexer multiplexer;
            bool ownsMultiplexer;
            CancellationTokenSource attachmentCancellation;
            FrameSession[] sessions;
            lock (_sync)
            {
                if (_browser == null)
                    return;
                browser = _browser;
                multiplexer = _multiplexer;
                ownsMultiplexer = _ownsMultiplexer;
                attachmentCancellation = _attachmentCancellation;
                sessions = _sessions.Values.ToArray();
                _sessions.Clear();
                _browser = null;
                _multiplexer = null;
                _ownsMultiplexer = false;
                _attachmentCancellation = null;
                _browserSessionId = null;
            }

            attachmentCancellation.Cancel();
            foreach (var session in sessions)
                session.Cancellation.Cancel();

            browser.FrameLoadEnd -= BrowserFrameLoadEnd;
            multiplexer.HandlerFailed -= MultiplexerHandlerFailed;
            multiplexer.Remove(_renderObserver);
            if (ownsMultiplexer && ReferenceEquals(browser.RenderProcessMessageHandler, multiplexer))
                browser.RenderProcessMessageHandler = null;
            if (!browser.IsDisposed && browser.JavascriptObjectRepository.IsBound(BridgeObjectName))
                browser.JavascriptObjectRepository.UnRegister(BridgeObjectName);

            foreach (var session in sessions)
            {
                try
                {
                    await ReleaseSessionAsync(session, DocumentLifecycleKind.ContextReleased, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    EmitDiagnostic(
                        "MSC102_CONTEXT_RELEASE_FAILED",
                        DiagnosticSeverity.Error,
                        "A CefSharp document session could not be released.",
                        exception,
                        session);
                }
            }
            await DrainBackgroundTasksAsync().ConfigureAwait(false);
            attachmentCancellation.Dispose();
        }

        /// <inheritdoc />
        public Task ExecuteAsync(DocumentFrame frame, string javaScript, CancellationToken cancellationToken)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            if (javaScript == null)
                throw new ArgumentNullException(nameof(javaScript));
            cancellationToken.ThrowIfCancellationRequested();
            FrameSession session;
            lock (_sync)
            {
                ThrowIfDisposed();
                session = _sessions.Values.FirstOrDefault(item => item.Frame.DocumentId == frame.DocumentId);
            }
            if (session == null || session.Cancellation.IsCancellationRequested)
                throw new InvalidOperationException("The target CefSharp document is no longer active.");
            if (!session.CefFrame.IsValid || session.CefFrame.IsDisposed)
                throw new InvalidOperationException("The target CefSharp frame is no longer valid.");

            session.CefFrame.ExecuteJavaScriptAsync(
                javaScript,
                "monkeysharp://runtime/" + frame.DocumentId,
                1);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void RegisterBridge(IUserScriptBridge bridge)
        {
            if (bridge == null)
                throw new ArgumentNullException(nameof(bridge));
            lock (_sync)
            {
                ThrowIfDisposed();
                if (_browser != null)
                    throw new InvalidOperationException("The bridge cannot be replaced while a browser is attached.");
                _bridge = bridge;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
                return;
            DetachAsync(CancellationToken.None).GetAwaiter().GetResult();
            lock (_sync)
            {
                if (_disposed)
                    return;
                _disposed = true;
            }
            _gateway.Notification -= GatewayNotification;
            _gateway.Diagnostic -= ForwardDiagnostic;
            _engine.Diagnostic -= ForwardDiagnostic;
            _gateway.Dispose();
            _engine.Dispose();
            _ownedStore?.Dispose();
        }

        private void ContextCreated(IWebBrowser browser, IBrowser cefBrowser, IFrame cefFrame)
        {
            FrameSession previous;
            FrameSession session;
            lock (_sync)
            {
                if (_browser == null || !ReferenceEquals(_browser, browser))
                    return;
                var frameId = cefFrame.Identifier.ToString(CultureInfo.InvariantCulture);
                _sessions.TryGetValue(frameId, out previous);
                var url = ParseUrl(cefFrame.Url);
                var frame = new DocumentFrame(
                    _browserSessionId,
                    Guid.NewGuid().ToString("D"),
                    frameId,
                    url,
                    cefFrame.IsMain,
                    TimingGuarantee.BestEffortDocumentStart,
                    BridgeIntegrity);
                session = new FrameSession(cefFrame, frame, CreateToken());
                _sessions[frameId] = session;
            }
            if (previous != null)
                previous.Cancellation.Cancel();
            Observe(InitializeSessionAsync(previous, session), "MSC101_CONTEXT_INITIALIZATION_FAILED", session);
        }

        private void ContextReleased(IWebBrowser browser, IBrowser cefBrowser, IFrame cefFrame)
        {
            FrameSession session;
            lock (_sync)
            {
                if (_browser == null || !ReferenceEquals(_browser, browser))
                    return;
                var frameId = cefFrame.Identifier.ToString(CultureInfo.InvariantCulture);
                if (!_sessions.TryGetValue(frameId, out session))
                    return;
                _sessions.Remove(frameId);
            }
            session.Cancellation.Cancel();
            Observe(
                ReleaseSessionAsync(session, DocumentLifecycleKind.ContextReleased, CancellationToken.None),
                "MSC102_CONTEXT_RELEASE_FAILED",
                session);
        }

        private void UncaughtException(IWebBrowser browser, IBrowser cefBrowser, IFrame cefFrame, JavascriptException exception)
        {
            FrameSession session;
            lock (_sync)
                _sessions.TryGetValue(cefFrame.Identifier.ToString(CultureInfo.InvariantCulture), out session);
            EmitDiagnostic(
                "MSC300_JAVASCRIPT_EXCEPTION",
                DiagnosticSeverity.Error,
                exception?.Message ?? "An uncaught JavaScript exception occurred.",
                null,
                session,
                Guid.NewGuid().ToString("D"));
        }

        private async Task InitializeSessionAsync(FrameSession previous, FrameSession session)
        {
            if (previous != null)
                await ReleaseSessionAsync(previous, DocumentLifecycleKind.NavigationSuperseded, CancellationToken.None)
                    .ConfigureAwait(false);
            await session.OperationGate.WaitAsync(session.Cancellation.Token).ConfigureAwait(false);
            try
            {
                var contextPlan = await ProcessLifecycleAsync(
                    DocumentLifecycleKind.ContextCreated,
                    session,
                    session.Cancellation.Token).ConfigureAwait(false);
                var startPlan = await ProcessLifecycleAsync(
                    DocumentLifecycleKind.DocumentStart,
                    session,
                    session.Cancellation.Token).ConfigureAwait(false);
                var script = new StringBuilder(LifecycleScriptBuilder.Build(
                    session.Frame.DocumentId,
                    session.Frame.FrameId,
                    session.LifecycleToken));
                if (startPlan.Invocations.Count != 0)
                    script.AppendLine().Append(BridgeScriptBuilder.BuildInjection(startPlan));
                await ExecuteAsync(session.Frame, script.ToString(), session.Cancellation.Token).ConfigureAwait(false);
            }
            finally
            {
                session.OperationGate.Release();
            }
        }

        private async Task ReleaseSessionAsync(
            FrameSession session,
            DocumentLifecycleKind kind,
            CancellationToken cancellationToken)
        {
            await session.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ProcessLifecycleAsync(kind, session, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                session.OperationGate.Release();
                session.Dispose();
            }
        }

        private async Task ProcessThroughAsync(FrameSession session, int requestedRank)
        {
            await session.OperationGate.WaitAsync(session.Cancellation.Token).ConfigureAwait(false);
            try
            {
                while (session.CompletedRank < requestedRank)
                {
                    session.CompletedRank++;
                    var kind = session.CompletedRank == 1
                        ? DocumentLifecycleKind.BodyAvailable
                        : session.CompletedRank == 2
                            ? DocumentLifecycleKind.DomContentLoaded
                            : DocumentLifecycleKind.Load;
                    var plan = await ProcessLifecycleAsync(kind, session, session.Cancellation.Token)
                        .ConfigureAwait(false);
                    if (plan.Invocations.Count != 0)
                    {
                        await ExecuteAsync(
                            session.Frame,
                            BridgeScriptBuilder.BuildInjection(plan),
                            session.Cancellation.Token).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                session.OperationGate.Release();
            }
        }

        private async Task<InjectionPlan> ProcessLifecycleAsync(
            DocumentLifecycleKind kind,
            FrameSession session,
            CancellationToken cancellationToken)
        {
            RaiseDocumentLifecycle(new DocumentLifecycleEventArgs(kind, session.Frame), session);
            return await _engine.ProcessLifecycleAsync(
                new DocumentLifecycleEventArgs(kind, session.Frame),
                cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> DispatchFromPageAsync(string requestJson)
        {
            IUserScriptBridge bridge;
            CancellationToken cancellationToken;
            DocumentFrame frame = null;
            lock (_sync)
            {
                bridge = _bridge;
                cancellationToken = _attachmentCancellation?.Token ?? new CancellationToken(true);
                var documentId = TryReadDocumentId(requestJson);
                frame = _sessions.Values.FirstOrDefault(item => item.Frame.DocumentId == documentId)?.Frame;
            }
            RaiseBridgeRequest(new BridgeRequestEventArgs(frame, requestJson));
            return await bridge.DispatchAsync(requestJson, cancellationToken).ConfigureAwait(false);
        }

        private async Task<bool> LifecycleFromPageAsync(string requestJson)
        {
            if (requestJson == null || Encoding.UTF8.GetByteCount(requestJson) > 64 * 1024)
                return false;
            try
            {
                using (var document = JsonDocument.Parse(requestJson))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object ||
                        !root.TryGetProperty("protocol", out var protocol) || protocol.GetInt32() != 1 ||
                        !root.TryGetProperty("documentId", out var documentIdValue) || documentIdValue.ValueKind != JsonValueKind.String ||
                        !root.TryGetProperty("frameId", out var frameIdValue) || frameIdValue.ValueKind != JsonValueKind.String ||
                        !root.TryGetProperty("token", out var tokenValue) || tokenValue.ValueKind != JsonValueKind.String ||
                        !root.TryGetProperty("stage", out var stageValue) || stageValue.ValueKind != JsonValueKind.String)
                        return false;
                    var documentId = documentIdValue.GetString();
                    var frameId = frameIdValue.GetString();
                    var token = tokenValue.GetString();
                    var stage = stageValue.GetString();
                    FrameSession session;
                    lock (_sync)
                    {
                        if (!_sessions.TryGetValue(frameId, out session) ||
                            session.Frame.DocumentId != documentId ||
                            !FixedTimeEquals(session.LifecycleToken, token))
                            return false;
                    }
                    var rank = stage == "body" ? 1 : stage == "dom-content-loaded" ? 2 : stage == "load" ? 3 : 0;
                    if (rank == 0)
                        return false;
                    await ProcessThroughAsync(session, rank).ConfigureAwait(false);
                    return true;
                }
            }
            catch (JsonException)
            {
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        private void BrowserFrameLoadEnd(object sender, FrameLoadEndEventArgs args)
        {
            FrameSession session;
            lock (_sync)
            {
                if (!_sessions.TryGetValue(
                    args.Frame.Identifier.ToString(CultureInfo.InvariantCulture), out session))
                    return;
            }
            Observe(ProcessThroughAsync(session, 3), "MSC103_LOAD_FALLBACK_FAILED", session);
        }

        private void GatewayNotification(object sender, BridgeNotificationEventArgs notification)
        {
            var session = FindSession(notification.Frame.DocumentId);
            try
            {
                Observe(
                    ExecuteAsync(
                        notification.Frame,
                        BridgeScriptBuilder.BuildNotification(notification),
                        GetAttachmentToken()),
                    "MSC201_NOTIFICATION_DELIVERY_FAILED",
                    session);
            }
            catch (Exception exception)
            {
                EmitDiagnostic(
                    "MSC201_NOTIFICATION_DELIVERY_FAILED",
                    DiagnosticSeverity.Error,
                    "A bridge notification could not be delivered.",
                    exception,
                    session);
            }
        }

        private void MultiplexerHandlerFailed(object sender, RenderHandlerFailedEventArgs args)
        {
            EmitDiagnostic(
                "MSC500_RENDER_HANDLER_FAILED",
                DiagnosticSeverity.Error,
                "A render process message handler failed.",
                args.Exception);
        }

        private void ForwardDiagnostic(object sender, UserScriptDiagnostic diagnostic)
        {
            RaiseDiagnostic(diagnostic);
        }

        private void RaiseDocumentLifecycle(DocumentLifecycleEventArgs args, FrameSession session)
        {
            var handlers = DocumentLifecycle;
            if (handlers == null)
                return;
            foreach (EventHandler<DocumentLifecycleEventArgs> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, args);
                }
                catch (Exception exception)
                {
                    EmitDiagnostic(
                        "MSC400_LIFECYCLE_OBSERVER_FAILED",
                        DiagnosticSeverity.Error,
                        "A document lifecycle observer failed.",
                        exception,
                        session);
                }
            }
        }

        private void Observe(Task task, string code, FrameSession session)
        {
            lock (_sync)
                _backgroundTasks.Add(task);
            task.ContinueWith(completed =>
            {
                lock (_sync)
                    _backgroundTasks.Remove(completed);
                if (completed.IsFaulted)
                {
                    EmitDiagnostic(
                        code,
                        DiagnosticSeverity.Error,
                        "A CefSharp adapter operation failed.",
                        completed.Exception?.GetBaseException(),
                        session);
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private async Task DrainBackgroundTasksAsync()
        {
            while (true)
            {
                Task[] tasks;
                lock (_sync)
                    tasks = _backgroundTasks.ToArray();
                if (tasks.Length == 0)
                    return;
                try
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    System.Diagnostics.Trace.TraceError(
                        "MonkeySharp background operation failed after diagnostics were emitted: {0}",
                        exception);
                }
            }
        }

        private void RaiseBridgeRequest(BridgeRequestEventArgs args)
        {
            var handlers = BridgeRequest;
            if (handlers == null)
                return;
            foreach (EventHandler<BridgeRequestEventArgs> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, args);
                }
                catch (Exception exception)
                {
                    EmitDiagnostic(
                        "MSC401_BRIDGE_OBSERVER_FAILED",
                        DiagnosticSeverity.Error,
                        "A bridge request observer failed.",
                        exception,
                        FindSession(args.Frame?.DocumentId));
                }
            }
        }

        private void RaiseDiagnostic(UserScriptDiagnostic diagnostic)
        {
            var handlers = Diagnostic;
            if (handlers == null)
                return;
            foreach (EventHandler<UserScriptDiagnostic> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, diagnostic);
                }
                catch (Exception exception)
                {
                    System.Diagnostics.Trace.TraceError(
                        "MonkeySharp diagnostic observer failed: {0}",
                        exception);
                }
            }
        }

        private void EmitDiagnostic(
            string code,
            DiagnosticSeverity severity,
            string message,
            Exception exception = null,
            FrameSession session = null,
            string correlationId = null)
        {
            RaiseDiagnostic(new UserScriptDiagnostic(
                code,
                severity,
                message,
                exception,
                documentId: session?.Frame.DocumentId,
                frameId: session?.Frame.FrameId,
                requestId: correlationId));
        }

        private FrameSession FindSession(string documentId)
        {
            lock (_sync)
                return _sessions.Values.FirstOrDefault(item => item.Frame.DocumentId == documentId);
        }

        private CancellationToken GetAttachmentToken()
        {
            lock (_sync)
                return _attachmentCancellation?.Token ?? new CancellationToken(true);
        }

        private static Uri ParseUrl(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : new Uri("about:blank");
        }

        private static string TryReadDocumentId(string requestJson)
        {
            try
            {
                using (var document = JsonDocument.Parse(requestJson))
                    return document.RootElement.TryGetProperty("documentId", out var value) && value.ValueKind == JsonValueKind.String
                        ? value.GetString()
                        : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string CreateToken()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            var difference = 0;
            for (var index = 0; index < left.Length; index++)
                difference |= left[index] ^ right[index];
            return difference == 0;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CefSharpUserScriptHost));
        }

        private sealed class BoundBridge
        {
            private readonly CefSharpUserScriptHost _host;

            public BoundBridge(CefSharpUserScriptHost host)
            {
                _host = host;
            }

            public Task<string> Dispatch(string requestJson)
            {
                return _host.DispatchFromPageAsync(requestJson);
            }

            public Task<bool> Lifecycle(string requestJson)
            {
                return _host.LifecycleFromPageAsync(requestJson);
            }
        }

        private sealed class RenderObserver : IRenderProcessMessageHandler
        {
            private readonly CefSharpUserScriptHost _host;

            public RenderObserver(CefSharpUserScriptHost host)
            {
                _host = host;
            }

            public void OnContextCreated(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame)
            {
                _host.ContextCreated(chromiumWebBrowser, browser, frame);
            }

            public void OnContextReleased(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame)
            {
                _host.ContextReleased(chromiumWebBrowser, browser, frame);
            }

            public void OnFocusedNodeChanged(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IDomNode node)
            {
            }

            public void OnUncaughtException(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, JavascriptException exception)
            {
                _host.UncaughtException(chromiumWebBrowser, browser, frame, exception);
            }
        }

        private sealed class FrameSession : IDisposable
        {
            public FrameSession(IFrame cefFrame, DocumentFrame frame, string lifecycleToken)
            {
                CefFrame = cefFrame;
                Frame = frame;
                LifecycleToken = lifecycleToken;
            }

            public IFrame CefFrame { get; }
            public DocumentFrame Frame { get; }
            public string LifecycleToken { get; }
            public CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();
            public SemaphoreSlim OperationGate { get; } = new SemaphoreSlim(1, 1);
            public int CompletedRank { get; set; }

            public void Dispose()
            {
                Cancellation.Dispose();
                OperationGate.Dispose();
            }
        }
    }
}
