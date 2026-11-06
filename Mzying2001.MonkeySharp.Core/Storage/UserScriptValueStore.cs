using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Storage
{
    /// <summary>
    /// Represents either a stored userscript value or an explicitly missing value.
    /// </summary>
    public readonly struct StoredValue
    {
        /// <summary>Initializes a stored value.</summary>
        /// <param name="exists">Whether a value exists for the key.</param>
        /// <param name="value">The stored userscript value, or <see langword="null"/> when missing.</param>
        public StoredValue(bool exists, UserScriptValue value)
        {
            if (exists && value == null)
                throw new ArgumentNullException(nameof(value));
            if (!exists && value != null)
                throw new ArgumentException("A missing key cannot contain a value.", nameof(value));
            Exists = exists;
            Value = value;
        }

        /// <summary>Gets whether the value exists.</summary>
        public bool Exists { get; }

        /// <summary>Gets the stored userscript value, or <see langword="null"/> when missing.</summary>
        public UserScriptValue Value { get; }

        /// <summary>Gets a value representing a missing storage key.</summary>
        public static StoredValue Missing => new StoredValue(false, null);
    }

    /// <summary>
    /// Specifies how a userscript storage value changed.
    /// </summary>
    public enum ValueChangeKind
    {
        /// <summary>A value was created or replaced.</summary>
        Set,

        /// <summary>An existing value was deleted.</summary>
        Deleted
    }

    /// <summary>
    /// Provides the ordered old and new values for a userscript storage change.
    /// </summary>
    public sealed class UserScriptValueChangedEventArgs : EventArgs
    {
        /// <summary>Initializes storage change event data.</summary>
        /// <param name="sequence">The per-partition monotonically increasing change sequence.</param>
        /// <param name="scriptKey">The storage partition identifier.</param>
        /// <param name="key">The changed storage key.</param>
        /// <param name="oldValue">The value before the change.</param>
        /// <param name="newValue">The value after the change.</param>
        /// <param name="kind">The kind of change.</param>
        /// <param name="mutationId">The optional gateway mutation identifier.</param>
        /// <param name="originExecutionId">The optional originating execution identifier.</param>
        /// <param name="mutationMethod">The API method that committed the batch.</param>
        public UserScriptValueChangedEventArgs(
            long sequence,
            string scriptKey,
            string key,
            StoredValue oldValue,
            StoredValue newValue,
            ValueChangeKind kind,
            string mutationId = null,
            string originExecutionId = null, string mutationMethod = null)
        {
            Sequence = sequence;
            ScriptKey = scriptKey;
            Key = key;
            OldValue = oldValue;
            NewValue = newValue;
            Kind = kind;
            MutationId = mutationId;
            OriginExecutionId = originExecutionId;
            MutationMethod = mutationMethod;
        }

        /// <summary>Gets the per-partition monotonically increasing change sequence.</summary>
        public long Sequence { get; }

        /// <summary>Gets the storage partition identifier.</summary>
        public string ScriptKey { get; }

        /// <summary>Gets the changed storage key.</summary>
        public string Key { get; }

        /// <summary>Gets the value before the change.</summary>
        public StoredValue OldValue { get; }

        /// <summary>Gets the value after the change.</summary>
        public StoredValue NewValue { get; }

        /// <summary>Gets the kind of change.</summary>
        public ValueChangeKind Kind { get; }

        /// <summary>Gets the optional mutation identifier assigned by a compatibility-aware gateway.</summary>
        public string MutationId { get; }

        /// <summary>Gets the optional originating execution identifier.</summary>
        public string OriginExecutionId { get; }

        /// <summary>Gets the API method that committed the mutation, if supplied.</summary>
        public string MutationMethod { get; }
    }

    /// <summary>
    /// Stores typed userscript values in isolated userscript partitions.
    /// </summary>
    public interface IUserScriptValueStore
    {
        /// <summary>Gets a value from a userscript storage partition.</summary>
        /// <param name="scriptKey">The storage partition identifier.</param>
        /// <param name="key">The storage key.</param>
        /// <param name="cancellationToken">A token that cancels the lookup.</param>
        /// <returns>The stored value, including whether the key exists.</returns>
        Task<StoredValue> GetAsync(string scriptKey, string key, CancellationToken cancellationToken);

        /// <summary>Creates or replaces a value in a userscript storage partition.</summary>
        /// <param name="scriptKey">The storage partition identifier.</param>
        /// <param name="key">The storage key.</param>
        /// <param name="value">The typed userscript value.</param>
        /// <param name="cancellationToken">A token that cancels the operation before it commits.</param>
        /// <returns>A task that completes after the value is committed.</returns>
        Task SetAsync(string scriptKey, string key, UserScriptValue value, CancellationToken cancellationToken);

        /// <summary>Deletes a value from a userscript storage partition.</summary>
        /// <param name="scriptKey">The storage partition identifier.</param>
        /// <param name="key">The storage key.</param>
        /// <param name="cancellationToken">A token that cancels the operation before it commits.</param>
        /// <returns><see langword="true"/> when a value was deleted.</returns>
        Task<bool> DeleteAsync(string scriptKey, string key, CancellationToken cancellationToken);

        /// <summary>Lists keys in insertion order.</summary>
        /// <param name="scriptKey">The storage partition identifier.</param>
        /// <param name="cancellationToken">A token that cancels the lookup.</param>
        /// <returns>An immutable list of storage keys.</returns>
        Task<IReadOnlyList<string>> ListKeysAsync(string scriptKey, CancellationToken cancellationToken);

        /// <summary>Gets values and their watermark atomically in insertion order.</summary>
        Task<UserScriptValueSnapshot> GetSnapshotAsync(string scriptKey, CancellationToken cancellationToken);

        /// <summary>Reads a contiguous change suffix from a captured watermark; incomplete results require a new snapshot.</summary>
        Task<UserScriptValueChangeBatch> GetChangesAsync(string scriptKey, long afterSequence, CancellationToken cancellationToken);

        /// <summary>Validates and commits the complete batch atomically, preserving authenticated origin data in its ordered events.</summary>
        Task<IReadOnlyList<UserScriptValueChangedEventArgs>> ApplyAsync(string scriptKey,
            IEnumerable<UserScriptValueMutation> mutations, UserScriptValueMutationOrigin origin, CancellationToken cancellationToken);

        /// <summary>Occurs after a storage mutation commits.</summary>
        event EventHandler<UserScriptValueChangedEventArgs> ValueChanged;
    }

    /// <summary>
    /// Provides a thread-safe, process-local userscript value store with per-script serialization.
    /// </summary>
    public class InMemoryUserScriptValueStore : IUserScriptValueStore, IDisposable
    {
        private readonly object _partitionsLock = new object();
        private readonly Dictionary<string, Partition> _partitions =
            new Dictionary<string, Partition>(StringComparer.Ordinal);
        private readonly UserScriptValueChangeJournal _journal = new UserScriptValueChangeJournal();
        private bool _disposed;

        /// <inheritdoc />
        public event EventHandler<UserScriptValueChangedEventArgs> ValueChanged;

        /// <inheritdoc />
        public async Task<StoredValue> GetAsync(string scriptKey, string key, CancellationToken cancellationToken)
        {
            ValidateKey(scriptKey, nameof(scriptKey));
            ValidateKey(key, nameof(key), allowEmpty: true);
            var partition = GetPartition(scriptKey);
            await partition.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return partition.Values.TryGetValue(key, out var value)
                    ? new StoredValue(true, value)
                    : StoredValue.Missing;
            }
            finally
            {
                partition.Gate.Release();
            }
        }

        /// <inheritdoc />
        public Task SetAsync(string scriptKey, string key, UserScriptValue value, CancellationToken cancellationToken)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return ApplyAsync(scriptKey, new[] { new UserScriptValueMutation(key, value) }, null, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string scriptKey, string key, CancellationToken cancellationToken)
            => (await ApplyAsync(scriptKey, new[] { new UserScriptValueMutation(key, null) }, null, cancellationToken).ConfigureAwait(false)).Count != 0;

        /// <inheritdoc />
        public async Task<IReadOnlyList<UserScriptValueChangedEventArgs>> ApplyAsync(string scriptKey,
            IEnumerable<UserScriptValueMutation> mutations, UserScriptValueMutationOrigin origin, CancellationToken cancellationToken)
        {
            ValidateKey(scriptKey, nameof(scriptKey));
            var batch = UserScriptValueMutation.Validate(mutations);
            var partition = GetPartition(scriptKey);
            await partition.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (var mutation in batch)
                    await BeforeCommitAsync(scriptKey, mutation.Key, mutation.Value == null ? ValueChangeKind.Deleted : ValueChangeKind.Set, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var changes = new List<UserScriptValueChangedEventArgs>();
                foreach (var mutation in batch)
                {
                    var oldValue = partition.Values.TryGetValue(mutation.Key, out var old) ? new StoredValue(true, old) : StoredValue.Missing;
                    if (mutation.Value == null)
                    {
                        if (!oldValue.Exists) continue;
                        partition.Values.Remove(mutation.Key);
                        partition.Keys.Remove(mutation.Key);
                    }
                    else
                    {
                        if (oldValue.Exists && oldValue.Value.Equals(mutation.Value)) continue;
                        if (!oldValue.Exists) partition.Keys.Add(mutation.Key);
                        partition.Values[mutation.Key] = mutation.Value;
                    }
                    changes.Add(_journal.Append(scriptKey, mutation.Key, oldValue,
                        mutation.Value == null ? StoredValue.Missing : new StoredValue(true, mutation.Value), origin));
                }
                foreach (var change in changes) OnCommitted(scriptKey, change.Key, change.Kind);
                foreach (var change in changes) Publish(change);
                return changes.AsReadOnly();
            }
            finally { partition.Gate.Release(); }
        }

        /// <inheritdoc />
        public async Task<UserScriptValueChangeBatch> GetChangesAsync(string scriptKey, long afterSequence, CancellationToken cancellationToken)
        {
            ValidateKey(scriptKey, nameof(scriptKey));
            var partition = GetPartition(scriptKey);
            await partition.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { return _journal.Read(scriptKey, afterSequence); }
            finally { partition.Gate.Release(); }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> ListKeysAsync(string scriptKey, CancellationToken cancellationToken)
        {
            ValidateKey(scriptKey, nameof(scriptKey));
            var partition = GetPartition(scriptKey);
            await partition.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ReadOnlyCollection<string>(
                    partition.Keys.ToList());
            }
            finally
            {
                partition.Gate.Release();
            }
        }

        /// <inheritdoc />
        public async Task<UserScriptValueSnapshot> GetSnapshotAsync(
            string scriptKey,
            CancellationToken cancellationToken)
        {
            ValidateKey(scriptKey, nameof(scriptKey));
            var partition = GetPartition(scriptKey);
            await partition.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new UserScriptValueSnapshot(partition.Keys.Select(key => new KeyValuePair<string, UserScriptValue>(key, partition.Values[key])), _journal.Sequence(scriptKey));
            }
            finally
            {
                partition.Gate.Release();
            }
        }

        /// <summary>Provides an asynchronous extension point immediately before a mutation commits.</summary>
        /// <param name="scriptKey">The storage partition identifier.</param>
        /// <param name="key">The storage key being changed.</param>
        /// <param name="kind">The kind of pending change.</param>
        /// <param name="cancellationToken">A token that can cancel the mutation before commit.</param>
        /// <returns>A task that completes when the mutation may commit.</returns>
        protected virtual Task BeforeCommitAsync(
            string scriptKey,
            string key,
            ValueChangeKind kind,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <summary>Provides an extension point after a mutation commits and before its event is published.</summary>
        /// <param name="scriptKey">The storage partition identifier.</param>
        /// <param name="key">The storage key that changed.</param>
        /// <param name="kind">The committed change kind.</param>
        protected virtual void OnCommitted(string scriptKey, string key, ValueChangeKind kind)
        {
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
                return;
            lock (_partitionsLock)
            {
                if (_disposed)
                    return;
                foreach (var partition in _partitions.Values)
                    partition.Gate.Dispose();
                _partitions.Clear();
                _disposed = true;
            }
        }

        private Partition GetPartition(string scriptKey)
        {
            lock (_partitionsLock)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(InMemoryUserScriptValueStore));
                if (!_partitions.TryGetValue(scriptKey, out var partition))
                {
                    partition = new Partition();
                    _partitions.Add(scriptKey, partition);
                }
                return partition;
            }
        }

        private void Publish(UserScriptValueChangedEventArgs args)
        {
            var handlers = ValueChanged;
            if (handlers == null) return;
            foreach (EventHandler<UserScriptValueChangedEventArgs> handler in handlers.GetInvocationList())
                try { handler(this, args); } catch (Exception exception) { System.Diagnostics.Trace.TraceError(exception.ToString()); }
        }

        private static void ValidateKey(string value, string parameterName, bool allowEmpty = false)
        {
            if (value == null) throw new ArgumentNullException(parameterName);
            if (!allowEmpty && value.Length == 0)
                throw new ArgumentException("The script partition ID must be non-empty.", parameterName);
        }

        private sealed class Partition
        {
            public SemaphoreSlim Gate { get; } = new SemaphoreSlim(1, 1);
            public List<string> Keys { get; } = new List<string>();
            public Dictionary<string, UserScriptValue> Values { get; } =
                new Dictionary<string, UserScriptValue>(StringComparer.Ordinal);
        }
    }
}
