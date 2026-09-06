using Microsoft.Data.Sqlite;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Demo.Persistence
{
    public sealed class SqliteUserScriptRepositoryPersistence : IUserScriptRepositoryPersistence
    {
        public const int MaximumSourceBytes = 10 * 1024 * 1024;
        private readonly SqliteDatabase _database;
        private readonly AppDataPaths _paths;
        private readonly Action<string> _report;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public SqliteUserScriptRepositoryPersistence(SqliteDatabase database, AppDataPaths paths, Action<string> report = null)
        {
            _database = database;
            _paths = paths;
            _report = report ?? (message => System.Diagnostics.Trace.TraceWarning(message));
        }

        public Task<IReadOnlyList<UserScriptPersistenceRecord>> LoadAsync(CancellationToken cancellationToken)
        {
            return Serialized<IReadOnlyList<UserScriptPersistenceRecord>>(() =>
            {
                var records = new List<UserScriptPersistenceRecord>();
                using (var connection = _database.OpenReady())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT script_key,source_origin,is_enabled,installed_at,updated_at,source_hash FROM scripts ORDER BY rowid";
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var key = ScriptKey.Parse(reader.GetString(0));
                            var path = SourcePath(key);
                            var hash = reader.IsDBNull(5) ? null : reader.GetString(5);
                            string source = null;
                            try
                            {
                                Recover(path, hash);
                                var bytes = ReadBounded(path);
                                if (hash != null && Hash(bytes) != hash) throw new IOException("Source hash mismatch; import or save a reviewed replacement.");
                                source = Utf8.GetString(bytes).TrimStart('\uFEFF');
                            }
                            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is DecoderFallbackException)
                            { _report("Script " + key + " unavailable: " + exception.Message); }
                            records.Add(new UserScriptPersistenceRecord(key, source,
                                reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetBoolean(2),
                                DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                                DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture)));
                        }
                    }
                }
                var knownKeys = new HashSet<string>(records.Select(record => record.ScriptKey.ToString()), StringComparer.OrdinalIgnoreCase);
                foreach (var tombstone in Directory.GetFiles(_paths.UserScriptsDirectory, "*.user.js.deleted"))
                {
                    var name = Path.GetFileName(tombstone);
                    var keyText = name.Substring(0, name.Length - ".user.js.deleted".Length);
                    if (ScriptKey.TryParse(keyText, out var unusedKey) && !knownKeys.Contains(keyText)) Cleanup(tombstone);
                }
                return records.AsReadOnly();
            }, cancellationToken);
        }

        public Task SaveAsync(UserScriptPersistenceRecord record, CancellationToken cancellationToken)
        {
            return Serialized(() =>
            {
                var bytes = Utf8.GetBytes(record.Source ?? throw new ArgumentException("Cannot save unavailable source."));
                if (bytes.Length > MaximumSourceBytes) throw new IOException("Script exceeds the 10 MiB limit.");
                var path = SourcePath(record.ScriptKey);
                var temporary = path + ".tmp";
                var backup = path + ".bak";
                var replaced = false;
                var existed = File.Exists(path);
                try
                {
                    using (var connection = _database.OpenReady())
                    using (var transaction = connection.BeginTransaction())
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = @"INSERT INTO scripts(script_key,source_path,source_origin,is_enabled,installed_at,updated_at,source_hash)
VALUES($key,$path,$origin,$enabled,$installed,$updated,$hash)
ON CONFLICT(script_key) DO UPDATE SET source_origin=excluded.source_origin,is_enabled=excluded.is_enabled,
 updated_at=excluded.updated_at,source_hash=excluded.source_hash";
                        command.Parameters.AddWithValue("$key", record.ScriptKey.ToString());
                        command.Parameters.AddWithValue("$path", Path.Combine("UserScripts", record.ScriptKey + ".user.js"));
                        command.Parameters.AddWithValue("$origin", (object)record.SourceOrigin ?? DBNull.Value);
                        command.Parameters.AddWithValue("$enabled", record.IsEnabled);
                        command.Parameters.AddWithValue("$installed", record.InstalledAt.ToString("O"));
                        command.Parameters.AddWithValue("$updated", record.UpdatedAt.ToString("O"));
                        command.Parameters.AddWithValue("$hash", Hash(bytes));
                        command.ExecuteNonQuery();
                        using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                        { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
                        cancellationToken.ThrowIfCancellationRequested();
                        if (File.Exists(backup)) throw new IOException("An unresolved source backup exists: " + backup);
                        if (existed) File.Replace(temporary, path, backup);
                        else File.Move(temporary, path);
                        replaced = true;
                        transaction.Commit();
                    }
                }
                catch
                {
                    if (replaced)
                    {
                        if (existed) File.Replace(backup, path, null);
                        else File.Delete(path);
                    }
                    throw;
                }
                finally { Cleanup(temporary); }
                Cleanup(backup);
                return true;
            }, cancellationToken);
        }

        public Task DeleteAsync(ScriptKey scriptKey, CancellationToken cancellationToken)
        {
            return Serialized(() =>
            {
                var path = SourcePath(scriptKey);
                var tombstone = path + ".deleted";
                var moved = false;
                try
                {
                    using (var connection = _database.OpenReady())
                    using (var transaction = connection.BeginTransaction())
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = "DELETE FROM scripts WHERE script_key=$key; DELETE FROM script_values WHERE script_key=$key; DELETE FROM tab_states WHERE script_key=$key;";
                        command.Parameters.AddWithValue("$key", scriptKey.ToString());
                        command.ExecuteNonQuery();
                        cancellationToken.ThrowIfCancellationRequested();
                        if (File.Exists(path)) { File.Move(path, tombstone); moved = true; }
                        transaction.Commit();
                    }
                }
                catch { if (moved) File.Move(tombstone, path); throw; }
                Cleanup(tombstone);
                Cleanup(path + ".bak");
                return true;
            }, cancellationToken);
        }

        private async Task<TResult> Serialized<TResult>(Func<TResult> operation, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { return await Task.Run(operation, cancellationToken).ConfigureAwait(false); }
            finally { _gate.Release(); }
        }

        private void Recover(string path, string hash)
        {
            if (!File.Exists(path) && File.Exists(path + ".deleted"))
            {
                File.Move(path + ".deleted", path);
                _report("Recovered interrupted script removal: " + Path.GetFileName(path));
            }
            var backup = path + ".bak";
            if (!File.Exists(backup)) return;
            if (File.Exists(path) && (hash == null || Hash(ReadBounded(path)) == hash)) Cleanup(backup);
            else if (hash != null && Hash(ReadBounded(backup)) == hash)
            {
                if (File.Exists(path)) File.Replace(backup, path, null);
                else File.Move(backup, path);
                _report("Recovered interrupted script update: " + Path.GetFileName(path));
            }
        }

        public static byte[] ReadBounded(string path)
        {
            using (var input = File.OpenRead(path))
            {
                if (input.Length > MaximumSourceBytes) throw new IOException("Script exceeds the 10 MiB limit.");
                using (var output = new MemoryStream()) { input.CopyTo(output); return output.ToArray(); }
            }
        }

        private string SourcePath(ScriptKey key) => Path.Combine(_paths.UserScriptsDirectory, key + ".user.js");
        internal static string Hash(byte[] bytes)
        {
            using (var algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private void Cleanup(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            { _report("Cleanup pending for " + Path.GetFileName(path) + ": " + exception.Message); }
        }
    }
}
