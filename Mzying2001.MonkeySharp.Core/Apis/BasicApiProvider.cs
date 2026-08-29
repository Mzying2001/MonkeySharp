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
    public sealed class BasicApiProvider : IUserScriptApiProvider
    {
        private static readonly IReadOnlyCollection<string> SupportedMethods =
            new ReadOnlyCollection<string>(new[]
            {
                "GM.log", "GM.getValue", "GM.setValue", "GM.deleteValue", "GM.listValues"
            });

        private readonly IUserScriptValueStore _store;
        private readonly Action<UserScriptLogEntry> _log;

        public BasicApiProvider(IUserScriptValueStore store, Action<UserScriptLogEntry> log = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _log = log;
        }

        public IReadOnlyCollection<string> Methods => SupportedMethods;

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

                default:
                    throw new UnsupportedApiException("The API '" + context.Method + "' is not supported.");
            }
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
