using Microsoft.Data.Sqlite;
using Mzying2001.MonkeySharp.Core.Storage;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
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

        public Task SetAsync(string scriptKey, string key, string jsonValue, CancellationToken cancellationToken)
        {
            Validate(key);
            string canonical;
            using (var document = JsonDocument.Parse(jsonValue)) canonical = JsonSerializer.Serialize(document.RootElement);
            return RunAsync(scriptKey, connection =>
            {
                var old = Read(connection, key, scriptKey);
                using (var transaction = connection.BeginTransaction())
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"INSERT INTO script_values(script_key,value_key,json_value,updated_at) VALUES($script,$key,$value,$updated)
ON CONFLICT(script_key,value_key) DO UPDATE SET json_value=excluded.json_value,updated_at=excluded.updated_at";
                    command.Parameters.AddWithValue("$script", scriptKey);
                    command.Parameters.AddWithValue("$key", key);
                    command.Parameters.AddWithValue("$value", canonical);
                    command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
                    command.ExecuteNonQuery();
                    cancellationToken.ThrowIfCancellationRequested();
                    transaction.Commit();
                }
                Publish(new UserScriptValueChangedEventArgs(++_sequence, scriptKey, key, old, new StoredValue(true, canonical), ValueChangeKind.Set));
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
            return values.Keys.OrderBy(key => key, StringComparer.Ordinal).ToList().AsReadOnly();
        }

        public Task<IReadOnlyDictionary<string, string>> GetSnapshotAsync(string scriptKey, CancellationToken cancellationToken)
        {
            return RunAsync<IReadOnlyDictionary<string, string>>(scriptKey, connection =>
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT value_key,json_value FROM script_values WHERE script_key=$script";
                    command.Parameters.AddWithValue("$script", scriptKey);
                    using (var reader = command.ExecuteReader())
                        while (reader.Read()) values.Add(reader.GetString(0), reader.GetString(1));
                }
                return new ReadOnlyDictionary<string, string>(values);
            }, cancellationToken);
        }

        private async Task<TResult> RunAsync<TResult>(string scriptKey, Func<SqliteConnection, TResult> operation, CancellationToken token)
        {
            Validate(scriptKey);
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
                command.CommandText = "SELECT json_value FROM script_values WHERE script_key=$script AND value_key=$key";
                command.Parameters.AddWithValue("$script", scriptKey);
                command.Parameters.AddWithValue("$key", key);
                var value = command.ExecuteScalar();
                return value == null ? StoredValue.Missing : new StoredValue(true, (string)value);
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
        private static void Validate(string key) { if (string.IsNullOrEmpty(key)) throw new ArgumentException("A nonempty key is required."); }
        public void Dispose() { _disposed = true; }
    }
}
