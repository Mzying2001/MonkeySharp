using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Demo.Services
{
    /// <summary>Private tab state never enters SQLite, its journal, or normal-tab enumeration.</summary>
    public sealed class MemoryTabStateService
    {
        private readonly object _sync = new object();
        private readonly HashSet<string> _active = new HashSet<string>();
        private readonly Dictionary<ScriptKey, Dictionary<string, string>> _states = new Dictionary<ScriptKey, Dictionary<string, string>>();
        public ITabStateService ForTab(string tabId)
        {
            lock (_sync) _active.Add(tabId);
            return new BoundService(this, tabId);
        }
        public void CloseTab(string tabId)
        {
            lock (_sync) { _active.Remove(tabId); foreach (var states in _states.Values) states.Remove(tabId); }
        }
        public void Clear() { lock (_sync) { _active.Clear(); _states.Clear(); } }
        private sealed class BoundService : ITabStateService
        {
            private readonly MemoryTabStateService _owner;
            private readonly string _tabId;
            public BoundService(MemoryTabStateService owner, string tabId) { _owner = owner; _tabId = tabId; }
            public async Task<string> GetAsync(ScriptKey scriptKey, DocumentFrame frame, CancellationToken token)
            { var states = await GetAllAsync(scriptKey, token); return states.TryGetValue(_tabId, out var value) ? value : "{}"; }
            public Task SaveAsync(ScriptKey scriptKey, DocumentFrame frame, string jsonValue, CancellationToken token)
            {
                using (var document = JsonDocument.Parse(jsonValue))
                    if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Tab state must be an object.");
                lock (_owner._sync)
                {
                    token.ThrowIfCancellationRequested();
                    if (!_owner._active.Contains(_tabId)) throw new OperationCanceledException("Tab has closed.");
                    if (!_owner._states.TryGetValue(scriptKey, out var states)) _owner._states.Add(scriptKey, states = new Dictionary<string, string>());
                    states[_tabId] = jsonValue;
                }
                return Task.CompletedTask;
            }
            public Task<IReadOnlyDictionary<string, string>> GetAllAsync(ScriptKey scriptKey, CancellationToken token)
            {
                lock (_owner._sync)
                {
                    token.ThrowIfCancellationRequested();
                    var states = _owner._states.TryGetValue(scriptKey, out var values) ? new Dictionary<string, string>(values) : new Dictionary<string, string>();
                    return Task.FromResult<IReadOnlyDictionary<string, string>>(new ReadOnlyDictionary<string, string>(states));
                }
            }
        }
    }
}
