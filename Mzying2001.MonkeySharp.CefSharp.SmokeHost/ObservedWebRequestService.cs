using Mzying2001.MonkeySharp.Core.Apis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.CefSharp.SmokeHost
{
    /// <summary>Test-only acknowledgement of committed fixture rule registrations.</summary>
    internal sealed class ObservedWebRequestService : IAsyncWebRequestService, IDisposable
    {
        private readonly InMemoryWebRequestService _inner = new InMemoryWebRequestService();
        private readonly object _sync = new object();
        private TaskCompletionSource<bool> _changed = Signal();
        private int _version;
        private bool _disposed;
        internal string InstallationId { get; set; }

        public IWebRequestRegistration SetRules(string owner, IReadOnlyList<WebRequestRule> rules,
            Func<WebRequestEvent, bool> scope, Action<WebRequestRule, WebRequestEvent, WebRequestDecision> listener = null,
            Func<WebRequestEvent, CancellationToken, Task<bool>> authorize = null, string installationId = null, bool isStatic = false)
        {
            var registration = _inner.SetRules(owner, rules, scope, listener, authorize, installationId, isStatic);
            if (!isStatic && InstallationId != null && installationId != null &&
                (installationId == InstallationId || installationId.EndsWith(":" + InstallationId, StringComparison.Ordinal)))
            {
                TaskCompletionSource<bool> changed;
                lock (_sync) { _version++; changed = _changed; _changed = Signal(); }
                changed.TrySetResult(true);
            }
            return registration;
        }

        internal async Task WaitAsync(int version, CancellationToken token)
        {
            var timer = Stopwatch.StartNew();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                Task changed;
                lock (_sync)
                {
                    if (_disposed) throw new ObjectDisposedException(nameof(ObservedWebRequestService));
                    if (_version >= version) return;
                    changed = _changed.Task;
                }
                var remaining = TimeSpan.FromSeconds(10) - timer.Elapsed;
                if (remaining <= TimeSpan.Zero) throw new TimeoutException("webRequest registration " + version + " was not acknowledged.");
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    if (await Task.WhenAny(changed, Task.Delay(remaining, deadline.Token)).ConfigureAwait(false) != changed)
                    {
                        token.ThrowIfCancellationRequested();
                        throw new TimeoutException("webRequest registration " + version + " was not acknowledged.");
                    }
                    deadline.Cancel();
                }
            }
        }

        public IWebRequestRegistration AddListener(WebRequestFilter filter, EventHandler<WebRequestEvent> listener) => _inner.AddListener(filter, listener);
        public WebRequestDecision Evaluate(WebRequestEvent request) => _inner.Evaluate(request);
        public Task<WebRequestDecision> EvaluateAsync(WebRequestEvent request, CancellationToken token) => _inner.EvaluateAsync(request, token);
        public void Dispose() { _inner.Dispose(); lock (_sync) { _disposed = true; _changed.TrySetCanceled(); } }
        private static TaskCompletionSource<bool> Signal() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
