using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Security;
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
using System.Text.RegularExpressions;

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
        private readonly SemaphoreSlim _queue = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _repositoryLifetime = new CancellationTokenSource();
        private readonly object _stateLock = new object();
        private readonly Dictionary<string, DocumentState> _documents =
            new Dictionary<string, DocumentState>(StringComparer.Ordinal);
        private readonly Dictionary<string, ExecutionRecord> _executions =
            new Dictionary<string, ExecutionRecord>(StringComparer.Ordinal);
        private bool _disposed;
        private IReadOnlyCollection<string> _hostCapabilities = new[] { "GM.info", "GM.addStyle", "GM.addElement" };

        internal void SetHostCapabilities(IEnumerable<string> capabilities)
            => _hostCapabilities = new ReadOnlyCollection<string>(capabilities.Distinct(StringComparer.Ordinal).ToArray());

        /// <summary>Initializes a userscript execution engine.</summary>
        /// <param name="repository">The repository containing installed userscripts.</param>
        /// <param name="matcher">The URL matcher, or <see langword="null"/> to use the default matcher.</param>
        /// <param name="options">Execution safety requirements, or <see langword="null"/> for defaults.</param>
        public UserScriptEngine(
            IUserScriptRepository repository,
            IUserScriptMatcher matcher = null,
            UserScriptEngineOptions options = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _matcher = matcher;
            _options = options ?? new UserScriptEngineOptions();
            _repository.Changed += RepositoryChanged;
        }

        private void RepositoryChanged(object sender, UserScriptRepositoryChangedEventArgs args)
        {
            if (args?.Installation != null)
                _ = RevokeChangedExecutionsAsync(args.Installation.ScriptKey);
        }

        private async Task RevokeChangedExecutionsAsync(ScriptKey key)
        {
            try
            {
                var ended = new List<ExecutionRecord>();
                await _queue.WaitAsync(_repositoryLifetime.Token).ConfigureAwait(false);
                try
                {
                    if (_disposed) return;
                    UserScriptInstallation current = null;
                    try { current = await _repository.GetAsync(key, _repositoryLifetime.Token).ConfigureAwait(false); }
                    catch (KeyNotFoundException) { }
                    lock (_stateLock)
                    {
                        foreach (var execution in _executions.Values.Where(item => item.Installation.ScriptKey == key &&
                            (current == null || !current.IsEnabled || current.RevisionId != item.Installation.RevisionId)).ToArray())
                        {
                            _executions.Remove(execution.Invocation.ExecutionId);
                            if (_documents.TryGetValue(execution.Frame.DocumentId, out var document))
                                document.ExecutionIds.Remove(execution.Invocation.ExecutionId);
                            execution.Cancellation.Cancel();
                            execution.Cancellation.Dispose();
                            ended.Add(execution);
                        }
                    }
                }
                finally { _queue.Release(); }
                NotifyExecutionsEnded(ended);
            }
            catch (OperationCanceledException) when (_repositoryLifetime.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (_disposed) { }
            catch (Exception error)
            {
                var diagnostic = new UserScriptDiagnostic("MSR301_EXECUTION_REVOCATION_FAILED", DiagnosticSeverity.Error,
                    "Repository changes could not revoke obsolete executions.", error, scriptKey: key);
                try { Diagnostic?.Invoke(this, diagnostic); }
                catch (Exception observerError) { System.Diagnostics.Trace.TraceError(observerError.ToString()); }
                System.Diagnostics.Trace.TraceError(error.ToString());
            }
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

        /// <summary>Gets context-menu scripts eligible for the supplied frame.</summary>
        public async Task<IReadOnlyList<ContextMenuEntry>> GetContextMenuEntriesAsync(
            DocumentFrame frame,
            CancellationToken cancellationToken)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            var diagnostics = new List<UserScriptDiagnostic>();
            await _queue.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                var installations = await _repository.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
                var entries = installations
                    .Where(item => item.IsEnabled && item.Definition.Metadata != null &&
                        item.Definition.Metadata.RunAt == UserScriptRunAt.ContextMenu &&
                        (frame.IsMainFrame || !item.Definition.Metadata.NoFrames) &&
                        Matches(item, frame, true, diagnostics))
                    .Select(item => new ContextMenuEntry(item.ScriptKey,
                        string.IsNullOrWhiteSpace(item.Definition.Metadata.Name)
                            ? item.ScriptKey.ToString()
                            : item.Definition.Metadata.Name))
                    .ToList();
                return new ReadOnlyCollection<ContextMenuEntry>(entries);
            }
            finally
            {
                _queue.Release();
                foreach (var diagnostic in diagnostics) Diagnostic?.Invoke(this, diagnostic);
            }
        }

        /// <summary>Creates a fresh execution for a selected context-menu script.</summary>
        public async Task<InjectionPlan> ProcessContextMenuAsync(
            DocumentFrame frame,
            ScriptKey scriptKey,
            CancellationToken cancellationToken)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            var diagnostics = new List<UserScriptDiagnostic>();
            await _queue.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                var installations = await _repository.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
                var installation = installations.FirstOrDefault(item => item.ScriptKey == scriptKey);
                if (installation == null || !installation.IsEnabled || installation.Definition.Metadata == null)
                    return EmptyPlan(frame, UserScriptRunAt.ContextMenu);
                var metadata = installation.Definition.Metadata;
                if (metadata.RunAt != UserScriptRunAt.ContextMenu ||
                    (!frame.IsMainFrame && metadata.NoFrames) ||
                    !Matches(installation, frame, metadata.RunAt == UserScriptRunAt.ContextMenu, diagnostics))
                    return EmptyPlan(frame, UserScriptRunAt.ContextMenu);
                if (_options.RequireVerifiedBridge &&
                    frame.BridgeIntegrity == BridgeIntegrityGuarantee.Unverified && RequiresBridge(metadata))
                {
                    diagnostics.Add(new UserScriptDiagnostic(
                        "MSR201_BRIDGE_INTEGRITY_REQUIRED", DiagnosticSeverity.Warning,
                        "Host-backed GM APIs require a verified bridge.", scriptKey: installation.ScriptKey,
                        documentId: frame.DocumentId, frameId: frame.FrameId));
                    return EmptyPlan(frame, UserScriptRunAt.ContextMenu);
                }
                var source = installation.PreparedRevision?.Source ?? throw new InvalidOperationException("The installation has no prepared revision.");
                var invocation = CreateInvocation(installation, source, frame);
                var execution = new ExecutionRecord(frame, installation, invocation, new CancellationTokenSource());
                lock (_stateLock)
                {
                    _executions.Add(invocation.ExecutionId, execution);
                    EnsureDocument(frame).ExecutionIds.Add(invocation.ExecutionId);
                }
                return new InjectionPlan(frame, UserScriptRunAt.ContextMenu, new[] { invocation });
            }
            finally
            {
                _queue.Release();
                foreach (var diagnostic in diagnostics)
                    Diagnostic?.Invoke(this, diagnostic);
            }
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
            _repository.Changed -= RepositoryChanged;
            // Cancel queued repository callbacks before disposing their semaphore.
            _repositoryLifetime.Cancel();
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
                _repositoryLifetime.Dispose();
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

        internal async Task<bool> IsInstallationActiveAsync(UserScriptInstallation installation, CancellationToken token)
        {
            try
            {
                var current = await _repository.GetAsync(installation.ScriptKey, token).ConfigureAwait(false);
                return current.IsEnabled && current.RevisionId == installation.RevisionId;
            }
            catch (KeyNotFoundException) { return false; }
        }

        internal async Task InvalidateExecutionAsync(string executionId, CancellationToken token)
        {
            ExecutionRecord ended = null;
            await _queue.WaitAsync(token).ConfigureAwait(false);
            try
            {
                lock (_stateLock)
                    if (_executions.TryGetValue(executionId, out ended))
                    {
                        _executions.Remove(executionId);
                        if (_documents.TryGetValue(ended.Frame.DocumentId, out var document)) document.ExecutionIds.Remove(executionId);
                        ended.Cancellation.Cancel(); ended.Cancellation.Dispose();
                    }
            }
            finally { _queue.Release(); }
            if (ended != null) NotifyExecutionsEnded(new[] { ended });
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
                    EnsureDocument(frame);
                    return EmptyPlan(frame, UserScriptRunAt.DocumentIdle);
                case DocumentLifecycleKind.UrlChanged:
                    EnsureDocument(frame);
                    lock (_stateLock)
                    {
                        foreach (var execution in _executions.Values.Where(item =>
                            item.Frame.DocumentId == frame.DocumentId))
                            execution.UpdateFrame(frame);
                    }
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
                    !Matches(installation, frame, metadata.RunAt == UserScriptRunAt.ContextMenu, diagnostics))
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
                    RequiresBridge(metadata))
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
                if (frame.BridgeIntegrity == BridgeIntegrityGuarantee.TrustedPageWorld && RequiresBridge(metadata))
                {
                    diagnostics.Add(new UserScriptDiagnostic(
                        "MSR200_UNVERIFIED_BRIDGE",
                        DiagnosticSeverity.Warning,
                        "TrustedPageWorld exposes capabilities to page-world code.",
                        scriptKey: installation.ScriptKey,
                        documentId: frame.DocumentId,
                        frameId: frame.FrameId));
                }

                var source = installation.PreparedRevision?.Source ?? throw new InvalidOperationException("The installation has no prepared revision.");
                var invocation = CreateInvocation(installation, source, frame);
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
                var document = new DocumentState();
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

        /// <summary>Resolves the requested Chromium world without silently downgrading an explicit requirement.</summary>
        public static string RequestedWorld(UserScriptMetadata metadata)
        {
            var sandbox = metadata.Sandbox;
            if (!string.IsNullOrEmpty(sandbox) && !new[] { "raw", "JavaScript", "DOM" }.Contains(sandbox, StringComparer.OrdinalIgnoreCase)) return null;
            var world = string.Equals(sandbox, "DOM", StringComparison.OrdinalIgnoreCase) ? "isolated" : "page";
            if (!string.IsNullOrEmpty(metadata.InjectInto) && metadata.InjectInto != "auto")
            {
                if (metadata.InjectInto == "content") { if (sandbox != null && world != "isolated") return null; world = "isolated"; }
                else if (metadata.InjectInto != "page" || world != "page") return null;
            }
            return world;
        }

        private bool Matches(UserScriptInstallation installation, DocumentFrame frame, bool contextMenu, ICollection<UserScriptDiagnostic> diagnostics)
        {
            var metadata = installation.Definition.Metadata;
            if (frame.ScriptScope.HasValue && frame.ScriptScope.Value != installation.ScriptKey) return false;
            if (frame.RevisionScope.HasValue && frame.RevisionScope.Value != installation.RevisionId) return false;
            var world = RequestedWorld(metadata);
            string unsupported = null;
            if (world == null) unsupported = "The requested execution world is not supported.";
            else if (metadata.Unwrap) unsupported = "@unwrap requires direct top-level execution and cannot use this wrapper.";
            else if (world == "isolated" && metadata.Capabilities.Contains("unsafeWindow")) unsupported = "DOM isolation cannot expose the page's unsafeWindow; request raw/JavaScript access explicitly.";
            else if (world == "isolated" && !_options.SupportsIsolatedWorlds) unsupported = "This host does not supply per-script isolated contexts.";
            if (unsupported != null)
            {
                diagnostics.Add(new UserScriptDiagnostic("MSR215_EXECUTION_WORLD_UNAVAILABLE", DiagnosticSeverity.Error, unsupported,
                    scriptKey: installation.ScriptKey, documentId: frame.DocumentId, frameId: frame.FrameId));
                return false;
            }
            if (world != frame.ExecutionWorld) return false;
            var rules = installation.PreparedRevision.UrlRules;
            var context = _options.EnvironmentProvider?.Invoke(frame)?.IsIncognito;
            if (!rules.AllowsEnvironment(context, out var reason))
            {
                diagnostics.Add(new UserScriptDiagnostic("MSR211_RUN_IN_UNAVAILABLE", DiagnosticSeverity.Warning, reason,
                    scriptKey: installation.ScriptKey, documentId: frame.DocumentId, frameId: frame.FrameId));
                return false;
            }
            try { return _matcher != null ? _matcher.IsMatch(installation.Definition.Metadata, frame.Url) : rules.IsMatch(frame.Url, contextMenu); }
            catch (RegexMatchTimeoutException exception)
            {
                diagnostics.Add(new UserScriptDiagnostic("MSR214_URL_RULE_TIMEOUT", DiagnosticSeverity.Error,
                    "A URL rule exceeded its time limit; execution was rejected.", exception, installation.ScriptKey, frame.DocumentId, frame.FrameId));
                return false;
            }
        }

        private ScriptInvocation CreateInvocation(UserScriptInstallation installation, string source, DocumentFrame frame)
        {
            var metadata = installation.Definition.Metadata;
            var environment = _options.EnvironmentProvider?.Invoke(frame) ?? new UserScriptExecutionEnvironment();
            var info = JsonSerializer.Serialize(BuildInfo(installation, environment, frame));
            return new ScriptInvocation(
                Guid.NewGuid().ToString("D"),
                installation.ScriptKey,
                source,
                metadata.DeclaredGrants,
                metadata.Capabilities,
                metadata.GrantDeclarationState,
                info,
                CreateToken(),
                CreateToken());
        }

        private object BuildInfo(UserScriptInstallation installation, UserScriptExecutionEnvironment environment, DocumentFrame frame)
        {
            var metadata = installation.Definition.Metadata;
            var header = ExtractMetadataHeader(installation.Definition.Source);
            var resources = metadata.Resources.Select(resource => new Dictionary<string, object>
            {
                ["name"] = resource.Name,
                ["url"] = resource.Url,
                ["integrity"] = resource.Integrity.Select(item => new Dictionary<string, object>
                {
                    ["algorithm"] = item.Algorithm,
                    ["digest"] = item.Digest
                }).ToArray()
            }).ToArray();
            var antifeatures = metadata.Antifeatures
                .GroupBy(item => item.Type, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => (object)group.ToDictionary(
                        item => item.Locale ?? "default",
                        item => item.Description,
                        StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase);
            var webRequest = metadata.WebRequest.Select(rule => new Dictionary<string, object>
            {
                ["selector"] = rule.Selector,
                ["action"] = rule.Action
            }).ToArray();
            var script = new Dictionary<string, object>
            {
                ["antifeatures"] = antifeatures,
                ["author"] = metadata.Author,
                ["connects"] = metadata.Connects,
                ["copyright"] = metadata.Copyright,
                ["description_i18n"] = metadata.LocalizedDescriptions.Count == 0 ? null : metadata.LocalizedDescriptions,
                ["description"] = metadata.Description,
                ["downloadURL"] = metadata.DownloadUrl,
                ["excludes"] = metadata.Excludes.Concat(metadata.ExcludeMatches).ToArray(),
                ["fileURL"] = installation.SourceOrigin,
                ["grant"] = metadata.DeclaredGrants,
                ["header"] = header,
                ["homepage"] = metadata.HomepageUrl,
                ["icon"] = metadata.IconUrl,
                ["icon64"] = metadata.Icon64Url,
                ["includes"] = metadata.Includes,
                ["lastModified"] = installation.UpdatedAt.ToUnixTimeMilliseconds(),
                ["matches"] = metadata.Matches,
                ["name_i18n"] = metadata.LocalizedNames.Count == 0 ? null : metadata.LocalizedNames,
                ["name"] = metadata.Name,
                ["namespace"] = metadata.Namespace,
                ["resources"] = resources,
                ["supportURL"] = metadata.SupportUrl,
                ["run-at"] = ToMetadataRunAt(metadata.RunAt),
                ["unwrap"] = metadata.Unwrap,
                ["updateURL"] = metadata.UpdateUrl,
                ["version"] = metadata.Version,
                ["webRequest"] = metadata.WebRequest.Count == 0 ? null : webRequest,
                ["options"] = new Dictionary<string, object>
                {
                    ["sandbox"] = metadata.Sandbox,
                    ["noframes"] = metadata.NoFrames,
                    ["run-in"] = metadata.RunIn.ToArray(),
                    ["unwrap"] = metadata.Unwrap,
                    ["run_at"] = ToMetadataRunAt(metadata.RunAt)
                }
            };
            var result = new Dictionary<string, object>
            {
                ["scriptKey"] = installation.ScriptKey.ToString(),
                ["name"] = metadata.Name,
                ["namespace"] = metadata.Namespace,
                ["version"] = typeof(UserScriptEngine).Assembly.GetName().Version.ToString(3),
                ["description"] = metadata.Description,
                ["grants"] = metadata.DeclaredGrants,
                ["grantDeclarationState"] = metadata.GrantDeclarationState.ToString(),
                ["downloadMode"] = environment.DownloadMode,
                ["sandboxMode"] = frame.ExecutionWorld == "isolated" ? "DOM" : "raw",
                ["scriptHandler"] = "MonkeySharp",
                ["scriptMetaStr"] = header,
                ["scriptUpdateURL"] = metadata.UpdateUrl,
                ["compatibility"] = new Dictionary<string, object>
                {
                    ["contractVersion"] = 2,
                    ["handler"] = "MonkeySharp",
                    ["executionWorld"] = frame.ExecutionWorld,
                    ["isolatedWorld"] = frame.ExecutionWorld == "isolated",
                    ["capabilities"] = UserScriptGrantCatalog.KnownCanonicalGrants
                        .Where(name => name.StartsWith("GM.", StringComparison.Ordinal))
                        .Concat(environment.CapabilityStatus.Keys).Distinct(StringComparer.Ordinal)
                        .ToDictionary(name => name, name => environment.CapabilityStatus.TryGetValue(name, out var status) ? status : frame.ExecutionWorld == "isolated" && name == "GM.webRequest" ? "isolated-frame-interception-unavailable" : _hostCapabilities.Contains(name) ? "available" :
                            environment.UnavailableCapabilities.TryGetValue(name, out var reason) ? reason : "service-unavailable"),
                    ["fields"] = new Dictionary<string, object>
                    {
                        ["scriptHandler"] = "fixed",
                        ["sandboxMode"] = "execution",
                        ["downloadMode"] = "host",
                        ["isIncognito"] = "host-when-known",
                        ["scriptWillUpdate"] = "host-when-known",
                        ["scriptUpdateURL"] = "metadata",
                        ["script"] = "mixed",
                        ["scriptKey"] = "extension",
                        ["grantDeclarationState"] = "extension"
                    }
                },
                ["script"] = script
            };
            if (environment.IsIncognito.HasValue) result["isIncognito"] = environment.IsIncognito.Value;
            if (environment.ScriptWillUpdate.HasValue) result["scriptWillUpdate"] = environment.ScriptWillUpdate.Value;
            return result;
        }

        private static string ToMetadataRunAt(UserScriptRunAt runAt)
        {
            switch (runAt)
            {
                case UserScriptRunAt.DocumentStart: return "document-start";
                case UserScriptRunAt.DocumentBody: return "document-body";
                case UserScriptRunAt.DocumentEnd: return "document-end";
                case UserScriptRunAt.ContextMenu: return "context-menu";
                default: return "document-idle";
            }
        }

        private static string ExtractMetadataHeader(string source)
        {
            if (string.IsNullOrEmpty(source))
                return null;
            var start = source.IndexOf("// ==UserScript==", StringComparison.Ordinal);
            if (start < 0)
                return null;
            var end = source.IndexOf("// ==/UserScript==", start, StringComparison.Ordinal);
            if (end < 0)
                return source.Substring(start);
            end += "// ==/UserScript==".Length;
            return source.Substring(start, end - start);
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

        private static bool RequiresBridge(UserScriptMetadata metadata)
        {
            return metadata.GrantDeclarationState == GrantDeclarationState.ExplicitList &&
                metadata.Grants.Any(item => !string.Equals(item, "none", StringComparison.Ordinal) &&
                    !string.Equals(item, "GM.info", StringComparison.Ordinal));
        }

        private static UserScriptRunAt ToRunAt(DocumentLifecycleKind kind)
        {
            switch (kind)
            {
                case DocumentLifecycleKind.DocumentStart: return UserScriptRunAt.DocumentStart;
                case DocumentLifecycleKind.BodyAvailable: return UserScriptRunAt.DocumentBody;
                case DocumentLifecycleKind.DomContentLoaded: return UserScriptRunAt.DocumentEnd;
                case DocumentLifecycleKind.DocumentIdle:
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

            public DocumentFrame Frame { get; private set; }
            public UserScriptInstallation Installation { get; }
            public ScriptInvocation Invocation { get; }
            public CancellationTokenSource Cancellation { get; }

            public void UpdateFrame(DocumentFrame frame)
            {
                Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            }
        }

        private sealed class DocumentState
        {
            public HashSet<string> Executed { get; } = new HashSet<string>(StringComparer.Ordinal);
            public List<string> ExecutionIds { get; } = new List<string>();
        }
    }
}
