using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Mzying2001.MonkeySharp.Core.Bridge
{
    /// <summary>
    /// Builds JavaScript payloads for userscript injection and bridge notifications.
    /// </summary>
    public static class BridgeScriptBuilder
    {
        private const string PayloadPlaceholder = "__MONKEYSHARP_PAYLOAD_BASE64__";
        private static readonly Lazy<string> BootstrapTemplate =
            new Lazy<string>(() => LoadResource("Mzying2001.MonkeySharp.Core.Js.Init.js"));
        private static readonly Lazy<string> NotificationTemplate =
            new Lazy<string>(() => LoadResource("Mzying2001.MonkeySharp.Core.Js.Inject.js"));

        /// <summary>Builds the bootstrap JavaScript for an injection plan.</summary>
        /// <param name="plan">The authenticated plan to serialize.</param>
        /// <returns>JavaScript that initializes the bridge and executes the plan.</returns>
        public static string BuildInjection(InjectionPlan plan)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));
            var payload = new Dictionary<string, object>
            {
                ["protocol"] = plan.ProtocolVersion,
                ["documentId"] = plan.Frame.DocumentId,
                ["frameId"] = plan.Frame.FrameId,
                ["runAt"] = plan.RunAt.ToString(),
                ["invocations"] = plan.Invocations.Select(invocation => new Dictionary<string, object>
                {
                    ["executionId"] = invocation.ExecutionId,
                    ["scriptKey"] = invocation.ScriptKey.ToString(),
                    ["source"] = invocation.Source,
                    ["declaredGrants"] = invocation.DeclaredGrants,
                    ["grants"] = invocation.Grants,
                    ["grantDeclarationState"] = invocation.GrantDeclarationState.ToString(),
                    ["info"] = ParseElement(invocation.SerializedInfo),
                    ["capability"] = invocation.Capability,
                    ["deliveryToken"] = invocation.DeliveryToken,
                    ["compatibility"] = new Dictionary<string, object>
                    {
                        ["profile"] = invocation.Compatibility.Profile.ToString(),
                        ["strict"] = invocation.Compatibility.Strict,
                        ["legacyGlobals"] = invocation.Compatibility.LegacyGlobals,
                        ["synchronousStorageMirror"] = invocation.Compatibility.SynchronousStorageMirror,
                        ["synchronousResourceSnapshot"] = invocation.Compatibility.SynchronousResourceSnapshot
                    }
                }).ToArray()
            };
            return InsertPayload(BootstrapTemplate.Value, JsonSerializer.Serialize(payload));
        }

        /// <summary>Builds JavaScript that delivers an authenticated bridge notification.</summary>
        /// <param name="notification">The notification to serialize.</param>
        /// <returns>JavaScript that delivers the notification to its target execution.</returns>
        public static string BuildNotification(BridgeNotificationEventArgs notification)
        {
            if (notification == null)
                throw new ArgumentNullException(nameof(notification));
            using (var data = JsonDocument.Parse(notification.DataJson))
            {
                var payload = new Dictionary<string, object>
                {
                    ["type"] = "notification",
                    ["protocol"] = InjectionPlan.CurrentProtocolVersion,
                    ["executionId"] = notification.ExecutionId,
                    ["deliveryToken"] = notification.DeliveryToken,
                    ["event"] = notification.EventName,
                    ["data"] = data.RootElement.Clone()
                };
                return InsertPayload(NotificationTemplate.Value, JsonSerializer.Serialize(payload));
            }
        }

        private static string InsertPayload(string template, string payloadJson)
        {
            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson));
            return template.Replace(PayloadPlaceholder, payload);
        }

        private static JsonElement ParseElement(string json)
        {
            using (var document = JsonDocument.Parse(json))
                return document.RootElement.Clone();
        }

        private static string LoadResource(string name)
        {
            var assembly = typeof(BridgeScriptBuilder).GetTypeInfo().Assembly;
            using (var stream = assembly.GetManifestResourceStream(name))
            {
                if (stream == null)
                    throw new InvalidOperationException("Embedded resource '" + name + "' was not found.");
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                    return reader.ReadToEnd();
            }
        }
    }
}
