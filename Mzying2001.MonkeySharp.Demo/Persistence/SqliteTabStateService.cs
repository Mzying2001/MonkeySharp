using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Demo.Persistence
{
    public sealed class SqliteTabStateService
    {
        private readonly SqliteDatabase _database;
        private readonly object _sync = new object();
        private readonly HashSet<string> _active = new HashSet<string>(StringComparer.Ordinal);
        public SqliteTabStateService(SqliteDatabase database)
        {
            _database = database;
            using (var connection = _database.OpenReady()) SqliteDatabase.Execute(connection, "DELETE FROM tab_states");
        }
        public ITabStateService ForTab(string tabId)
        {
            lock (_sync) _active.Add(tabId);
            return new BoundService(this, tabId);
        }
        public void CloseTab(string tabId)
        {
            lock (_sync)
            {
                _active.Remove(tabId);
                using (var connection = _database.OpenReady())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "DELETE FROM tab_states WHERE tab_id=$tab";
                    command.Parameters.AddWithValue("$tab", tabId);
                    command.ExecuteNonQuery();
                }
            }
        }

        private sealed class BoundService : ITabStateService
        {
            private readonly SqliteTabStateService _owner;
            private readonly string _tabId;
            public BoundService(SqliteTabStateService owner, string tabId) { _owner = owner; _tabId = tabId; }
            public async Task<string> GetAsync(ScriptKey scriptKey, DocumentFrame frame, CancellationToken cancellationToken)
            {
                var states = await GetAllAsync(scriptKey, cancellationToken).ConfigureAwait(false);
                return states.TryGetValue(_tabId, out var value) ? value : "{}";
            }
            public Task SaveAsync(ScriptKey scriptKey, DocumentFrame frame, string jsonValue, CancellationToken cancellationToken)
            {
                using (var document = JsonDocument.Parse(jsonValue))
                    if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Tab state must be a JSON object.");
                return Task.Run(() =>
                {
                    lock (_owner._sync)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!_owner._active.Contains(_tabId)) throw new OperationCanceledException("Tab has closed.");
                        using (var connection = _owner._database.OpenReady())
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = @"INSERT INTO tab_states(script_key,tab_id,json_value,updated_at) VALUES($script,$tab,$value,$updated)
ON CONFLICT(script_key,tab_id) DO UPDATE SET json_value=excluded.json_value,updated_at=excluded.updated_at";
                            command.Parameters.AddWithValue("$script", scriptKey.ToString());
                            command.Parameters.AddWithValue("$tab", _tabId);
                            command.Parameters.AddWithValue("$value", jsonValue);
                            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
                            command.ExecuteNonQuery();
                        }
                    }
                }, cancellationToken);
            }
            public Task<IReadOnlyDictionary<string, string>> GetAllAsync(ScriptKey scriptKey, CancellationToken cancellationToken)
            {
                return Task.Run<IReadOnlyDictionary<string, string>>(() =>
                {
                    lock (_owner._sync)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var states = new Dictionary<string, string>(StringComparer.Ordinal);
                        using (var connection = _owner._database.OpenReady())
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = "SELECT tab_id,json_value FROM tab_states WHERE script_key=$script";
                            command.Parameters.AddWithValue("$script", scriptKey.ToString());
                            using (var reader = command.ExecuteReader())
                                while (reader.Read())
                                    if (_owner._active.Contains(reader.GetString(0))) states.Add(reader.GetString(0), reader.GetString(1));
                        }
                        return states;
                    }
                }, cancellationToken);
            }
        }
    }
}
