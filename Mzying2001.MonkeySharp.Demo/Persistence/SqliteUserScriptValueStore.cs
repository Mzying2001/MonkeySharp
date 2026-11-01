using Microsoft.Data.Sqlite;
using Mzying2001.MonkeySharp.Core.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Demo.Persistence
{
    public sealed class SqliteUserScriptValueStore : IUserScriptValueStore, IUserScriptValueSnapshotProvider, IDisposable
    {
        private readonly SqliteDatabase _database;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private long _sequence;
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
            Validate(key);
            if (value == null) throw new ArgumentNullException(nameof(value));
            return RunAsync(scriptKey, connection =>
            {
                var old = Read(connection, key, scriptKey);
                if (old.Exists && old.Value.Equals(value)) return false;
                using (var transaction = connection.BeginTransaction())
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"INSERT INTO script_values(script_key,value_key,encoded_value,updated_at) VALUES($script,$key,$value,$updated)
ON CONFLICT(script_key,value_key) DO UPDATE SET encoded_value=excluded.encoded_value,updated_at=excluded.updated_at";
                    command.Parameters.AddWithValue("$script", scriptKey);
                    command.Parameters.AddWithValue("$key", key);
                    command.Parameters.AddWithValue("$value", value.EncodedValue);
                    command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
                    command.ExecuteNonQuery();
                    cancellationToken.ThrowIfCancellationRequested();
                    transaction.Commit();
                }
                Publish(new UserScriptValueChangedEventArgs(++_sequence, scriptKey, key, old, new StoredValue(true, value), ValueChangeKind.Set));
                return true;
            }, cancellationToken);
        }

        public Task<bool> DeleteAsync(string scriptKey, string key, CancellationToken cancellationToken)
        {
            Validate(key);
            return RunAsync(scriptKey, connection =>
            {
                var old = Read(connection, key, scriptKey);
                if (!old.Exists) return false;
                using (var transaction = connection.BeginTransaction())
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "DELETE FROM script_values WHERE script_key=$script AND value_key=$key";
                    command.Parameters.AddWithValue("$script", scriptKey);
                    command.Parameters.AddWithValue("$key", key);
                    command.ExecuteNonQuery();
                    cancellationToken.ThrowIfCancellationRequested();
                    transaction.Commit();
                }
                Publish(new UserScriptValueChangedEventArgs(++_sequence, scriptKey, key, old, StoredValue.Missing, ValueChangeKind.Deleted));
                return true;
            }, cancellationToken);
        }

        public async Task<IReadOnlyList<string>> ListKeysAsync(string scriptKey, CancellationToken cancellationToken)
        {
            var values = await GetSnapshotAsync(scriptKey, cancellationToken).ConfigureAwait(false);
            return values.Select(item => item.Key).ToList().AsReadOnly();
        }

        public Task<IReadOnlyList<KeyValuePair<string, UserScriptValue>>> GetSnapshotAsync(string scriptKey, CancellationToken cancellationToken)
        {
            return RunAsync<IReadOnlyList<KeyValuePair<string, UserScriptValue>>>(scriptKey, connection =>
            {
                var values = new List<KeyValuePair<string, UserScriptValue>>();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT value_key,encoded_value FROM script_values WHERE script_key=$script ORDER BY rowid";
                    command.Parameters.AddWithValue("$script", scriptKey);
                    using (var reader = command.ExecuteReader())
                        while (reader.Read()) values.Add(new KeyValuePair<string, UserScriptValue>(reader.GetString(0), UserScriptValue.FromEncoded(reader.GetString(1))));
                }
                return values.AsReadOnly();
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

        private static StoredValue Read(SqliteConnection connection, string key, string scriptKey)
        {
            using (var command = connection.CreateCommand())
            {
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
