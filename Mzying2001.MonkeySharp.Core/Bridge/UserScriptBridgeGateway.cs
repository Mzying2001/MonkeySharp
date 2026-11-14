using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Security;
using Mzying2001.MonkeySharp.Core.Permissions;
using Mzying2001.MonkeySharp.Core.Runtime;
using Mzying2001.MonkeySharp.Core.Storage;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Bridge
{
    /// <summary>
    /// Validates and dispatches authenticated userscript bridge requests to host API providers.
    /// </summary>
    public sealed partial class UserScriptBridgeGateway : IUserScriptBridge, IDisposable
    {
        private static readonly HashSet<string> LocalApis = new HashSet<string>(StringComparer.Ordinal)
        {
            "GM.info", "GM.addStyle", "GM.addElement"
        };

        private readonly UserScriptEngine _engine;
        private readonly IUserScriptValueStore _store;
        private readonly IUserScriptPermissionPolicy _permissionPolicy;
        private readonly BridgeOptions _options;
        private readonly Dictionary<string, IUserScriptApiProvider> _providers =
            new Dictionary<string, IUserScriptApiProvider>(StringComparer.Ordinal);
        private readonly HashSet<IUserScriptApiProvider> _providerInstances =
            new HashSet<IUserScriptApiProvider>();
        private readonly object _stateLock = new object();
        private readonly Dictionary<string, PendingRequest> _pending =
            new Dictionary<string, PendingRequest>(StringComparer.Ordinal);
        private readonly HashSet<string> _seenRequests = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Queue<string>> _seenRequestOrder =
            new Dictionary<string, Queue<string>>(StringComparer.Ordinal);
        private int _activeDispatches;
        private bool _providerResourcesDisposed;
        private bool _disposed;

        /// <summary>Initializes a userscript bridge gateway.</summary>
        /// <param name="engine">The engine that owns active script executions.</param>
        /// <param name="store">The value store used by built-in storage APIs.</param>
        /// <param name="permissionPolicy">The host authorization policy, or <see langword="null"/> to allow declared grants.</param>
        /// <param name="providers">Additional host API providers.</param>
        /// <param name="options">Bridge limits and timeout settings, or <see langword="null"/> for defaults.</param>
        /// <param name="log">An optional sink for <c>GM.log</c> entries.</param>
        public UserScriptBridgeGateway(
            UserScriptEngine engine,
            IUserScriptValueStore store,
            IUserScriptPermissionPolicy permissionPolicy = null,
            IEnumerable<IUserScriptApiProvider> providers = null,
            BridgeOptions options = null,
            Action<UserScriptLogEntry> log = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _permissionPolicy = permissionPolicy ?? new AllowDeclaredPermissionsPolicy();
            _options = options ?? new BridgeOptions();
            RegisterProvider(new BasicApiProvider(store, log));
            if (providers != null)
            {
                foreach (var provider in providers)
                    RegisterProvider(provider);
            }
            _store.ValueChanged += StoreValueChanged;
            _engine.SetHostCapabilities(SupportedApis());
            _engine.ExecutionEnded += EngineExecutionEnded;
        }

        /// <summary>Occurs when JavaScript must be executed to deliver a provider notification.</summary>
        public event EventHandler<BridgeNotificationEventArgs> Notification;

        /// <summary>Occurs when bridge processing or an API provider produces a diagnostic.</summary>
        public event EventHandler<UserScriptDiagnostic> Diagnostic;

        /// <inheritdoc />
        public Task<string> DispatchAsync(string requestJson, CancellationToken cancellationToken)
        {
            if (!TryEnterDispatch())
            {
                return Task.FromResult(ProtocolJson.Error(
                    string.Empty,
                    BridgeErrorCodes.SessionExpired,
                    "The bridge is disposed."));
            }
            return DispatchEnteredAsync(requestJson, cancellationToken);
        }

        private async Task<string> DispatchEnteredAsync(
            string requestJson,
            CancellationToken cancellationToken)
        {
            try
            {
                return await DispatchCoreAsync(requestJson, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ExitDispatch();
            }
        }

        private async Task<string> DispatchCoreAsync(
            string requestJson,
            CancellationToken cancellationToken)
        {
            if (requestJson == null)
                return ProtocolJson.Error(string.Empty, BridgeErrorCodes.MalformedMessage, "The message is required.");
            if (Encoding.UTF8.GetByteCount(requestJson) > _options.MaxRequestBytes)
                return ProtocolJson.Error(string.Empty, BridgeErrorCodes.PayloadTooLarge, "The request exceeds the configured limit.");

            var correlationId = string.Empty;
            var isHello = false;
            try
            {
                using (var document = JsonDocument.Parse(requestJson))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                        return ProtocolJson.Error(string.Empty, BridgeErrorCodes.MalformedMessage, "The message must be an object.");
                    var root = document.RootElement;
                    correlationId = ReadOptionalString(root, "requestId") ?? string.Empty;
                    var type = ReadRequiredString(root, "type");
                    isHello = type == "hello";
                    var protocol = ReadRequiredInt32(root, "protocol");
                    if (protocol != InjectionPlan.CurrentProtocolVersion)
                    {
                        return Limit(ProtocolJson.Error(
                            ReadOptionalString(root, "requestId"),
                            BridgeErrorCodes.ProtocolVersion,
                            "Only bridge protocol " + InjectionPlan.CurrentProtocolVersion + " is supported.",
                            type == "hello"));
                    }

                    switch (type)
                    {
                        case "hello":
                            return await HandleHelloAsync(root, cancellationToken).ConfigureAwait(false);
                        case "request":
                            return await HandleRequestAsync(root, cancellationToken).ConfigureAwait(false);
                        case "cancel":
                            return HandleCancel(root);
                        default:
                            return Limit(ProtocolJson.Error(
                                ReadOptionalString(root, "requestId"),
                                BridgeErrorCodes.MalformedMessage,
                                "Unknown message type '" + type + "'."));
                    }
                }
            }
            catch (JsonException)
            {
                return Limit(ProtocolJson.Error(string.Empty, BridgeErrorCodes.MalformedMessage, "The message is not valid JSON."));
            }
            catch (BridgeProtocolException exception)
            {
                return Limit(ProtocolJson.Error(correlationId, exception.Code, exception.Message, isHello));
            }
            catch (OperationCanceledException)
            {
                return Limit(ProtocolJson.Error(correlationId, BridgeErrorCodes.Canceled, "The request was canceled.", isHello));
            }
            catch (Exception exception)
            {
                EmitDiagnostic("MSP999_INTERNAL", "An unexpected bridge error occurred.", exception);
                return Limit(ProtocolJson.Error(correlationId, BridgeErrorCodes.Internal, "An internal host error occurred.", isHello));
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            var disposeProviderResources = false;
            lock (_stateLock)
            {
                if (_disposed)
                    return;
                _disposed = true;
                foreach (var pending in _pending.Values)
                    pending.Cancellation.Cancel();
                _seenRequests.Clear();
                _seenRequestOrder.Clear();
                _listeners.Clear();
                _storageSnapshots.Clear();
                if (_activeDispatches == 0 && !_providerResourcesDisposed)
                {
                    _providerResourcesDisposed = true;
                    disposeProviderResources = true;
                }
            }
            _store.ValueChanged -= StoreValueChanged;
            _engine.ExecutionEnded -= EngineExecutionEnded;
            if (disposeProviderResources)
                DisposeProviderResources();
        }

        private bool TryEnterDispatch()
        {
            lock (_stateLock)
            {
                if (_disposed)
                    return false;
                _activeDispatches++;
                return true;
            }
        }

        private void ExitDispatch()
        {
            var disposeProviderResources = false;
            lock (_stateLock)
            {
                _activeDispatches--;
                if (_disposed && _activeDispatches == 0 && !_providerResourcesDisposed)
                {
                    _providerResourcesDisposed = true;
                    disposeProviderResources = true;
                }
            }
            if (disposeProviderResources)
                DisposeProviderResources();
        }

        private void DisposeProviderResources()
        {
            foreach (var source in _providerInstances.OfType<IUserScriptNotificationSource>())
                source.Notification -= ProviderNotification;
            foreach (var source in _providerInstances.OfType<IUserScriptDiagnosticSource>())
                source.Diagnostic -= ProviderDiagnostic;
            foreach (var disposable in _providerInstances.OfType<IDisposable>())
                disposable.Dispose();
        }

        private async Task<string> HandleRequestAsync(JsonElement root, CancellationToken cancellationToken)
        {
            var requestId = ReadRequiredString(root, "requestId");
            if (!Guid.TryParse(requestId, out _))
                return Limit(ProtocolJson.Error(requestId, BridgeErrorCodes.MalformedMessage, "requestId must be a UUID."));
            if (!TryReadExecution(root, out var execution))
                return Limit(ProtocolJson.Error(requestId, BridgeErrorCodes.SessionExpired, "The script execution session is not active."));
            if (!await _engine.IsInstallationActiveAsync(execution.Installation, cancellationToken).ConfigureAwait(false))
            {
                await _engine.InvalidateExecutionAsync(execution.Invocation.ExecutionId, CancellationToken.None).ConfigureAwait(false);
                return Limit(ProtocolJson.Error(requestId, BridgeErrorCodes.SessionExpired, "The installation changed or was disabled."));
            }
            var method = ReadRequiredString(root, "method");
            if (execution.Frame.ExecutionWorld == "isolated" && method == "GM.webRequest")
                return Limit(ProtocolJson.Error(requestId, BridgeErrorCodes.NotSupported, "Historical webRequest page-frame interception is not implemented for isolated contexts."));
            if (!root.TryGetProperty("params", out var parameters))
                return Limit(ProtocolJson.Error(requestId, BridgeErrorCodes.MalformedMessage, "The params field is required."));
            parameters = parameters.Clone();

            if (!TryBeginRequest(execution, requestId, out var pending, out var beginError))
                return Limit(ProtocolJson.Error(requestId, beginError, "The request cannot be started."));

            var longRunningHttp = IsLongRunningHttpExecute(method, parameters);
            using (var timeout = longRunningHttp ? null : new CancellationTokenSource())
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                RequestTokens(method, execution, pending, cancellationToken, timeout)))
            {
                try
                {
                    if (method.StartsWith("runtime.", StringComparison.Ordinal)) timeout?.CancelAfter(_options.RequestTimeout);
                    if (method == "runtime.reportError")
                    {
                        HandleRuntimeError(execution, requestId, parameters);
                        return Limit(ProtocolJson.Success(requestId, ApiResult.Undefined.Json));
                    }

                    if (method == "runtime.getResourceChunk")
                    {
                        if (!HasResourceCapability(execution.Installation.Definition.Metadata.Capabilities))
                            throw new BridgeProtocolException(BridgeErrorCodes.GrantDenied, "Resource access is not granted.");
                        var json = ResourcePackage(execution);
                        var chunkSize = Math.Max(1, (_options.MaxResponseBytes - 512) / 6);
                        var index = ReadRequiredInt32(parameters, "index");
                        if (index < 0 || (long)index * chunkSize >= json.Length)
                            throw ProviderParameters.Invalid("Invalid resource chunk index.");
                        var offset = index * chunkSize;
                        return Limit(ProtocolJson.Success(requestId, JsonSerializer.Serialize(new
                        { index, chunk = json.Substring(offset, Math.Min(chunkSize, json.Length - offset)) })));
                    }

                    if (method == "runtime.getStorageChunk" || method == "runtime.getStorageChanges" || method == "runtime.getStorageSnapshot")
                        if (!HasStorageCapability(execution.Installation.Definition.Metadata.Capabilities))
                            throw new BridgeProtocolException(BridgeErrorCodes.GrantDenied, "Storage access is not granted.");
                    if (method == "runtime.getStorageChunk")
                        return HandleStorageChunk(execution, parameters, requestId);
                    if (method == "runtime.getStorageChanges")
                        return await HandleStorageChangesAsync(execution, parameters, requestId, linked.Token).ConfigureAwait(false);

                    if (method == "runtime.getStorageSnapshot")
                    {
                        var snapshot = await BuildStorageBootstrapAsync(execution, linked.Token)
                            .ConfigureAwait(false);
                        return Limit(ProtocolJson.Success(requestId, JsonSerializer.Serialize(snapshot)));
                    }

                    if (!execution.Installation.Definition.Metadata.Capabilities.Contains(method))
                        return Limit(ProtocolJson.Error(requestId, BridgeErrorCodes.GrantDenied, "The script did not declare '" + method + "'."));

                    var authorization = new ApiAuthorizationRequest(
                        execution.Installation,
                        execution.Frame,
                        method,
                        ReadTarget(method, parameters, execution.Frame.Url),
                        Summarize(parameters),
                        SupportedApis());
                    PermissionDecision decision;
                    using (var authorizationLifetime = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, execution.Cancellation.Token))
                    {
                        decision = await AuthorizeAsync(authorization, authorizationLifetime.Token).ConfigureAwait(false);
                        authorizationLifetime.Token.ThrowIfCancellationRequested();
                    }
                    if (decision != PermissionDecision.Allow)
                        return Limit(ProtocolJson.Error(requestId, BridgeErrorCodes.PermissionDenied, "The host permission policy denied the request."));
                    if (!await _engine.IsInstallationActiveAsync(execution.Installation, linked.Token).ConfigureAwait(false))
                        return Limit(ProtocolJson.Error(requestId, BridgeErrorCodes.SessionExpired, "The installation changed while authorization was pending."));

                    timeout?.CancelAfter(_options.RequestTimeout);
                    ApiResult result;
                    if (method == "GM.addValueChangeListener")
                        result = AddValueListener(execution, parameters);
                    else if (method == "GM.removeValueChangeListener")
                        result = RemoveValueListener(execution, parameters);
                    else if (_providers.TryGetValue(method, out var provider))
                    {
                        result = await provider.InvokeAsync(new ApiInvocationContext(
                            execution.Installation, execution.Frame, execution.Invocation.ExecutionId, requestId, method, parameters,
                            (target, token) => ReauthorizeTargetAsync(execution, method, parameters, target, token)), linked.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        return Limit(ProtocolJson.Error(requestId, BridgeErrorCodes.NotSupported, "The host does not implement '" + method + "'."));
                    }
                    return Limit(ProtocolJson.Success(requestId, result.Json));
                }
                catch (BridgeProtocolException exception)
                {
                    return Limit(ProtocolJson.Error(requestId, exception.Code, exception.Message));
                }
                catch (UnsupportedApiException exception)
                {
                    return Limit(ProtocolJson.Error(requestId, BridgeErrorCodes.NotSupported, exception.Message));
                }
                catch (ResourceIntegrityException exception)
                {
                    EmitDiagnostic(exception.Code, "A declared resource failed integrity validation.", exception, execution, requestId);
                    return Limit(ProtocolJson.Error(requestId, BridgeErrorCodes.Internal, "A declared resource failed integrity validation."));
                }
                catch (OperationCanceledException)
                {
                    if (execution.Cancellation.IsCancellationRequested)
                        return Limit(ProtocolJson.Error(requestId, BridgeErrorCodes.SessionExpired, "The document session expired."));
                    if (timeout != null && timeout.IsCancellationRequested)
                        return Limit(ProtocolJson.Error(requestId, BridgeErrorCodes.Timeout, "The request timed out."));
                    return Limit(ProtocolJson.Error(requestId, BridgeErrorCodes.Canceled, "The request was canceled."));
                }
                catch (JsonException)
                {
                    return Limit(ProtocolJson.Error(requestId, BridgeErrorCodes.InvalidParams, "The parameters contain invalid JSON."));
                }
                catch (Exception exception)
                {
                    EmitDiagnostic(BridgeErrorCodes.Internal, "An API provider failed.", exception, execution, requestId);
                    return Limit(ProtocolJson.Error(requestId, BridgeErrorCodes.Internal, "An internal host error occurred."));
                }
                finally
                {
                    EndRequest(execution.Invocation.ExecutionId, requestId);
                }
            }
        }

        private static CancellationToken[] RequestTokens(
            string method,
            UserScriptEngine.ExecutionRecord execution,
            PendingRequest pending,
            CancellationToken cancellationToken,
            CancellationTokenSource timeout)
        {
            var tokens = new List<CancellationToken> { cancellationToken, pending.Cancellation.Token };
            if (!IsValueMutation(method)) tokens.Add(execution.Cancellation.Token);
            if (timeout != null) tokens.Add(timeout.Token);
            return tokens.ToArray();
        }

        private async Task<bool> ReauthorizeTargetAsync(
            UserScriptEngine.ExecutionRecord execution,
            string method,
            JsonElement parameters,
            string target,
            CancellationToken cancellationToken)
        {
            if ((method != "GM.download" && execution.Cancellation.IsCancellationRequested) ||
                !execution.Installation.Definition.Metadata.Capabilities.Contains(method))
                return false;
            var canPrompt = !execution.Cancellation.IsCancellationRequested &&
                await _engine.IsInstallationActiveAsync(execution.Installation, cancellationToken).ConfigureAwait(false);
            // Only a previously authorized download retains this callback beyond its
            // execution. It keeps the captured installation, but cannot reopen prompts.
            if (method != "GM.download" && !canPrompt) return false;
            var authorization = new ApiAuthorizationRequest(execution.Installation, execution.Frame, method, target,
                Summarize(parameters), SupportedApis(), canPrompt);
            if (!canPrompt) return await AuthorizeAsync(authorization, cancellationToken).ConfigureAwait(false) == PermissionDecision.Allow;
            using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, execution.Cancellation.Token))
                return await AuthorizeAsync(authorization, lifetime.Token).ConfigureAwait(false) == PermissionDecision.Allow;
        }

        private async Task<PermissionDecision> AuthorizeAsync(ApiAuthorizationRequest request, CancellationToken token)
        {
            if (!UserScriptPermissionTargets.IsDeclared(request)) return PermissionDecision.Deny;
            var decision = await _permissionPolicy.AuthorizeAsync(request, token).ConfigureAwait(false);
            if (decision == PermissionDecision.Prompt)
                throw new BridgeProtocolException(BridgeErrorCodes.NotSupported, "The host must configure InteractiveUserScriptPermissionPolicy with a prompt or an explicit RejectUserScriptPermissionPrompt.");
            return decision;
        }

        private static bool IsLongRunningHttpExecute(string method, JsonElement parameters)
        {
            if (!string.Equals(method, "GM.xmlHttpRequest", StringComparison.Ordinal) ||
                parameters.ValueKind != JsonValueKind.Object ||
                !parameters.TryGetProperty("operation", out var operation) ||
                operation.ValueKind != JsonValueKind.String)
                return false;
            return operation.GetString() == "execute" || operation.GetString() == "readBody";
        }

        private string HandleCancel(JsonElement root)
        {
            var requestId = ReadRequiredString(root, "requestId");
            if (!Guid.TryParse(requestId, out _))
                return Limit(ProtocolJson.Error(requestId, BridgeErrorCodes.MalformedMessage, "requestId must be a UUID."));
            if (!TryReadExecution(root, out var execution))
                return Limit(ProtocolJson.Error(requestId, BridgeErrorCodes.SessionExpired, "The script execution session is not active."));
            var key = PendingKey(execution.Invocation.ExecutionId, requestId);
            lock (_stateLock)
            {
                if (_pending.TryGetValue(key, out var pending))
                    pending.Cancellation.Cancel();
            }
            return Limit(ProtocolJson.Success(requestId, ApiResult.Undefined.Json));
        }

        private bool TryReadExecution(JsonElement root, out UserScriptEngine.ExecutionRecord execution)
        {
            var documentId = ReadRequiredString(root, "documentId");
            var scriptKey = ReadRequiredString(root, "scriptKey");
            var capability = ReadRequiredString(root, "capability");
            return _engine.TryGetExecution(documentId, scriptKey, capability, out execution);
        }

        private bool TryBeginRequest(
            UserScriptEngine.ExecutionRecord execution,
            string requestId,
            out PendingRequest pending,
            out string errorCode)
        {
            var key = PendingKey(execution.Invocation.ExecutionId, requestId);
            lock (_stateLock)
            {
                pending = null;
                if (_disposed)
                {
                    errorCode = BridgeErrorCodes.SessionExpired;
                    return false;
                }
                if (_pending.ContainsKey(key) || _seenRequests.Contains(key))
                {
                    errorCode = BridgeErrorCodes.MalformedMessage;
                    return false;
                }
                var documentPending = _pending.Values.Count(item =>
                    item.Execution.Frame.DocumentId == execution.Frame.DocumentId);
                if (documentPending >= _options.MaxPendingRequestsPerDocument)
                {
                    errorCode = BridgeErrorCodes.PayloadTooLarge;
                    return false;
                }
                pending = new PendingRequest(execution);
                _pending.Add(key, pending);
                RememberRequest(execution.Invocation.ExecutionId, key);
                errorCode = null;
                return true;
            }
        }

        private void EndRequest(string executionId, string requestId)
        {
            var key = PendingKey(executionId, requestId);
            lock (_stateLock)
            {
                if (_pending.TryGetValue(key, out var pending))
                {
                    _pending.Remove(key);
                    pending.Cancellation.Dispose();
                }
            }
        }

        private void RememberRequest(string executionId, string key)
        {
            if (!_seenRequestOrder.TryGetValue(executionId, out var order))
            {
                order = new Queue<string>();
                _seenRequestOrder.Add(executionId, order);
            }
            _seenRequests.Add(key);
            order.Enqueue(key);
            while (order.Count > _options.MaxReplayEntriesPerExecution)
                _seenRequests.Remove(order.Dequeue());
        }

        private static bool IsValueMutation(string method)
        {
            return method == "GM.setValue" || method == "GM.deleteValue" ||
                method == "GM.setValues" || method == "GM.deleteValues";
        }

        private void RegisterProvider(IUserScriptApiProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));
            foreach (var method in provider.Methods)
            {
                if (string.IsNullOrEmpty(method) || _providers.ContainsKey(method))
                    throw new ArgumentException("API method '" + method + "' is registered more than once.", nameof(provider));
                _providers.Add(method, provider);
            }
            if (_providerInstances.Add(provider) && provider is IUserScriptNotificationSource source)
                source.Notification += ProviderNotification;
            if (provider is IUserScriptDiagnosticSource diagnosticSource)
                diagnosticSource.Diagnostic += ProviderDiagnostic;
        }

        private void ProviderDiagnostic(object sender, UserScriptDiagnostic diagnostic)
        {
            Diagnostic?.Invoke(this, diagnostic);
        }

        private void ProviderNotification(object sender, ApiNotificationEventArgs notification)
        {
            var executions = _engine.GetExecutionsForScript(notification.ScriptKey);
            if (notification.ExecutionId != null)
                executions = new ReadOnlyCollection<UserScriptEngine.ExecutionRecord>(
                    executions.Where(item => item.Invocation.ExecutionId == notification.ExecutionId).ToList());
            foreach (var execution in executions)
            {
                Notification?.Invoke(this, new BridgeNotificationEventArgs(
                    execution.Frame,
                    execution.Invocation.ExecutionId,
                    execution.Invocation.DeliveryToken,
                    notification.EventName,
                    notification.DataJson));
            }
        }

        private void EngineExecutionEnded(UserScriptEngine.ExecutionRecord execution)
        {
            var handlers = Notification;
            if (handlers != null)
                foreach (EventHandler<BridgeNotificationEventArgs> handler in handlers.GetInvocationList())
                    try { handler(this, new BridgeNotificationEventArgs(execution.Frame, execution.Invocation.ExecutionId,
                        execution.Invocation.DeliveryToken, "execution-ended", "{}")); }
                    catch (Exception exception) { System.Diagnostics.Trace.TraceError(exception.ToString()); }
            lock (_stateLock)
            {
                _listeners.Remove(execution.Invocation.ExecutionId);
                _storageSnapshots.Remove(execution.Invocation.ExecutionId);
                if (_seenRequestOrder.TryGetValue(execution.Invocation.ExecutionId, out var order))
                {
                    foreach (var key in order)
                        _seenRequests.Remove(key);
                    _seenRequestOrder.Remove(execution.Invocation.ExecutionId);
                }
            }
            foreach (var observer in _providerInstances.OfType<IUserScriptExecutionObserver>())
            {
                try
                {
                    observer.OnExecutionEnded(execution.Invocation.ExecutionId);
                }
                catch (Exception exception)
                {
                    EmitDiagnostic(
                        BridgeErrorCodes.Internal,
                        "An API provider failed to release execution state.",
                        exception,
                        execution);
                }
            }
        }

        private IEnumerable<string> SupportedApis()
        {
            return _providers.Keys
                .Concat(new[] { "GM.addValueChangeListener", "GM.removeValueChangeListener" })
                .Concat(LocalApis)
                .Distinct(StringComparer.Ordinal);
        }

        private string Limit(string response)
        {
            if (Encoding.UTF8.GetByteCount(response) <= _options.MaxResponseBytes)
                return response;
            try
            {
                using (var document = JsonDocument.Parse(response))
                {
                    var root = document.RootElement;
                    var hello = ReadOptionalString(root, "type") == "hello-result";
                    var requestId = ReadOptionalString(root, "requestId") ?? string.Empty;
                    return ProtocolJson.Error(requestId, BridgeErrorCodes.PayloadTooLarge, "The response exceeds the configured limit.", hello);
                }
            }
            catch (JsonException)
            {
                return ProtocolJson.Error(string.Empty, BridgeErrorCodes.PayloadTooLarge, "The response exceeds the configured limit.");
            }
        }

        private void HandleRuntimeError(
            UserScriptEngine.ExecutionRecord execution,
            string requestId,
            JsonElement parameters)
        {
            EnsureObject(parameters);
            var message = ReadOptionalString(parameters, "message") ?? "The userscript failed.";
            var code = ReadOptionalString(parameters, "code");
            if (!string.Equals(code, "MSC413_COMPATIBILITY_MUTATION_FAILED", StringComparison.Ordinal))
                code = "MSR300_SCRIPT_EXCEPTION";
            EmitDiagnostic(code, message, null, execution, requestId);
        }

        private void EmitDiagnostic(
            string code,
            string message,
            Exception exception = null,
            UserScriptEngine.ExecutionRecord execution = null,
            string requestId = null)
        {
            Diagnostic?.Invoke(this, new UserScriptDiagnostic(
                code,
                exception == null ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
                message,
                exception,
                execution?.Invocation.ScriptKey,
                execution?.Frame.DocumentId,
                execution?.Frame.FrameId,
                requestId));
        }

        private static string ReadTarget(string method, JsonElement parameters, Uri frameUrl)
        {
            if (parameters.ValueKind != JsonValueKind.Object)
                return null;
            if (method == "GM.download" && parameters.TryGetProperty("operation", out var downloadOperation) && downloadOperation.ValueKind == JsonValueKind.String &&
                downloadOperation.GetString() == "createBody") return frameUrl?.AbsoluteUri;
            if (string.Equals(method, "GM.cookie", StringComparison.Ordinal))
            {
                if (parameters.TryGetProperty("details", out var details) &&
                    details.ValueKind == JsonValueKind.Object &&
                    details.TryGetProperty("url", out var nestedUrl) &&
                    nestedUrl.ValueKind == JsonValueKind.String)
                    return nestedUrl.GetString();
                if (parameters.TryGetProperty("url", out var directUrl) && directUrl.ValueKind == JsonValueKind.String)
                    return directUrl.GetString();
                return frameUrl?.AbsoluteUri;
            }
            foreach (var name in new[] { "url", "name", "key" })
            {
                if (parameters.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString();
            }
            return null;
        }

        private static string Summarize(JsonElement parameters)
        {
            if (parameters.ValueKind != JsonValueKind.Object)
                return parameters.ValueKind.ToString();
            return string.Join(",", parameters.EnumerateObject().Select(item => item.Name));
        }

        private static void EnsureObject(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Object)
                throw new BridgeProtocolException(BridgeErrorCodes.InvalidParams, "The params value must be an object.");
        }

        private static string ReadRequiredString(JsonElement value, string propertyName)
        {
            if (!value.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
                throw new BridgeProtocolException(BridgeErrorCodes.MalformedMessage, "The '" + propertyName + "' field must be a string.");
            var result = property.GetString();
            if (string.IsNullOrEmpty(result))
                throw new BridgeProtocolException(BridgeErrorCodes.MalformedMessage, "The '" + propertyName + "' field cannot be empty.");
            return result;
        }

        private static string ReadOptionalString(JsonElement value, string propertyName)
        {
            return value.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }

        private static int ReadRequiredInt32(JsonElement value, string propertyName)
        {
            if (!value.TryGetProperty(propertyName, out var property) ||
                property.ValueKind != JsonValueKind.Number ||
                !property.TryGetInt32(out var result))
            {
                throw new BridgeProtocolException(BridgeErrorCodes.MalformedMessage, "The '" + propertyName + "' field must be an integer.");
            }
            return result;
        }

        private static string PendingKey(string executionId, string requestId)
        {
            return executionId + ":" + requestId;
        }

        private sealed class PendingRequest
        {
            public PendingRequest(UserScriptEngine.ExecutionRecord execution)
            {
                Execution = execution;
            }

            public UserScriptEngine.ExecutionRecord Execution { get; }
            public CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();
        }

    }
}
