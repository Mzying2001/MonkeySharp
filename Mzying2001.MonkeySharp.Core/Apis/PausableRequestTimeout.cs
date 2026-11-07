using System;
using System.Diagnostics;
using System.Threading;

namespace Mzying2001.MonkeySharp.Core.Apis
{
    /// <summary>Counts active operation time while excluding asynchronous authorization waits.</summary>
    public sealed class PausableRequestTimeout : IDisposable
    {
        private readonly object _sync = new object();
        private readonly TimeSpan _budget;
        private readonly Stopwatch _active = new Stopwatch();
        private readonly CancellationTokenSource _source = new CancellationTokenSource();
        private bool _started, _disposed;
        private int _pauses;
        /// <summary>Creates an unstarted timeout; effect-free operation creation does not spend its budget.</summary>
        public PausableRequestTimeout(TimeSpan budget)
        { if (budget <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(budget)); _budget = budget; Token = _source.Token; }
        /// <summary>Gets the deadline token.</summary>
        public CancellationToken Token { get; }
        /// <summary>Gets whether active time exhausted the budget.</summary>
        public bool IsCancellationRequested => Token.IsCancellationRequested;
        /// <summary>Starts counting once.</summary>
        public void Start()
        { lock (_sync) { if (_disposed || _started) return; _started = true; if (_pauses == 0) { _active.Start(); _source.CancelAfter(_budget); } } }
        /// <summary>Excludes a nested asynchronous authorization interval until the returned lease is disposed.</summary>
        public IDisposable Pause()
        {
            lock (_sync)
            {
                if (!_disposed && _pauses++ == 0) { _active.Stop(); _source.CancelAfter(Timeout.InfiniteTimeSpan); }
                return new Lease(this);
            }
        }
        private void Resume()
        {
            var cancel = false;
            lock (_sync)
            {
                if (_disposed || _pauses == 0 || --_pauses != 0 || !_started) return;
                var remaining = _budget - _active.Elapsed;
                if (remaining <= TimeSpan.Zero) cancel = true;
                else { _active.Start(); _source.CancelAfter(remaining); }
            }
            if (cancel) try { _source.Cancel(); } catch (ObjectDisposedException) { }
        }
        /// <inheritdoc />
        public void Dispose() { lock (_sync) { if (_disposed) return; _disposed = true; _source.Dispose(); } }
        private sealed class Lease : IDisposable
        {
            private PausableRequestTimeout _owner;
            internal Lease(PausableRequestTimeout owner) { _owner = owner; }
            public void Dispose() { Interlocked.Exchange(ref _owner, null)?.Resume(); }
        }
    }
}
