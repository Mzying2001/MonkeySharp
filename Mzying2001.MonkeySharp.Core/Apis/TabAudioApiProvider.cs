using Mzying2001.MonkeySharp.Core.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Apis
{
    /// <summary>Actual audio state; unknown optional fields are omitted at the bridge.</summary>
    public sealed class TabAudioState : EventArgs
    {
        /// <summary>Creates a state from native tab observations.</summary>
        public TabAudioState(bool isMuted, string muteReason = null, bool? isAudible = null)
        { IsMuted = isMuted; MuteReason = muteReason; IsAudible = isAudible; }
        /// <summary>Gets the observed mute state.</summary>
        public bool IsMuted { get; }
        /// <summary>Gets a known cause, or null when the host cannot identify it.</summary>
        public string MuteReason { get; }
        /// <summary>Gets a reliable audible observation, or null when unavailable.</summary>
        public bool? IsAudible { get; }
        internal Dictionary<string, object> ToValue()
        {
            var value = new Dictionary<string, object> { ["isMuted"] = IsMuted };
            if (MuteReason != null) value["muteReason"] = MuteReason;
            if (IsAudible.HasValue) value["isAudible"] = IsAudible.Value;
            return value;
        }
        internal Dictionary<string, object> ToChange()
        {
            var value = new Dictionary<string, object> { ["muted"] = IsMuted ? (object)(MuteReason ?? "unknown") : false };
            if (IsAudible.HasValue) value["audible"] = IsAudible.Value;
            return value;
        }
    }

    /// <summary>A browser-independent service for exactly one host tab.</summary>
    public interface ITabAudioService
    {
        /// <summary>Gets whether this backend has a reliable audible source.</summary>
        bool SupportsAudible { get; }
        /// <summary>Occurs after an observed state change, including other host mute controls.</summary>
        event EventHandler<TabAudioState> StateChanged;
        /// <summary>Reads actual native tab state.</summary>
        Task<TabAudioState> GetStateAsync(CancellationToken cancellationToken);
        /// <summary>Changes native mute state and verifies its readback.</summary>
        Task<TabAudioState> SetMuteAsync(bool isMuted, CancellationToken cancellationToken);
    }

    /// <summary>Provides GM.audio/GM_audio operations and execution-owned function listeners.</summary>
    public sealed class TabAudioApiProvider : IUserScriptApiProvider, IUserScriptNotificationSource, IUserScriptExecutionObserver, IDisposable
    {
        private readonly ITabAudioService _service;
        private readonly object _sync = new object();
        private readonly Dictionary<string, Listener> _listeners = new Dictionary<string, Listener>();
        private bool _disposed;
        /// <summary>Creates a provider over an application-owned tab service.</summary>
        public TabAudioApiProvider(ITabAudioService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            service.StateChanged += StateChanged;
        }
        /// <inheritdoc />
        public IReadOnlyCollection<string> Methods { get; } = new[] { "GM.audio" };
        /// <inheritdoc />
        public event EventHandler<ApiNotificationEventArgs> Notification;
        /// <inheritdoc />
        public async Task<ApiResult> InvokeAsync(ApiInvocationContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync) if (_disposed) throw new ObjectDisposedException(nameof(TabAudioApiProvider));
            ProviderParameters.RequireObject(context.Parameters);
            var operation = ProviderParameters.RequiredString(context.Parameters, "operation");
            if (operation == "getState") return ApiResult.FromValue((await _service.GetStateAsync(cancellationToken).ConfigureAwait(false)).ToValue());
            if (operation == "setMute")
            {
                var muted = ProviderParameters.RequiredProperty(context.Parameters, "isMuted");
                if (muted.ValueKind != JsonValueKind.True && muted.ValueKind != JsonValueKind.False) throw ProviderParameters.Invalid("isMuted must be a boolean.");
                var state = await _service.SetMuteAsync(muted.GetBoolean(), cancellationToken).ConfigureAwait(false);
                return ApiResult.FromValue(new { isMuted = state.IsMuted });
            }
            if (operation == "addListener" || operation == "removeListener")
            {
                var value = ProviderParameters.RequiredProperty(context.Parameters, "listenerId");
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var id) || id <= 0) throw ProviderParameters.Invalid("listenerId must be a positive integer.");
                lock (_sync)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_disposed) throw new ObjectDisposedException(nameof(TabAudioApiProvider));
                    var key = context.ExecutionId + ":" + id;
                    if (operation == "addListener") _listeners[key] = new Listener(context.Installation.ScriptKey, context.ExecutionId, id);
                    else _listeners.Remove(key);
                }
                return ApiResult.Undefined;
            }
            throw ProviderParameters.Invalid("Unknown audio operation.");
        }
        private void StateChanged(object sender, TabAudioState state)
        {
            Listener[] listeners;
            lock (_sync) { if (_disposed) return; listeners = _listeners.Values.ToArray(); }
            foreach (var listener in listeners)
                Notification?.Invoke(this, new ApiNotificationEventArgs(listener.ScriptKey, listener.ExecutionId, "audio-state",
                    JsonSerializer.Serialize(new { listenerId = listener.Id, change = state.ToChange() })));
        }
        /// <inheritdoc />
        public void OnExecutionEnded(string executionId)
        {
            lock (_sync)
                foreach (var key in _listeners.Where(item => item.Value.ExecutionId == executionId).Select(item => item.Key).ToArray()) _listeners.Remove(key);
        }
        /// <inheritdoc />
        public void Dispose()
        {
            _service.StateChanged -= StateChanged;
            lock (_sync) { _disposed = true; _listeners.Clear(); }
        }
        private sealed class Listener
        {
            public Listener(ScriptKey scriptKey, string executionId, int id) { ScriptKey = scriptKey; ExecutionId = executionId; Id = id; }
            public ScriptKey ScriptKey { get; }
            public string ExecutionId { get; }
            public int Id { get; }
        }
    }
}
