using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

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
                    writer.WriteNumber("protocol", 1);
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
                    writer.WriteNumber("protocol", 1);
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
                ["protocol"] = 1,
                ["ok"] = true,
                ["limits"] = new Dictionary<string, object>
                {
                    ["requestBytes"] = options.MaxRequestBytes,
                    ["responseBytes"] = options.MaxResponseBytes,
                    ["resourceBytes"] = options.MaxResourceBytes,
                    ["timeoutMilliseconds"] = (long)options.RequestTimeout.TotalMilliseconds,
                    ["pendingRequests"] = options.MaxPendingRequestsPerDocument
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
            string oldJson,
            string newJson,
            string mutationId = null,
            string originExecutionId = null,
            long? sequence = null)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("listenerId", listenerId);
                    writer.WriteString("key", key);
                    writer.WritePropertyName("oldValue");
                    WriteOptionalValue(writer, oldJson);
                    writer.WritePropertyName("newValue");
                    WriteOptionalValue(writer, newJson);
                    writer.WriteBoolean("remote", true);
                    if (mutationId != null)
                        writer.WriteString("mutationId", mutationId);
                    if (originExecutionId != null)
                        writer.WriteString("originExecutionId", originExecutionId);
                    if (sequence.HasValue)
                        writer.WriteNumber("sequence", sequence.Value);
                    writer.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static void WriteOptionalValue(Utf8JsonWriter writer, string json)
        {
            if (json == null)
            {
                writer.WriteStartObject();
                writer.WriteString("$monkeySharpType", "undefined");
                writer.WriteEndObject();
                return;
            }
            using (var value = JsonDocument.Parse(json))
                value.RootElement.WriteTo(writer);
        }
    }
}
