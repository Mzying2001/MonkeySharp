using Mzying2001.MonkeySharp.Core.Storage;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.CefSharp.SmokeHost
{
    internal sealed class PreparedValueStore : IUserScriptValueStore, IDisposable
    {
        private readonly InMemoryUserScriptValueStore _inner = new InMemoryUserScriptValueStore();
        internal readonly ConcurrentDictionary<string, TimeSpan> SnapshotDelays = new ConcurrentDictionary<string, TimeSpan>();
        public event EventHandler<UserScriptValueChangedEventArgs> ValueChanged { add { _inner.ValueChanged += value; } remove { _inner.ValueChanged -= value; } }
        public Task<StoredValue> GetAsync(string script, string key, CancellationToken token) => _inner.GetAsync(script, key, token);
        public Task SetAsync(string script, string key, UserScriptValue value, CancellationToken token) => _inner.SetAsync(script, key, value, token);
        public Task<bool> DeleteAsync(string script, string key, CancellationToken token) => _inner.DeleteAsync(script, key, token);
        public Task<IReadOnlyList<string>> ListKeysAsync(string script, CancellationToken token) => _inner.ListKeysAsync(script, token);
        public Task<UserScriptValueCommit> ApplyAsync(string script, IEnumerable<UserScriptValueMutation> mutations, UserScriptValueMutationOrigin origin, CancellationToken token) => _inner.ApplyAsync(script, mutations, origin, token);
        public Task<UserScriptValueChangeBatch> GetChangesAsync(string script, long after, CancellationToken token) => _inner.GetChangesAsync(script, after, token);
        public async Task<UserScriptValueSnapshot> GetSnapshotAsync(string script, CancellationToken token)
        { if (SnapshotDelays.TryGetValue(script, out var delay)) await Task.Delay(delay, token); return await _inner.GetSnapshotAsync(script, token); }
        public void Dispose() => _inner.Dispose();
    }
}
