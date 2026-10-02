using Mzying2001.MonkeySharp.Core.Domain;
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
        private readonly IReadOnlyCollection<string> _methods;
        private readonly object _sync = new object();
        private readonly Dictionary<string, IMenuRegistration> _menuRegistrations =
            new Dictionary<string, IMenuRegistration>(StringComparer.Ordinal);
        private readonly HashSet<string> _pendingMenus = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _endedPendingMenus = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, INotificationHandle> _notificationHandles =
            new Dictionary<string, INotificationHandle>(StringComparer.Ordinal);
        private readonly Dictionary<string, ITabHandle> _tabHandles =
            new Dictionary<string, ITabHandle>(StringComparer.Ordinal);
        private readonly Dictionary<string, IDownloadOperation> _downloadOperations =
            new Dictionary<string, IDownloadOperation>(StringComparer.Ordinal);
        private bool _disposed;

        /// <summary>Initializes a provider from the host interaction services that are available.</summary>
        /// <param name="menu">The optional menu command service.</param>
        /// <param name="notifications">The optional notification service.</param>
        /// <param name="clipboard">The optional clipboard service.</param>
        /// <param name="tabs">The optional tab-opening service.</param>
        /// <param name="downloads">The optional download service.</param>
        /// <param name="tabState">The optional tab-state service.</param>
        public HostInteractionApiProvider(
            IMenuService menu = null,
            INotificationService notifications = null,
            IClipboardService clipboard = null,
            ITabService tabs = null,
            IDownloadService downloads = null,
            ITabStateService tabState = null)
        {
            if (menu == null && notifications == null && clipboard == null && tabs == null &&
                downloads == null && tabState == null)
                throw new ArgumentException("At least one host interaction service is required.");
            _menu = menu;
            _notifications = notifications;
            _clipboard = clipboard;
            _tabs = tabs;
            _downloads = downloads;
            _tabState = tabState;
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
                    await _clipboard.SetTextAsync(
                        ProviderParameters.RequiredString(context.Parameters, "text"),
                        ProviderParameters.OptionalString(context.Parameters, "type") ?? "text/plain",
                        cancellationToken).ConfigureAwait(false);
                    return ApiResult.Undefined;
                case "GM.openInTab":
                    if (ProviderParameters.OptionalBoolean(context.Parameters, "close"))
                    {
                        var tabId = ProviderParameters.RequiredString(context.Parameters, "tabId");
                        return await CloseTabAsync(context, tabId, cancellationToken).ConfigureAwait(false);
                    }
                    var tabUrl = ReadHttpUrl(context.Parameters, "url");
                    var tab = await _tabs.OpenAsync(new OpenTabRequest(
                        tabUrl,
                        ProviderParameters.OptionalBoolean(context.Parameters, "active", true),
                        ProviderParameters.OptionalBoolean(context.Parameters, "insert"),
                        ProviderParameters.OptionalBoolean(context.Parameters, "setParent")), cancellationToken)
                        .ConfigureAwait(false);
                    if (tab == null || string.IsNullOrEmpty(tab.TabId))
                        throw new InvalidOperationException("The tab service returned no tab handle.");
                    TrackTab(context, tab);
                    return ApiResult.FromValue(new { id = tab.TabId });
                case "GM.download":
                    if (string.Equals(ProviderParameters.OptionalString(context.Parameters, "operation"), "abort", StringComparison.Ordinal))
                        return AbortDownload(context);
                    var download = await _downloads.DownloadAsync(new DownloadRequest(
                        context.Installation.ScriptKey,
                        ReadHttpUrl(context.Parameters, "url"),
                        ProviderParameters.OptionalString(context.Parameters, "name"),
                        ProviderParameters.OptionalBoolean(context.Parameters, "saveAs"),
                        ReadHeaders(context.Parameters),
                        ReadConflictAction(context.Parameters),
                        ReadTimeout(context.Parameters)), cancellationToken)
                        .ConfigureAwait(false);
                    if (download == null || string.IsNullOrEmpty(download.DownloadId))
                        throw new InvalidOperationException("The download service returned no operation.");
                    var clientId = ProviderParameters.OptionalString(context.Parameters, "clientId") ?? Guid.NewGuid().ToString("D");
                    TrackDownload(context, clientId, download);
                    return ApiResult.FromValue(new { id = download.DownloadId, clientId = clientId });
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
            INotificationHandle[] notifications;
            ITabHandle[] tabs;
            IDownloadOperation[] downloads;
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
                _pendingMenus.Clear();
                _endedPendingMenus.Clear();
                _disposed = true;
            }
            foreach (var registration in registrations)
                registration.Dispose();
            foreach (var handle in notifications)
                handle.Dispose();
            foreach (var handle in tabs)
                handle.Dispose();
            foreach (var operation in downloads)
                operation.Dispose();
        }

        /// <inheritdoc />
        public void OnExecutionEnded(string executionId)
        {
            if (string.IsNullOrEmpty(executionId))
                throw new ArgumentException("The execution ID is required.", nameof(executionId));
            var prefix = executionId + ":";
            IMenuRegistration[] registrations;
            INotificationHandle[] notifications;
            ITabHandle[] tabs;
            IDownloadOperation[] downloads;
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
                foreach (var key in _notificationHandles.Keys.Where(item => item.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
                    _notificationHandles.Remove(key);
                foreach (var key in _tabHandles.Keys.Where(item => item.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
                    _tabHandles.Remove(key);
                foreach (var key in _downloadOperations.Keys.Where(item => item.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
                    _downloadOperations.Remove(key);
            }
            foreach (var registration in registrations)
                registration.Dispose();
            foreach (var handle in notifications)
                handle.Dispose();
            foreach (var handle in tabs)
                handle.Dispose();
            foreach (var operation in downloads)
                operation.Dispose();
        }

        private ApiResult ShowNotification(ApiInvocationContext context, CancellationToken cancellationToken)
        {
            var handle = _notifications.ShowAsync(new UserScriptNotificationRequest(
                context.Installation.ScriptKey,
                ProviderParameters.OptionalString(context.Parameters, "title") ??
                    context.Installation.Definition.Metadata.Name,
                ProviderParameters.RequiredString(context.Parameters, "text"),
                ProviderParameters.OptionalString(context.Parameters, "imageUrl")), cancellationToken);
            if (handle == null)
                throw new InvalidOperationException("The notification service returned no handle.");
            var notificationId = Guid.NewGuid().ToString("D");
            var key = context.ExecutionId + ":" + notificationId;
            handle.Clicked += (_, __) => Notification?.Invoke(this, new ApiNotificationEventArgs(
                context.Installation.ScriptKey, context.ExecutionId, "notification-click",
                JsonSerializer.Serialize(new { notificationId })));
            handle.Closed += (_, __) => Notification?.Invoke(this, new ApiNotificationEventArgs(
                context.Installation.ScriptKey, context.ExecutionId, "notification-done",
                JsonSerializer.Serialize(new { notificationId })));
            lock (_sync)
            {
                if (_disposed)
                {
                    handle.Dispose();
                    throw new ObjectDisposedException(nameof(HostInteractionApiProvider));
                }
                _notificationHandles[key] = handle;
            }
            return ApiResult.FromValue(new { id = notificationId });
        }

        private async Task<ApiResult> CloseTabAsync(
            ApiInvocationContext context, string tabId, CancellationToken cancellationToken)
        {
            ITabHandle handle;
            lock (_sync)
                _tabHandles.TryGetValue(context.ExecutionId + ":" + tabId, out handle);
            if (handle == null)
                return ApiResult.FromValue(false);
            await handle.CloseAsync(cancellationToken).ConfigureAwait(false);
            return ApiResult.FromValue(true);
        }

        private void TrackTab(ApiInvocationContext context, ITabHandle tab)
        {
            var notified = 0;
            EventHandler closed = null;
            closed = (_, __) =>
            {
                if (Interlocked.Exchange(ref notified, 1) != 0)
                    return;
                Notification?.Invoke(this, new ApiNotificationEventArgs(
                    context.Installation.ScriptKey, context.ExecutionId, "tab-closed",
                    JsonSerializer.Serialize(new { tabId = tab.TabId })));
            };
            tab.OnClose += closed;
            lock (_sync)
                _tabHandles[context.ExecutionId + ":" + tab.TabId] = tab;
            if (tab.Closed)
                closed(tab, EventArgs.Empty);
        }

        private ApiResult AbortDownload(ApiInvocationContext context)
        {
            var downloadId = ProviderParameters.RequiredString(context.Parameters, "downloadId");
            IDownloadOperation operation;
            lock (_sync)
                _downloadOperations.TryGetValue(context.ExecutionId + ":" + downloadId, out operation);
            if (operation == null)
                return ApiResult.FromValue(false);
            operation.Abort();
            return ApiResult.FromValue(true);
        }

        private void TrackDownload(ApiInvocationContext context, string clientId, IDownloadOperation operation)
        {
            var key = context.ExecutionId + ":" + operation.DownloadId;
            operation.Progress += (_, progress) => Notification?.Invoke(this, new ApiNotificationEventArgs(
                context.Installation.ScriptKey, context.ExecutionId, "download-progress",
                JsonSerializer.Serialize(new { downloadId = operation.DownloadId, clientId, loaded = progress.Loaded, total = progress.Total })));
            operation.Completed += (_, __) => Notification?.Invoke(this, new ApiNotificationEventArgs(
                context.Installation.ScriptKey, context.ExecutionId, "download-complete",
                JsonSerializer.Serialize(new { downloadId = operation.DownloadId, clientId })));
            operation.Failed += (_, failure) => Notification?.Invoke(this, new ApiNotificationEventArgs(
                context.Installation.ScriptKey, context.ExecutionId, "download-error",
                JsonSerializer.Serialize(new { downloadId = operation.DownloadId, clientId,
                    error = "not_succeeded", details = failure.Error?.Message })));
            operation.Aborted += (_, __) => Notification?.Invoke(this, new ApiNotificationEventArgs(
                context.Installation.ScriptKey, context.ExecutionId, "download-aborted",
                JsonSerializer.Serialize(new { downloadId = operation.DownloadId, clientId,
                    error = "not_succeeded", details = "The download was aborted." })));
            operation.TimedOut += (_, __) => Notification?.Invoke(this, new ApiNotificationEventArgs(
                context.Installation.ScriptKey, context.ExecutionId, "download-timeout",
                JsonSerializer.Serialize(new { downloadId = operation.DownloadId, clientId,
                    error = "timeout", details = "The download timed out." })));
            lock (_sync)
                _downloadOperations[key] = operation;
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
            var commandId = ProviderParameters.RequiredInt32(context.Parameters, "commandId");
            if (commandId <= 0)
                throw ProviderParameters.Invalid("commandId must be positive.");
            var registrationKey = context.ExecutionId + ":" + commandId;
            lock (_sync)
            {
                if (_menuRegistrations.ContainsKey(registrationKey) || !_pendingMenus.Add(registrationKey))
                    throw ProviderParameters.Invalid("The menu command ID is already registered.");
            }
            IMenuRegistration registration = null;
            try
            {
                registration = await _menu.RegisterAsync(new MenuCommandRequest(
                    context.Installation.ScriptKey,
                    ProviderParameters.RequiredString(context.Parameters, "name"),
                    ProviderParameters.OptionalString(context.Parameters, "accessKey")),
                    () => Notification?.Invoke(this, new ApiNotificationEventArgs(
                        context.Installation.ScriptKey,
                        context.ExecutionId,
                        "menu-command",
                        JsonSerializer.Serialize(new { commandId }))),
                    cancellationToken).ConfigureAwait(false);
                if (registration == null)
                    throw new InvalidOperationException("The menu service returned no registration.");
                var providerDisposed = false;
                var executionEnded = false;
                lock (_sync)
                {
                    _pendingMenus.Remove(registrationKey);
                    executionEnded = _endedPendingMenus.Remove(registrationKey);
                    providerDisposed = _disposed;
                    if (!providerDisposed && !executionEnded)
                        _menuRegistrations.Add(registrationKey, registration);
                }
                if (providerDisposed || executionEnded)
                {
                    registration.Dispose();
                    registration = null;
                    if (providerDisposed)
                        throw new ObjectDisposedException(nameof(HostInteractionApiProvider));
                    throw new OperationCanceledException("The script execution ended while registering the menu command.", cancellationToken);
                }
                return ApiResult.FromValue(commandId);
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
            var commandId = ProviderParameters.RequiredInt32(context.Parameters, "commandId");
            var registrationKey = context.ExecutionId + ":" + commandId;
            IMenuRegistration registration;
            lock (_sync)
            {
                if (!_menuRegistrations.TryGetValue(registrationKey, out registration))
                    return ApiResult.FromValue(false);
                _menuRegistrations.Remove(registrationKey);
            }
            registration.Dispose();
            return ApiResult.FromValue(true);
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
