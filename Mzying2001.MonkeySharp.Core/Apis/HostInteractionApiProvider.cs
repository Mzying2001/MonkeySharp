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
        private bool _disposed;

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

        public event EventHandler<ApiNotificationEventArgs> Notification;
        public IReadOnlyCollection<string> Methods => _methods;

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
                    await _notifications.ShowAsync(new UserScriptNotificationRequest(
                        context.Installation.ScriptKey,
                        ProviderParameters.OptionalString(context.Parameters, "title") ??
                            context.Installation.Definition.Metadata.Name,
                        ProviderParameters.RequiredString(context.Parameters, "text"),
                        ProviderParameters.OptionalString(context.Parameters, "imageUrl")), cancellationToken)
                        .ConfigureAwait(false);
                    return ApiResult.Undefined;
                case "GM.setClipboard":
                    await _clipboard.SetTextAsync(
                        ProviderParameters.RequiredString(context.Parameters, "text"),
                        ProviderParameters.OptionalString(context.Parameters, "type") ?? "text/plain",
                        cancellationToken).ConfigureAwait(false);
                    return ApiResult.Undefined;
                case "GM.openInTab":
                    var tabUrl = ReadHttpUrl(context.Parameters, "url");
                    var tab = await _tabs.OpenAsync(new OpenTabRequest(
                        tabUrl,
                        ProviderParameters.OptionalBoolean(context.Parameters, "active", true),
                        ProviderParameters.OptionalBoolean(context.Parameters, "insert"),
                        ProviderParameters.OptionalBoolean(context.Parameters, "setParent")), cancellationToken)
                        .ConfigureAwait(false);
                    return ApiResult.FromValue(new { id = tab?.TabId });
                case "GM.download":
                    var download = await _downloads.DownloadAsync(new DownloadRequest(
                        context.Installation.ScriptKey,
                        ReadHttpUrl(context.Parameters, "url"),
                        ProviderParameters.OptionalString(context.Parameters, "name"),
                        ProviderParameters.OptionalBoolean(context.Parameters, "saveAs")), cancellationToken)
                        .ConfigureAwait(false);
                    return ApiResult.FromValue(new { id = download?.DownloadId });
                case "GM.getTab":
                    return ApiResult.FromJson(ValidateJson(await _tabState.GetAsync(
                        context.Installation.ScriptKey, context.Frame, cancellationToken).ConfigureAwait(false)));
                case "GM.saveTab":
                    ProviderParameters.RequireObject(context.Parameters);
                    if (!context.Parameters.TryGetProperty("value", out var value))
                        throw ProviderParameters.Invalid("value is required.");
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

        public void Dispose()
        {
            if (_disposed)
                return;
            IMenuRegistration[] registrations;
            lock (_sync)
            {
                if (_disposed)
                    return;
                registrations = _menuRegistrations.Values.ToArray();
                _menuRegistrations.Clear();
                _pendingMenus.Clear();
                _endedPendingMenus.Clear();
                _disposed = true;
            }
            foreach (var registration in registrations)
                registration.Dispose();
        }

        public void OnExecutionEnded(string executionId)
        {
            if (string.IsNullOrEmpty(executionId))
                throw new ArgumentException("The execution ID is required.", nameof(executionId));
            var prefix = executionId + ":";
            IMenuRegistration[] registrations;
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
            }
            foreach (var registration in registrations)
                registration.Dispose();
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
                return "null";
            using (JsonDocument.Parse(json)) { }
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
                        using (var value = JsonDocument.Parse(tab.Value))
                            value.RootElement.WriteTo(writer);
                    }
                    writer.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
