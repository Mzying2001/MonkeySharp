using Mzying2001.MonkeySharp.Core.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Demo.Services
{
    /// <summary>Copies each script's normal baseline once; private writes stay in session memory.</summary>
    public sealed class PrivateUserScriptValueStore : IUserScriptValueStore, IDisposable
    {
        private readonly IUserScriptValueStore _source;
        private readonly InMemoryUserScriptValueStore _memory = new InMemoryUserScriptValueStore();
        private readonly object _sync = new object();
        private readonly Dictionary<string, Task> _seeds = new Dictionary<string, Task>();
        private readonly HashSet<string> _ready = new HashSet<string>();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private bool _disposed;
        public PrivateUserScriptValueStore(IUserScriptValueStore source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _memory.ValueChanged += Forward;
        }
        public event EventHandler<UserScriptValueChangedEventArgs> ValueChanged;
        private void Forward(object sender, UserScriptValueChangedEventArgs args)
        {
            lock (_sync) if (_disposed || !_ready.Contains(args.ScriptKey)) return;
            ValueChanged?.Invoke(this, args);
        }
        private async Task Seed(string scriptKey, CancellationToken token)
        {
            Task seed;
            lock (_sync)
            {
                token.ThrowIfCancellationRequested();
                if (_disposed) throw new ObjectDisposedException(nameof(PrivateUserScriptValueStore));
                if (!_seeds.TryGetValue(scriptKey, out seed)) _seeds.Add(scriptKey, seed = Copy(scriptKey, _lifetime.Token));
            }
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => canceled.TrySetCanceled())) await await Task.WhenAny(seed, canceled.Task).ConfigureAwait(false);
        }
        private async Task Copy(string scriptKey, CancellationToken token)
        {
            var snapshot = await _source.GetSnapshotAsync(scriptKey, token).ConfigureAwait(false);
            await _memory.ApplyAsync(scriptKey, snapshot.Select(pair => new UserScriptValueMutation(pair.Key, pair.Value)), null, token).ConfigureAwait(false);
            lock (_sync) _ready.Add(scriptKey);
        }
        public async Task<StoredValue> GetAsync(string scriptKey, string key, CancellationToken token)
        { await Seed(scriptKey, token).ConfigureAwait(false); return await _memory.GetAsync(scriptKey, key, token).ConfigureAwait(false); }
        public async Task SetAsync(string scriptKey, string key, UserScriptValue value, CancellationToken token)
        { await Seed(scriptKey, token).ConfigureAwait(false); await _memory.SetAsync(scriptKey, key, value, token).ConfigureAwait(false); }
        public async Task<bool> DeleteAsync(string scriptKey, string key, CancellationToken token)
        { await Seed(scriptKey, token).ConfigureAwait(false); return await _memory.DeleteAsync(scriptKey, key, token).ConfigureAwait(false); }
        public async Task<IReadOnlyList<string>> ListKeysAsync(string scriptKey, CancellationToken token)
        { await Seed(scriptKey, token).ConfigureAwait(false); return await _memory.ListKeysAsync(scriptKey, token).ConfigureAwait(false); }
        public async Task<UserScriptValueSnapshot> GetSnapshotAsync(string scriptKey, CancellationToken token)
        { await Seed(scriptKey, token).ConfigureAwait(false); return await _memory.GetSnapshotAsync(scriptKey, token).ConfigureAwait(false); }
        public async Task<UserScriptValueChangeBatch> GetChangesAsync(string scriptKey, long afterSequence, CancellationToken token)
        { await Seed(scriptKey, token).ConfigureAwait(false); return await _memory.GetChangesAsync(scriptKey, afterSequence, token).ConfigureAwait(false); }
        public async Task<UserScriptValueCommit> ApplyAsync(string scriptKey, IEnumerable<UserScriptValueMutation> mutations, UserScriptValueMutationOrigin origin, CancellationToken token)
        {
            var batch = UserScriptValueMutation.Validate(mutations);
            await Seed(scriptKey, token).ConfigureAwait(false);
            return await _memory.ApplyAsync(scriptKey, batch, origin, token).ConfigureAwait(false);
        }
        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true; _lifetime.Cancel(); _memory.ValueChanged -= Forward; _memory.Dispose(); _seeds.Clear(); _ready.Clear();
            }
            _lifetime.Dispose();
        }
    }
}
