using Mzying2001.MonkeySharp.Core.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Repository
{
    /// <summary>Stores immutable installation records in memory with conditional source saves.</summary>
    public sealed class InMemoryUserScriptPersistence : IUserScriptPersistence
    {
        private readonly object _sync = new object();
        private readonly Dictionary<ScriptKey, UserScriptPersistenceRecord> _records = new Dictionary<ScriptKey, UserScriptPersistenceRecord>();
        private readonly List<ScriptKey> _order = new List<ScriptKey>();

        /// <inheritdoc />
        public Task<IReadOnlyList<UserScriptLoadResult>> LoadAllAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync) return Task.FromResult<IReadOnlyList<UserScriptLoadResult>>(Array.AsReadOnly(_order.Select(key => Load(_records[key])).ToArray()));
        }
        /// <inheritdoc />
        public Task<UserScriptLoadResult> LoadAsync(ScriptKey scriptKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync) return Task.FromResult(Load(Get(scriptKey)));
        }
        /// <inheritdoc />
        public Task<string> SaveAsync(UserScriptSaveRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                var record = request.Record;
                var exists = _records.TryGetValue(record.ScriptKey, out var previous);
                if (exists ? request.ExpectedSourceVersion != Version(previous.Source) : request.ExpectedSourceVersion != null)
                    throw new UserScriptSourceConflictException(record.ScriptKey);
                if (request.SourceWriteMode == SourceWriteMode.Preserve && (!exists || previous.Source != record.Source))
                    throw new UserScriptSourceConflictException(record.ScriptKey);
                if (exists) record = new UserScriptPersistenceRecord(record.ScriptKey, record.Source, record.SourceOrigin, record.IsEnabled,
                    previous.InstalledAt, record.UpdatedAt, record.Assets);
                else _order.Add(record.ScriptKey);
                _records[record.ScriptKey] = record;
                return Task.FromResult(Version(record.Source));
            }
        }
        /// <inheritdoc />
        public Task SetEnabledAsync(ScriptKey scriptKey, bool enabled, DateTimeOffset updatedAt, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                var old = Get(scriptKey);
                _records[scriptKey] = new UserScriptPersistenceRecord(scriptKey, old.Source, old.SourceOrigin, enabled, old.InstalledAt, updatedAt, old.Assets);
            }
            return Task.CompletedTask;
        }
        /// <inheritdoc />
        public Task DeleteAsync(ScriptKey scriptKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync) { _records.Remove(scriptKey); _order.Remove(scriptKey); }
            return Task.CompletedTask;
        }
        private UserScriptPersistenceRecord Get(ScriptKey key) => _records.TryGetValue(key, out var record) ? record : throw new KeyNotFoundException("Script not found: " + key);
        private static UserScriptLoadResult Load(UserScriptPersistenceRecord record) => new UserScriptLoadResult(record, Version(record.Source), false, Array.Empty<UserScriptDiagnostic>());
        private static string Version(string source)
        {
            using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(source)));
        }
    }
}
