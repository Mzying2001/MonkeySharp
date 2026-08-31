using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Compatibility;
using Mzying2001.MonkeySharp.Core.Matching;
using Mzying2001.MonkeySharp.Core.Repository;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Runtime
{
    /// <summary>
    /// Selects eligible userscripts and creates authenticated injection plans for document lifecycle events.
    /// </summary>
    public sealed class UserScriptEngine : IDisposable
    {
        private readonly IUserScriptRepository _repository;
        private readonly IUserScriptMatcher _matcher;
        private readonly UserScriptEngineOptions _options;
        private readonly IUserScriptSourceResolver _sourceResolver;
        private readonly SemaphoreSlim _queue = new SemaphoreSlim(1, 1);
        private readonly object _stateLock = new object();
        private readonly Dictionary<string, DocumentState> _documents =
            new Dictionary<string, DocumentState>(StringComparer.Ordinal);
        private readonly Dictionary<string, ExecutionRecord> _executions =
            new Dictionary<string, ExecutionRecord>(StringComparer.Ordinal);
        private bool _disposed;

        /// <summary>Initializes a userscript execution engine.</summary>
        /// <param name="repository">The repository containing installed userscripts.</param>
        /// <param name="matcher">The URL matcher, or <see langword="null"/> to use the default matcher.</param>
        /// <param name="options">Execution safety requirements, or <see langword="null"/> for defaults.</param>
        /// <param name="sourceResolver">An optional resolver for script dependencies and final source.</param>
        public UserScriptEngine(
            IUserScriptRepository repository,
            IUserScriptMatcher matcher = null,
            UserScriptEngineOptions options = null,
            IUserScriptSourceResolver sourceResolver = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _matcher = matcher ?? new UserScriptMatcher();
            _options = options ?? new UserScriptEngineOptions();
            if (_options.Compatibility == null)
                _options.Compatibility = new UserScriptCompatibilityOptions();
            _sourceResolver = sourceResolver;
        }

        /// <summary>Occurs when script selection or source resolution produces a diagnostic.</summary>
        public event EventHandler<UserScriptDiagnostic> Diagnostic;
        internal event Action<ExecutionRecord> ExecutionEnded;

        /// <summary>Processes a document lifecycle transition and creates the corresponding injection plan.</summary>
        /// <param name="lifecycle">The lifecycle transition to process.</param>
        /// <param name="cancellationToken">A token that cancels processing.</param>
        /// <returns>An injection plan containing each eligible script that has not already run in the document.</returns>
        public async Task<InjectionPlan> ProcessLifecycleAsync(
            DocumentLifecycleEventArgs lifecycle,
            CancellationToken cancellationToken)
        {
            if (lifecycle == null)
                throw new ArgumentNullException(nameof(lifecycle));

            var diagnostics = new List<UserScriptDiagnostic>();
            InjectionPlan plan;
            await _queue.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                plan = await ProcessLifecycleCoreAsync(lifecycle, diagnostics, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _queue.Release();
            }

            foreach (var diagnostic in diagnostics)
                Diagnostic?.Invoke(this, diagnostic);
            return plan;
        }

        /// <summary>Ends active executions and releases state for a document.</summary>
        /// <param name="documentId">The document identifier to invalidate.</param>
        /// <param name="cancellationToken">A token that cancels invalidation before it starts.</param>
        /// <returns>A task that completes when document state has been released.</returns>
        public async Task InvalidateDocumentAsync(string documentId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(documentId))
                throw new ArgumentException("The document ID is required.", nameof(documentId));
            await _queue.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                InvalidateDocument(documentId);
            }
            finally
            {
                _queue.Release();
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
                return;
            List<ExecutionRecord> endedExecutions = null;
            _queue.Wait();
            try
            {
                if (_disposed)
                    return;
                lock (_stateLock)
                {
                    endedExecutions = _executions.Values.ToList();
                    foreach (var execution in _executions.Values)
                        execution.Cancellation.Cancel();
                    foreach (var execution in _executions.Values)
                        execution.Cancellation.Dispose();
                    _executions.Clear();
                    _documents.Clear();
                }
                _disposed = true;
            }
            finally
            {
                _queue.Release();
                _queue.Dispose();
            }
            NotifyExecutionsEnded(endedExecutions);
        }

        internal bool TryGetExecution(
            string documentId,
            string scriptKey,
            string capability,
            out ExecutionRecord execution)
        {
            lock (_stateLock)
            {
                execution = _executions.Values.FirstOrDefault(item =>
                    item.Frame.DocumentId == documentId &&
                    item.Invocation.ScriptKey.ToString() == scriptKey &&
                    FixedTimeEquals(item.Invocation.Capability, capability));
                return execution != null && !execution.Cancellation.IsCancellationRequested;
            }
        }

        internal IReadOnlyList<ExecutionRecord> GetExecutionsForScript(ScriptKey scriptKey)
        {
            lock (_stateLock)
            {
                return new ReadOnlyCollection<ExecutionRecord>(
                    _executions.Values.Where(item => item.Invocation.ScriptKey == scriptKey).ToList());
            }
        }

        private async Task<InjectionPlan> ProcessLifecycleCoreAsync(
            DocumentLifecycleEventArgs lifecycle,
            ICollection<UserScriptDiagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            var frame = lifecycle.Frame;
            switch (lifecycle.Kind)
            {
                case DocumentLifecycleKind.ContextReleased:
                case DocumentLifecycleKind.NavigationSuperseded:
                    InvalidateDocument(frame.DocumentId);
                    return EmptyPlan(frame, UserScriptRunAt.DocumentIdle);
                case DocumentLifecycleKind.ContextCreated:
                case DocumentLifecycleKind.UrlChanged:
                    EnsureDocument(frame);
                    return EmptyPlan(frame, UserScriptRunAt.DocumentIdle);
            }

            var runAt = ToRunAt(lifecycle.Kind);
            var document = EnsureDocument(frame);
            var installations = await _repository.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var invocations = new List<ScriptInvocation>();
            foreach (var installation in installations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var metadata = installation.Definition.Metadata;
                if (!installation.IsEnabled || metadata.RunAt != runAt ||
                    (!frame.IsMainFrame && metadata.NoFrames) ||
                    !_matcher.IsMatch(metadata, frame.Url))
                    continue;

                var onceKey = installation.ScriptKey + ":" + runAt;
                if (document.Executed.Contains(onceKey))
                    continue;

                if (runAt == UserScriptRunAt.DocumentStart)
                {
                    if (_options.RequireGuaranteedDocumentStart && frame.TimingGuarantee != TimingGuarantee.GuaranteedDocumentStart)
                    {
                        diagnostics.Add(new UserScriptDiagnostic(
                            "MSR101_DOCUMENT_START_UNAVAILABLE",
                            DiagnosticSeverity.Warning,
                            "The host cannot guarantee document-start timing.",
                            scriptKey: installation.ScriptKey,
                            documentId: frame.DocumentId,
                            frameId: frame.FrameId));
                        continue;
                    }
                    if (frame.TimingGuarantee == TimingGuarantee.BestEffortDocumentStart)
                    {
                        diagnostics.Add(new UserScriptDiagnostic(
                            "MSR100_DOCUMENT_START_BEST_EFFORT",
                            DiagnosticSeverity.Info,
                            "The script is running with best-effort document-start timing.",
                            scriptKey: installation.ScriptKey,
                            documentId: frame.DocumentId,
                            frameId: frame.FrameId));
                    }
                }

                if (_options.RequireVerifiedBridge &&
                    frame.BridgeIntegrity == BridgeIntegrityGuarantee.Unverified &&
                    !IsGrantNone(metadata.Grants))
                {
                    diagnostics.Add(new UserScriptDiagnostic(
                        "MSR201_BRIDGE_INTEGRITY_REQUIRED",
                        DiagnosticSeverity.Warning,
                        "Host-backed GM APIs require a verified bridge.",
                        scriptKey: installation.ScriptKey,
                        documentId: frame.DocumentId,
                        frameId: frame.FrameId));
                    continue;
                }
                if (frame.BridgeIntegrity == BridgeIntegrityGuarantee.TrustedPageWorld && !IsGrantNone(metadata.Grants))
                {
                    diagnostics.Add(new UserScriptDiagnostic(
                        "MSR200_UNVERIFIED_BRIDGE",
                        DiagnosticSeverity.Warning,
                        "TrustedPageWorld exposes capabilities to page-world code.",
                        scriptKey: installation.ScriptKey,
                        documentId: frame.DocumentId,
                        frameId: frame.FrameId));
                }

                if (!string.IsNullOrEmpty(metadata.InjectInto) &&
                    !string.Equals(metadata.InjectInto, "page", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(new UserScriptDiagnostic(
                        "MSR210_UNSUPPORTED_INJECT_WORLD",
                        DiagnosticSeverity.Warning,
                        "The requested @inject-into world is not available in the CefSharp page world.",
                        scriptKey: installation.ScriptKey,
                        documentId: frame.DocumentId,
                        frameId: frame.FrameId));
                }
                if (!string.IsNullOrEmpty(metadata.RunIn))
                {
                    diagnostics.Add(new UserScriptDiagnostic(
                        "MSR211_UNSUPPORTED_RUN_IN",
                        DiagnosticSeverity.Warning,
                        "The requested @run-in environment is preserved but not implemented by this host.",
                        scriptKey: installation.ScriptKey,
                        documentId: frame.DocumentId,
                        frameId: frame.FrameId));
                }
                if (_options.Compatibility.Profile == UserScriptCompatibilityProfile.LegacyCompatible &&
                    metadata.Requires.Count != 0 && _sourceResolver == null)
                {
                    diagnostics.Add(new UserScriptDiagnostic(
                        "MSR401_DEPENDENCY_PROVIDER_UNAVAILABLE",
                        DiagnosticSeverity.Error,
                        "The script declares @require entries but no dependency provider is configured.",
                        scriptKey: installation.ScriptKey,
                        documentId: frame.DocumentId,
                        frameId: frame.FrameId));
                    continue;
                }

                string source;
                try
                {
                    source = _sourceResolver == null
                        ? installation.Definition.Source
                        : await _sourceResolver.ResolveSourceAsync(installation, cancellationToken).ConfigureAwait(false);
                    if (source == null)
                        throw new InvalidOperationException("The script source resolver returned null.");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    diagnostics.Add(new UserScriptDiagnostic(
                        "MSR400_DEPENDENCY_RESOLUTION_FAILED",
                        DiagnosticSeverity.Error,
                        "The script dependencies could not be resolved.",
                        exception,
                        installation.ScriptKey,
                        frame.DocumentId,
                        frame.FrameId));
                    continue;
                }
                var invocation = CreateInvocation(installation, source);
                var execution = new ExecutionRecord(frame, installation, invocation, new CancellationTokenSource());
                lock (_stateLock)
                    _executions.Add(invocation.ExecutionId, execution);
                document.ExecutionIds.Add(invocation.ExecutionId);
                invocations.Add(invocation);
                document.Executed.Add(onceKey);
            }

            return new InjectionPlan(frame, runAt, invocations);
        }

        private DocumentState EnsureDocument(DocumentFrame frame)
        {
            lock (_stateLock)
            {
                if (_documents.TryGetValue(frame.DocumentId, out var existing))
                    return existing;
                var document = new DocumentState(frame);
                _documents.Add(frame.DocumentId, document);
                return document;
            }
        }

        private void InvalidateDocument(string documentId)
        {
            List<ExecutionRecord> endedExecutions;
            lock (_stateLock)
            {
                if (!_documents.TryGetValue(documentId, out var document))
                    return;
                endedExecutions = new List<ExecutionRecord>();
                foreach (var executionId in document.ExecutionIds)
                {
                    if (_executions.TryGetValue(executionId, out var execution))
                    {
                        execution.Cancellation.Cancel();
                        execution.Cancellation.Dispose();
                        _executions.Remove(executionId);
                        endedExecutions.Add(execution);
                    }
                }
                _documents.Remove(documentId);
            }
            NotifyExecutionsEnded(endedExecutions);
        }

        private void NotifyExecutionsEnded(IEnumerable<ExecutionRecord> executions)
        {
            if (executions == null)
                return;
            foreach (var execution in executions)
                ExecutionEnded?.Invoke(execution);
        }

        private ScriptInvocation CreateInvocation(UserScriptInstallation installation, string source)
        {
            var metadata = installation.Definition.Metadata;
            var info = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["scriptKey"] = installation.ScriptKey.ToString(),
                ["name"] = metadata.Name,
                ["namespace"] = metadata.Namespace,
                ["version"] = metadata.Version,
                ["description"] = metadata.Description,
                ["grants"] = metadata.DeclaredGrants
            });
            return new ScriptInvocation(
                Guid.NewGuid().ToString("D"),
                installation.ScriptKey,
                source,
                metadata.DeclaredGrants,
                metadata.Grants,
                info,
                CreateToken(),
                CreateToken(),
                new ScriptCompatibilityDescriptor(_options.Compatibility));
        }

        private static string CreateToken()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            var difference = 0;
            for (var index = 0; index < left.Length; index++)
                difference |= left[index] ^ right[index];
            return difference == 0;
        }

        private static bool IsGrantNone(IReadOnlyList<string> grants)
        {
            return grants.Count == 1 && grants[0] == "none";
        }

        private static UserScriptRunAt ToRunAt(DocumentLifecycleKind kind)
        {
            switch (kind)
            {
                case DocumentLifecycleKind.DocumentStart: return UserScriptRunAt.DocumentStart;
                case DocumentLifecycleKind.BodyAvailable: return UserScriptRunAt.DocumentBody;
                case DocumentLifecycleKind.DomContentLoaded: return UserScriptRunAt.DocumentEnd;
                case DocumentLifecycleKind.Load: return UserScriptRunAt.DocumentIdle;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "The lifecycle event does not run scripts.");
            }
        }

        private static InjectionPlan EmptyPlan(DocumentFrame frame, UserScriptRunAt runAt)
        {
            return new InjectionPlan(frame, runAt, new ScriptInvocation[0]);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(UserScriptEngine));
        }

        internal sealed class ExecutionRecord
        {
            public ExecutionRecord(
                DocumentFrame frame,
                UserScriptInstallation installation,
                ScriptInvocation invocation,
                CancellationTokenSource cancellation)
            {
                Frame = frame;
                Installation = installation;
                Invocation = invocation;
                Cancellation = cancellation;
            }

            public DocumentFrame Frame { get; }
            public UserScriptInstallation Installation { get; }
            public ScriptInvocation Invocation { get; }
            public CancellationTokenSource Cancellation { get; }
        }

        private sealed class DocumentState
        {
            public DocumentState(DocumentFrame frame)
            {
                Frame = frame;
            }

            public DocumentFrame Frame { get; }
            public HashSet<string> Executed { get; } = new HashSet<string>(StringComparer.Ordinal);
            public List<string> ExecutionIds { get; } = new List<string>();
        }
    }
}
