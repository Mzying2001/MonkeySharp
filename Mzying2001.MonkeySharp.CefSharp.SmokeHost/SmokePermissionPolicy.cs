using Mzying2001.MonkeySharp.Core.Permissions;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.CefSharp.SmokeHost
{
    internal sealed class SmokePermissionPolicy : IUserScriptPermissionPolicy, IUserScriptPermissionPrompt, IDisposable
    {
        private readonly InteractiveUserScriptPermissionPolicy _interactive;
        private readonly int _deniedPort;
        internal int Prompts;
        internal SmokePermissionPolicy(int deniedPort)
        { _deniedPort = deniedPort; _interactive = new InteractiveUserScriptPermissionPolicy(new TargetPolicy(), new InMemoryUserScriptPermissionDecisionStore(), this); }
        public Task<PermissionDecision> AuthorizeAsync(ApiAuthorizationRequest request, CancellationToken token) => _interactive.AuthorizeAsync(request, token);
        public async Task<UserScriptPermissionChoice> PromptAsync(ApiAuthorizationRequest request, UserScriptPermissionScope scope, CancellationToken token)
        {
            Interlocked.Increment(ref Prompts);
            await Task.Delay(400, token).ConfigureAwait(false);
            var denied = new Uri(scope.Target).Port == _deniedPort;
            return new UserScriptPermissionChoice(denied ? PermissionDecision.Deny : PermissionDecision.Allow, denied);
        }
        public void Dispose() => _interactive.Dispose();
        private sealed class TargetPolicy : IUserScriptPermissionPolicy
        {
            public Task<PermissionDecision> AuthorizeAsync(ApiAuthorizationRequest request, CancellationToken token)
            { token.ThrowIfCancellationRequested(); return Task.FromResult(request.Installation.Definition.Metadata.Name == "Permission browser" && request.Target != null ? PermissionDecision.Prompt : PermissionDecision.Allow); }
        }
    }
}
