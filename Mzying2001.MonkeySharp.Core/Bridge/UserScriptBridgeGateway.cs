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
    public sealed class UserScriptBridgeGateway : IUserScriptBridge, IDisposable
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
        private readonly Dictionary<string, Dictionary<int, ValueListener>> _listeners =
            new Dictionary<string, Dictionary<int, ValueListener>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Queue<MutationOrigin>> _mutationOrigins =
            new Dictionary<string, Queue<MutationOrigin>>(StringComparer.Ordinal);
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
                            "Only bridge protocol 1 is supported.",
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
                _mutationOrigins.Clear();
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

        private async Task<string> HandleHelloAsync(JsonElement root, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryReadExecution(root, out var execution))
            {
                return Limit(ProtocolJson.Error(
                    null,
                    BridgeErrorCodes.SessionExpired,
                    "The script execution session is not active.",
                    true));
            }

            var grants = execution.Installation.Definition.Metadata.Grants;
            var supported = SupportedApis().Where(grants.Contains);
            var compatibility = new Dictionary<string, object>
            {
                ["profile"] = execution.Invocation.Compatibility.Profile.ToString(),
                ["strict"] = execution.Invocation.Compatibility.Strict,
                ["legacyGlobals"] = execution.Invocation.Compatibility.LegacyGlobals
            };
            if (execution.Invocation.Compatibility.SynchronousStorageMirror && HasStorageCapability(grants))
            {
                try
                {
                    using (var timeout = new CancellationTokenSource(_options.CompatibilityBootstrapTimeout))
                    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken, execution.Cancellation.Token, timeout.Token))
                    {
                        compatibility["storage"] = await BuildStorageBootstrapAsync(execution, linked.Token)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
                    !execution.Cancellation.IsCancellationRequested)
                {
                    EmitDiagnostic(
                        "MSC410_COMPATIBILITY_BOOTSTRAP_FAILED",
                        "The compatibility storage snapshot timed out.",
                        null,
                        execution);
                }
                catch (Exception exception)
                {
                    EmitDiagnostic(
                        "MSC410_COMPATIBILITY_BOOTSTRAP_FAILED",
                        "The compatibility storage snapshot could not be prepared.",
                        exception,
                        execution);
                }
            }
            if (execution.Invocation.Compatibility.SynchronousResourceSnapshot && HasResourceCapability(grants))
            {
                try
                {
                    using (var timeout = new CancellationTokenSource(_options.CompatibilityBootstrapTimeout))
                    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken, execution.Cancellation.Token, timeout.Token))
                    {
                        compatibility["resources"] = await BuildResourceBootstrapAsync(execution, linked.Token)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
                    !execution.Cancellation.IsCancellationRequested)
                {
                    EmitDiagnostic(
                        "MSC410_COMPATIBILITY_BOOTSTRAP_FAILED",
                        "The compatibility resource snapshot timed out.",
                        null,
                        execution);
                }
                catch (Exception exception)
                {
                    EmitDiagnostic(
                        "MSC410_COMPATIBILITY_BOOTSTRAP_FAILED",
                        "The compatibility resource snapshot could not be prepared.",
                        exception,
                        execution);
                }
            }
            return Limit(ProtocolJson.Hello(_options, supported, compatibility));
        }

        private async Task<object> BuildStorageBootstrapAsync(
            UserScriptEngine.ExecutionRecord execution,
            CancellationToken cancellationToken)
        {
            var scriptKey = execution.Installation.ScriptKey.ToString();
            IReadOnlyDictionary<string, string> snapshot;
            if (_store is IUserScriptValueSnapshotProvider provider)
            {
                snapshot = await provider.GetSnapshotAsync(scriptKey, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                var keys = await _store.ListKeysAsync(scriptKey, cancellationToken).ConfigureAwait(false);
                foreach (var key in keys)
                {
                    var value = await _store.GetAsync(scriptKey, key, cancellationToken).ConfigureAwait(false);
                    if (value.Exists)
                        values[key] = value.JsonValue;
                }
                snapshot = values;
                EmitDiagnostic(
                    "MSC411_COMPATIBILITY_SNAPSHOT_DEGRADED",
                    "The value store does not provide an atomic snapshot.",
                    null,
                    execution);
            }

            var jsonValues = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var item in snapshot)
            {
                using (var value = JsonDocument.Parse(item.Value))
                    jsonValues[item.Key] = value.RootElement.Clone();
            }
            var result = new Dictionary<string, object>
            {
                ["complete"] = true,
                ["values"] = jsonValues
            };
            if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(result)) > _options.MaxCompatibilityBootstrapBytes)
            {
                EmitDiagnostic(
                    "MSC412_COMPATIBILITY_BOOTSTRAP_LIMIT",
                    "The compatibility storage snapshot exceeds the configured limit.",
                    null,
                    execution);
                return new Dictionary<string, object> { ["complete"] = false, ["values"] = new Dictionary<string, object>() };
            }
            return result;
        }

        private static bool HasStorageCapability(IReadOnlyList<string> grants)
        {
            return grants.Contains("GM.getValue") || grants.Contains("GM.setValue") ||
                grants.Contains("GM.deleteValue") || grants.Contains("GM.listValues") ||
                grants.Contains("GM.addValueChangeListener") || grants.Contains("GM.removeValueChangeListener");
        }

        private static bool HasResourceCapability(IReadOnlyList<string> grants)
        {
            return grants.Contains("GM.getResourceText") || grants.Contains("GM.getResourceURL");
        }

        private async Task<object> BuildResourceBootstrapAsync(
            UserScriptEngine.ExecutionRecord execution,
            CancellationToken cancellationToken)
        {
            var resources = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var provider in _providerInstances.OfType<IUserScriptCompatibilityBootstrapProvider>())
            {
                var contribution = await provider.PrepareAsync(
                    execution.Installation,
                    execution.Frame,
                    execution.Installation.Definition.Metadata.Grants,
                    cancellationToken).ConfigureAwait(false);
                if (contribution == null)
                    continue;
                foreach (var item in contribution)
                {
                    resources[item.Key] = new Dictionary<string, object>
                    {
                        ["text"] = item.Value.Text,
                        ["url"] = item.Value.Url
                    };
                }
            }
            var result = new Dictionary<string, object>
            {
                ["complete"] = true,
                ["values"] = resources
            };
            if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(result)) > _options.MaxCompatibilityBootstrapBytes)
            {
                EmitDiagnostic(
                    "MSC412_COMPATIBILITY_BOOTSTRAP_LIMIT",
                    "The compatibility resource snapshot exceeds the configured limit.",
                    null,
                    execution);
                return new Dictionary<string, object> { ["complete"] = false, ["values"] = new Dictionary<string, object>() };
            }
            return result;
        }

        private async Task<string> HandleRequestAsync(JsonElement root, CancellationToken cancellationToken)
        {
            var requestId = ReadRequiredString(root, "requestId");
            if (!Guid.TryParse(requestId, out _))
                return Limit(ProtocolJson.Error(requestId, BridgeErrorCodes.MalformedMessage, "requestId must be a UUID."));
            if (!TryReadExecution(root, out var execution))
                return Limit(ProtocolJson.Error(requestId, BridgeErrorCodes.SessionExpired, "The script execution session is not active."));
            var method = ReadRequiredString(root, "method");
            if (!root.TryGetProperty("params", out var parameters))
                return Limit(ProtocolJson.Error(requestId, BridgeErrorCodes.MalformedMessage, "The params field is required."));
            parameters = parameters.Clone();

            if (!TryBeginRequest(execution, requestId, out var pending, out var beginError))
                return Limit(ProtocolJson.Error(requestId, beginError, "The request cannot be started."));

            var longRunningHttp = IsLongRunningHttpExecute(method, parameters);
            using (var timeout = longRunningHttp ? null : new CancellationTokenSource(_options.RequestTimeout))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                RequestTokens(method, execution, pending, cancellationToken, timeout)))
            {
                try
                {
                    if (method == "runtime.reportError")
                    {
                        HandleRuntimeError(execution, requestId, parameters);
                        return Limit(ProtocolJson.Success(requestId, ApiResult.Undefined.Json));
                    }

                    if (method == "runtime.getStorageSnapshot")
                    {
                        var snapshot = await BuildStorageBootstrapAsync(execution, linked.Token)
                            .ConfigureAwait(false);
                        return Limit(ProtocolJson.Success(requestId, JsonSerializer.Serialize(snapshot)));
                    }

                    if (!execution.Installation.Definition.Metadata.Grants.Contains(method))
                        return Limit(ProtocolJson.Error(requestId, BridgeErrorCodes.GrantDenied, "The script did not declare '" + method + "'."));

                    var authorization = new ApiAuthorizationRequest(
                        execution.Installation,
                        execution.Frame,
                        method,
                        ReadTarget(method, parameters, execution.Frame.Url),
                        Summarize(parameters),
                        SupportedApis());
                    var decision = await _permissionPolicy.AuthorizeAsync(authorization, linked.Token).ConfigureAwait(false);
                    if (decision != PermissionDecision.Allow)
                        return Limit(ProtocolJson.Error(requestId, BridgeErrorCodes.PermissionDenied, "The host permission policy denied the request."));

                    ApiResult result;
                    if (method == "GM.addValueChangeListener")
                        result = AddValueListener(execution, parameters);
                    else if (method == "GM.removeValueChangeListener")
                        result = RemoveValueListener(execution, parameters);
                    else if (_providers.TryGetValue(method, out var provider))
                    {
                        var mutation = IsValueMutation(method)
                            ? BeginMutation(execution, parameters)
                            : null;
                        try
                        {
                            result = await provider.InvokeAsync(new ApiInvocationContext(
                                execution.Installation,
                                execution.Frame,
                                execution.Invocation.ExecutionId,
                                requestId,
                                method,
                                parameters,
                                (target, token) => ReauthorizeTargetAsync(
                                    execution, method, parameters, target, token)), linked.Token).ConfigureAwait(false);
                        }
                        finally
                        {
                            if (mutation != null)
                                RemovePendingMutation(mutation);
                        }
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
            if (execution.Cancellation.IsCancellationRequested ||
                !execution.Installation.Definition.Metadata.Grants.Contains(method))
                return false;
            var authorization = new ApiAuthorizationRequest(
                execution.Installation,
                execution.Frame,
                method,
                target,
                Summarize(parameters),
                SupportedApis());
            return await _permissionPolicy.AuthorizeAsync(authorization, cancellationToken)
                .ConfigureAwait(false) == PermissionDecision.Allow;
        }

        private static bool IsLongRunningHttpExecute(string method, JsonElement parameters)
        {
            if (!string.Equals(method, "GM.xmlHttpRequest", StringComparison.Ordinal) ||
                parameters.ValueKind != JsonValueKind.Object ||
                !parameters.TryGetProperty("operation", out var operation) ||
                operation.ValueKind != JsonValueKind.String)
                return false;
            return string.Equals(operation.GetString(), "execute", StringComparison.Ordinal);
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

        private ApiResult AddValueListener(UserScriptEngine.ExecutionRecord execution, JsonElement parameters)
        {
            EnsureObject(parameters);
            var listenerId = ReadRequiredInt32(parameters, "listenerId");
            var key = ReadRequiredString(parameters, "key");
            if (listenerId <= 0 || key.Length == 0)
                throw new BridgeProtocolException(BridgeErrorCodes.InvalidParams, "listenerId and key are invalid.");
            lock (_stateLock)
            {
                if (!_listeners.TryGetValue(execution.Invocation.ExecutionId, out var executionListeners))
                {
                    executionListeners = new Dictionary<int, ValueListener>();
                    _listeners.Add(execution.Invocation.ExecutionId, executionListeners);
                }
                if (executionListeners.ContainsKey(listenerId))
                    throw new BridgeProtocolException(BridgeErrorCodes.InvalidParams, "The listener ID is already registered.");
                executionListeners.Add(listenerId, new ValueListener(listenerId, key));
            }
            return ApiResult.FromValue(true);
        }

        private ApiResult RemoveValueListener(UserScriptEngine.ExecutionRecord execution, JsonElement parameters)
        {
            EnsureObject(parameters);
            var listenerId = ReadRequiredInt32(parameters, "listenerId");
            lock (_stateLock)
            {
                var removed = _listeners.TryGetValue(execution.Invocation.ExecutionId, out var executionListeners) &&
                    executionListeners.Remove(listenerId);
                return ApiResult.FromValue(removed);
            }
        }

        private void StoreValueChanged(object sender, UserScriptValueChangedEventArgs args)
        {
            if (!ScriptKey.TryParse(args.ScriptKey, out var scriptKey))
                return;
            var mutation = TakeMutation(args.ScriptKey, args.Key);
            var originExecutionId = args.OriginExecutionId ?? mutation?.OriginExecutionId;
            var mutationId = args.MutationId ?? mutation?.MutationId;
            var executions = _engine.GetExecutionsForScript(scriptKey).ToList();
            var deliveries = new List<Tuple<UserScriptEngine.ExecutionRecord, ValueListener>>();
            lock (_stateLock)
            {
                foreach (var execution in executions)
                {
                    if (_listeners.TryGetValue(execution.Invocation.ExecutionId, out var listeners))
                    {
                        deliveries.AddRange(listeners.Values
                            .Where(item => item.Key == args.Key)
                            .Select(item => Tuple.Create(execution, item)));
                    }
                }
            }

            var data = ProtocolJson.ValueChangeData(
                0,
                args.Key,
                args.OldValue.Exists ? args.OldValue.JsonValue : null,
                args.NewValue.Exists ? args.NewValue.JsonValue : null,
                mutationId,
                originExecutionId,
                args.Sequence);
            foreach (var execution in executions)
            {
                Notification?.Invoke(this, new BridgeNotificationEventArgs(
                    execution.Frame,
                    execution.Invocation.ExecutionId,
                    execution.Invocation.DeliveryToken,
                    "storage-sync",
                    data));
            }
            foreach (var delivery in deliveries)
            {
                var listenerData = ReplaceListenerId(data, delivery.Item2.Id);
                Notification?.Invoke(this, new BridgeNotificationEventArgs(
                    delivery.Item1.Frame,
                    delivery.Item1.Invocation.ExecutionId,
                    delivery.Item1.Invocation.DeliveryToken,
                    "value-change",
                    listenerData));
            }
        }

        private static bool IsValueMutation(string method)
        {
            return method == "GM.setValue" || method == "GM.deleteValue";
        }

        private MutationOrigin BeginMutation(UserScriptEngine.ExecutionRecord execution, JsonElement parameters)
        {
            var key = ReadRequiredString(parameters, "key");
            var mutation = new MutationOrigin(
                Guid.NewGuid().ToString("D"), execution.Invocation.ExecutionId, execution.Installation.ScriptKey.ToString(), key);
            lock (_stateLock)
            {
                var bucketKey = mutation.ScriptKey + ":" + mutation.Key;
                if (!_mutationOrigins.TryGetValue(bucketKey, out var queue))
                {
                    queue = new Queue<MutationOrigin>();
                    _mutationOrigins.Add(bucketKey, queue);
                }
                queue.Enqueue(mutation);
            }
            return mutation;
        }

        private MutationOrigin TakeMutation(string scriptKey, string key)
        {
            lock (_stateLock)
            {
                var bucketKey = scriptKey + ":" + key;
                if (!_mutationOrigins.TryGetValue(bucketKey, out var queue) || queue.Count == 0)
                    return null;
                var mutation = queue.Dequeue();
                if (queue.Count == 0)
                    _mutationOrigins.Remove(bucketKey);
                return mutation;
            }
        }

        private void RemovePendingMutation(MutationOrigin mutation)
        {
            lock (_stateLock)
            {
                var bucketKey = mutation.ScriptKey + ":" + mutation.Key;
                if (!_mutationOrigins.TryGetValue(bucketKey, out var queue))
                    return;
                var remaining = queue.Where(item => item.MutationId != mutation.MutationId).ToList();
                if (remaining.Count == 0)
                    _mutationOrigins.Remove(bucketKey);
                else
                    _mutationOrigins[bucketKey] = new Queue<MutationOrigin>(remaining);
            }
        }

        private static string ReplaceListenerId(string data, int listenerId)
        {
            using (var document = JsonDocument.Parse(data))
            {
                var root = document.RootElement;
                var values = new Dictionary<string, object>
                {
                    ["listenerId"] = listenerId,
                    ["key"] = root.GetProperty("key").GetString(),
                    ["oldValue"] = root.GetProperty("oldValue").Clone(),
                    ["newValue"] = root.GetProperty("newValue").Clone(),
                    ["remote"] = true
                };
                if (root.TryGetProperty("mutationId", out var mutationId))
                    values["mutationId"] = mutationId.GetString();
                if (root.TryGetProperty("originExecutionId", out var origin))
                    values["originExecutionId"] = origin.GetString();
                if (root.TryGetProperty("sequence", out var sequence))
                    values["sequence"] = sequence.GetInt64();
                return JsonSerializer.Serialize(values);
            }
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
            lock (_stateLock)
            {
                _listeners.Remove(execution.Invocation.ExecutionId);
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

        private sealed class ValueListener
        {
            public ValueListener(int id, string key)
            {
                Id = id;
                Key = key;
            }

            public int Id { get; }
            public string Key { get; }
        }

        private sealed class MutationOrigin
        {
            public MutationOrigin(string mutationId, string originExecutionId, string scriptKey, string key)
            {
                MutationId = mutationId;
                OriginExecutionId = originExecutionId;
                ScriptKey = scriptKey;
                Key = key;
            }

            public string MutationId { get; }
            public string OriginExecutionId { get; }
            public string ScriptKey { get; }
            public string Key { get; }
        }
    }
}
