using Mzying2001.MonkeySharp.Core.Runtime;
using System;

namespace Mzying2001.MonkeySharp.Core.Bridge
{
    public static class BridgeErrorCodes
    {
        public const string ProtocolVersion = "MSP001_PROTOCOL_VERSION";
        public const string MalformedMessage = "MSP002_MALFORMED_MESSAGE";
        public const string SessionExpired = "MSP003_SESSION_EXPIRED";
        public const string GrantDenied = "MSP004_GRANT_DENIED";
        public const string PermissionDenied = "MSP005_PERMISSION_DENIED";
        public const string NotSupported = "MSP006_NOT_SUPPORTED";
        public const string InvalidParams = "MSP007_INVALID_PARAMS";
        public const string PayloadTooLarge = "MSP008_PAYLOAD_TOO_LARGE";
        public const string Timeout = "MSP009_TIMEOUT";
        public const string Canceled = "MSP010_CANCELED";
        public const string Internal = "MSP999_INTERNAL";
    }

    public sealed class BridgeOptions
    {
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

        public int MaxRequestBytes { get; }
        public int MaxResponseBytes { get; }
        public int MaxResourceBytes { get; }
        public TimeSpan RequestTimeout { get; }
        public int MaxPendingRequestsPerDocument { get; }
    }

    public sealed class BridgeNotificationEventArgs : EventArgs
    {
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

        public DocumentFrame Frame { get; }
        public string ExecutionId { get; }
        public string DeliveryToken { get; }
        public string EventName { get; }
        public string DataJson { get; }
    }
}
