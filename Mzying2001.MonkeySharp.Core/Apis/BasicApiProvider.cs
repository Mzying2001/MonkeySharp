using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Storage;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Apis
{
    /// <summary>
    /// Implements logging and persistent-value GM APIs.
    /// </summary>
    public sealed class BasicApiProvider : IUserScriptApiProvider
    {
        private static readonly IReadOnlyCollection<string> SupportedMethods =
            new ReadOnlyCollection<string>(new[]
            {
                "GM.log", "GM.getValue", "GM.setValue", "GM.deleteValue", "GM.listValues",
                "GM.getValues", "GM.setValues", "GM.deleteValues"
            });

        private readonly IUserScriptValueStore _store;
        private readonly Action<UserScriptLogEntry> _log;

        /// <summary>Initializes the basic API provider.</summary>
        /// <param name="store">The store used by persistent-value APIs.</param>
        /// <param name="log">An optional sink for <c>GM.log</c> entries.</param>
        public BasicApiProvider(IUserScriptValueStore store, Action<UserScriptLogEntry> log = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _log = log;
        }

        /// <inheritdoc />
        public IReadOnlyCollection<string> Methods => SupportedMethods;

        /// <inheritdoc />
        public async Task<ApiResult> InvokeAsync(ApiInvocationContext context, CancellationToken cancellationToken)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var scriptKey = context.Installation.ScriptKey.ToString();
            switch (context.Method)
            {
                case "GM.log":
                    EnsureObject(context.Parameters);
                    if (!context.Parameters.TryGetProperty("value", out var logValue))
                        throw InvalidParams("The value parameter is required.");
                    _log?.Invoke(new UserScriptLogEntry(
                        context.Installation.ScriptKey,
                        context.Frame.DocumentId,
                        logValue.GetRawText()));
                    return ApiResult.Undefined;

                case "GM.getValue":
                    var getKey = ReadKey(context.Parameters);
                    var stored = await _store.GetAsync(scriptKey, getKey, cancellationToken).ConfigureAwait(false);
                    return ApiResult.FromValue(new { exists = stored.Exists, value = stored.Value?.EncodedValue });

                case "GM.setValue":
                    var setKey = ReadKey(context.Parameters);
                    if (!context.Parameters.TryGetProperty("value", out var value))
                        throw InvalidParams("The value parameter is required.");
                    await _store.ApplyAsync(scriptKey, new[] { new UserScriptValueMutation(setKey, ReadValue(value)) }, Origin(context), cancellationToken).ConfigureAwait(false);
                    return ApiResult.Undefined;

                case "GM.deleteValue":
                    await _store.ApplyAsync(scriptKey, new[] { new UserScriptValueMutation(ReadKey(context.Parameters), null) }, Origin(context), cancellationToken).ConfigureAwait(false);
                    return ApiResult.Undefined;

                case "GM.listValues":
                    EnsureObject(context.Parameters);
                    return ApiResult.FromValue(await _store.ListKeysAsync(scriptKey, cancellationToken).ConfigureAwait(false));

                case "GM.getValues":
                    return await GetValuesAsync(scriptKey, context.Parameters, cancellationToken).ConfigureAwait(false);

                case "GM.setValues":
                    var values = ReadValues(context.Parameters, "values").ToDictionary(
                        item => item.Key, item => ReadValue(item.Value), StringComparer.Ordinal);
                    await _store.ApplyAsync(scriptKey, values.Select(item => new UserScriptValueMutation(item.Key, item.Value)), Origin(context), cancellationToken).ConfigureAwait(false);
                    return ApiResult.Undefined;

                case "GM.deleteValues":
                    var keys = ReadKeys(context.Parameters);
                    await _store.ApplyAsync(scriptKey, keys.Select(key => new UserScriptValueMutation(key, null)), Origin(context), cancellationToken).ConfigureAwait(false);
                    return ApiResult.Undefined;

                default:
                    throw new UnsupportedApiException("The API '" + context.Method + "' is not supported.");
            }
        }

        private async Task<ApiResult> GetValuesAsync(
            string scriptKey, JsonElement parameters, CancellationToken cancellationToken)
        {
            EnsureObject(parameters);
            var all = !parameters.TryGetProperty("keys", out var requested) || requested.ValueKind == JsonValueKind.Null;
            IReadOnlyList<string> keys = all ? null : ReadKeys(parameters);
            var snapshot = await _store.GetSnapshotAsync(scriptKey, cancellationToken).ConfigureAwait(false);
            if (all) return ApiResult.FromValue(snapshot.ToDictionary(item => item.Key, item => item.Value.EncodedValue, StringComparer.Ordinal));
            var available = snapshot.ToDictionary(item => item.Key, item => item.Value.EncodedValue, StringComparer.Ordinal);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in keys)
            {
                if (available.TryGetValue(key, out var encoded)) result[key] = encoded;
            }
            return ApiResult.FromValue(result);
        }

        private static UserScriptValueMutationOrigin Origin(ApiInvocationContext context) =>
            new UserScriptValueMutationOrigin(context.RequestId, context.ExecutionId, context.Method);

        private static Dictionary<string, JsonElement> ReadValues(JsonElement parameters, string name)
        {
            EnsureObject(parameters);
            if (!parameters.TryGetProperty(name, out var values) || values.ValueKind != JsonValueKind.Object)
                throw InvalidParams("The " + name + " parameter must be an object.");
            var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var item in values.EnumerateObject())
            {
                result[item.Name] = item.Value;
            }
            return result;
        }

        private static IReadOnlyList<string> ReadKeys(JsonElement parameters)
        {
            EnsureObject(parameters);
            if (!parameters.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Array)
                throw InvalidParams("The keys parameter must be an array of strings.");
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in keys.EnumerateArray())
            {
                if (key.ValueKind != JsonValueKind.String)
                    throw InvalidParams("Storage keys must be strings.");
                if (seen.Add(key.GetString())) result.Add(key.GetString());
            }
            return result;
        }

        internal static string ReadKey(JsonElement parameters)
        {
            EnsureObject(parameters);
            if (!parameters.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.String)
                throw InvalidParams("The key parameter must be a string.");
            var value = key.GetString();
            return value;
        }

        private static UserScriptValue ReadValue(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.String) throw InvalidParams("A typed storage value is required.");
            try { return UserScriptValue.FromEncoded(value.GetString()); }
            catch (Exception exception) when (exception is FormatException || exception is JsonException || exception is OverflowException)
            { throw InvalidParams("Invalid storage value encoding."); }
        }

        private static void EnsureObject(JsonElement parameters)
        {
            if (parameters.ValueKind != JsonValueKind.Object)
                throw InvalidParams("The params value must be an object.");
        }

        private static BridgeProtocolException InvalidParams(string message)
        {
            return new BridgeProtocolException("MSP007_INVALID_PARAMS", message);
        }
    }
}
