using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Matching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Permissions
{
    /// <summary>Identifies one installation, capability and exact remembered target scope.</summary>
    public sealed class UserScriptPermissionScope
    {
        /// <summary>Creates an immutable permission key.</summary>
        public UserScriptPermissionScope(string scriptKey, string kind, string target)
        { ScriptKey = scriptKey ?? throw new ArgumentNullException(nameof(scriptKey)); Kind = kind ?? throw new ArgumentNullException(nameof(kind)); Target = target ?? throw new ArgumentNullException(nameof(target)); }
        /// <summary>Gets the stable installation identity.</summary>
        public string ScriptKey { get; }
        /// <summary>Gets the capability family.</summary>
        public string Kind { get; }
        /// <summary>Gets the exact target origin or non-network scope.</summary>
        public string Target { get; }
        internal string Key => ScriptKey + "\n" + Kind + "\n" + Target;
        /// <summary>Derives an origin scope without URL credentials, query strings or path secrets.</summary>
        public static UserScriptPermissionScope From(ApiAuthorizationRequest request)
        {
            var target = request.Target ?? string.Empty;
            if (Uri.TryCreate(target, UriKind.Absolute, out var url) && (url.Scheme == "http" || url.Scheme == "https"))
                target = url.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
            return new UserScriptPermissionScope(request.Installation.ScriptKey.ToString(), request.Method, target);
        }
    }

    /// <summary>Contains an explicit user choice and whether it applies to later requests.</summary>
    public sealed class UserScriptPermissionChoice
    {
        /// <summary>Creates an allow/deny choice; Prompt is not a terminal choice.</summary>
        public UserScriptPermissionChoice(PermissionDecision decision, bool remember = false)
        { if (decision != PermissionDecision.Allow && decision != PermissionDecision.Deny) throw new ArgumentException("A prompt needs an explicit allow/deny choice."); Decision = decision; Remember = remember; }
        /// <summary>Gets the terminal decision.</summary>
        public PermissionDecision Decision { get; }
        /// <summary>Gets whether to persist this exact scope.</summary>
        public bool Remember { get; }
    }

    /// <summary>Stores explicit decisions, partitioned by installation, permission kind and target scope.</summary>
    public interface IUserScriptPermissionDecisionStore
    {
        /// <summary>Gets a remembered decision, or null.</summary>
        Task<PermissionDecision?> GetAsync(UserScriptPermissionScope scope, CancellationToken token);
        /// <summary>Atomically saves a terminal decision.</summary>
        Task SetAsync(UserScriptPermissionScope scope, PermissionDecision decision, CancellationToken token);
        /// <summary>Revokes decisions belonging to an installation, or all when scriptKey is null.</summary>
        Task RevokeAsync(string scriptKey, CancellationToken token);
    }

    /// <summary>Obtains a user decision asynchronously; cancellation must close its interaction.</summary>
    public interface IUserScriptPermissionPrompt
    {
        /// <summary>Prompts for a scope; allow-once applies only to the current coalesced request batch.</summary>
        Task<UserScriptPermissionChoice> PromptAsync(ApiAuthorizationRequest request, UserScriptPermissionScope scope, CancellationToken token);
    }

    /// <summary>Explicit non-interactive configuration that rejects requests requiring a prompt.</summary>
    public sealed class RejectUserScriptPermissionPrompt : IUserScriptPermissionPrompt
    {
        /// <inheritdoc />
        public Task<UserScriptPermissionChoice> PromptAsync(ApiAuthorizationRequest request, UserScriptPermissionScope scope, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(new UserScriptPermissionChoice(PermissionDecision.Deny)); }
    }

    /// <summary>Requests consent for network/cookie/download targets and allows other declared APIs.</summary>
    public sealed class SensitiveUserScriptPermissionsPolicy : IUserScriptPermissionPolicy
    {
        /// <inheritdoc />
        public Task<PermissionDecision> AuthorizeAsync(ApiAuthorizationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(request.Target != null && (request.Method == "GM.xmlHttpRequest" || request.Method == "GM.cookie" || request.Method == "GM.download")
                ? PermissionDecision.Prompt : PermissionDecision.Allow);
        }
    }

    /// <summary>Resolves policy, remembered choices and shared asynchronous prompts without widening a scope.</summary>
    public sealed class InteractiveUserScriptPermissionPolicy : IUserScriptPermissionPolicy, IDisposable
    {
        private readonly IUserScriptPermissionPolicy _policy;
        private readonly IUserScriptPermissionDecisionStore _store;
        private readonly IUserScriptPermissionPrompt _prompt;
        private readonly object _sync = new object();
        private readonly Dictionary<string, Pending> _pending = new Dictionary<string, Pending>();
        private bool _disposed;
        /// <summary>Creates a shared coordinator. Hosts without UI must explicitly use a rejecting prompt.</summary>
        public InteractiveUserScriptPermissionPolicy(IUserScriptPermissionPolicy policy, IUserScriptPermissionDecisionStore store, IUserScriptPermissionPrompt prompt)
        { _policy = policy ?? throw new ArgumentNullException(nameof(policy)); _store = store ?? throw new ArgumentNullException(nameof(store)); _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt)); }

        /// <inheritdoc />
        public async Task<PermissionDecision> AuthorizeAsync(ApiAuthorizationRequest request, CancellationToken cancellationToken)
        {
            lock (_sync) if (_disposed) throw new ObjectDisposedException(nameof(InteractiveUserScriptPermissionPolicy));
            if (!UserScriptPermissionTargets.IsDeclared(request)) return PermissionDecision.Deny;
            var decision = await _policy.AuthorizeAsync(request, cancellationToken).ConfigureAwait(false);
            if (decision != PermissionDecision.Prompt) return decision;
            var scope = UserScriptPermissionScope.From(request);
            var remembered = await _store.GetAsync(scope, cancellationToken).ConfigureAwait(false);
            if (remembered.HasValue) return remembered.Value;
            if (!request.CanPrompt) return PermissionDecision.Deny;
            Pending pending;
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(InteractiveUserScriptPermissionPolicy));
                if (_pending.TryGetValue(scope.Key, out pending) && pending.Task.IsCompleted) { _pending.Remove(scope.Key); pending = null; }
                if (pending == null)
                {
                    pending = new Pending();
                    _pending.Add(scope.Key, pending);
                    pending.Task = ResolveAsync(pending, request, scope);
                }
                pending.Waiters++;
            }
            try
            {
                var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (cancellationToken.Register(() => canceled.TrySetCanceled()))
                {
                    await await Task.WhenAny(pending.Task, canceled.Task).ConfigureAwait(false);
                    return await pending.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                lock (_sync)
                {
                    pending.Waiters--;
                    if (pending.Waiters == 0)
                    {
                        if (_pending.TryGetValue(scope.Key, out var current) && ReferenceEquals(current, pending)) _pending.Remove(scope.Key);
                        if (!pending.Task.IsCompleted) pending.Cancel();
                    }
                }
            }
        }

        private async Task<PermissionDecision> ResolveAsync(Pending pending, ApiAuthorizationRequest request, UserScriptPermissionScope scope)
        {
            // Always leave the lock/caller before invoking host UI.
            await Task.Yield();
            try
            {
                var remembered = await _store.GetAsync(scope, pending.Cancellation.Token).ConfigureAwait(false);
                if (remembered.HasValue) return remembered.Value;
                var choice = await _prompt.PromptAsync(request, scope, pending.Cancellation.Token).ConfigureAwait(false);
                pending.Cancellation.Token.ThrowIfCancellationRequested();
                if (choice.Remember) await _store.SetAsync(scope, choice.Decision, pending.Cancellation.Token).ConfigureAwait(false);
                return choice.Decision;
            }
            finally { pending.Cancellation.Dispose(); }
        }

        /// <summary>Cancels outstanding prompts, for example when remembered decisions are revoked.</summary>
        public void CancelPending()
        { lock (_sync) foreach (var value in _pending.Values) if (!value.Task.IsCompleted) value.Cancel(); }
        /// <inheritdoc />
        public void Dispose() { lock (_sync) { _disposed = true; CancelPending(); } }
        private sealed class Pending
        {
            internal readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            internal Task<PermissionDecision> Task;
            internal int Waiters;
            internal void Cancel() { try { Cancellation.Cancel(); } catch (ObjectDisposedException) { } }
        }
    }

    /// <summary>Provides a process-local decision store with the same exact-scope semantics as persistent hosts.</summary>
    public sealed class InMemoryUserScriptPermissionDecisionStore : IUserScriptPermissionDecisionStore
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, Tuple<UserScriptPermissionScope, PermissionDecision>> _values = new Dictionary<string, Tuple<UserScriptPermissionScope, PermissionDecision>>();
        /// <inheritdoc />
        public Task<PermissionDecision?> GetAsync(UserScriptPermissionScope scope, CancellationToken token)
        { token.ThrowIfCancellationRequested(); lock (_sync) return Task.FromResult<PermissionDecision?>(_values.TryGetValue(scope.Key, out var value) ? value.Item2 : (PermissionDecision?)null); }
        /// <inheritdoc />
        public Task SetAsync(UserScriptPermissionScope scope, PermissionDecision decision, CancellationToken token)
        {
            if (decision != PermissionDecision.Allow && decision != PermissionDecision.Deny) throw new ArgumentException("Only terminal choices can be stored.");
            token.ThrowIfCancellationRequested(); lock (_sync) _values[scope.Key] = Tuple.Create(scope, decision);
            return Task.CompletedTask;
        }
        /// <inheritdoc />
        public Task RevokeAsync(string scriptKey, CancellationToken token)
        { token.ThrowIfCancellationRequested(); lock (_sync) foreach (var key in _values.Where(item => scriptKey == null || item.Value.Item1.ScriptKey == scriptKey).Select(item => item.Key).ToArray()) _values.Remove(key); return Task.CompletedTask; }
    }

    /// <summary>Shared declaration checks applied before consent and again by effect providers.</summary>
    public static class UserScriptPermissionTargets
    {
        /// <summary>Checks a concrete target against connect or cookie URL declarations. User consent cannot widen these declarations.</summary>
        public static bool IsDeclared(ApiAuthorizationRequest request)
        {
            if (request.Target == null) return true;
            if (request.Method != "GM.xmlHttpRequest" && request.Method != "GM.cookie") return true;
            if (!Uri.TryCreate(request.Target, UriKind.Absolute, out var target) || (target.Scheme != "http" && target.Scheme != "https")) return false;
            return request.Method == "GM.cookie" ? new UserScriptMatcher().IsMatch(request.Installation.Definition.Metadata, target) :
                ResourceAndNetworkApiProvider.ConnectAllows(request.Installation.Definition.Metadata.Connects, request.Frame.Url, target);
        }
    }
}
