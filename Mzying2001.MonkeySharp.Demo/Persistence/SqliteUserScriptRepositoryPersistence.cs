using Microsoft.Data.Sqlite;
using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using Mzying2001.MonkeySharp.Demo.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Demo.Persistence
{
    public sealed class SqliteUserScriptRepositoryPersistence : IUserScriptPersistence
    {
        public const int MaximumSourceBytes = UserScriptSourceFile.MaximumBytes;
        private readonly SqliteDatabase _database;
        private readonly UserScriptSourceFiles _sources;
        private readonly UserScriptContentCache _cache;
        private readonly Action<string> _report;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        internal Action<string> Checkpoint { get; set; }

        public SqliteUserScriptRepositoryPersistence(SqliteDatabase database, AppDataPaths paths, Action<string> report = null)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            _report = message =>
            {
                try { if (report != null) report(message); else System.Diagnostics.Trace.TraceWarning(message); }
                catch (Exception error) { System.Diagnostics.Trace.TraceError(error.ToString()); }
            };
            _sources = new UserScriptSourceFiles(paths.UserScriptsDirectory, _report);
            _cache = new UserScriptContentCache(paths.UserScriptCacheDirectory, _report);
        }

        public Task<IReadOnlyList<UserScriptLoadResult>> LoadAllAsync(CancellationToken cancellationToken) => SerializedAsync<IReadOnlyList<UserScriptLoadResult>>(() =>
        {
            using (var connection = _database.OpenReady())
            {
                var rows = ReadRows(connection);
                var known = rows.ToDictionary(row => row.Key);
                // Scan orphan markers too; unregistered directories without markers are never imported.
                foreach (var key in _sources.RecoveryKeys())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (known.ContainsKey(key)) continue; // LoadOne isolates registered recovery failures.
                    try { _sources.Recover(key, null); }
                    catch (Exception error) when (IsFileError(error)) { _report("Orphan recovery pending for " + key + ": " + error.Message); }
                }
                var loaded = new List<UserScriptLoadResult>();
                foreach (var row in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    loaded.Add(LoadOne(connection, row));
                }
                return loaded.AsReadOnly();
            }
        }, cancellationToken);

        public Task<UserScriptLoadResult> LoadAsync(ScriptKey scriptKey, CancellationToken cancellationToken) => SerializedAsync(() =>
        {
            using (var connection = _database.OpenReady())
            {
                var row = Find(connection, scriptKey);
                if (row == null) throw new KeyNotFoundException("Script not found: " + scriptKey);
                return LoadOne(connection, row);
            }
        }, cancellationToken);

        private UserScriptLoadResult LoadOne(SqliteConnection connection, Row row)
        {
            var diagnostics = new List<UserScriptDiagnostic>();
            var assets = new List<UserScriptAsset>();
            string source = null, version = null;
            var changed = false;
            try
            {
                ValidateRow(row);
                _sources.Recover(row.Key, row.Commit);
                foreach (var descriptor in ReadAssets(connection, row.Key))
                {
                    ValidateAsset(descriptor);
                    ResourceContent content = null;
                    try { content = new ResourceContent(_cache.Read(descriptor.Hash), descriptor.Mime, descriptor.Charset); }
                    catch (Exception error) when (IsFileError(error)) { diagnostics.Add(Diagnostic(row.Key, "MSR421_ASSET_UNAVAILABLE", "Asset cache unavailable: " + descriptor.Url, error)); }
                    assets.Add(new UserScriptAsset(descriptor.Kind, descriptor.Ordinal, descriptor.Name, descriptor.Url, content));
                }
                // Validate complete association structure even when content is missing.
                new UserScriptPersistenceRecord(row.Key, null, row.Origin, row.Enabled, row.Installed, row.Updated, assets);
                try
                {
                    var hash = _sources.Hash(row.Key);
                    version = UserScriptSourceFiles.VersionOf(hash);
                    if (hash == null) diagnostics.Add(Diagnostic(row.Key, "MSR420_SOURCE_UNAVAILABLE", "Source file is missing.", null));
                    else
                    {
                        var bytes = _sources.Read(row.Key);
                        hash = ContentHash.Sha256(bytes); // The version belongs to these exact bytes, not an earlier file read.
                        version = UserScriptSourceFiles.VersionOf(hash);
                        changed = hash != row.Hash;
                        try { source = UserScriptSourceFile.Decode(bytes); }
                        catch (DecoderFallbackException error) { diagnostics.Add(Diagnostic(row.Key, "MSR420_SOURCE_UNAVAILABLE", "Source is not valid UTF-8.", error)); }
                    }
                }
                catch (Exception error) when (IsFileError(error))
                {
                    version = null;
                    diagnostics.Add(Diagnostic(row.Key, "MSR420_SOURCE_UNAVAILABLE", "Source could not be read.", error));
                }
            }
            catch (Exception error) when (IsFileError(error) || error is ArgumentException || error is FormatException || error is InvalidCastException)
            {
                version = null;
                assets.Clear();
                diagnostics.Add(Diagnostic(row.Key, error is RecoveryConflictException ? "MSR424_RECOVERY_CONFLICT" : "MSR423_PERSISTENCE_FAILED", error.Message, error));
            }
            foreach (var diagnostic in diagnostics) _report("Script " + row.Key + " unavailable: " + diagnostic.Message);
            return new UserScriptLoadResult(new UserScriptPersistenceRecord(row.Key, source, row.Origin, row.Enabled, row.Installed, row.Updated, assets), version, changed, diagnostics);
        }

        public Task<string> SaveAsync(UserScriptSaveRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SerializedAsync(() =>
            {
                var record = request.Record;
                using (var connection = _database.OpenReady()) // Database availability is a repository-level failure.
                {
                    var previous = Find(connection, record.ScriptKey);
                    UserScriptSourceFiles.Journal journal = null;
                    try
                    {
                        if (previous != null) ValidateRow(previous);
                        _sources.Recover(record.ScriptKey, previous?.Commit);
                        if (previous == null)
                        {
                            if (request.ExpectedSourceVersion != null || _sources.Hash(record.ScriptKey) != null || request.SourceWriteMode != SourceWriteMode.Replace)
                                throw new UserScriptSourceConflictException(record.ScriptKey);
                        }
                        else
                        {
                            if (request.ExpectedSourceVersion == null) throw new UserScriptSourceConflictException(record.ScriptKey);
                            _sources.CheckVersion(record.ScriptKey, request.ExpectedSourceVersion);
                        }
                        if (request.SourceWriteMode == SourceWriteMode.Preserve) CheckPreservedSource(request);
                        var bytes = request.SourceWriteMode == SourceWriteMode.Replace ? UserScriptSourceFile.Encode(record.Source) : null;
                        if (bytes != null && bytes.Length > MaximumSourceBytes) throw new IOException("Source exceeds the 10 MiB limit.");
                        var descriptors = record.Assets.Select(asset => new AssetRow { Kind = asset.Kind, Ordinal = asset.Ordinal, Name = asset.Name, Url = asset.Url,
                            Hash = _cache.Write(asset.Content.Bytes), Mime = asset.Content.MediaType, Charset = asset.Content.Charset }).ToArray();
                        Checkpoint?.Invoke("objects-written");
                        cancellationToken.ThrowIfCancellationRequested();
                        if (bytes != null) journal = _sources.Stage(record.ScriptKey, previous == null ? "install" : "replace", previous?.Commit, bytes);
                        Checkpoint?.Invoke("marker-written");
                        var commit = journal?.CommitId ?? Guid.NewGuid().ToString("D");
                        var sourceHash = bytes == null ? _sources.Hash(record.ScriptKey) : ContentHash.Sha256(bytes);
                        using (var transaction = connection.BeginTransaction())
                        {
                            SaveRows(connection, transaction, record, previous, descriptors, sourceHash, commit);
                            Checkpoint?.Invoke("rows-written");
                            cancellationToken.ThrowIfCancellationRequested();
                            if (journal == null) CheckPreservedSource(request);
                            else
                            {
                                if (previous != null) _sources.CheckVersion(record.ScriptKey, request.ExpectedSourceVersion);
                                else if (_sources.Hash(record.ScriptKey) != null) throw new UserScriptSourceConflictException(record.ScriptKey);
                                _sources.Switch(record.ScriptKey, journal);
                            }
                            // File switch is the cancellation boundary: finish commit or rollback from here.
                            Checkpoint?.Invoke("source-switched");
                            transaction.Commit();
                        }
                        if (journal != null) _sources.Complete(record.ScriptKey, journal);
                        ReportCheckpoint("committed");
                        return UserScriptSourceFiles.VersionOf(sourceHash);
                    }
                    catch (Exception error)
                    {
                        if (journal != null)
                        {
                            try { _sources.Recover(record.ScriptKey, Find(connection, record.ScriptKey)?.Commit); }
                            catch (Exception rollback) when (IsFileError(rollback))
                            { throw new UserScriptPersistenceException(record.ScriptKey, "Commit failed and source recovery remains unresolved.", new AggregateException(error, rollback)); }
                        }
                        if (IsFileError(error) || error is SqliteException sql && sql.SqliteErrorCode == 19)
                            throw new UserScriptPersistenceException(record.ScriptKey, "Installation save failed: " + error.Message, error);
                        throw;
                    }
                }
            }, cancellationToken);
        }

        private void CheckPreservedSource(UserScriptSaveRequest request)
        {
            _sources.CheckVersion(request.Record.ScriptKey, request.ExpectedSourceVersion);
            try
            {
                if (UserScriptSourceFile.Decode(_sources.Read(request.Record.ScriptKey)) != request.Record.Source) throw new UserScriptSourceConflictException(request.Record.ScriptKey);
            }
            catch (FileNotFoundException) { throw new UserScriptSourceConflictException(request.Record.ScriptKey); }
            catch (DirectoryNotFoundException) { throw new UserScriptSourceConflictException(request.Record.ScriptKey); }
        }

        public Task SetEnabledAsync(ScriptKey scriptKey, bool enabled, DateTimeOffset updatedAt, CancellationToken cancellationToken) => SerializedAsync(() =>
        {
            using (var connection = _database.OpenReady())
            using (var transaction = connection.BeginTransaction())
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "UPDATE scripts SET is_enabled=$enabled,updated_at=$updated WHERE script_key=$key";
                command.Parameters.AddWithValue("$key", scriptKey.ToString()); command.Parameters.AddWithValue("$enabled", enabled);
                command.Parameters.AddWithValue("$updated", Time(updatedAt));
                cancellationToken.ThrowIfCancellationRequested();
                if (command.ExecuteNonQuery() == 0) throw new KeyNotFoundException("Script not found: " + scriptKey);
                transaction.Commit();
            }
            return true;
        }, cancellationToken);

        public Task DeleteAsync(ScriptKey scriptKey, CancellationToken cancellationToken) => SerializedAsync(() =>
        {
            using (var connection = _database.OpenReady())
            {
                var previous = Find(connection, scriptKey);
                if (previous == null) return true;
                UserScriptSourceFiles.Journal journal = null;
                try
                {
                    _sources.Recover(scriptKey, previous.Commit);
                    journal = _sources.Stage(scriptKey, "delete", previous.Commit, null);
                    using (var transaction = connection.BeginTransaction())
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = "DELETE FROM script_assets WHERE script_key=$key; DELETE FROM script_values WHERE script_key=$key; DELETE FROM tab_states WHERE script_key=$key; DELETE FROM permission_decisions WHERE script_key=$key; DELETE FROM scripts WHERE script_key=$key;";
                        command.Parameters.AddWithValue("$key", scriptKey.ToString()); command.ExecuteNonQuery();
                        cancellationToken.ThrowIfCancellationRequested();
                        _sources.Switch(scriptKey, journal);
                        Checkpoint?.Invoke("source-switched");
                        transaction.Commit();
                    }
                    _sources.Complete(scriptKey, journal);
                }
                catch (Exception error)
                {
                    if (journal != null)
                    {
                        try { _sources.Recover(scriptKey, Find(connection, scriptKey)?.Commit); }
                        catch (Exception rollback) when (IsFileError(rollback))
                        { throw new UserScriptPersistenceException(scriptKey, "Removal failed and source recovery remains unresolved.", new AggregateException(error, rollback)); }
                    }
                    if (IsFileError(error) || error is SqliteException sql && sql.SqliteErrorCode == 19) throw new UserScriptPersistenceException(scriptKey, "Installation removal failed: " + error.Message, error);
                    throw;
                }
                Collect(connection);
            }
            return true;
        }, cancellationToken);

        internal Task CollectAsync(CancellationToken token) => SerializedAsync(() =>
        {
            using (var connection = _database.OpenReady()) Collect(connection);
            return true;
        }, token);
        private void Collect(SqliteConnection connection)
        {
            try
            {
                var referenced = new HashSet<string>(StringComparer.Ordinal);
                var rows = ReadRows(connection);
                foreach (var row in rows)
                {
                    ValidateRow(row);
                    var assets = ReadAssets(connection, row.Key);
                    foreach (var asset in assets) { ValidateAsset(asset); referenced.Add(asset.Hash); }
                    new UserScriptPersistenceRecord(row.Key, null, row.Origin, row.Enabled, row.Installed, row.Updated,
                        assets.Select(asset => new UserScriptAsset(asset.Kind, asset.Ordinal, asset.Name, asset.Url, null)));
                }
                if (Convert.ToInt64(SqliteDatabase.Scalar(connection, "SELECT COUNT(*) FROM script_assets WHERE script_key NOT IN (SELECT script_key FROM scripts)")) != 0)
                    throw new FormatException("Orphan asset associations.");
                _cache.Collect(referenced);
            }
            catch (Exception error) when (IsFileError(error) || error is ArgumentException || error is FormatException || error is SqliteException)
            { _report("Asset collection skipped or incomplete: " + error.Message); }
        }

        private async Task<T> SerializedAsync<T>(Func<T> action, CancellationToken token)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try { token.ThrowIfCancellationRequested(); return await Task.Run(action).ConfigureAwait(false); }
            finally { _gate.Release(); }
        }
        private void ReportCheckpoint(string point)
        {
            try { Checkpoint?.Invoke(point); }
            catch (Exception error) { _report("Post-commit observer failed: " + error.Message); }
        }
        private static UserScriptDiagnostic Diagnostic(ScriptKey key, string code, string message, Exception error) => new UserScriptDiagnostic(code, DiagnosticSeverity.Error, message, error, key);
        private static bool IsFileError(Exception error) => error is IOException || error is UnauthorizedAccessException;
        private static string Time(DateTimeOffset time) => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        private static void ValidateRow(Row row)
        {
            if (!StorageFiles.IsHash(row.Hash) || !Guid.TryParseExact(row.Commit, "D", out var commit) || commit == Guid.Empty) throw new FormatException("Invalid installation source hash or commit ID.");
        }
        private static void ValidateAsset(AssetRow asset)
        {
            if (!StorageFiles.IsHash(asset.Hash)) throw new FormatException("Invalid asset object hash.");
            HttpContentService.RequireHttp(asset.Url);
            new ResourceContent(Array.Empty<byte>(), asset.Mime, asset.Charset);
            new UserScriptAsset(asset.Kind, asset.Ordinal, asset.Name, asset.Url, null);
        }
        private sealed class Row
        {
            internal ScriptKey Key;
            internal string Origin, Hash, Commit;
            internal bool Enabled;
            internal DateTimeOffset Installed, Updated;
        }
        private sealed class AssetRow
        {
            internal UserScriptAssetKind Kind;
            internal int Ordinal;
            internal string Name, Url, Hash, Mime, Charset;
        }
        private static List<Row> ReadRows(SqliteConnection connection, ScriptKey? key = null)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT script_key,source_origin,is_enabled,installed_at,updated_at,source_hash,commit_id FROM scripts" + (key.HasValue ? " WHERE script_key=$key" : "") + " ORDER BY install_order";
                if (key.HasValue) command.Parameters.AddWithValue("$key", key.Value.ToString());
                var result = new List<Row>();
                using (var reader = command.ExecuteReader())
                    while (reader.Read()) result.Add(new Row { Key = ScriptKey.Parse(reader.GetString(0)), Origin = Text(reader, 1), Enabled = reader.GetBoolean(2),
                        Installed = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture), Updated = DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture), Hash = Text(reader, 5), Commit = Text(reader, 6) });
                return result;
            }
        }
        private static Row Find(SqliteConnection connection, ScriptKey key) => ReadRows(connection, key).SingleOrDefault();
        private static List<AssetRow> ReadAssets(SqliteConnection connection, ScriptKey key)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT kind,ordinal,name,url,content_hash,mime,charset FROM script_assets WHERE script_key=$key ORDER BY kind,ordinal";
                command.Parameters.AddWithValue("$key", key.ToString());
                var assets = new List<AssetRow>();
                using (var reader = command.ExecuteReader())
                    while (reader.Read()) assets.Add(new AssetRow { Kind = (UserScriptAssetKind)reader.GetInt32(0), Ordinal = reader.GetInt32(1), Name = Text(reader, 2), Url = Text(reader, 3), Hash = Text(reader, 4), Mime = Text(reader, 5), Charset = Text(reader, 6) });
                return assets;
            }
        }
        private static string Text(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        private static void SaveRows(SqliteConnection connection, SqliteTransaction transaction, UserScriptPersistenceRecord record, Row previous, IEnumerable<AssetRow> assets, string hash, string commit)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO scripts(script_key,install_order,source_origin,is_enabled,installed_at,updated_at,source_hash,commit_id) " +
                    "VALUES($key,(SELECT COALESCE(MAX(install_order),-1)+1 FROM scripts),$origin,$enabled,$installed,$updated,$hash,$commit) " +
                    "ON CONFLICT(script_key) DO UPDATE SET source_origin=excluded.source_origin,is_enabled=excluded.is_enabled,updated_at=excluded.updated_at,source_hash=excluded.source_hash,commit_id=excluded.commit_id;" +
                    "DELETE FROM script_assets WHERE script_key=$key;";
                command.Parameters.AddWithValue("$key", record.ScriptKey.ToString()); command.Parameters.AddWithValue("$origin", (object)record.SourceOrigin ?? DBNull.Value);
                command.Parameters.AddWithValue("$enabled", record.IsEnabled); command.Parameters.AddWithValue("$installed", Time(previous?.Installed ?? record.InstalledAt));
                command.Parameters.AddWithValue("$updated", Time(record.UpdatedAt)); command.Parameters.AddWithValue("$hash", hash); command.Parameters.AddWithValue("$commit", commit); command.ExecuteNonQuery();
            }
            foreach (var asset in assets)
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "INSERT INTO script_assets(script_key,kind,ordinal,name,url,content_hash,mime,charset) VALUES($key,$kind,$ordinal,$name,$url,$hash,$mime,$charset)";
                    command.Parameters.AddWithValue("$key", record.ScriptKey.ToString()); command.Parameters.AddWithValue("$kind", (int)asset.Kind); command.Parameters.AddWithValue("$ordinal", asset.Ordinal);
                    command.Parameters.AddWithValue("$name", (object)asset.Name ?? DBNull.Value); command.Parameters.AddWithValue("$url", asset.Url); command.Parameters.AddWithValue("$hash", asset.Hash);
                    command.Parameters.AddWithValue("$mime", asset.Mime); command.Parameters.AddWithValue("$charset", (object)asset.Charset ?? DBNull.Value); command.ExecuteNonQuery();
                }
        }
    }
}
