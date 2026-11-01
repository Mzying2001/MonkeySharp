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
                    if (stored.Exists)
                        return ApiResult.FromJson(stored.JsonValue);
                    return context.Parameters.TryGetProperty("defaultValue", out var defaultValue)
                        ? ApiResult.FromJson(defaultValue.GetRawText())
                        : ApiResult.Undefined;

                case "GM.setValue":
                    var setKey = ReadKey(context.Parameters);
                    if (!context.Parameters.TryGetProperty("value", out var value))
                        throw InvalidParams("The value parameter is required.");
                    await _store.SetAsync(scriptKey, setKey, value.GetRawText(), cancellationToken).ConfigureAwait(false);
                    return ApiResult.Undefined;

                case "GM.deleteValue":
                    return ApiResult.FromValue(await _store.DeleteAsync(
                        scriptKey,
                        ReadKey(context.Parameters),
                        cancellationToken).ConfigureAwait(false));

                case "GM.listValues":
                    EnsureObject(context.Parameters);
                    return ApiResult.FromValue(await _store.ListKeysAsync(scriptKey, cancellationToken).ConfigureAwait(false));

                case "GM.getValues":
                    return await GetValuesAsync(scriptKey, context.Parameters, cancellationToken).ConfigureAwait(false);

                case "GM.setValues":
                    var values = ReadValues(context.Parameters, "values");
                    foreach (var item in values)
                        await _store.SetAsync(scriptKey, item.Key, item.Value.GetRawText(), cancellationToken).ConfigureAwait(false);
                    return ApiResult.Undefined;

                case "GM.deleteValues":
                    var keys = ReadKeys(context.Parameters);
                    foreach (var key in keys)
                        await _store.DeleteAsync(scriptKey, key, cancellationToken).ConfigureAwait(false);
                    return ApiResult.Undefined;

                default:
                    throw new UnsupportedApiException("The API '" + context.Method + "' is not supported.");
            }
        }

        private async Task<ApiResult> GetValuesAsync(
            string scriptKey, JsonElement parameters, CancellationToken cancellationToken)
        {
            EnsureObject(parameters);
            var defaults = parameters.TryGetProperty("defaults", out _)
                ? ReadValues(parameters, "defaults")
                : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            var all = !parameters.TryGetProperty("keys", out var requested) || requested.ValueKind == JsonValueKind.Null;
            IReadOnlyList<string> keys = all ? null : ReadKeys(parameters);
            if (all && _store is IUserScriptValueSnapshotProvider snapshots)
            {
                var snapshot = await snapshots.GetSnapshotAsync(scriptKey, cancellationToken).ConfigureAwait(false);
                return ApiResult.FromValue(snapshot.ToDictionary(
                    item => item.Key, item => JsonSerializer.Deserialize<JsonElement>(item.Value), StringComparer.Ordinal));
            }
            if (all)
                keys = await _store.ListKeysAsync(scriptKey, cancellationToken).ConfigureAwait(false);
            var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var key in keys)
            {
                var stored = await _store.GetAsync(scriptKey, key, cancellationToken).ConfigureAwait(false);
                if (stored.Exists)
                    result[key] = JsonSerializer.Deserialize<JsonElement>(stored.JsonValue);
                else if (defaults.TryGetValue(key, out var fallback))
                    result[key] = fallback;
            }
            return ApiResult.FromValue(result);
        }

        internal static IReadOnlyList<string> ReadMutationKeys(string method, JsonElement parameters)
        {
            if (method == "GM.setValues") return ReadValues(parameters, "values").Keys.ToList();
            if (method == "GM.deleteValues") return ReadKeys(parameters);
            return new[] { ReadKey(parameters) };
        }

        private static Dictionary<string, JsonElement> ReadValues(JsonElement parameters, string name)
        {
            EnsureObject(parameters);
            if (!parameters.TryGetProperty(name, out var values) || values.ValueKind != JsonValueKind.Object)
                throw InvalidParams("The " + name + " parameter must be an object.");
            var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var item in values.EnumerateObject())
            {
                if (item.Name.Length == 0) throw InvalidParams("Storage keys cannot be empty.");
                result[item.Name] = item.Value;
            }
            return result;
        }

        private static IReadOnlyList<string> ReadKeys(JsonElement parameters)
        {
            EnsureObject(parameters);
            if (!parameters.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Array)
                throw InvalidParams("The keys parameter must be an array of non-empty strings.");
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in keys.EnumerateArray())
            {
                if (key.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(key.GetString()))
                    throw InvalidParams("Storage keys must be non-empty strings.");
                if (seen.Add(key.GetString())) result.Add(key.GetString());
            }
            return result;
        }

        private static string ReadKey(JsonElement parameters)
        {
            EnsureObject(parameters);
            if (!parameters.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.String)
                throw InvalidParams("The key parameter must be a string.");
            var value = key.GetString();
            if (string.IsNullOrEmpty(value))
                throw InvalidParams("The key parameter cannot be empty.");
            return value;
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
