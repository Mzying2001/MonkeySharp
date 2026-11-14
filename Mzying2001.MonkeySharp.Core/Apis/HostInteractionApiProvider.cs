using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Bridge;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Apis
{
    /// <summary>
    /// Adapts optional host UI, clipboard, tab, download, and tab-state services to GM APIs.
    /// </summary>
    public sealed class HostInteractionApiProvider :
        IUserScriptApiProvider,
        IUserScriptNotificationSource,
        IUserScriptExecutionObserver,
        IDisposable
    {
        private readonly IMenuService _menu;
        private readonly INotificationService _notifications;
        private readonly IClipboardService _clipboard;
        private readonly ITabService _tabs;
        private readonly IDownloadService _downloads;
        private readonly ITabStateService _tabState;
        private readonly IUserScriptWindowService _window;
        private readonly IReadOnlyCollection<string> _methods;
        private readonly object _sync = new object();
        private readonly Dictionary<string, IMenuRegistration> _menuRegistrations =
            new Dictionary<string, IMenuRegistration>(StringComparer.Ordinal);
        private readonly HashSet<string> _pendingMenus = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _endedPendingMenus = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, NotificationRegistration> _notificationHandles =
            new Dictionary<string, NotificationRegistration>(StringComparer.Ordinal);
        private readonly Dictionary<string, TabRegistration> _tabHandles =
            new Dictionary<string, TabRegistration>(StringComparer.Ordinal);
        private readonly Dictionary<string, DownloadRegistration> _downloadOperations =
            new Dictionary<string, DownloadRegistration>(StringComparer.Ordinal);
        private readonly BridgeOptions _options;
        private readonly Dictionary<string, BinaryDownload> _downloadBodies = new Dictionary<string, BinaryDownload>();
        private bool _disposed;

        /// <summary>Initializes a provider from the host interaction services that are available.</summary>
        /// <param name="menu">The optional menu command service.</param>
        /// <param name="notifications">The optional notification service.</param>
        /// <param name="clipboard">The optional clipboard service.</param>
        /// <param name="tabs">The optional tab-opening service.</param>
        /// <param name="downloads">The optional download service.</param>
        /// <param name="tabState">The optional tab-state service.</param>
        /// <param name="window">The optional current-window service.</param>
        /// <param name="options">Binary staging limits shared with the bridge.</param>
        public HostInteractionApiProvider(
            IMenuService menu = null,
            INotificationService notifications = null,
            IClipboardService clipboard = null,
            ITabService tabs = null,
            IDownloadService downloads = null,
            ITabStateService tabState = null,
            IUserScriptWindowService window = null, BridgeOptions options = null)
        {
            if (menu == null && notifications == null && clipboard == null && tabs == null &&
                downloads == null && tabState == null && window == null)
                throw new ArgumentException("At least one host interaction service is required.");
            _menu = menu;
            _notifications = notifications;
            _clipboard = clipboard;
            _tabs = tabs;
            _downloads = downloads;
            _tabState = tabState;
            _window = window;
            _options = options ?? new BridgeOptions();
            var methods = new List<string>();
            if (menu != null)
            {
                methods.Add("GM.registerMenuCommand");
                methods.Add("GM.unregisterMenuCommand");
            }
            if (notifications != null) methods.Add("GM.notification");
            if (clipboard != null) methods.Add("GM.setClipboard");
            if (tabs != null) methods.Add("GM.openInTab");
            if (downloads != null) methods.Add("GM.download");
            if (tabState != null)
            {
                methods.Add("GM.getTab");
                methods.Add("GM.saveTab");
                methods.Add("GM.getTabs");
            }
            if (window != null)
            {
                methods.Add("window.close");
                methods.Add("window.focus");
            }
            _methods = new ReadOnlyCollection<string>(methods);
        }

        /// <inheritdoc />
        public event EventHandler<ApiNotificationEventArgs> Notification;

        /// <inheritdoc />
        public IReadOnlyCollection<string> Methods => _methods;

        /// <inheritdoc />
        public async Task<ApiResult> InvokeAsync(ApiInvocationContext context, CancellationToken cancellationToken)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HostInteractionApiProvider));
            switch (context.Method)
            {
                case "GM.registerMenuCommand":
                    return await RegisterMenuAsync(context, cancellationToken).ConfigureAwait(false);
                case "GM.unregisterMenuCommand":
                    return UnregisterMenu(context);
                case "GM.notification":
                    return ShowNotification(context, cancellationToken);
                case "GM.setClipboard":
                    var clipboardType = ProviderParameters.OptionalString(context.Parameters, "type") ?? "text/plain";
                    if (clipboardType != "text/plain" && clipboardType != "text/html") throw new UnsupportedApiException("Only text and HTML clipboard formats are supported.");
                    await _clipboard.SetTextAsync(
                        ProviderParameters.RequiredString(context.Parameters, "text"),
                        clipboardType,
                        cancellationToken).ConfigureAwait(false);
                    return ApiResult.Undefined;
                case "GM.openInTab":
                    if (ProviderParameters.OptionalBoolean(context.Parameters, "close"))
                    {
                        var tabId = ProviderParameters.RequiredString(context.Parameters, "tabId");
                        return await CloseTabAsync(context, tabId, cancellationToken).ConfigureAwait(false);
                    }
                    bool? incognito = null;
                    if (context.Parameters.TryGetProperty("incognito", out var privacy))
                    {
                        if (privacy.ValueKind != JsonValueKind.True && privacy.ValueKind != JsonValueKind.False) throw ProviderParameters.Invalid("incognito must be a boolean.");
                        incognito = privacy.GetBoolean();
                        if (incognito.Value && !_tabs.SupportsIncognito) throw new UnsupportedApiException("This tab service does not create private browsing contexts.");
                    }
                    var tabAddress = ProviderParameters.RequiredString(context.Parameters, "url");
                    var tabUrl = tabAddress == "about:blank" ? new Uri(tabAddress) : ReadHttpUrl(context.Parameters, "url");
                    var tabClientId = ProviderParameters.OptionalString(context.Parameters, "clientId");
                    if (tabClientId != null && !Guid.TryParse(tabClientId, out _)) throw ProviderParameters.Invalid("clientId must be a UUID.");
                    int? insertIndex = null;
                    var insert = false;
                    if (context.Parameters.TryGetProperty("insert", out var insertion))
                    {
                        if (insertion.ValueKind == JsonValueKind.Number && insertion.TryGetInt32(out var index) && index >= 0) insertIndex = index;
                        else if (insertion.ValueKind == JsonValueKind.True || insertion.ValueKind == JsonValueKind.False) insert = insertion.GetBoolean();
                        else throw ProviderParameters.Invalid("insert must be a boolean or nonnegative integer.");
                    }
                    var tab = await _tabs.OpenAsync(new OpenTabRequest(
                        tabUrl,
                        ProviderParameters.OptionalBoolean(context.Parameters, "active", false),
                        insert,
                        ProviderParameters.OptionalBoolean(context.Parameters, "setParent"), insertIndex, incognito), cancellationToken)
                        .ConfigureAwait(false);
                    if (tab == null || string.IsNullOrEmpty(tab.TabId))
                        throw new InvalidOperationException("The tab service returned no tab handle.");
                    TrackTab(context, tab, tabClientId);
                    return ApiResult.FromValue(new { id = tab.TabId, closed = tab.Closed });
                case "GM.download":
                    var downloadOperation = ProviderParameters.OptionalString(context.Parameters, "operation");
                    if (downloadOperation == "createBody" || downloadOperation == "appendBody" || downloadOperation == "releaseBody") return TransferDownloadBody(context, downloadOperation, cancellationToken);
                    if (string.Equals(ProviderParameters.OptionalString(context.Parameters, "operation"), "abort", StringComparison.Ordinal))
                        return AbortDownload(context);
                    return await StartDownloadAsync(context, cancellationToken).ConfigureAwait(false);
                case "GM.getTab":
                    return ApiResult.FromJson(ValidateJson(await _tabState.GetAsync(
                        context.Installation.ScriptKey, context.Frame, cancellationToken).ConfigureAwait(false)));
                case "GM.saveTab":
                    ProviderParameters.RequireObject(context.Parameters);
                    if (!context.Parameters.TryGetProperty("value", out var value))
                        throw ProviderParameters.Invalid("value is required.");
                    if (value.ValueKind != JsonValueKind.Object)
                        throw ProviderParameters.Invalid("value must be a JSON object.");
                    await _tabState.SaveAsync(
                        context.Installation.ScriptKey,
                        context.Frame,
                        value.GetRawText(),
                        cancellationToken).ConfigureAwait(false);
                    return ApiResult.Undefined;
                case "GM.getTabs":
                    return ApiResult.FromJson(BuildTabsJson(await _tabState.GetAllAsync(
                        context.Installation.ScriptKey, cancellationToken).ConfigureAwait(false)));
                case "window.close":
                    return ApiResult.FromValue(await _window.CloseAsync(context.Frame, cancellationToken).ConfigureAwait(false));
                case "window.focus":
                    return ApiResult.FromValue(await _window.FocusAsync(context.Frame, cancellationToken).ConfigureAwait(false));
                default:
                    throw new UnsupportedApiException("The API '" + context.Method + "' is not supported.");
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
                return;
            IMenuRegistration[] registrations;
            NotificationRegistration[] notifications;
            TabRegistration[] tabs;
            DownloadRegistration[] downloads;
            lock (_sync)
            {
                if (_disposed)
                    return;
                registrations = _menuRegistrations.Values.ToArray();
                notifications = _notificationHandles.Values.ToArray();
                tabs = _tabHandles.Values.ToArray();
                downloads = _downloadOperations.Values.ToArray();
                _menuRegistrations.Clear();
                _notificationHandles.Clear();
                _tabHandles.Clear();
                _downloadOperations.Clear();
                foreach (var body in _downloadBodies.Values) body.Buffer.Dispose();
                _downloadBodies.Clear();
                _pendingMenus.Clear();
                _endedPendingMenus.Clear();
                _disposed = true;
            }
            foreach (var registration in registrations)
                registration.Dispose();
            foreach (var handle in notifications) handle.Dispose();
            foreach (var handle in tabs) handle.Dispose();
            foreach (var operation in downloads) operation.Dispose();
        }

        /// <inheritdoc />
        public void OnExecutionEnded(string executionId)
        {
            if (string.IsNullOrEmpty(executionId))
                throw new ArgumentException("The execution ID is required.", nameof(executionId));
            var prefix = executionId + ":";
            lock (_sync) foreach (var key in _downloadBodies.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            { _downloadBodies[key].Buffer.Dispose(); _downloadBodies.Remove(key); }
            IMenuRegistration[] registrations;
            NotificationRegistration[] notifications;
            TabRegistration[] tabs;
            DownloadRegistration[] downloads;
            lock (_sync)
            {
                if (_disposed)
                    return;
                var keys = _menuRegistrations.Keys
                    .Where(item => item.StartsWith(prefix, StringComparison.Ordinal))
                    .ToArray();
                registrations = keys.Select(item => _menuRegistrations[item]).ToArray();
                foreach (var key in keys)
                    _menuRegistrations.Remove(key);
                foreach (var pending in _pendingMenus.Where(item => item.StartsWith(prefix, StringComparison.Ordinal)))
                    _endedPendingMenus.Add(pending);
                notifications = _notificationHandles
                    .Where(item => item.Key.StartsWith(prefix, StringComparison.Ordinal))
                    .Select(item => item.Value).ToArray();
                tabs = _tabHandles
                    .Where(item => item.Key.StartsWith(prefix, StringComparison.Ordinal))
                    .Select(item => item.Value).ToArray();
                downloads = _downloadOperations
                    .Where(item => item.Key.StartsWith(prefix, StringComparison.Ordinal))
                    .Select(item => item.Value).ToArray();
                foreach (var key in _tabHandles.Keys.Where(item => item.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
                    _tabHandles.Remove(key);
            }
            foreach (var registration in registrations)
                registration.Dispose();
            foreach (var handle in notifications) handle.EndDocument();
            foreach (var handle in tabs) handle.Dispose();
            foreach (var operation in downloads) operation.DetachCallbacks();
        }

        private ApiResult ShowNotification(ApiInvocationContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var clientId = ProviderParameters.RequiredString(context.Parameters, "clientId");
            var key = context.ExecutionId + ":" + clientId;
            if (ProviderParameters.OptionalString(context.Parameters, "operation") == "click-result")
            {
                NotificationRegistration pending;
                lock (_sync) _notificationHandles.TryGetValue(key, out pending);
                pending?.ResolveClick(ProviderParameters.OptionalBoolean(context.Parameters, "preventDefault"));
                return ApiResult.Undefined;
            }
            if (!Guid.TryParse(clientId, out _)) throw ProviderParameters.Invalid("clientId must be a UUID.");
            var text = ProviderParameters.OptionalString(context.Parameters, "text");
            var highlight = ProviderParameters.OptionalBoolean(context.Parameters, "highlight");
            if (string.IsNullOrEmpty(text) && !highlight) throw ProviderParameters.Invalid("text is required unless highlight is true.");
            var timeout = ProviderParameters.OptionalInt32(context.Parameters, "timeout");
            if (timeout < 0) throw ProviderParameters.Invalid("timeout must not be negative.");
            var tag = ProviderParameters.OptionalString(context.Parameters, "tag");
            var url = ProviderParameters.OptionalString(context.Parameters, "url") == null ? null : ReadHttpUrl(context.Parameters, "url");
            var request = new UserScriptNotificationRequest(context.Installation.ScriptKey,
                ProviderParameters.OptionalString(context.Parameters, "title") ?? context.Installation.Definition.Metadata.Name,
                text, ProviderParameters.OptionalString(context.Parameters, "imageUrl"), highlight,
                ProviderParameters.OptionalBoolean(context.Parameters, "silent"), timeout, tag, url);
            var handle = _notifications.Create(request, CancellationToken.None);
            if (handle == null) throw new InvalidOperationException("The notification service returned no handle.");
            var registration = new NotificationRegistration(this, key, handle, request, context.ExecutionId, clientId);
            NotificationRegistration[] replaced;
            lock (_sync)
            {
                if (cancellationToken.IsCancellationRequested) { handle.Dispose(); cancellationToken.ThrowIfCancellationRequested(); }
                if (_disposed || _notificationHandles.ContainsKey(key))
                {
                    handle.Dispose();
                    throw new InvalidOperationException("Notification cannot be registered.");
                }
                replaced = string.IsNullOrEmpty(tag) ? new NotificationRegistration[0] : _notificationHandles.Values
                    .Where(item => item.ScriptKey == request.ScriptKey && item.Tag == tag).ToArray();
                _notificationHandles.Add(key, registration);
            }
            foreach (var previous in replaced) previous.Dispose();
            try { registration.Start(); }
            catch (Exception error) { registration.StartFailed(error); throw; }
            return ApiResult.Undefined;
        }

        private async Task<ApiResult> CloseTabAsync(
            ApiInvocationContext context, string tabId, CancellationToken cancellationToken)
        {
            TabRegistration registration;
            lock (_sync)
                _tabHandles.TryGetValue(context.ExecutionId + ":" + tabId, out registration);
            if (registration == null)
                return ApiResult.FromValue(false);
            await registration.CloseAsync(cancellationToken).ConfigureAwait(false);
            return ApiResult.FromValue(true);
        }

        private void TrackTab(ApiInvocationContext context, ITabHandle tab, string clientId)
        {
            var key = context.ExecutionId + ":" + tab.TabId;
            var registration = new TabRegistration(this, key, tab, context.Installation.ScriptKey, context.ExecutionId, clientId);
            lock (_sync) _tabHandles[key] = registration;
            registration.Subscribe();
            if (tab.Closed) registration.Terminalize();
        }

        private ApiResult TransferDownloadBody(ApiInvocationContext context, string operation, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var client = ProviderParameters.RequiredString(context.Parameters, "clientId");
            if (!Guid.TryParse(client, out _)) throw ProviderParameters.Invalid("clientId must be a UUID.");
            var key = context.ExecutionId + ":" + client;
            lock (_sync)
            {
                if (operation == "releaseBody")
                {
                    if (_downloadBodies.TryGetValue(key, out var removed)) { removed.Buffer.Dispose(); _downloadBodies.Remove(key); }
                    return ApiResult.Undefined;
                }
                if (operation == "createBody")
                {
                    if (!context.Parameters.TryGetProperty("length", out var size) || size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out var length) || length < 0 || length > _options.MaxBinaryTransferBytes)
                        throw new BridgeProtocolException(BridgeErrorCodes.PayloadTooLarge, "The binary download exceeds MaxBinaryTransferBytes.");
                    if (!Uri.TryCreate(ProviderParameters.RequiredString(context.Parameters, "url"), UriKind.Absolute, out var url) || url.Scheme != "blob")
                        throw ProviderParameters.Invalid("A renderer binary download needs a blob source identifier.");
                    if (_downloadBodies.ContainsKey(key) || _downloadOperations.ContainsKey(key)) throw ProviderParameters.Invalid("Duplicate binary operation.");
                    if (_downloadBodies.Keys.Count(value => value.StartsWith(context.ExecutionId + ":", StringComparison.Ordinal)) >= _options.MaxPendingRequestsPerDocument)
                        throw new BridgeProtocolException(BridgeErrorCodes.PayloadTooLarge, "Too many staged binary downloads.");
                    _downloadBodies.Add(key, new BinaryDownload(url, length));
                    return ApiResult.Undefined;
                }
                if (!_downloadBodies.TryGetValue(key, out var body)) throw ProviderParameters.Invalid("The binary download is not staged by this execution.");
                if (!context.Parameters.TryGetProperty("offset", out var position) || position.ValueKind != JsonValueKind.Number || !position.TryGetInt64(out var offset) || offset != body.Buffer.Length)
                    throw ProviderParameters.Invalid("Binary chunks must have contiguous, nonduplicate offsets.");
                var encoded = ProviderParameters.RequiredString(context.Parameters, "chunk");
                if (encoded.Length > (HttpSpoolingBuffer.BridgeChunkSize + 2) / 3 * 4) throw ProviderParameters.Invalid("Binary chunk too large.");
                byte[] bytes;
                try { bytes = Convert.FromBase64String(encoded); } catch (FormatException) { throw ProviderParameters.Invalid("Invalid binary chunk."); }
                if (bytes.Length > HttpSpoolingBuffer.BridgeChunkSize || body.Buffer.Length + bytes.Length > body.Length) throw ProviderParameters.Invalid("Binary chunk exceeds the declared body length.");
                body.Buffer.Append(bytes, 0, bytes.Length);
                return ApiResult.Undefined;
            }
        }

        private sealed class BinaryDownload
        {
            internal BinaryDownload(Uri url, long length) { Url = url; Length = length; }
            internal readonly Uri Url;
            internal readonly long Length;
            internal readonly HttpSpoolingBuffer Buffer = new HttpSpoolingBuffer();
        }

        private async Task<ApiResult> StartDownloadAsync(ApiInvocationContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var clientId = ProviderParameters.RequiredString(context.Parameters, "clientId");
            if (!Guid.TryParse(clientId, out _)) throw ProviderParameters.Invalid("clientId must be a UUID.");
            BinaryDownload binary = null;
            var binaryStart = ProviderParameters.OptionalString(context.Parameters, "operation") == "startBody";
            var key = context.ExecutionId + ":" + clientId;
            if (binaryStart)
                lock (_sync)
                {
                    if (!_downloadBodies.TryGetValue(key, out binary) || binary.Buffer.Length != binary.Length) throw ProviderParameters.Invalid("The complete binary body is required before starting.");
                }
            var request = new DownloadRequest(context.Installation.ScriptKey,
                binary?.Url ?? ReadHttpUrl(context.Parameters, "url"), ProviderParameters.OptionalString(context.Parameters, "name"),
                ProviderParameters.OptionalBoolean(context.Parameters, "saveAs"), ReadHeaders(context.Parameters),
                ReadConflictAction(context.Parameters), ReadTimeout(context.Parameters),
                ProviderParameters.OptionalBoolean(context.Parameters, "anonymous"),
                (target, token) => context.AuthorizeTargetAsync == null ? Task.FromResult(true) : context.AuthorizeTargetAsync(target.AbsoluteUri, token), binary?.Buffer, context.Frame);
            if (_downloads.DownloadMode == "disabled" ||
                (_downloads.DownloadMode != "browser" && (request.SaveAs || request.ConflictAction != "uniquify")))
                throw new UnsupportedApiException("The download backend does not support the requested destination options.");
            var registration = new DownloadRegistration(this, key, request, context.ExecutionId, clientId, binary?.Buffer);
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(HostInteractionApiProvider));
                if (_downloadOperations.ContainsKey(key)) throw ProviderParameters.Invalid("Duplicate download operation ID.");
                if (binary != null) _downloadBodies.Remove(key);
                _downloadOperations.Add(key, registration);
            }
            try
            {
                var operation = await _downloads.CreateAsync(request, registration.CancellationToken).ConfigureAwait(false);
                if (operation == null || string.IsNullOrEmpty(operation.DownloadId))
                {
                    operation?.Dispose();
                    throw new InvalidOperationException("The download service returned no operation.");
                }
                registration.Start(operation);
                return ApiResult.FromValue(new { id = operation.DownloadId, clientId });
            }
            catch (Exception error) { registration.StartFailed(error); throw; }
        }

        private ApiResult AbortDownload(ApiInvocationContext context)
        {
            var clientId = ProviderParameters.RequiredString(context.Parameters, "clientId");
            DownloadRegistration registration;
            lock (_sync) _downloadOperations.TryGetValue(context.ExecutionId + ":" + clientId, out registration);
            registration?.Abort();
            return ApiResult.FromValue(new { accepted = registration != null });
        }

        private static IDictionary<string, string> ReadHeaders(JsonElement parameters)
        {
            if (!parameters.TryGetProperty("headers", out var value))
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (value.ValueKind != JsonValueKind.Object)
                throw ProviderParameters.Invalid("headers must be an object.");
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in value.EnumerateObject())
            {
                if (header.Value.ValueKind != JsonValueKind.String)
                    throw ProviderParameters.Invalid("headers." + header.Name + " must be a string.");
                headers[header.Name] = header.Value.GetString();
            }
            return headers;
        }

        private abstract class Registration : IDisposable
        {
            protected readonly HostInteractionApiProvider Owner;
            protected readonly string Key;
            private int _terminal;
            protected Registration(HostInteractionApiProvider owner, string key) { Owner = owner; Key = key; }
            protected bool TryTerminal()
            {
                if (Interlocked.Exchange(ref _terminal, 1) != 0) return false;
                lock (Owner._sync)
                {
                    Owner._notificationHandles.Remove(Key);
                    Owner._tabHandles.Remove(Key);
                    Owner._downloadOperations.Remove(Key);
                }
                return true;
            }
            protected bool IsTerminal => Volatile.Read(ref _terminal) != 0;
            public abstract void Dispose();
        }

        private sealed class NotificationRegistration : Registration
        {
            private readonly INotificationHandle _handle;
            private readonly UserScriptNotificationRequest _request;
            private readonly string _executionId, _clientId;
            private readonly EventHandler _clicked, _closed;
            private volatile bool _callbacksEnabled = true;
            private int _wasClicked;
            private int _clickPending;
            private Timer _clickFallback;
            private Exception _failure;
            public ScriptKey ScriptKey => _request.ScriptKey;
            public string Tag => _request.Tag;
            public bool Persistent => !string.IsNullOrEmpty(Tag) || _request.Url != null;
            public NotificationRegistration(HostInteractionApiProvider owner, string key, INotificationHandle handle,
                UserScriptNotificationRequest request, string executionId, string clientId) : base(owner, key)
            {
                _handle = handle; _request = request; _executionId = executionId; _clientId = clientId;
                _clicked = (_, __) =>
                {
                    if (IsTerminal || Interlocked.Exchange(ref _wasClicked, 1) != 0) return;
                    Interlocked.Exchange(ref _clickPending, 1);
                    if (!_callbacksEnabled) { ResolveClick(false); return; }
                    _clickFallback = new Timer(___ => ResolveClick(false), null, 5000, Timeout.Infinite);
                    Publish("notification-click", new { clientId });
                };
                _closed = (_, __) => Finish();
            }
            public void Start()
            {
                _handle.Clicked += _clicked; _handle.Closed += _closed;
                _handle.Start();
                _handle.Completion.ContinueWith(task => { var observed = task.Exception; Finish(); }, TaskScheduler.Default);
            }
            public void ResolveClick(bool preventDefault)
            {
                if (IsTerminal || Interlocked.Exchange(ref _clickPending, 0) == 0) return;
                _clickFallback?.Dispose();
                _handle.ResolveClick(preventDefault);
            }
            public void StartFailed(Exception error) { _failure = error; Dispose(); }
            public void EndDocument()
            {
                _callbacksEnabled = false;
                if (Persistent) ResolveClick(false); else Dispose();
            }
            private void Publish(string eventName, object value)
            {
                if (_callbacksEnabled) Owner.Notification?.Invoke(Owner,
                    new ApiNotificationEventArgs(ScriptKey, _executionId, eventName, JsonSerializer.Serialize(value)));
            }
            private void Finish()
            {
                if (!TryTerminal()) return;
                try
                {
                    var error = _failure ?? (_handle.Completion.IsFaulted ? _handle.Completion.Exception.GetBaseException() : null);
                    if (error != null) Publish("notification-error", new { clientId = _clientId, details = error.Message });
                    else Publish("notification-done", new { clientId = _clientId, clicked = _wasClicked != 0 });
                }
                finally { _clickFallback?.Dispose(); _handle.Clicked -= _clicked; _handle.Closed -= _closed; _handle.Dispose(); }
            }
            public override void Dispose() { _handle.Dispose(); Finish(); }
        }

        private sealed class TabRegistration : Registration
        {
            private readonly ITabHandle _handle;
            private readonly ScriptKey _scriptKey;
            private readonly string _executionId;
            private readonly EventHandler _closed;
            private readonly string _clientId;
            public TabRegistration(HostInteractionApiProvider owner, string key, ITabHandle handle, ScriptKey scriptKey, string executionId, string clientId) : base(owner, key)
            {
                _handle = handle; _scriptKey = scriptKey; _executionId = executionId; _clientId = clientId;
                _closed = (_, __) => Terminalize();
            }
            public void Subscribe() { _handle.OnClose += _closed; }
            public async Task CloseAsync(CancellationToken cancellationToken)
            {
                await _handle.CloseAsync(cancellationToken).ConfigureAwait(false);
                if (_handle.Closed) Terminalize();
            }
            public void Terminalize()
            {
                if (!TryTerminal()) return;
                Owner.Notification?.Invoke(Owner, new ApiNotificationEventArgs(_scriptKey, _executionId, "tab-closed", JsonSerializer.Serialize(new { tabId = _handle.TabId, clientId = _clientId })));
                Dispose();
            }
            public override void Dispose() { _handle.OnClose -= _closed; _handle.Dispose(); }
        }

        private static object DownloadFailure(string clientId, Exception error)
        {
            var code = (error as BridgeProtocolException)?.Code;
            if (error is UnsupportedApiException) code = BridgeErrorCodes.NotSupported;
            var reason = code == BridgeErrorCodes.NotSupported ? "not_supported" :
                code == BridgeErrorCodes.PermissionDenied || code == BridgeErrorCodes.GrantDenied ? "not_permitted" :
                code == BridgeErrorCodes.Timeout || error is TimeoutException ? "timeout" : "not_succeeded";
            return new { clientId, error = reason, details = error?.Message, code };
        }

        private sealed class DownloadRegistration : Registration
        {
            private readonly DownloadRequest _request;
            private readonly string _executionId;
            private readonly string _clientId;
            private readonly IDisposable _bodyOwner;
            private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
            private IDownloadOperation _operation;
            private volatile bool _callbacksEnabled = true;
            private readonly EventHandler<UserScriptDownloadProgress> _progress;
            private readonly EventHandler _completed, _aborted, _timedOut;
            private readonly EventHandler<UserScriptDownloadFailure> _failed;
            public DownloadRegistration(HostInteractionApiProvider owner, string key, DownloadRequest request,
                string executionId, string clientId, IDisposable bodyOwner = null) : base(owner, key)
            {
                _request = request; _executionId = executionId; _clientId = clientId; _bodyOwner = bodyOwner;
                _progress = (_, progress) => Publish("download-progress", new { clientId, loaded = progress.Loaded, total = progress.Total });
                _completed = (_, __) =>
                {
                    var result = _operation.Result;
                    Terminal("download-complete", new { clientId, result = new
                    {
                        url = (result?.FinalUrl ?? request.Url).AbsoluteUri,
                        name = result?.Name ?? request.Name, loaded = result?.BytesReceived
                    } });
                };
                _failed = (_, failure) => Terminal("download-error", DownloadFailure(clientId, failure.Error));
                _aborted = (_, __) => Terminal("download-aborted", new { clientId, error = "not_succeeded", details = "The download was aborted." });
                _timedOut = (_, __) => Terminal("download-timeout", new { clientId, error = "timeout", details = "The download timed out." });
            }
            public CancellationToken CancellationToken => _lifetime.Token;
            public void Start(IDownloadOperation operation)
            {
                _operation = operation;
                operation.Progress += _progress; operation.Completed += _completed;
                operation.Failed += _failed; operation.Aborted += _aborted; operation.TimedOut += _timedOut;
                if (_lifetime.IsCancellationRequested) operation.Abort();
                operation.Completion.ContinueWith(task => { var observed = task.Exception; }, TaskScheduler.Default);
                operation.Start();
            }
            public void Abort()
            {
                if (IsTerminal) return;
                try { _lifetime.Cancel(); } catch (ObjectDisposedException) { }
                _operation?.Abort();
            }
            public void DetachCallbacks() { _callbacksEnabled = false; }
            public void StartFailed(Exception error)
            {
                Terminal(error is OperationCanceledException ? "download-aborted" : "download-error",
                    DownloadFailure(_clientId, error));
            }
            private void Publish(string eventName, object data)
            {
                if (_callbacksEnabled && !IsTerminal)
                    Owner.Notification?.Invoke(Owner, new ApiNotificationEventArgs(_request.ScriptKey, _executionId, eventName, JsonSerializer.Serialize(data)));
            }
            private void Terminal(string eventName, object data)
            {
                if (!TryTerminal()) return;
                try
                {
                    if (_callbacksEnabled) Owner.Notification?.Invoke(Owner, new ApiNotificationEventArgs(_request.ScriptKey, _executionId, eventName, JsonSerializer.Serialize(data)));
                }
                finally
                {
                    if (_operation != null)
                    {
                        _operation.Progress -= _progress; _operation.Completed -= _completed; _operation.Failed -= _failed;
                        _operation.Aborted -= _aborted; _operation.TimedOut -= _timedOut;
                        _operation.Dispose();
                    }
                    _bodyOwner?.Dispose();
                    _lifetime.Dispose();
                }
            }
            public override void Dispose() { DetachCallbacks(); Abort(); }
        }

        private static string ReadConflictAction(JsonElement parameters)
        {
            var value = ProviderParameters.OptionalString(parameters, "conflictAction") ?? "uniquify";
            if (value != "uniquify" && value != "overwrite" && value != "prompt")
                throw ProviderParameters.Invalid("conflictAction must be 'uniquify', 'overwrite', or 'prompt'.");
            return value;
        }

        private static TimeSpan? ReadTimeout(JsonElement parameters)
        {
            if (!parameters.TryGetProperty("timeout", out var value))
                return null;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var milliseconds) || milliseconds <= 0)
                throw ProviderParameters.Invalid("timeout must be a positive integer.");
            return TimeSpan.FromMilliseconds(milliseconds);
        }

        private async Task<ApiResult> RegisterMenuAsync(
            ApiInvocationContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var commandId = ReadMenuId(context.Parameters);
            var generation = ProviderParameters.OptionalString(context.Parameters, "generation");
            var clickedFrame = ProviderParameters.OptionalBoolean(context.Parameters, "clickedFrame");
            var registrationKey = context.ExecutionId + ":" + commandId;
            lock (_sync)
            {
                if (!_pendingMenus.Add(registrationKey))
                    throw ProviderParameters.Invalid("The menu command ID is already registered.");
            }
            IMenuRegistration registration = null;
            try
            {
                registration = await _menu.RegisterAsync(new MenuCommandRequest(
                    context.Installation.ScriptKey,
                    ProviderParameters.RequiredString(context.Parameters, "name"),
                    ProviderParameters.OptionalString(context.Parameters, "accessKey"),
                    ProviderParameters.OptionalString(context.Parameters, "title"),
                    ProviderParameters.OptionalBoolean(context.Parameters, "autoClose", true), context.Frame, clickedFrame),
                    input =>
                    {
                        lock (_sync)
                            if (_disposed || registration == null || !_menuRegistrations.TryGetValue(registrationKey, out var current) ||
                                !ReferenceEquals(current, registration)) return;
                        if (clickedFrame && (input?.Frame == null || input.Frame.BrowserSessionId != context.Frame.BrowserSessionId ||
                            input.Frame.DocumentId != context.Frame.DocumentId || input.Frame.FrameId != context.Frame.FrameId)) return;
                        using (var idDocument = JsonDocument.Parse(commandId))
                            Notification?.Invoke(this, new ApiNotificationEventArgs(context.Installation.ScriptKey,
                                context.ExecutionId, "menu-command", JsonSerializer.Serialize(new
                                {
                                    commandId = idDocument.RootElement, generation,
                                    input = new { kind = input?.Kind ?? "unknown", ctrlKey = input?.CtrlKey, shiftKey = input?.ShiftKey,
                                        altKey = input?.AltKey, metaKey = input?.MetaKey, button = input?.Button, key = input?.Key, code = input?.Code }
                                })));
                    },
                    cancellationToken).ConfigureAwait(false);
                if (registration == null)
                    throw new InvalidOperationException("The menu service returned no registration.");
                var providerDisposed = false;
                var executionEnded = false;
                IMenuRegistration previous = null;
                lock (_sync)
                {
                    _pendingMenus.Remove(registrationKey);
                    executionEnded = _endedPendingMenus.Remove(registrationKey);
                    providerDisposed = _disposed;
                    if (!providerDisposed && !executionEnded)
                    {
                        _menuRegistrations.TryGetValue(registrationKey, out previous);
                        _menuRegistrations[registrationKey] = registration;
                    }
                }
                if (providerDisposed || executionEnded)
                {
                    registration.Dispose();
                    registration = null;
                    if (providerDisposed)
                        throw new ObjectDisposedException(nameof(HostInteractionApiProvider));
                    throw new OperationCanceledException("The script execution ended while registering the menu command.", cancellationToken);
                }
                previous?.Dispose();
                return ApiResult.FromJson(commandId);
            }
            catch
            {
                lock (_sync)
                {
                    _pendingMenus.Remove(registrationKey);
                    _endedPendingMenus.Remove(registrationKey);
                }
                registration?.Dispose();
                throw;
            }
        }

        private ApiResult UnregisterMenu(ApiInvocationContext context)
        {
            var commandId = ReadMenuId(context.Parameters);
            var registrationKey = context.ExecutionId + ":" + commandId;
            IMenuRegistration registration;
            lock (_sync)
            {
                if (_pendingMenus.Contains(registrationKey)) _endedPendingMenus.Add(registrationKey);
                if (!_menuRegistrations.TryGetValue(registrationKey, out registration))
                    return ApiResult.Undefined;
                _menuRegistrations.Remove(registrationKey);
            }
            registration.Dispose();
            return ApiResult.Undefined;
        }

        private static string ReadMenuId(JsonElement parameters)
        {
            if (parameters.TryGetProperty("commandId", out var value) &&
                ((value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString())) ||
                (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var id) && id > 0))) return value.GetRawText();
            throw ProviderParameters.Invalid("commandId must be a nonempty string or positive integer.");
        }

        private static Uri ReadHttpUrl(JsonElement parameters, string name)
        {
            var text = ProviderParameters.RequiredString(parameters, name);
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw ProviderParameters.Invalid(name + " must be an absolute HTTP or HTTPS URL.");
            return uri;
        }

        private static string ValidateJson(string json)
        {
            if (json == null)
                throw ProviderParameters.Invalid("tab state must be a JSON object.");
            using (var document = JsonDocument.Parse(json))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw ProviderParameters.Invalid("tab state must be a JSON object.");
            }
            return json;
        }

        private static string BuildTabsJson(IReadOnlyDictionary<string, string> tabs)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    foreach (var tab in tabs.OrderBy(item => item.Key, StringComparer.Ordinal))
                    {
                        writer.WritePropertyName(tab.Key);
                        using (var value = JsonDocument.Parse(ValidateJson(tab.Value)))
                            value.RootElement.WriteTo(writer);
                    }
                    writer.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
