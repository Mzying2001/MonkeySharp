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
                    var version = Convert.ToInt32(Scalar(connection, "PRAGMA user_version"));
                    if ((version != 0 && version != 3) || (version == 0 && Convert.ToInt32(Scalar(connection,
                        "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")) != 0))
                        throw new InvalidOperationException("Unsupported Demo database format. Back up the existing database and rebuild it manually; automatic migration is not supported.");
                    Execute(connection, "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;");
                    if (version == 3) return;
                    using (var transaction = connection.BeginTransaction())
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText =
                            "CREATE TABLE scripts(script_key TEXT PRIMARY KEY NOT NULL, install_order INTEGER NOT NULL UNIQUE, " +
                            "source_origin TEXT, is_enabled INTEGER NOT NULL CHECK(is_enabled IN (0,1)), installed_at TEXT NOT NULL, updated_at TEXT NOT NULL, source_hash TEXT NOT NULL, commit_id TEXT NOT NULL);" +
                            "CREATE TABLE script_assets(script_key TEXT NOT NULL REFERENCES scripts(script_key) ON DELETE CASCADE, kind INTEGER NOT NULL CHECK(kind IN (0,1)), " +
                            "ordinal INTEGER NOT NULL CHECK(ordinal>=0),name TEXT,url TEXT NOT NULL,content_hash TEXT NOT NULL,mime TEXT NOT NULL,charset TEXT,PRIMARY KEY(script_key,kind,ordinal), " +
                            "CHECK((kind=0 AND name IS NULL) OR (kind=1 AND name IS NOT NULL AND length(name)>0)));" +
                            "CREATE UNIQUE INDEX resource_names ON script_assets(script_key,name) WHERE kind=1; CREATE INDEX asset_content ON script_assets(content_hash);" +
                            "CREATE TABLE IF NOT EXISTS script_values(script_key TEXT NOT NULL,value_key TEXT NOT NULL,encoded_value TEXT NOT NULL, " +
                            "updated_at TEXT NOT NULL,PRIMARY KEY(script_key,value_key));" +
                            "CREATE TABLE IF NOT EXISTS tab_states(script_key TEXT NOT NULL,tab_id TEXT NOT NULL,json_value TEXT NOT NULL, " +
                            "updated_at TEXT NOT NULL,PRIMARY KEY(script_key,tab_id));" +
                            "CREATE TABLE IF NOT EXISTS permission_decisions(script_key TEXT NOT NULL,kind TEXT NOT NULL,target TEXT NOT NULL,decision INTEGER NOT NULL CHECK(decision IN (0,1)),PRIMARY KEY(script_key,kind,target));" +
                            "PRAGMA user_version=3;";
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
