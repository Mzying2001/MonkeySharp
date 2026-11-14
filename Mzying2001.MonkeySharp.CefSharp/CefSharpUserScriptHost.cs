using CefSharp;
using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Bridge;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Runtime;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Core.Permissions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;

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
        private readonly CefSharpRequestContextAccessor _requestContextAccessor;
        private readonly IReadOnlyList<IDisposable> _ownedServices;
        private readonly IWebRequestService _webRequestService;
        private readonly RenderObserver _renderObserver;
        private readonly Dictionary<string, FrameSession> _sessions =
            new Dictionary<string, FrameSession>(StringComparer.Ordinal);
        // Retain only generation numbers after release so in-flight creation
        // probes can distinguish a released owner from a superseding document.
        private readonly Dictionary<string, long> _contextGenerations = new Dictionary<string, long>(StringComparer.Ordinal);
        private long _contextGeneration;
        private readonly HashSet<Task> _backgroundTasks = new HashSet<Task>();
        private IUserScriptBridge _bridge;
        private IWebBrowser _browser;
        private RenderProcessMessageHandlerMultiplexer _multiplexer;
        private ContextMenuHandler _contextMenuHandler;
        private CefSharpWebRequestHandlerMultiplexer _requestMultiplexer;
        private CefSharpWebRequestHandler _webRequestHandler;
        private bool _ownsRequestMultiplexer;
        private bool _ownsMultiplexer;
        private CancellationTokenSource _attachmentCancellation;
        private string _browserSessionId;
        private bool _disposed;
        private long _attachmentGeneration;
        private Task<CefSharpCdpSession> _cdpPreparation;
        private readonly IUserScriptRepository _repository;
        private CefSharpIsolatedExecutor _isolated;
        private Task _isolatedPreparation;
        private string _rawDocumentMarker;
        private StaticWebRequestRules _staticWebRequests;
        private Task _staticWebRequestPreparation;
        private readonly IUserScriptPermissionPolicy _permissionPolicy;

        /// <summary>Gets whether preparation began after the browser was initialized.</summary>
        public bool IsLatePreparation { get; private set; }

        /// <summary>Gets whether attachment occurred after native browser initialization.</summary>
        public bool IsLateAttach { get; private set; }

        /// <summary>Prepares the browser-scoped protocol session before business navigation.</summary>
        public async Task PrepareAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            Task rules;
            lock (_sync) rules = _staticWebRequestPreparation;
            if (rules != null) await rules.ConfigureAwait(false);
            await GetCdpSessionAsync(cancellationToken).ConfigureAwait(false);
            await InitializeIsolatedIfNeededAsync().ConfigureAwait(false);
        }

        /// <summary>Waits for preparation, then navigates. This does not guarantee userscript document-start timing.</summary>
        public async Task NavigateAsync(string url, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out _)) throw new ArgumentException("An absolute URL is required.", nameof(url));
            await PrepareAsync(cancellationToken).ConfigureAwait(false);
            var session = await GetCdpSessionAsync(cancellationToken).ConfigureAwait(false);
            var result = await session.ExecuteAsync("Page.navigate", new { url }, cancellationToken).ConfigureAwait(false);
            if (result.TryGetProperty("errorText", out var error)) throw new InvalidOperationException(error.GetString());
        }

        internal Task<CefSharpCdpSession> GetCdpSessionAsync(CancellationToken cancellationToken)
        {
            Task<CefSharpCdpSession> preparation;
            lock (_sync)
            {
                ThrowIfDisposed();
                if (_browser == null) throw new InvalidOperationException("No browser is attached.");
                if (_cdpPreparation != null && (_cdpPreparation.IsFaulted || _cdpPreparation.IsCanceled)) _cdpPreparation = null;
                if (_cdpPreparation == null)
                {
                    IsLatePreparation = _browser.IsBrowserInitialized;
                    _cdpPreparation = PrepareCdpAsync(_browser, _attachmentCancellation.Token);
                }
                preparation = _cdpPreparation;
            }
            return AwaitPreparationAsync(preparation, cancellationToken);
        }

        private static async Task<CefSharpCdpSession> AwaitPreparationAsync(Task<CefSharpCdpSession> preparation, CancellationToken cancellationToken)
        {
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => canceled.TrySetCanceled()))
            {
                await await Task.WhenAny(preparation, canceled.Task).ConfigureAwait(false);
                return await preparation.ConfigureAwait(false);
            }
        }

        private void RepositoryChanged(object sender, UserScriptRepositoryChangedEventArgs args)
        {
            lock (_sync) if (_browser == null || _disposed) return;
            Observe(InitializeIsolatedIfNeededAsync(), "MSC230_ISOLATED_PREPARATION_FAILED", null);
        }

        private async Task InitializeIsolatedIfNeededAsync()
        {
            var scripts = await _repository.GetSnapshotAsync(CancellationToken.None).ConfigureAwait(false);
            if (!scripts.Any(script => script.IsEnabled && UserScriptEngine.RequestedWorld(script.Definition.Metadata) == "isolated")) return;
            var session = await GetCdpSessionAsync(GetAttachmentToken()).ConfigureAwait(false);
            Task preparation;
            lock (_sync)
            {
                if (_browser == null || _disposed) return;
                if (_isolatedPreparation == null)
                {
                    _isolated = new CefSharpIsolatedExecutor(session, _repository, _engine, _gateway, _browserSessionId,
                        () => _options.IsIncognito?.Invoke(_requestContextAccessor.Current), _options.Bridge,
                        diagnostic => Diagnostic?.Invoke(this, diagnostic), args => RaiseDocumentLifecycle(args, null), _attachmentCancellation.Token);
                    foreach (var frame in _sessions.Values)
                    {
                        using (var current = GetFrameByIdentifier(_browser.GetBrowser(), frame.Frame.FrameId))
                            if (current != null && !current.IsDisposed) InstallDocumentMarker(frame, current);
                    }
                    _isolatedPreparation = _isolated.InitializeAsync();
                }
                preparation = _isolatedPreparation;
            }
            await preparation.ConfigureAwait(false);
        }

        private static async Task<CefSharpCdpSession> PrepareCdpAsync(IWebBrowser browser, CancellationToken cancellationToken)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(15));
                while (!browser.IsBrowserInitialized)
                {
                    if (browser.IsDisposed) throw new ObjectDisposedException(nameof(browser));
                    await Task.Delay(10, deadline.Token).ConfigureAwait(false);
                }
                deadline.Token.ThrowIfCancellationRequested();
                var session = new CefSharpCdpSession(browser.GetBrowser().GetHost());
                try
                {
                    await session.InitializeAsync().ConfigureAwait(false);
                    await session.ExecuteAsync("Browser.getVersion", cancellationToken: deadline.Token).ConfigureAwait(false);
                    return session;
                }
                catch { session.Dispose(); throw; }
            }
        }

        internal CefSharpUserScriptHost(
            UserScriptEngine engine,
            UserScriptBridgeGateway gateway,
            CefSharpHostOptions options,
            IDisposable ownedStore,
            IWebRequestService webRequestService = null,
            CefSharpRequestContextAccessor requestContextAccessor = null,
            IReadOnlyList<IDisposable> ownedServices = null, IUserScriptRepository repository = null, IUserScriptPermissionPolicy permissionPolicy = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _ownedStore = ownedStore;
            _requestContextAccessor = requestContextAccessor ?? new CefSharpRequestContextAccessor();
            _ownedServices = ownedServices ?? new List<IDisposable>();
            _webRequestService = webRequestService;
            _permissionPolicy = permissionPolicy ?? new AllowDeclaredPermissionsPolicy();
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _repository.Changed += RepositoryChanged;
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

        internal IRequestContext CurrentRequestContext => _requestContextAccessor.Current;

        /// <summary>Gets whether the host is currently attached to a browser.</summary>
        public bool IsAttached
        {
            get { lock (_sync) return _browser != null; }
        }

        /// <summary>Gets the bridge integrity guarantee provided by the configured JavaScript world.</summary>
        public BridgeIntegrityGuarantee BridgeIntegrity => _options.TrustedPageWorld
            ? BridgeIntegrityGuarantee.TrustedPageWorld
            : BridgeIntegrityGuarantee.Unverified;

        /// <summary>Attaches the host. Attach before initialization to observe the first document.</summary>
        /// <param name="browser">The browser to attach; initialized browsers are marked as late attachments.</param>
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
            EnsureCompatibleCefSharp();
            var lateAttach = browser.IsBrowserInitialized;
            if (browser.JavascriptObjectRepository.IsBound(BridgeObjectName))
                throw new InvalidOperationException("The MonkeySharp bridge name is already registered.");
            var contextMenuProperty = GetContextMenuHandlerProperty(browser);
            var existingContextMenuHandler = contextMenuProperty == null
                ? null
                : contextMenuProperty.GetValue(browser, null) as IContextMenuHandler;
            if (browser.RenderProcessMessageHandler != null &&
                !(browser.RenderProcessMessageHandler is RenderProcessMessageHandlerMultiplexer))
            {
                throw new InvalidOperationException(
                    "The browser already has a render process message handler. " +
                    "Assign a RenderProcessMessageHandlerMultiplexer before attaching MonkeySharp.");
            }
            if (_webRequestService != null && browser.RequestHandler != null &&
                !(browser.RequestHandler is CefSharpWebRequestHandlerMultiplexer))
            {
                throw new InvalidOperationException(
                    "The browser already has a request handler. " +
                    "Assign a CefSharpWebRequestHandlerMultiplexer before attaching MonkeySharp.");
            }

            var multiplexer = browser.RenderProcessMessageHandler as RenderProcessMessageHandlerMultiplexer;
            var ownsMultiplexer = false;
            if (multiplexer == null)
            {
                multiplexer = new RenderProcessMessageHandlerMultiplexer();
                browser.RenderProcessMessageHandler = multiplexer;
                ownsMultiplexer = true;
            }

            CefSharpWebRequestHandlerMultiplexer requestMultiplexer = null;
            CefSharpWebRequestHandler webRequestHandler = null;
            var ownsRequestMultiplexer = false;
            if (_webRequestService != null)
            {
                webRequestHandler = new CefSharpWebRequestHandler(_webRequestService, RequestFrame);
                requestMultiplexer = browser.RequestHandler as CefSharpWebRequestHandlerMultiplexer;
                if (requestMultiplexer == null)
                {
                    requestMultiplexer = new CefSharpWebRequestHandlerMultiplexer();
                    browser.RequestHandler = requestMultiplexer;
                    ownsRequestMultiplexer = true;
                }
                requestMultiplexer.Add(webRequestHandler);
            }

            var boundBridge = new BoundBridge(this);
            var requestContext = browser.RequestContext;
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
                _contextMenuHandler = new ContextMenuHandler(this, existingContextMenuHandler);
                if (contextMenuProperty != null)
                    contextMenuProperty.SetValue(browser, _contextMenuHandler, null);
                lock (_sync)
                {
                    _browser = browser;
                    IsLateAttach = lateAttach;
                    IsLatePreparation = false;
                    _multiplexer = multiplexer;
                    _ownsMultiplexer = ownsMultiplexer;
                    _requestMultiplexer = requestMultiplexer;
                    _webRequestHandler = webRequestHandler;
                    _ownsRequestMultiplexer = ownsRequestMultiplexer;
                    _attachmentCancellation = new CancellationTokenSource();
                    _browserSessionId = Guid.NewGuid().ToString("D");
                    _rawDocumentMarker = "__MonkeySharpDocument_" + Guid.NewGuid().ToString("N");
                    _requestContextAccessor.Current = requestContext;
                    _requestContextAccessor.IsAttached = true;
                    _attachmentGeneration++;
                    if (_webRequestService != null)
                    {
                        _staticWebRequests = new StaticWebRequestRules(_repository, _webRequestService, _permissionPolicy,
                            _browserSessionId, () => _options.IsIncognito?.Invoke(_requestContextAccessor.Current));
                        _staticWebRequestPreparation = _staticWebRequests.InitializeAsync(_attachmentCancellation.Token);
                    }
                }
                browser.JavascriptMessageReceived += BrowserJavascriptMessageReceived;
                Observe(InitializeIsolatedIfNeededAsync(), "MSC230_ISOLATED_PREPARATION_FAILED", null);
            }
            catch (Exception exception)
            {
                webRequestHandler?.Dispose();
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
                if (contextMenuProperty != null && ReferenceEquals(contextMenuProperty.GetValue(browser, null), _contextMenuHandler))
                    contextMenuProperty.SetValue(browser, existingContextMenuHandler, null);
                if (requestMultiplexer != null)
                {
                    requestMultiplexer.Remove(webRequestHandler);
                    if (ownsRequestMultiplexer && ReferenceEquals(browser.RequestHandler, requestMultiplexer))
                        browser.RequestHandler = null;
                }
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
            CefSharpWebRequestHandlerMultiplexer requestMultiplexer;
            CefSharpWebRequestHandler webRequestHandler;
            bool ownsRequestMultiplexer;
            bool ownsMultiplexer;
            CancellationTokenSource attachmentCancellation;
            ContextMenuHandler contextMenuHandler;
            FrameSession[] sessions;
            Task<CefSharpCdpSession> cdpPreparation;
            long attachmentGeneration;
            CefSharpIsolatedExecutor isolated;
            Task isolatedPreparation;
            lock (_sync)
            {
                if (_browser == null)
                    return;
                browser = _browser;
                multiplexer = _multiplexer;
                requestMultiplexer = _requestMultiplexer;
                webRequestHandler = _webRequestHandler;
                ownsRequestMultiplexer = _ownsRequestMultiplexer;
                ownsMultiplexer = _ownsMultiplexer;
                attachmentCancellation = _attachmentCancellation;
                contextMenuHandler = _contextMenuHandler;
                attachmentGeneration = _attachmentGeneration;
                sessions = _sessions.Values.ToArray();
                isolated = _isolated;
                isolatedPreparation = _isolatedPreparation;
                _isolated = null;
                _isolatedPreparation = null;
                cdpPreparation = _cdpPreparation;
                _cdpPreparation = null;
                _staticWebRequests?.Dispose();
                _staticWebRequests = null;
                _staticWebRequestPreparation = null;
                _sessions.Clear();
                _contextGenerations.Clear();
                _browser = null;
                _multiplexer = null;
                _requestMultiplexer = null;
                _webRequestHandler = null;
                _ownsRequestMultiplexer = false;
                _ownsMultiplexer = false;
                _attachmentCancellation = null;
                _contextMenuHandler = null;
                _browserSessionId = null;
            }

            foreach (var session in sessions)
            {
                try
                {
                    using (var frame = GetFrameByIdentifier(browser.GetBrowser(), session.Frame.FrameId))
                        if (frame != null && !frame.IsDisposed && frame.IsValid)
                            frame.ExecuteJavaScriptAsync("if(globalThis.__MonkeySharpRuntime) globalThis.__MonkeySharpRuntime.endDocument(" +
                                JsonSerializer.Serialize(session.Frame.DocumentId) + ");if(globalThis.__MonkeySharpLifecycle&&globalThis.__MonkeySharpLifecycle.documentId===" +
                                JsonSerializer.Serialize(session.Frame.DocumentId) + ")globalThis.__MonkeySharpLifecycle.dispose();", "monkeysharp://end-document", 1);
                }
                catch (Exception exception) { EmitDiagnostic("MSC103_RENDERER_RELEASE_FAILED", DiagnosticSeverity.Warning, "The renderer execution could not be released during detach.", exception, session); }
            }
            if (isolated != null)
            {
                if (isolatedPreparation != null && !isolatedPreparation.IsCompleted) attachmentCancellation.Cancel();
                try { if (isolatedPreparation != null) await isolatedPreparation.ConfigureAwait(false); } catch (Exception) { }
                await isolated.DisposeAsync().ConfigureAwait(false);
            }
            webRequestHandler?.Dispose();
            attachmentCancellation.Cancel();
            if (cdpPreparation != null)
            {
                try { (await cdpPreparation.ConfigureAwait(false)).Dispose(); }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
                catch (Exception error) { EmitDiagnostic("MSC220_CDP_PREPARATION_FAILED", DiagnosticSeverity.Warning, "CDP preparation failed during detach.", error); }
            }
            foreach (var session in sessions)
                session.Cancel();

            browser.FrameLoadEnd -= BrowserFrameLoadEnd;
            browser.JavascriptMessageReceived -= BrowserJavascriptMessageReceived;
            var contextMenuProperty = GetContextMenuHandlerProperty(browser);
            if (contextMenuProperty != null && ReferenceEquals(contextMenuProperty.GetValue(browser, null), contextMenuHandler))
                contextMenuProperty.SetValue(browser, contextMenuHandler.Previous, null);
            multiplexer.HandlerFailed -= MultiplexerHandlerFailed;
            multiplexer.Remove(_renderObserver);
            if (ownsMultiplexer && ReferenceEquals(browser.RenderProcessMessageHandler, multiplexer))
                browser.RenderProcessMessageHandler = null;
            if (requestMultiplexer != null)
            {
                requestMultiplexer.Remove(webRequestHandler);
                if (ownsRequestMultiplexer && ReferenceEquals(browser.RequestHandler, requestMultiplexer))
                    browser.RequestHandler = null;
            }
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
            lock (_sync)
            {
                if (_attachmentGeneration == attachmentGeneration)
                {
                    _requestContextAccessor.Current = null;
                    _requestContextAccessor.IsAttached = false;
                }
            }
        }

        /// <inheritdoc />
        public Task ExecuteAsync(DocumentFrame frame, string javaScript, CancellationToken cancellationToken)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            if (javaScript == null)
                throw new ArgumentNullException(nameof(javaScript));
            cancellationToken.ThrowIfCancellationRequested();
            if (frame.ExecutionWorld == "isolated")
            {
                CefSharpIsolatedExecutor executor;
                lock (_sync) executor = _isolated;
                if (executor == null) throw new InvalidOperationException("The isolated executor is unavailable.");
                return executor.ExecuteAsync(frame, javaScript);
            }
            FrameSession session;
            lock (_sync)
            {
                ThrowIfDisposed();
                session = _sessions.Values.FirstOrDefault(item => item.Frame.DocumentId == frame.DocumentId);
            }
            if (session == null || session.Cancellation.IsCancellationRequested)
                throw new InvalidOperationException("The target CefSharp document is no longer active.");

            // CefSharp invalidates callback-owned IFrame wrappers after the
            // renderer callback returns. Resolve a fresh wrapper for delayed
            // lifecycle injections; mock hosts may not implement GetFrame, so
            // retain the callback wrapper as a compatibility fallback.
            IFrame targetFrame = session.CefFrame;
            IFrame refreshedFrame = null;
            try
            {
                IWebBrowser browser;
                lock (_sync)
                    browser = _browser;
                if (browser != null)
                {
                    var cefBrowser = browser.GetBrowser();
                    refreshedFrame = GetFrameByIdentifier(cefBrowser, frame.FrameId);
                    if (refreshedFrame != null)
                        targetFrame = refreshedFrame;
                }
                if (targetFrame.IsDisposed || !targetFrame.IsValid)
                    throw new OperationCanceledException("The target CefSharp frame is no longer valid.", cancellationToken);

                targetFrame.ExecuteJavaScriptAsync(
                    javaScript,
                    "monkeysharp://runtime/" + frame.DocumentId,
                    1);
            }
            catch (ObjectDisposedException exception) when (targetFrame.IsDisposed)
            {
                // A native wrapper can retire after the validity check, including
                // while IsValid itself is being read. Never dispatch to a successor.
                throw new OperationCanceledException("The target CefSharp frame was retired.", exception, cancellationToken);
            }
            finally
            {
                if (refreshedFrame != null)
                    refreshedFrame.Dispose();
            }
            return Task.CompletedTask;
        }

        // IBrowser.GetFrame(long) was renamed to GetFrameByIdentifier(string)
        // in newer CefSharp releases. Resolve either shape at runtime so the
        // adapter can be compiled against the host application's CefSharp.
        internal static IFrame GetFrameByIdentifier(IBrowser browser, string frameId)
        {
            if (browser == null || frameId == null)
                return null;

            var byString = typeof(IBrowser).GetMethod(
                "GetFrameByIdentifier",
                new[] { typeof(string) });
            if (byString != null)
                return byString.Invoke(browser, new object[] { frameId }) as IFrame;

            if (long.TryParse(frameId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var identifier))
            {
                var byLong = typeof(IBrowser).GetMethod(
                    "GetFrame",
                    new[] { typeof(long) });
                if (byLong != null)
                    return byLong.Invoke(browser, new object[] { identifier }) as IFrame;
            }

            // Some transitional releases expose only the string overload
            // under the original GetFrame name.
            var legacyByString = typeof(IBrowser).GetMethod(
                "GetFrame",
                new[] { typeof(string) });
            return legacyByString?.Invoke(browser, new object[] { frameId }) as IFrame;
        }

        private static void EnsureCompatibleCefSharp()
        {
            var identifier = typeof(IFrame).GetProperty("Identifier");
            if (identifier == null ||
                (identifier.PropertyType != typeof(long) && identifier.PropertyType != typeof(string)))
            {
                throw new NotSupportedException(
                    "The loaded CefSharp version exposes an unsupported IFrame.Identifier type. " +
                    "Build MonkeySharp against the CefSharp version used by the host.");
            }

            var browser = typeof(IBrowser);
            var hasFrameLookup = browser.GetMethod("GetFrameByIdentifier", new[] { typeof(string) }) != null ||
                browser.GetMethod("GetFrame", new[] { typeof(string) }) != null ||
                browser.GetMethod("GetFrame", new[] { typeof(long) }) != null;
            if (!hasFrameLookup)
            {
                throw new NotSupportedException(
                    "The loaded CefSharp version does not expose a supported frame lookup API. " +
                    "Build MonkeySharp against the CefSharp version used by the host.");
            }
        }

        // IFrame.Identifier changed from Int64 to String in newer CefSharp
        // releases. Reading it through reflection keeps lifecycle code source
        // compatible when the adapter is compiled against either interface shape.
        private static string GetFrameIdentifier(IFrame frame)
        {
            if (frame == null)
                return string.Empty;
            var property = typeof(IFrame).GetProperty("Identifier");
            var value = property?.GetValue(frame, null);
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
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
            _repository.Changed -= RepositoryChanged;
            _gateway.Notification -= GatewayNotification;
            _gateway.Diagnostic -= ForwardDiagnostic;
            _engine.Diagnostic -= ForwardDiagnostic;
            _gateway.Dispose();
            _engine.Dispose();
            for (var index = _ownedServices.Count - 1; index >= 0; index--)
                _ownedServices[index].Dispose();
            _ownedStore?.Dispose();
        }

        internal IBrowserHost GetAudioBrowserHost()
        {
            lock (_sync) return _browser == null || !_browser.IsBrowserInitialized || _browser.IsDisposed ? null : _browser.GetBrowser()?.GetHost();
        }

        private DocumentFrame RequestFrame(IFrame frame, IRequest request)
        {
            try
            {
                if (frame == null || frame.IsDisposed || !frame.IsValid) return null;
                lock (_sync)
                {
                    if (_browser == null) return null;
                    var id = GetFrameIdentifier(frame);
                    var navigation = request.ResourceType.ToString() == "SubFrame" || request.ResourceType.ToString() == "MainFrame";
                    if (!navigation && _sessions.TryGetValue(id, out var session) && session.Frame.Url.AbsoluteUri == frame.Url) return session.Frame;
                    if (!Uri.TryCreate(navigation ? request.Url : frame.Url, UriKind.Absolute, out var url)) return null;
                    return new DocumentFrame(_browserSessionId, "navigation:" + id, id, url, frame.IsMain,
                        TimingGuarantee.BestEffortDocumentStart, BridgeIntegrityGuarantee.Verified);
                }
            }
            // Native wrappers can become invalid between IsValid and Identifier.
            // Unknown ownership cannot inherit another document's rules.
            catch (Exception) { return null; }
        }

        private void ContextCreated(IWebBrowser browser, IBrowser cefBrowser, IFrame cefFrame, bool probeDocument = true, bool recoverMainFrame = false)
        {
            FrameSession session;
            lock (_sync)
            {
                if (_browser == null || !ReferenceEquals(_browser, browser))
                    return;
                var frameId = GetFrameIdentifier(cefFrame);
                _sessions.TryGetValue(frameId, out var previous);
                if (probeDocument && _isolated != null && previous != null && previous.HasDocumentMarker)
                {
                    var probe = cefFrame.EvaluateScriptAsync("globalThis[" + JsonSerializer.Serialize(_rawDocumentMarker) + "]", timeout: TimeSpan.FromSeconds(2));
                    Observe(ProbeContextAsync(browser, previous, probe, false), "MSC105_CONTEXT_IDENTITY_FAILED", previous);
                    return;
                }
                var url = ParseUrl(cefFrame.Url);
                var frame = new DocumentFrame(
                    _browserSessionId,
                    Guid.NewGuid().ToString("D"),
                    frameId,
                    url,
                    cefFrame.IsMain,
                    TimingGuarantee.BestEffortDocumentStart,
                    BridgeIntegrity);
                session = new FrameSession(cefFrame, frame, CreateToken()) { AttachmentGeneration = _attachmentGeneration };
                // A cross-process navigation can change the main frame ID without
                // releasing the old renderer's contexts. Retire the whole old page,
                // including child frames whose menu registrations may be merged.
                var preserved = recoverMainFrame ? _sessions.Values.Where(old => !old.Frame.IsMainFrame &&
                    IsNativeDescendant(cefBrowser, old.Frame.FrameId, frameId)).ToArray() : Array.Empty<FrameSession>();
                var retired = cefFrame.IsMain ? _sessions.Values.Except(preserved).ToArray() :
                    previous == null ? Array.Empty<FrameSession>() : new[] { previous };
                foreach (var old in retired) _sessions.Remove(old.Frame.FrameId);
                if (cefFrame.IsMain)
                {
                    _contextGenerations.Clear();
                    foreach (var child in preserved) _contextGenerations[child.Frame.FrameId] = child.ContextGeneration;
                }
                session.ContextGeneration = ++_contextGeneration;
                _contextGenerations[frameId] = session.ContextGeneration;
                _sessions[frameId] = session;
                session.PredecessorsReleased = Task.WhenAll(retired.Select(old =>
                    ReleaseSessionAsync(old, DocumentLifecycleKind.NavigationSuperseded, CancellationToken.None)));
                if (_isolated != null) InstallDocumentMarker(session, cefFrame);
                session.Initialization = InitializeSessionAsync(session);
            }
            Observe(session.Initialization, "MSC101_CONTEXT_INITIALIZATION_FAILED", session);
        }

        private void InstallDocumentMarker(FrameSession session, IFrame frame)
        {
            frame.ExecuteJavaScriptAsync("if(typeof globalThis[" + JsonSerializer.Serialize(_rawDocumentMarker) + "]==='undefined')Object.defineProperty(globalThis," +
                JsonSerializer.Serialize(_rawDocumentMarker) + ",{value:" + JsonSerializer.Serialize(session.Frame.DocumentId) + "});", "monkeysharp://document-identity", 1);
            session.HasDocumentMarker = true;
        }

        private static bool IsNativeDescendant(IBrowser browser, string frameId, string mainId)
        {
            IFrame current = null;
            try
            {
                current = GetFrameByIdentifier(browser, frameId);
                var visited = new HashSet<string>(StringComparer.Ordinal);
                while (current != null && !current.IsDisposed && current.IsValid)
                {
                    var id = GetFrameIdentifier(current);
                    if (id == mainId) return true;
                    if (!visited.Add(id)) return false;
                    var parent = current.Parent;
                    current.Dispose();
                    current = parent;
                }
                return false;
            }
            catch (ObjectDisposedException) { return false; }
            finally { current?.Dispose(); }
        }

        private async Task ProbeContextAsync(IWebBrowser browser, FrameSession session, Task<JavascriptResponse> probe, bool released)
        {
            JavascriptResponse response;
            try { response = await probe.ConfigureAwait(false); }
            catch (Exception) { response = null; }
            // CEF also reports V8 contexts created/released for CDP isolated worlds.
            // This marker only deduplicates the explicitly trusted page-world path;
            // isolated dispatch never uses page-provided identity or CEF frame casts.
            if (response?.Success == true && response.Result as string == session.Frame.DocumentId) return;
            if (!released)
            {
                using (var frame = GetFrameByIdentifier(browser.GetBrowser(), session.Frame.FrameId))
                {
                    if (frame == null || frame.IsDisposed || !frame.IsValid) return;
                    lock (_sync)
                    {
                        if (_browser == null || !ReferenceEquals(_browser, browser) || session.AttachmentGeneration != _attachmentGeneration ||
                            !_contextGenerations.TryGetValue(session.Frame.FrameId, out var generation) || generation != session.ContextGeneration) return;
                        ContextCreated(browser, browser.GetBrowser(), frame, false);
                    }
                }
                return;
            }
            lock (_sync)
            {
                if (_browser == null || !ReferenceEquals(_browser, browser) || session.AttachmentGeneration != _attachmentGeneration ||
                    !_sessions.TryGetValue(session.Frame.FrameId, out var current) || !ReferenceEquals(current, session)) return;
                _sessions.Remove(session.Frame.FrameId);
            }
            await ReleaseSessionAsync(session, DocumentLifecycleKind.ContextReleased, CancellationToken.None).ConfigureAwait(false);
        }

        private void ContextReleased(IWebBrowser browser, IBrowser cefBrowser, IFrame cefFrame)
        {
            FrameSession session;
            lock (_sync)
            {
                if (_browser == null || !ReferenceEquals(_browser, browser))
                    return;
                var frameId = GetFrameIdentifier(cefFrame);
                if (!_sessions.TryGetValue(frameId, out session))
                    return;
                if (_isolated != null && session.HasDocumentMarker && !cefFrame.IsDisposed && cefFrame.IsValid)
                {
                    var probe = cefFrame.EvaluateScriptAsync("globalThis[" + JsonSerializer.Serialize(_rawDocumentMarker) + "]", timeout: TimeSpan.FromSeconds(2));
                    Observe(ProbeContextAsync(browser, session, probe, true), "MSC105_CONTEXT_IDENTITY_FAILED", session);
                    return;
                }
                _sessions.Remove(frameId);
            }
            Observe(
                ReleaseSessionAsync(session, DocumentLifecycleKind.ContextReleased, CancellationToken.None),
                "MSC102_CONTEXT_RELEASE_FAILED",
                session);
        }

        private void UncaughtException(IWebBrowser browser, IBrowser cefBrowser, IFrame cefFrame, JavascriptException exception)
        {
            FrameSession session;
            lock (_sync)
                _sessions.TryGetValue(GetFrameIdentifier(cefFrame), out session);
            EmitDiagnostic(
                "MSC300_JAVASCRIPT_EXCEPTION",
                DiagnosticSeverity.Error,
                exception?.Message ?? "An uncaught JavaScript exception occurred.",
                null,
                session,
                Guid.NewGuid().ToString("D"));
        }

        private async Task InitializeSessionAsync(FrameSession session)
        {
            await session.PredecessorsReleased.ConfigureAwait(false);
            session.Token.ThrowIfCancellationRequested();
            await session.OperationGate.WaitAsync(session.Token).ConfigureAwait(false);
            try
            {
                // Older CefSharp releases can report the previous navigation's URL
                // in OnContextCreated. Resolve the renderer's current location before
                // matching even document-start scripts against this new document.
                var response = await ReadRendererLocationAsync(session).ConfigureAwait(false);
                if (response == null) return;
                if (!response.Success || !(response.Result is string location) ||
                    !Uri.TryCreate(location, UriKind.Absolute, out var currentUrl))
                    throw new InvalidOperationException("The current CefSharp document URL could not be resolved: " + response.Message);
                session.UpdateUrl(currentUrl);
                await ProcessLifecycleAsync(
                    DocumentLifecycleKind.ContextCreated,
                    session,
                    session.Token).ConfigureAwait(false);
                var startPlan = await ProcessLifecycleAsync(
                    DocumentLifecycleKind.DocumentStart,
                    session,
                    session.Token).ConfigureAwait(false);
                var script = new StringBuilder(LifecycleScriptBuilder.Build(
                    session.Frame.DocumentId,
                    session.Frame.FrameId,
                    session.LifecycleToken));
                if (startPlan.Invocations.Count != 0)
                    script.AppendLine().Append(BridgeScriptBuilder.BuildInjection(startPlan));
                await ExecuteAsync(session.Frame, script.ToString(), session.Token).ConfigureAwait(false);
                session.Initialized = true;
            }
            finally
            {
                session.OperationGate.Release();
            }
        }

        private Task<JavascriptResponse> ReadRendererLocationAsync(FrameSession session)
        {
            return ReadOnlyScriptEvaluator.EvaluateAsync(async timeout =>
            {
                IWebBrowser browser;
                lock (_sync)
                {
                    if (!IsCurrentSession(session)) return null;
                    browser = _browser;
                }
                var refreshedFrame = GetFrameByIdentifier(browser.GetBrowser(), session.Frame.FrameId);
                var nativeFrame = refreshedFrame ?? session.CefFrame;
                try
                {
                    if (nativeFrame == null || nativeFrame.IsDisposed || !nativeFrame.IsValid) return null;
                    var response = await nativeFrame.EvaluateScriptAsync("location.href", timeout: timeout).ConfigureAwait(false);
                    session.Token.ThrowIfCancellationRequested();
                    lock (_sync) if (!IsCurrentSession(session)) return null;
                    if (nativeFrame.IsDisposed || !nativeFrame.IsValid) return null;
                    return response ?? throw new InvalidOperationException("The CefSharp document URL probe returned no response.");
                }
                catch (ObjectDisposedException) when (nativeFrame != null && (nativeFrame.IsDisposed || !nativeFrame.IsValid))
                {
                    // A retired native frame must not fall back into its replacement document.
                    return null;
                }
                finally { refreshedFrame?.Dispose(); }
            }, TimeSpan.FromSeconds(15), session.Token);
        }

        // Caller holds _sync while checking attachment and document ownership.
        private bool IsCurrentSession(FrameSession session)
        {
            return _browser != null && session.AttachmentGeneration == _attachmentGeneration &&
                _sessions.TryGetValue(session.Frame.FrameId, out var current) && ReferenceEquals(current, session);
        }

        private Task ReleaseSessionAsync(
            FrameSession session,
            DocumentLifecycleKind kind,
            CancellationToken cancellationToken)
        {
            lock (session.LifetimeSync)
            {
                session.Cancel();
                return session.ReleaseTask ?? (session.ReleaseTask = ReleaseSessionCoreAsync(session, kind, cancellationToken));
            }
        }

        private async Task ReleaseSessionCoreAsync(
            FrameSession session,
            DocumentLifecycleKind kind,
            CancellationToken cancellationToken)
        {
            // Preserve cleanup ordering even if this document is replaced while
            // it is still waiting for its own predecessor to finish retiring.
            await session.PredecessorsReleased.ConfigureAwait(false);
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
            session.Token.ThrowIfCancellationRequested();
            await session.OperationGate.WaitAsync(session.Token).ConfigureAwait(false);
            try
            {
                if (!session.Initialized) return;
                while (session.CompletedRank < requestedRank)
                {
                    session.CompletedRank++;
                    var kind = session.CompletedRank == 1
                        ? DocumentLifecycleKind.BodyAvailable
                        : session.CompletedRank == 2
                            ? DocumentLifecycleKind.DomContentLoaded
                            : session.CompletedRank == 3 ? DocumentLifecycleKind.DocumentIdle : DocumentLifecycleKind.Load;
                    var plan = await ProcessLifecycleAsync(kind, session, session.Token)
                        .ConfigureAwait(false);
                    if (plan.Invocations.Count != 0)
                    {
                        await ExecuteAsync(
                            session.Frame,
                            BridgeScriptBuilder.BuildInjection(plan),
                            session.Token).ConfigureAwait(false);
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
            if (frame == null && TryReadDocumentId(requestJson) != null)
            {
                using (var document = JsonDocument.Parse(requestJson))
                {
                    var root = document.RootElement;
                    var hello = root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "hello";
                    return JsonSerializer.Serialize(new { protocol = InjectionPlan.CurrentProtocolVersion, type = hello ? "hello-result" : "response",
                        requestId = root.TryGetProperty("requestId", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : string.Empty,
                        ok = false, error = new { code = BridgeErrorCodes.SessionExpired, message = "This proof does not belong to an active page-world document." } });
                }
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
                        !root.TryGetProperty("protocol", out var protocol) ||
                        protocol.ValueKind != JsonValueKind.Number ||
                        !protocol.TryGetInt32(out var protocolVersion) || protocolVersion != InjectionPlan.CurrentProtocolVersion ||
                        !root.TryGetProperty("documentId", out var documentIdValue) || documentIdValue.ValueKind != JsonValueKind.String ||
                        !root.TryGetProperty("frameId", out var frameIdValue) || frameIdValue.ValueKind != JsonValueKind.String ||
                        !root.TryGetProperty("token", out var tokenValue) || tokenValue.ValueKind != JsonValueKind.String ||
                        !root.TryGetProperty("stage", out var stageValue) || stageValue.ValueKind != JsonValueKind.String)
                        return false;
                    var documentId = documentIdValue.GetString();
                    var frameId = frameIdValue.GetString();
                    var token = tokenValue.GetString();
                    var stage = stageValue.GetString();
                    Uri changedUrl = null;
                    if (root.TryGetProperty("url", out var urlValue) && urlValue.ValueKind == JsonValueKind.String)
                        changedUrl = ParseUrl(urlValue.GetString());
                    FrameSession session;
                    lock (_sync)
                    {
                        if (!_sessions.TryGetValue(frameId, out session) ||
                            session.Frame.DocumentId != documentId ||
                            !FixedTimeEquals(session.LifecycleToken, token))
                            return false;
                    }
                    if (stage == "url-changed")
                    {
                        if (changedUrl == null)
                            return false;
                        session.UpdateUrl(changedUrl);
                        await ProcessLifecycleAsync(
                            DocumentLifecycleKind.UrlChanged,
                            session,
                            session.Token).ConfigureAwait(false);
                        return true;
                    }
                    var rank = stage == "body" ? 1 : stage == "dom-content-loaded" ? 2 : stage == "idle" ? 3 : stage == "load" ? 4 : 0;
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
                var frameId = GetFrameIdentifier(args.Frame);
                if (!_sessions.TryGetValue(frameId, out session))
                {
                    // CEF 84 can report the old main-frame wrapper in the new
                    // renderer's context callback. Recover only a different,
                    // native-attested main frame, never a failed/retired session.
                    if (_browser == null || !args.Frame.IsMain || args.Frame.IsDisposed || !args.Frame.IsValid ||
                        _contextGenerations.ContainsKey(frameId) || !_sessions.Values.Any(item => item.Frame.IsMainFrame)) return;
                    using (var current = args.Browser.MainFrame)
                    {
                        if (current == null || current.IsDisposed || !current.IsValid || GetFrameIdentifier(current) != frameId) return;
                    }
                    ContextCreated(_browser, args.Browser, args.Frame, recoverMainFrame: true);
                    if (!_sessions.TryGetValue(frameId, out session)) return;
                }
            }
            Observe(ProcessLoadedSessionAsync(session), "MSC103_LOAD_FALLBACK_FAILED", session);
        }

        private async Task ProcessLoadedSessionAsync(FrameSession session)
        {
            await session.Initialization.ConfigureAwait(false);
            await ProcessThroughAsync(session, 4).ConfigureAwait(false);
        }

        private void GatewayNotification(object sender, BridgeNotificationEventArgs notification)
        {
            if (notification.Frame.ExecutionWorld == "isolated")
            {
                CefSharpIsolatedExecutor executor;
                lock (_sync) executor = _isolated;
                if (executor != null && executor.Owns(notification.Frame.DocumentId))
                    Observe(executor.ExecuteAsync(notification.Frame, BridgeScriptBuilder.BuildNotification(notification)), "MSC233_ISOLATED_DELIVERY_FAILED", null);
                return;
            }
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

        private static PropertyInfo GetContextMenuHandlerProperty(IWebBrowser browser)
        {
            if (browser == null)
                return null;
            var flags = BindingFlags.Instance | BindingFlags.Public;
            return browser.GetType().GetProperty("MenuHandler", flags) ??
                browser.GetType().GetProperty("ContextMenuHandler", flags);
        }

        private void BrowserJavascriptMessageReceived(object sender, JavascriptMessageReceivedEventArgs args)
        {
            if (args.Message is string proof && args.Frame != null && !args.Frame.IsDisposed)
                _isolated?.ConfirmNativeMenuFrame(proof, GetFrameIdentifier(args.Frame), args.Frame.IsMain);
        }

        /// <summary>Resolves a menu subscriber against a native menu event frame, rejecting stale documents and unknown mappings.</summary>
        /// <param name="nativeFrame">The native CEF event frame; never construct it from page-supplied IDs.</param>
        /// <param name="request">The authenticated registration received by the menu service.</param>
        /// <returns>The current subscribing frame to place in MenuCommandInvocation.Frame, or null if it cannot be attested.</returns>
        public DocumentFrame ResolveMenuFrame(IFrame nativeFrame, MenuCommandRequest request)
        {
            var frame = request?.OriginFrame;
            CefSharpIsolatedExecutor isolated;
            string id;
            bool main;
            lock (_sync)
            {
                if (_browser == null || nativeFrame == null || nativeFrame.IsDisposed || frame == null ||
                    frame.BrowserSessionId != _browserSessionId || nativeFrame.Browser.Identifier != _browser.GetBrowser().Identifier) return null;
                id = GetFrameIdentifier(nativeFrame);
                if (frame.ExecutionWorld != "isolated") return _sessions.TryGetValue(id, out var session) && session.Initialized &&
                    !session.Token.IsCancellationRequested && CefSharpDocumentIdentity.Matches(session.Frame, frame) ? session.Frame : null;
                isolated = _isolated;
                main = nativeFrame.IsMain;
            }
            return isolated?.OwnsNativeMenuFrame(frame, id, main) == true ? frame : null;
        }

        internal IReadOnlyList<CefSharpMenuTarget> GetContextMenuEntries(IFrame cefFrame)
        {
            FrameSession session;
            lock (_sync)
                _sessions.TryGetValue(GetFrameIdentifier(cefFrame), out session);
            var entries = new List<CefSharpMenuTarget>();
            if (session != null && session.Initialized)
                entries.AddRange(_engine.GetContextMenuEntriesAsync(session.Frame, CancellationToken.None).GetAwaiter().GetResult()
                    .Select(entry => new CefSharpMenuTarget(entry, session.Frame, GetFrameIdentifier(cefFrame))));
            if (_isolated != null) entries.AddRange(_isolated.MenuAsync(GetFrameIdentifier(cefFrame), cefFrame.IsMain).GetAwaiter().GetResult());
            return entries;
        }

        internal void ExecuteContextMenu(IFrame cefFrame, CefSharpMenuTarget target)
        {
            if (GetFrameIdentifier(cefFrame) != target.NativeFrameId) return;
            if (target.Frame.ExecutionWorld == "isolated")
            {
                var executor = _isolated;
                if (executor != null) Observe(executor.ExecuteMenuAsync(target.Frame.DocumentId, target.NativeFrameId, target.Entry.ScriptKey), "MSC104_CONTEXT_MENU_FAILED", null);
                return;
            }
            FrameSession session;
            lock (_sync)
                _sessions.TryGetValue(GetFrameIdentifier(cefFrame), out session);
            if (session == null || session.Frame.DocumentId != target.Frame.DocumentId)
                return;
            Observe(ExecuteContextMenuAsync(session, target.Entry.ScriptKey), "MSC104_CONTEXT_MENU_FAILED", session);
        }

        private async Task ExecuteContextMenuAsync(FrameSession session, ScriptKey scriptKey)
        {
            session.Token.ThrowIfCancellationRequested();
            await session.OperationGate.WaitAsync(session.Token).ConfigureAwait(false);
            try
            {
                var plan = await _engine.ProcessContextMenuAsync(
                    session.Frame, scriptKey, session.Token).ConfigureAwait(false);
                if (plan.Invocations.Count != 0)
                    await ExecuteAsync(session.Frame, BridgeScriptBuilder.BuildInjection(plan), session.Token)
                        .ConfigureAwait(false);
            }
            finally
            {
                session.OperationGate.Release();
            }
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
            if (requestJson == null)
                return null;
            try
            {
                using (var document = JsonDocument.Parse(requestJson))
                    return document.RootElement.ValueKind == JsonValueKind.Object &&
                        document.RootElement.TryGetProperty("documentId", out var value) &&
                        value.ValueKind == JsonValueKind.String
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

        private sealed class ContextMenuHandler : IContextMenuHandler
        {
            private readonly CefSharpUserScriptHost _host;
            private readonly IContextMenuHandler _inner;
            private readonly object _sync = new object();
            private readonly Dictionary<CefMenuCommand, CefSharpMenuTarget> _commands =
                new Dictionary<CefMenuCommand, CefSharpMenuTarget>();

            public ContextMenuHandler(CefSharpUserScriptHost host, IContextMenuHandler inner)
            {
                _host = host;
                _inner = inner;
            }

            public IContextMenuHandler Previous => _inner;

            public void OnBeforeContextMenu(IWebBrowser browser, IBrowser cefBrowser, IFrame frame,
                IContextMenuParams parameters, IMenuModel model)
            {
                _inner?.OnBeforeContextMenu(browser, cefBrowser, frame, parameters, model);
                lock (_sync) _commands.Clear();
                var entries = _host.GetContextMenuEntries(frame);
                if (entries.Count == 0)
                    return;
                model.AddSeparator();
                for (var index = 0; index < entries.Count; index++)
                {
                    var command = (CefMenuCommand)((int)CefMenuCommand.UserFirst + index);
                    lock (_sync) _commands[command] = entries[index];
                    model.AddItem(command, entries[index].Entry.Title);
                }
            }

            public bool OnContextMenuCommand(IWebBrowser browser, IBrowser cefBrowser, IFrame frame,
                IContextMenuParams parameters, CefMenuCommand commandId, CefEventFlags eventFlags)
            {
                if (_inner != null && _inner.OnContextMenuCommand(browser, cefBrowser, frame, parameters, commandId, eventFlags))
                    return true;
                CefSharpMenuTarget target;
                lock (_sync)
                {
                    if (!_commands.TryGetValue(commandId, out target))
                        return false;
                }
                _host.ExecuteContextMenu(frame, target);
                return true;
            }

            public void OnContextMenuDismissed(IWebBrowser browser, IBrowser cefBrowser, IFrame frame)
            {
                lock (_sync) _commands.Clear();
                _inner?.OnContextMenuDismissed(browser, cefBrowser, frame);
            }

            public bool RunContextMenu(IWebBrowser browser, IBrowser cefBrowser, IFrame frame,
                IContextMenuParams parameters, IMenuModel model, IRunContextMenuCallback callback)
            {
                return _inner != null && _inner.RunContextMenu(browser, cefBrowser, frame, parameters, model, callback);
            }
        }

        private sealed class FrameSession : IDisposable
        {
            private bool _disposed;
            public FrameSession(IFrame cefFrame, DocumentFrame frame, string lifecycleToken)
            {
                CefFrame = cefFrame;
                Frame = frame;
                LifecycleToken = lifecycleToken;
                Token = Cancellation.Token;
            }

            public long AttachmentGeneration { get; set; }
            public long ContextGeneration { get; set; }
            public bool HasDocumentMarker { get; set; }
            public IFrame CefFrame { get; }
            public DocumentFrame Frame { get; private set; }
            public string LifecycleToken { get; }
            public CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();
            public CancellationToken Token { get; }
            public object LifetimeSync { get; } = new object();
            public Task ReleaseTask { get; set; }
            public Task PredecessorsReleased { get; set; } = Task.CompletedTask;
            public Task Initialization { get; set; } = Task.CompletedTask;
            public SemaphoreSlim OperationGate { get; } = new SemaphoreSlim(1, 1);
            public int CompletedRank { get; set; }
            public bool Initialized { get; set; }

            public void UpdateUrl(Uri url)
            {
                Frame = new DocumentFrame(
                    Frame.BrowserSessionId,
                    Frame.DocumentId,
                    Frame.FrameId,
                    url,
                    Frame.IsMainFrame,
                    Frame.TimingGuarantee,
                    Frame.BridgeIntegrity);
            }

            public void Cancel()
            {
                lock (LifetimeSync)
                    if (!_disposed) Cancellation.Cancel();
            }

            public void Dispose()
            {
                lock (LifetimeSync)
                {
                    if (_disposed) return;
                    _disposed = true;
                    Cancellation.Dispose();
                    OperationGate.Dispose();
                }
            }
        }
    }
}
