using CefSharp;
using Mzying2001.MonkeySharp.Core.Apis;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.CefSharp
{
    /// <summary>Observes actual CEF mute state without manufacturing an audible value.</summary>
    public sealed class CefSharpTabAudioService : ITabAudioService, IDisposable
    {
        private readonly Func<IBrowserHost> _host;
        private readonly Timer _timer;
        private readonly object _sync = new object();
        private TabAudioState _last;
        private string _reason;
        private bool _disposed;
        private int _polling;
        /// <summary>Creates a tab service. Polling also observes persistent changes from other host controls.</summary>
        public CefSharpTabAudioService(Func<IBrowserHost> host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _timer = new Timer(_ => Poll(), null, 200, 200);
        }
        /// <inheritdoc />
        public bool SupportsAudible => false;
        /// <inheritdoc />
        public event EventHandler<TabAudioState> StateChanged;
        /// <inheritdoc />
        public Task<TabAudioState> GetStateAsync(CancellationToken cancellationToken) => Cef.UIThreadTaskFactory.StartNew(() => Read(null, cancellationToken));
        /// <inheritdoc />
        public Task<TabAudioState> SetMuteAsync(bool isMuted, CancellationToken cancellationToken) => Cef.UIThreadTaskFactory.StartNew(() => Read(isMuted, cancellationToken));
        private TabAudioState Read(bool? requested, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            TabAudioState state;
            bool changed;
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(CefSharpTabAudioService));
                var host = _host() ?? throw new InvalidOperationException("The tab browser is not initialized.");
                var before = host.IsAudioMuted;
                if (_last != null && before != _last.IsMuted) _reason = null;
                if (requested.HasValue)
                {
                    host.SetAudioMuted(requested.Value);
                    _reason = requested.Value ? "extension" : null;
                }
                var muted = host.IsAudioMuted;
                if (requested.HasValue && muted != requested.Value) throw new InvalidOperationException("CEF did not accept the requested mute state.");
                state = new TabAudioState(muted, muted ? _reason : null);
                changed = _last != null && (_last.IsMuted != muted || _last.MuteReason != state.MuteReason) || requested.HasValue && before != muted;
                _last = state;
            }
            if (changed) StateChanged?.Invoke(this, state);
            return state;
        }
        private async void Poll()
        {
            if (Interlocked.Exchange(ref _polling, 1) != 0) return;
            try
            {
                lock (_sync) if (_disposed || _host() == null) return;
                await GetStateAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            finally { Volatile.Write(ref _polling, 0); }
        }
        /// <inheritdoc />
        public void Dispose() { lock (_sync) _disposed = true; _timer.Dispose(); }
    }
}
