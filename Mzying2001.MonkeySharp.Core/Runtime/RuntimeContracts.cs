using Mzying2001.MonkeySharp.Core.Domain;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Runtime
{
    public enum TimingGuarantee
    {
        GuaranteedDocumentStart,
        BestEffortDocumentStart,
        NoDocumentStart
    }

    public enum BridgeIntegrityGuarantee
    {
        Verified,
        Unverified,
        TrustedPageWorld
    }

    public sealed class DocumentFrame
    {
        public DocumentFrame(
            string browserSessionId,
            string documentId,
            string frameId,
            Uri url,
            bool isMainFrame,
            TimingGuarantee timingGuarantee,
            BridgeIntegrityGuarantee bridgeIntegrity = BridgeIntegrityGuarantee.Unverified)
        {
            BrowserSessionId = Require(browserSessionId, nameof(browserSessionId));
            DocumentId = Require(documentId, nameof(documentId));
            FrameId = Require(frameId, nameof(frameId));
            Url = url != null && url.IsAbsoluteUri ? url : throw new ArgumentException("The URL must be absolute.", nameof(url));
            IsMainFrame = isMainFrame;
            TimingGuarantee = timingGuarantee;
            BridgeIntegrity = bridgeIntegrity;
        }

        public string BrowserSessionId { get; }
        public string DocumentId { get; }
        public string FrameId { get; }
        public Uri Url { get; }
        public bool IsMainFrame { get; }
        public TimingGuarantee TimingGuarantee { get; }
        public BridgeIntegrityGuarantee BridgeIntegrity { get; }

        private static string Require(string value, string parameterName)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("The value cannot be null or empty.", parameterName);
            return value;
        }
    }

    public enum DocumentLifecycleKind
    {
        ContextCreated,
        DocumentStart,
        BodyAvailable,
        DomContentLoaded,
        Load,
        UrlChanged,
        ContextReleased,
        NavigationSuperseded
    }

    public sealed class DocumentLifecycleEventArgs : EventArgs
    {
        public DocumentLifecycleEventArgs(DocumentLifecycleKind kind, DocumentFrame frame)
        {
            Kind = kind;
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        }

        public DocumentLifecycleKind Kind { get; }
        public DocumentFrame Frame { get; }
    }

    public sealed class ScriptInvocation
    {
        internal ScriptInvocation(
            string executionId,
            ScriptKey scriptKey,
            string source,
            IReadOnlyList<string> grants,
            string serializedInfo,
            string capability,
            string deliveryToken)
        {
            ExecutionId = executionId;
            ScriptKey = scriptKey;
            Source = source;
            Grants = grants;
            SerializedInfo = serializedInfo;
            Capability = capability;
            DeliveryToken = deliveryToken;
        }

        public string ExecutionId { get; }
        public ScriptKey ScriptKey { get; }
        public string Source { get; }
        public IReadOnlyList<string> Grants { get; }
        public string SerializedInfo { get; }
        public string Capability { get; }
        public string DeliveryToken { get; }
    }

    public sealed class InjectionPlan
    {
        public const int CurrentProtocolVersion = 1;

        internal InjectionPlan(DocumentFrame frame, UserScriptRunAt runAt, IEnumerable<ScriptInvocation> invocations)
        {
            ProtocolVersion = CurrentProtocolVersion;
            Frame = frame;
            RunAt = runAt;
            Invocations = new ReadOnlyCollection<ScriptInvocation>(invocations.ToList());
        }

        public int ProtocolVersion { get; }
        public DocumentFrame Frame { get; }
        public UserScriptRunAt RunAt { get; }
        public IReadOnlyList<ScriptInvocation> Invocations { get; }
    }

    public interface IUserScriptBridge
    {
        Task<string> DispatchAsync(string requestJson, CancellationToken cancellationToken);
    }

    public sealed class BridgeRequestEventArgs : EventArgs
    {
        public BridgeRequestEventArgs(DocumentFrame frame, string requestJson)
        {
            Frame = frame;
            RequestJson = requestJson;
        }

        public DocumentFrame Frame { get; }
        public string RequestJson { get; }
    }

    public interface IUserScriptHost
    {
        Task ExecuteAsync(DocumentFrame frame, string javaScript, CancellationToken cancellationToken);
        void RegisterBridge(IUserScriptBridge bridge);
        event EventHandler<DocumentLifecycleEventArgs> DocumentLifecycle;
        event EventHandler<BridgeRequestEventArgs> BridgeRequest;
    }

    public sealed class UserScriptEngineOptions
    {
        public bool RequireGuaranteedDocumentStart { get; set; }
        public bool RequireVerifiedBridge { get; set; } = true;
    }
}
