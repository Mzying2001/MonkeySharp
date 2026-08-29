using Mzying2001.MonkeySharp.Core.Runtime;
using System;

namespace Mzying2001.MonkeySharp.Core.Bridge
{
    /// <summary>
    /// Defines stable error codes returned by the MonkeySharp bridge protocol.
    /// </summary>
    public static class BridgeErrorCodes
    {
        /// <summary>The peer requested an unsupported protocol version.</summary>
        public const string ProtocolVersion = "MSP001_PROTOCOL_VERSION";

        /// <summary>The bridge message is missing required fields or has an invalid shape.</summary>
        public const string MalformedMessage = "MSP002_MALFORMED_MESSAGE";

        /// <summary>The document or userscript execution session is no longer active.</summary>
        public const string SessionExpired = "MSP003_SESSION_EXPIRED";

        /// <summary>The userscript did not declare the requested API grant.</summary>
        public const string GrantDenied = "MSP004_GRANT_DENIED";

        /// <summary>The host permission policy denied the requested operation.</summary>
        public const string PermissionDenied = "MSP005_PERMISSION_DENIED";

        /// <summary>The host does not implement the requested API.</summary>
        public const string NotSupported = "MSP006_NOT_SUPPORTED";

        /// <summary>The API parameters are invalid.</summary>
        public const string InvalidParams = "MSP007_INVALID_PARAMS";

        /// <summary>A request, response, resource, or pending-request limit was exceeded.</summary>
        public const string PayloadTooLarge = "MSP008_PAYLOAD_TOO_LARGE";

        /// <summary>The API request exceeded its configured timeout.</summary>
        public const string Timeout = "MSP009_TIMEOUT";

        /// <summary>The caller canceled the API request.</summary>
        public const string Canceled = "MSP010_CANCELED";

        /// <summary>An unexpected host error occurred.</summary>
        public const string Internal = "MSP999_INTERNAL";
    }

    /// <summary>
    /// Configures bridge payload, concurrency, resource, and timeout limits.
    /// </summary>
    public sealed class BridgeOptions
    {
        /// <summary>Initializes bridge protocol limits.</summary>
        /// <param name="maxRequestBytes">The maximum UTF-8 request size.</param>
        /// <param name="maxResponseBytes">The maximum UTF-8 response size.</param>
        /// <param name="maxResourceBytes">The maximum resource or dependency size.</param>
        /// <param name="requestTimeout">The API request timeout, or <see langword="null"/> for 30 seconds.</param>
        /// <param name="maxPendingRequestsPerDocument">The maximum concurrent requests for one document.</param>
        public BridgeOptions(
            int maxRequestBytes = 1024 * 1024,
            int maxResponseBytes = 1024 * 1024,
            int maxResourceBytes = 10 * 1024 * 1024,
            TimeSpan? requestTimeout = null,
            int maxPendingRequestsPerDocument = 64)
        {
            if (maxRequestBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxRequestBytes));
            if (maxResponseBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxResponseBytes));
            if (maxResourceBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxResourceBytes));
            if (maxPendingRequestsPerDocument <= 0) throw new ArgumentOutOfRangeException(nameof(maxPendingRequestsPerDocument));
            MaxRequestBytes = maxRequestBytes;
            MaxResponseBytes = maxResponseBytes;
            MaxResourceBytes = maxResourceBytes;
            RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
            if (RequestTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(requestTimeout));
            MaxPendingRequestsPerDocument = maxPendingRequestsPerDocument;
        }

        /// <summary>Gets the maximum UTF-8 request size.</summary>
        public int MaxRequestBytes { get; }

        /// <summary>Gets the maximum UTF-8 response size.</summary>
        public int MaxResponseBytes { get; }

        /// <summary>Gets the maximum resource or dependency size.</summary>
        public int MaxResourceBytes { get; }

        /// <summary>Gets the API request timeout.</summary>
        public TimeSpan RequestTimeout { get; }

        /// <summary>Gets the maximum number of concurrent requests for one document.</summary>
        public int MaxPendingRequestsPerDocument { get; }
    }

    /// <summary>
    /// Provides an authenticated bridge notification ready for frame delivery.
    /// </summary>
    public sealed class BridgeNotificationEventArgs : EventArgs
    {
        /// <summary>Initializes bridge notification event data.</summary>
        /// <param name="frame">The target document frame.</param>
        /// <param name="executionId">The target userscript execution.</param>
        /// <param name="deliveryToken">The delivery token for the target execution.</param>
        /// <param name="eventName">The JavaScript event name.</param>
        /// <param name="dataJson">The event data encoded as JSON.</param>
        public BridgeNotificationEventArgs(
            DocumentFrame frame,
            string executionId,
            string deliveryToken,
            string eventName,
            string dataJson)
        {
            Frame = frame;
            ExecutionId = executionId;
            DeliveryToken = deliveryToken;
            EventName = eventName;
            DataJson = dataJson;
        }

        /// <summary>Gets the target document frame.</summary>
        public DocumentFrame Frame { get; }

        /// <summary>Gets the target userscript execution identifier.</summary>
        public string ExecutionId { get; }

        /// <summary>Gets the delivery token for the target execution.</summary>
        public string DeliveryToken { get; }

        /// <summary>Gets the JavaScript event name.</summary>
        public string EventName { get; }

        /// <summary>Gets the event data encoded as JSON.</summary>
        public string DataJson { get; }
    }
}
