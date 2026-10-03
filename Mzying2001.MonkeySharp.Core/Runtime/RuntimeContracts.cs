using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Compatibility;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Runtime
{
    /// <summary>
    /// Describes how reliably a host can execute code at document start.
    /// </summary>
    public enum TimingGuarantee
    {
        /// <summary>The host guarantees execution before page scripts run.</summary>
        GuaranteedDocumentStart,

        /// <summary>The host attempts document-start execution but cannot guarantee ordering.</summary>
        BestEffortDocumentStart,

        /// <summary>The host cannot execute scripts at document start.</summary>
        NoDocumentStart
    }

    /// <summary>
    /// Describes the integrity isolation provided to the userscript bridge.
    /// </summary>
    public enum BridgeIntegrityGuarantee
    {
        /// <summary>The bridge identity and delivery path are verified by the host.</summary>
        Verified,

        /// <summary>The host cannot verify bridge isolation from page code.</summary>
        Unverified,

        /// <summary>The bridge intentionally runs in the page world and requires trusted pages.</summary>
        TrustedPageWorld
    }

    /// <summary>
    /// Identifies a browser document frame and the guarantees provided by its host.
    /// </summary>
    public sealed class DocumentFrame
    {
        /// <summary>Initializes a document frame descriptor.</summary>
        /// <param name="browserSessionId">The identifier of the containing browser session.</param>
        /// <param name="documentId">The identifier of the current document navigation.</param>
        /// <param name="frameId">The identifier of the browser frame.</param>
        /// <param name="url">The absolute document URL.</param>
        /// <param name="isMainFrame">Whether this is the browser's main frame.</param>
        /// <param name="timingGuarantee">The document-start timing guarantee.</param>
        /// <param name="bridgeIntegrity">The bridge integrity guarantee.</param>
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

        /// <summary>Gets the containing browser session identifier.</summary>
        public string BrowserSessionId { get; }

        /// <summary>Gets the current document navigation identifier.</summary>
        public string DocumentId { get; }

        /// <summary>Gets the browser frame identifier.</summary>
        public string FrameId { get; }

        /// <summary>Gets the absolute document URL.</summary>
        public Uri Url { get; }

        /// <summary>Gets whether this is the browser's main frame.</summary>
        public bool IsMainFrame { get; }

        /// <summary>Gets the document-start timing guarantee.</summary>
        public TimingGuarantee TimingGuarantee { get; }

        /// <summary>Gets the bridge integrity guarantee.</summary>
        public BridgeIntegrityGuarantee BridgeIntegrity { get; }

        private static string Require(string value, string parameterName)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("The value cannot be null or empty.", parameterName);
            return value;
        }
    }

    /// <summary>
    /// Specifies a lifecycle transition for a browser document frame.
    /// </summary>
    public enum DocumentLifecycleKind
    {
        /// <summary>A JavaScript context was created for the frame.</summary>
        ContextCreated,

        /// <summary>The host reached its document-start injection point.</summary>
        DocumentStart,

        /// <summary>The document body became available.</summary>
        BodyAvailable,

        /// <summary>The DOM content loaded event occurred.</summary>
        DomContentLoaded,

        /// <summary>The document load event occurred.</summary>
        Load,

        /// <summary>The current frame URL changed without replacing its context.</summary>
        UrlChanged,

        /// <summary>The JavaScript context was released.</summary>
        ContextReleased,

        /// <summary>A newer navigation superseded the document.</summary>
        NavigationSuperseded
    }

    /// <summary>
    /// Provides data for a document lifecycle transition.
    /// </summary>
    public sealed class DocumentLifecycleEventArgs : EventArgs
    {
        /// <summary>Initializes lifecycle event data.</summary>
        /// <param name="kind">The lifecycle transition.</param>
        /// <param name="frame">The affected document frame.</param>
        public DocumentLifecycleEventArgs(DocumentLifecycleKind kind, DocumentFrame frame)
        {
            Kind = kind;
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        }

        /// <summary>Gets the lifecycle transition.</summary>
        public DocumentLifecycleKind Kind { get; }

        /// <summary>Gets the affected document frame.</summary>
        public DocumentFrame Frame { get; }
    }

    /// <summary>
    /// Describes one userscript execution included in an injection plan.
    /// </summary>
    public sealed class ScriptInvocation
    {
        internal ScriptInvocation(
            string executionId,
            ScriptKey scriptKey,
            string source,
            IReadOnlyList<string> declaredGrants,
            IReadOnlyList<string> grants,
            GrantDeclarationState grantDeclarationState,
            string serializedInfo,
            string capability,
            string deliveryToken,
            ScriptCompatibilityDescriptor compatibility)
        {
            ExecutionId = executionId;
            ScriptKey = scriptKey;
            Source = source;
            DeclaredGrants = declaredGrants;
            Grants = grants;
            GrantDeclarationState = grantDeclarationState;
            SerializedInfo = serializedInfo;
            Capability = capability;
            DeliveryToken = deliveryToken;
            Compatibility = compatibility;
        }

        /// <summary>Gets the unique execution identifier.</summary>
        public string ExecutionId { get; }

        /// <summary>Gets the installed script key.</summary>
        public ScriptKey ScriptKey { get; }

        /// <summary>Gets the complete source to execute, including resolved dependencies.</summary>
        public string Source { get; }

        /// <summary>Gets the API grants exposed to this execution.</summary>
        public IReadOnlyList<string> DeclaredGrants { get; }

        /// <summary>Gets the normalized API capabilities exposed to this execution.</summary>
        public IReadOnlyList<string> Grants { get; }

        /// <summary>Gets how the script declared its grants.</summary>
        public GrantDeclarationState GrantDeclarationState { get; }

        /// <summary>Gets the serialized metadata object exposed to the script.</summary>
        public string SerializedInfo { get; }

        /// <summary>Gets the unforgeable capability used to authorize bridge requests.</summary>
        public string Capability { get; }

        /// <summary>Gets the token used to route notifications to this execution.</summary>
        public string DeliveryToken { get; }

        /// <summary>Gets the compatibility behavior used by this execution.</summary>
        public ScriptCompatibilityDescriptor Compatibility { get; }
    }

    /// <summary>Describes compatibility behavior serialized with one script invocation.</summary>
    public sealed class ScriptCompatibilityDescriptor
    {
        internal ScriptCompatibilityDescriptor(UserScriptCompatibilityOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            Profile = options.Profile;
            Strict = options.Strict;
            LegacyGlobals = options.LegacyGlobalsEnabled;
            SynchronousStorageMirror = options.EnableSynchronousStorageMirror;
            SynchronousResourceSnapshot = options.EnableSynchronousResourceSnapshot;
        }

        /// <summary>Gets the selected compatibility profile.</summary>
        public UserScriptCompatibilityProfile Profile { get; }

        /// <summary>Gets whether the wrapper uses strict JavaScript semantics.</summary>
        public bool Strict { get; }

        /// <summary>Gets whether legacy global API names are available.</summary>
        public bool LegacyGlobals { get; }

        /// <summary>Gets whether synchronous storage mirror bootstrap is enabled.</summary>
        public bool SynchronousStorageMirror { get; }

        /// <summary>Gets whether synchronous resource snapshot bootstrap is enabled.</summary>
        public bool SynchronousResourceSnapshot { get; }
    }

    /// <summary>
    /// Contains the userscript invocations scheduled for a frame lifecycle phase.
    /// </summary>
    public sealed class InjectionPlan
    {
        /// <summary>The bridge protocol version emitted by this runtime.</summary>
        public const int CurrentProtocolVersion = 1;

        internal InjectionPlan(DocumentFrame frame, UserScriptRunAt runAt, IEnumerable<ScriptInvocation> invocations)
        {
            ProtocolVersion = CurrentProtocolVersion;
            Frame = frame;
            RunAt = runAt;
            Invocations = new ReadOnlyCollection<ScriptInvocation>(invocations.ToList());
        }

        /// <summary>Gets the bridge protocol version.</summary>
        public int ProtocolVersion { get; }

        /// <summary>Gets the target document frame.</summary>
        public DocumentFrame Frame { get; }

        /// <summary>Gets the lifecycle phase represented by this plan.</summary>
        public UserScriptRunAt RunAt { get; }

        /// <summary>Gets the script invocations in execution order.</summary>
        public IReadOnlyList<ScriptInvocation> Invocations { get; }
    }

    /// <summary>
    /// Dispatches serialized requests from an injected userscript bridge.
    /// </summary>
    public interface IUserScriptBridge
    {
        /// <summary>Processes a serialized bridge request.</summary>
        /// <param name="requestJson">The request encoded as JSON.</param>
        /// <param name="cancellationToken">A token that cancels request processing.</param>
        /// <returns>The serialized JSON response.</returns>
        Task<string> DispatchAsync(string requestJson, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Resolves the complete executable source for an installed userscript.
    /// </summary>
    public interface IUserScriptSourceResolver
    {
        /// <summary>Resolves dependencies and source for an installation.</summary>
        /// <param name="installation">The userscript installation to resolve.</param>
        /// <param name="cancellationToken">A token that cancels source resolution.</param>
        /// <returns>The complete JavaScript source in execution order.</returns>
        Task<string> ResolveSourceAsync(UserScriptInstallation installation, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Provides a serialized bridge request together with its source frame.
    /// </summary>
    public sealed class BridgeRequestEventArgs : EventArgs
    {
        /// <summary>Initializes bridge request event data.</summary>
        /// <param name="frame">The frame that emitted the request.</param>
        /// <param name="requestJson">The serialized request JSON.</param>
        public BridgeRequestEventArgs(DocumentFrame frame, string requestJson)
        {
            Frame = frame;
            RequestJson = requestJson;
        }

        /// <summary>Gets the frame that emitted the request.</summary>
        public DocumentFrame Frame { get; }

        /// <summary>Gets the serialized request JSON.</summary>
        public string RequestJson { get; }
    }

    /// <summary>
    /// Defines the browser integration required by the userscript engine.
    /// </summary>
    public interface IUserScriptHost
    {
        /// <summary>Executes JavaScript in the specified document frame.</summary>
        /// <param name="frame">The target frame.</param>
        /// <param name="javaScript">The JavaScript source to execute.</param>
        /// <param name="cancellationToken">A token that cancels execution.</param>
        /// <returns>A task that completes when the host has dispatched the script.</returns>
        Task ExecuteAsync(DocumentFrame frame, string javaScript, CancellationToken cancellationToken);

        /// <summary>Registers the bridge that receives requests from injected scripts.</summary>
        /// <param name="bridge">The bridge implementation.</param>
        void RegisterBridge(IUserScriptBridge bridge);

        /// <summary>Occurs when a document frame changes lifecycle state.</summary>
        event EventHandler<DocumentLifecycleEventArgs> DocumentLifecycle;

        /// <summary>Occurs when an injected script emits a bridge request.</summary>
        event EventHandler<BridgeRequestEventArgs> BridgeRequest;
    }

    /// <summary>
    /// Configures userscript execution safety requirements.
    /// </summary>
    public sealed class UserScriptEngineOptions
    {
        /// <summary>Gets or sets whether document-start scripts require guaranteed timing.</summary>
        public bool RequireGuaranteedDocumentStart { get; set; }

        /// <summary>Gets or sets whether scripts requiring privileged APIs need a verified bridge.</summary>
        public bool RequireVerifiedBridge { get; set; } = true;

        /// <summary>Gets or sets the userscript compatibility options.</summary>
        public UserScriptCompatibilityOptions Compatibility { get; set; } =
            new UserScriptCompatibilityOptions();
    }
}
