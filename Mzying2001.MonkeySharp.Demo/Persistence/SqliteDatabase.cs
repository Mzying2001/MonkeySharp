using Microsoft.Data.Sqlite;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Demo.Persistence
{
    public sealed class SqliteDatabase : IDisposable
    {
        private readonly string _connectionString;
        private bool _disposed;

        public SqliteDatabase(string path)
        {
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate,
                ForeignKeys = true, Pooling = false, DefaultTimeout = 10
            }.ToString();
        }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (var connection = OpenReady())
                {
                    Execute(connection, "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;");
                    var version = Convert.ToInt32(Scalar(connection, "PRAGMA user_version"));
                    if (version > 1) throw new InvalidOperationException("The database was created by a newer Demo.");
                    if (version == 1) return;
                    using (var transaction = connection.BeginTransaction())
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = @"
CREATE TABLE IF NOT EXISTS scripts(script_key TEXT PRIMARY KEY, source_path TEXT NOT NULL,
 source_origin TEXT, is_enabled INTEGER NOT NULL, installed_at TEXT NOT NULL, updated_at TEXT NOT NULL);
ALTER TABLE scripts ADD COLUMN source_hash TEXT;
CREATE TABLE IF NOT EXISTS script_values(script_key TEXT NOT NULL,value_key TEXT NOT NULL,json_value TEXT NOT NULL,
 updated_at TEXT NOT NULL,PRIMARY KEY(script_key,value_key));
CREATE TABLE IF NOT EXISTS tab_states(script_key TEXT NOT NULL,tab_id TEXT NOT NULL,json_value TEXT NOT NULL,
 updated_at TEXT NOT NULL,PRIMARY KEY(script_key,tab_id));
PRAGMA user_version=1;";
                        command.ExecuteNonQuery();
                        transaction.Commit();
                    }
                }
            }, cancellationToken);
        }

        public SqliteConnection Open()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SqliteDatabase));
            return new SqliteConnection(_connectionString);
        }

        public SqliteConnection OpenReady()
        {
            var connection = Open();
            try { connection.Open(); return connection; }
            catch { connection.Dispose(); throw; }
        }

        internal static void Execute(SqliteConnection connection, string sql)
        {
            using (var command = connection.CreateCommand()) { command.CommandText = sql; command.ExecuteNonQuery(); }
        }

        internal static object Scalar(SqliteConnection connection, string sql)
        {
            using (var command = connection.CreateCommand()) { command.CommandText = sql; return command.ExecuteScalar(); }
        }

        public void Dispose() { _disposed = true; }
    }
}
