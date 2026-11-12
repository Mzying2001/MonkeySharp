using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Mzying2001.MonkeySharp.Core.Runtime;

namespace Mzying2001.MonkeySharp.Core.Bridge
{
    internal static class ProtocolJson
    {
        public static string Success(string requestId, string resultJson)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteString("type", "response");
                    writer.WriteNumber("protocol", InjectionPlan.CurrentProtocolVersion);
                    writer.WriteString("requestId", requestId ?? string.Empty);
                    writer.WriteBoolean("ok", true);
                    writer.WritePropertyName("result");
                    using (var result = JsonDocument.Parse(resultJson))
                        result.RootElement.WriteTo(writer);
                    writer.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static string Error(string requestId, string code, string message, bool hello = false)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteString("type", hello ? "hello-result" : "response");
                    writer.WriteNumber("protocol", InjectionPlan.CurrentProtocolVersion);
                    if (!hello)
                        writer.WriteString("requestId", requestId ?? string.Empty);
                    writer.WriteBoolean("ok", false);
                    writer.WriteStartObject("error");
                    writer.WriteString("code", code);
                    writer.WriteString("message", message);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static string Hello(BridgeOptions options, IEnumerable<string> apis)
        {
            return Hello(options, apis, null);
        }

        public static string Hello(BridgeOptions options, IEnumerable<string> apis, object compatibility)
        {
            var payload = new Dictionary<string, object>
            {
                ["type"] = "hello-result",
                ["protocol"] = InjectionPlan.CurrentProtocolVersion,
                ["ok"] = true,
                ["limits"] = new Dictionary<string, object>
                {
                    ["requestBytes"] = options.MaxRequestBytes,
                    ["responseBytes"] = options.MaxResponseBytes,
                    ["resourceBytes"] = options.MaxResourceBytes,
                    ["binaryTransferBytes"] = options.MaxBinaryTransferBytes,
                    ["timeoutMilliseconds"] = (long)options.RequestTimeout.TotalMilliseconds,
                    ["pendingRequests"] = options.MaxPendingRequestsPerDocument,
                    ["replayRequestsPerExecution"] = options.MaxReplayEntriesPerExecution
                },
                ["apis"] = apis.OrderBy(item => item, StringComparer.Ordinal).ToArray()
            };
            if (compatibility != null)
                payload["compatibility"] = compatibility;
            return JsonSerializer.Serialize(payload);
        }

        public static string ValueChangeData(
            int listenerId,
            string key,
            string oldEncoded,
            string newEncoded,
            string mutationId = null,
            string originExecutionId = null,
            long? sequence = null,
            string mutationMethod = null)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("listenerId", listenerId);
                    writer.WriteString("key", key);
                    writer.WritePropertyName("oldValue");
                    WriteStoredValue(writer, oldEncoded);
                    writer.WritePropertyName("newValue");
                    WriteStoredValue(writer, newEncoded);
                    writer.WriteBoolean("oldValueExists", oldEncoded != null);
                    writer.WriteBoolean("newValueExists", newEncoded != null);
                    writer.WriteBoolean("remote", true);
                    if (mutationId != null)
                        writer.WriteString("mutationId", mutationId);
                    if (mutationMethod != null)
                        writer.WriteString("mutationMethod", mutationMethod);
                    if (originExecutionId != null)
                        writer.WriteString("originExecutionId", originExecutionId);
                    if (sequence.HasValue)
                        writer.WriteNumber("sequence", sequence.Value);
                    writer.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static void WriteStoredValue(Utf8JsonWriter writer, string encoded)
        {
            if (encoded == null) writer.WriteNullValue();
            else writer.WriteStringValue(encoded);
        }
    }
}
