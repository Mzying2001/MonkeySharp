using Microsoft.Data.Sqlite;
using Mzying2001.MonkeySharp.Core.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Demo.Persistence
{
    public sealed class SqliteUserScriptValueStore : IUserScriptValueStore, IDisposable
    {
        private readonly SqliteDatabase _database;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly UserScriptValueChangeJournal _journal = new UserScriptValueChangeJournal();
        private bool _disposed;
        public SqliteUserScriptValueStore(SqliteDatabase database) { _database = database; }
        public event EventHandler<UserScriptValueChangedEventArgs> ValueChanged;

        public Task<StoredValue> GetAsync(string scriptKey, string key, CancellationToken cancellationToken)
        {
            Validate(key);
            return RunAsync(scriptKey, connection => Read(connection, key, scriptKey), cancellationToken);
        }

        public Task SetAsync(string scriptKey, string key, UserScriptValue value, CancellationToken cancellationToken)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return ApplyAsync(scriptKey, new[] { new UserScriptValueMutation(key, value) }, null, cancellationToken);
        }

        public async Task<bool> DeleteAsync(string scriptKey, string key, CancellationToken cancellationToken)
            => (await ApplyAsync(scriptKey, new[] { new UserScriptValueMutation(key, null) }, null, cancellationToken).ConfigureAwait(false)).Count != 0;

        public Task<UserScriptValueCommit> ApplyAsync(string scriptKey,
            IEnumerable<UserScriptValueMutation> mutations, UserScriptValueMutationOrigin origin, CancellationToken cancellationToken)
        {
            var batch = UserScriptValueMutation.Validate(mutations);
            return RunAsync<UserScriptValueCommit>(scriptKey, connection =>
            {
                var pending = new List<Tuple<string, StoredValue, StoredValue>>();
                using (var transaction = connection.BeginTransaction())
                {
                    foreach (var mutation in batch)
                    {
                        var old = Read(connection, mutation.Key, scriptKey, transaction);
                        var value = mutation.Value;
                        if (value == null && !old.Exists || value != null && old.Exists && old.Value.Equals(value)) continue;
                        using (var command = connection.CreateCommand())
                        {
                            command.Transaction = transaction;
                            command.CommandText = value == null
                                ? "DELETE FROM script_values WHERE script_key=$script AND value_key=$key"
                                : ("INSERT INTO script_values(script_key,value_key,encoded_value,updated_at) VALUES($script,$key,$value,$updated) " +
                                   "ON CONFLICT(script_key,value_key) DO UPDATE SET encoded_value=excluded.encoded_value,updated_at=excluded.updated_at");
                            command.Parameters.AddWithValue("$script", scriptKey);
                            command.Parameters.AddWithValue("$key", mutation.Key);
                            if (value != null)
                            {
                                command.Parameters.AddWithValue("$value", value.EncodedValue);
                                command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
                            }
                            command.ExecuteNonQuery();
                        }
                        pending.Add(Tuple.Create(mutation.Key, old, value == null ? StoredValue.Missing : new StoredValue(true, value)));
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    transaction.Commit();
                }
                var changes = pending.Select(item => _journal.Append(scriptKey, item.Item1, item.Item2, item.Item3, origin)).ToList().AsReadOnly();
                foreach (var change in changes) Publish(change);
                return new UserScriptValueCommit(changes, _journal.Sequence(scriptKey));
            }, cancellationToken);
        }

        public Task<UserScriptValueChangeBatch> GetChangesAsync(string scriptKey, long afterSequence, CancellationToken cancellationToken)
            => RunAsync(scriptKey, connection => _journal.Read(scriptKey, afterSequence), cancellationToken);

        public async Task<IReadOnlyList<string>> ListKeysAsync(string scriptKey, CancellationToken cancellationToken)
        {
            var values = await GetSnapshotAsync(scriptKey, cancellationToken).ConfigureAwait(false);
            return values.Select(item => item.Key).ToList().AsReadOnly();
        }

        public Task<UserScriptValueSnapshot> GetSnapshotAsync(string scriptKey, CancellationToken cancellationToken)
        {
            return RunAsync<UserScriptValueSnapshot>(scriptKey, connection =>
            {
                var values = new List<KeyValuePair<string, UserScriptValue>>();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT value_key,encoded_value FROM script_values WHERE script_key=$script ORDER BY rowid";
                    command.Parameters.AddWithValue("$script", scriptKey);
                    using (var reader = command.ExecuteReader())
                        while (reader.Read()) values.Add(new KeyValuePair<string, UserScriptValue>(reader.GetString(0), UserScriptValue.FromEncoded(reader.GetString(1))));
                }
                return new UserScriptValueSnapshot(values, _journal.Sequence(scriptKey));
            }, cancellationToken);
        }

        private async Task<TResult> RunAsync<TResult>(string scriptKey, Func<SqliteConnection, TResult> operation, CancellationToken token)
        {
            if (string.IsNullOrEmpty(scriptKey)) throw new ArgumentException("A script key is required.");
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_disposed) throw new ObjectDisposedException(nameof(SqliteUserScriptValueStore));
                return await Task.Run(() => { token.ThrowIfCancellationRequested(); using (var connection = _database.OpenReady()) return operation(connection); }, token).ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }

        private static StoredValue Read(SqliteConnection connection, string key, string scriptKey, SqliteTransaction transaction = null)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT encoded_value FROM script_values WHERE script_key=$script AND value_key=$key";
                command.Parameters.AddWithValue("$script", scriptKey);
                command.Parameters.AddWithValue("$key", key);
                var value = command.ExecuteScalar();
                return value == null ? StoredValue.Missing : new StoredValue(true, UserScriptValue.FromEncoded((string)value));
            }
        }

        private void Publish(UserScriptValueChangedEventArgs args)
        {
            var handlers = ValueChanged;
            if (handlers == null) return;
            foreach (EventHandler<UserScriptValueChangedEventArgs> handler in handlers.GetInvocationList())
            {
                try { handler(this, args); } catch (Exception exception) { System.Diagnostics.Trace.TraceError(exception.ToString()); }
            }
        }
        private static void Validate(string key) { if (key == null) throw new ArgumentNullException(nameof(key)); }
        public void Dispose() { _disposed = true; }
    }
}
