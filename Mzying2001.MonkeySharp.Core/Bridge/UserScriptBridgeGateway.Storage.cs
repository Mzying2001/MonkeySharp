using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Runtime;
using Mzying2001.MonkeySharp.Core.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Bridge
{
    public sealed partial class UserScriptBridgeGateway
    {
        private readonly Dictionary<string, StorageTransfer> _storageSnapshots = new Dictionary<string, StorageTransfer>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<int, ValueListener>> _listeners =
            new Dictionary<string, Dictionary<int, ValueListener>>(StringComparer.Ordinal);

        private async Task<object> BuildStorageBootstrapAsync(
            UserScriptEngine.ExecutionRecord execution,
            CancellationToken cancellationToken)
        {
            var scriptKey = execution.Installation.ScriptKey.ToString();
            var snapshot = await _store.GetSnapshotAsync(scriptKey, cancellationToken).ConfigureAwait(false);
            var json = JsonSerializer.Serialize(new
            {
                complete = true, sequence = snapshot.Sequence,
                entries = snapshot.Select(item => new[] { item.Key, item.Value.EncodedValue }).ToArray()
            });
            if (Encoding.UTF8.GetByteCount(json) > _options.MaxCompatibilityBootstrapBytes)
                throw new BridgeProtocolException(BridgeErrorCodes.PayloadTooLarge, "The complete storage snapshot exceeds MaxCompatibilityBootstrapBytes.");
            var transfer = new StorageTransfer(Guid.NewGuid().ToString("D"), json);
            lock (_stateLock)
            {
                execution.Cancellation.Token.ThrowIfCancellationRequested();
                _storageSnapshots[execution.Invocation.ExecutionId] = transfer;
            }
            if (Encoding.UTF8.GetByteCount(json) <= _options.MaxResponseBytes / 4)
            {
                using (var document = JsonDocument.Parse(json)) return document.RootElement.Clone();
            }
            return new { complete = false, snapshotId = transfer.Id, sequence = snapshot.Sequence,
                chunkCount = (json.Length + BootstrapChunkSize - 1) / BootstrapChunkSize, jsonLength = json.Length };
        }

        private int BootstrapChunkSize => Math.Max(1, (_options.MaxResponseBytes - 512) / 6);

        private static bool HasStorageCapability(IReadOnlyList<string> grants)
        {
            return grants.Contains("GM.getValue") || grants.Contains("GM.setValue") ||
                grants.Contains("GM.getValues") || grants.Contains("GM.setValues") || grants.Contains("GM.deleteValues") ||
                grants.Contains("GM.deleteValue") || grants.Contains("GM.listValues") ||
                grants.Contains("GM.addValueChangeListener") || grants.Contains("GM.removeValueChangeListener");
        }

        private string HandleStorageChunk(UserScriptEngine.ExecutionRecord execution, JsonElement parameters, string requestId)
        {
            StorageTransfer transfer;
            lock (_stateLock) _storageSnapshots.TryGetValue(execution.Invocation.ExecutionId, out transfer);
            if (transfer == null || transfer.Id != ReadRequiredString(parameters, "snapshotId"))
                throw new BridgeProtocolException(BridgeErrorCodes.SessionExpired, "The storage snapshot transfer expired; capture a new snapshot.");
            var index = ReadRequiredInt32(parameters, "index");
            if (index < 0 || (long)index * BootstrapChunkSize >= transfer.Json.Length)
                throw ProviderParameters.Invalid("Invalid storage chunk index.");
            var offset = index * BootstrapChunkSize;
            return Limit(ProtocolJson.Success(requestId, JsonSerializer.Serialize(new
            { index, snapshotId = transfer.Id, chunk = transfer.Json.Substring(offset, Math.Min(BootstrapChunkSize, transfer.Json.Length - offset)) })));
        }

        private async Task<string> HandleStorageChangesAsync(UserScriptEngine.ExecutionRecord execution, JsonElement parameters, string requestId, CancellationToken token)
        {
            if (!parameters.TryGetProperty("afterSequence", out var watermark) ||
                watermark.ValueKind != JsonValueKind.Number || !watermark.TryGetInt64(out var after) || after < 0)
                throw ProviderParameters.Invalid("afterSequence must be a nonnegative integer.");
            var replay = await _store.GetChangesAsync(execution.Installation.ScriptKey.ToString(), after, token).ConfigureAwait(false);
            var changes = replay.Changes.Select(change => JsonDocument.Parse(ProtocolJson.ValueChangeData(
                0, change.Key, change.OldValue.Value?.EncodedValue, change.NewValue.Value?.EncodedValue,
                change.MutationId, change.OriginExecutionId, change.Sequence, change.MutationMethod))).ToArray();
            try
            {
                var json = JsonSerializer.Serialize(new { complete = replay.Complete, sequence = replay.Sequence,
                    changes = changes.Select(document => document.RootElement.Clone()).ToArray() });
                if (!replay.Complete || Encoding.UTF8.GetByteCount(json) > _options.MaxResponseBytes / 2)
                    EmitDiagnostic("MSC414_STORAGE_REPLAY_RESET", "Storage replay requires a new snapshot; listener events outside retained or transferable history cannot be reconstructed.", null, execution, requestId);
                if (Encoding.UTF8.GetByteCount(json) > _options.MaxResponseBytes / 2)
                    json = JsonSerializer.Serialize(new { complete = false, sequence = replay.Sequence });
                return Limit(ProtocolJson.Success(requestId, json));
            }
            finally { foreach (var document in changes) document.Dispose(); }
        }

        private ApiResult AddValueListener(UserScriptEngine.ExecutionRecord execution, JsonElement parameters)
        {
            EnsureObject(parameters);
            var listenerId = ReadRequiredInt32(parameters, "listenerId");
            var key = BasicApiProvider.ReadKey(parameters);
            if (listenerId <= 0)
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
            var originExecutionId = args.OriginExecutionId;
            var mutationId = args.MutationId;
            var executions = _engine.GetExecutionsForScript(scriptKey).Where(execution => HasStorageCapability(execution.Installation.Definition.Metadata.Capabilities)).ToList();
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
                args.OldValue.Exists ? args.OldValue.Value.EncodedValue : null,
                args.NewValue.Exists ? args.NewValue.Value.EncodedValue : null,
                mutationId,
                originExecutionId,
                args.Sequence,
                args.MutationMethod);
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
                    ["oldValueExists"] = root.GetProperty("oldValueExists").GetBoolean(),
                    ["newValueExists"] = root.GetProperty("newValueExists").GetBoolean(),
                    ["remote"] = true
                };
                if (root.TryGetProperty("mutationId", out var mutationId))
                    values["mutationId"] = mutationId.GetString();
                if (root.TryGetProperty("mutationMethod", out var mutationMethod))
                    values["mutationMethod"] = mutationMethod.GetString();
                if (root.TryGetProperty("originExecutionId", out var origin))
                    values["originExecutionId"] = origin.GetString();
                if (root.TryGetProperty("sequence", out var sequence))
                    values["sequence"] = sequence.GetInt64();
                return JsonSerializer.Serialize(values);
            }
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

        private sealed class StorageTransfer
        {
            public StorageTransfer(string id, string json) { Id = id; Json = json; }
            public string Id { get; }
            public string Json { get; }
        }
    }
}
