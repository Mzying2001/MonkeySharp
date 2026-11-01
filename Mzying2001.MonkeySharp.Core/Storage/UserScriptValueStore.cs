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
        /// <param name="sequence">The process-wide monotonically increasing change sequence.</param>
        /// <param name="scriptKey">The storage partition identifier.</param>
        /// <param name="key">The changed storage key.</param>
        /// <param name="oldValue">The value before the change.</param>
        /// <param name="newValue">The value after the change.</param>
        /// <param name="kind">The kind of change.</param>
        /// <param name="mutationId">The optional gateway mutation identifier.</param>
        /// <param name="originExecutionId">The optional originating execution identifier.</param>
        public UserScriptValueChangedEventArgs(
            long sequence,
            string scriptKey,
            string key,
            StoredValue oldValue,
            StoredValue newValue,
            ValueChangeKind kind,
            string mutationId = null,
            string originExecutionId = null)
        {
            Sequence = sequence;
            ScriptKey = scriptKey;
            Key = key;
            OldValue = oldValue;
            NewValue = newValue;
            Kind = kind;
            MutationId = mutationId;
            OriginExecutionId = originExecutionId;
        }

        /// <summary>Gets the process-wide monotonically increasing change sequence.</summary>
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

        /// <summary>Occurs after a storage mutation commits.</summary>
        event EventHandler<UserScriptValueChangedEventArgs> ValueChanged;
    }

    /// <summary>Provides an atomic snapshot of one userscript storage partition.</summary>
    public interface IUserScriptValueSnapshotProvider
    {
        /// <summary>Gets typed userscript values for all keys in a script partition.</summary>
        /// <param name="scriptKey">The storage partition identifier.</param>
        /// <param name="cancellationToken">A token that cancels the snapshot.</param>
        /// <returns>An immutable snapshot in key insertion order.</returns>
        Task<IReadOnlyList<KeyValuePair<string, UserScriptValue>>> GetSnapshotAsync(
            string scriptKey,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Provides a thread-safe, process-local userscript value store with per-script serialization.
    /// </summary>
    public class InMemoryUserScriptValueStore : IUserScriptValueStore, IUserScriptValueSnapshotProvider, IDisposable
    {
        private readonly object _partitionsLock = new object();
        private readonly Dictionary<string, Partition> _partitions =
            new Dictionary<string, Partition>(StringComparer.Ordinal);
        private long _sequence;
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
        public async Task SetAsync(string scriptKey, string key, UserScriptValue value, CancellationToken cancellationToken)
        {
            ValidateKey(scriptKey, nameof(scriptKey));
            ValidateKey(key, nameof(key), allowEmpty: true);
            if (value == null) throw new ArgumentNullException(nameof(value));
            var partition = GetPartition(scriptKey);
            await partition.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await BeforeCommitAsync(scriptKey, key, ValueChangeKind.Set, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var oldValue = partition.Values.TryGetValue(key, out var existing)
                    ? new StoredValue(true, existing)
                    : StoredValue.Missing;
                if (oldValue.Exists && oldValue.Value.Equals(value)) return;
                if (!oldValue.Exists) partition.Keys.Add(key);
                partition.Values[key] = value;
                OnCommitted(scriptKey, key, ValueChangeKind.Set);
                Publish(new UserScriptValueChangedEventArgs(
                    Interlocked.Increment(ref _sequence),
                    scriptKey,
                    key,
                    oldValue,
                    new StoredValue(true, value),
                    ValueChangeKind.Set));
            }
            finally
            {
                partition.Gate.Release();
            }
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string scriptKey, string key, CancellationToken cancellationToken)
        {
            ValidateKey(scriptKey, nameof(scriptKey));
            ValidateKey(key, nameof(key), allowEmpty: true);
            var partition = GetPartition(scriptKey);
            await partition.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await BeforeCommitAsync(scriptKey, key, ValueChangeKind.Deleted, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!partition.Values.TryGetValue(key, out var existing))
                    return false;
                partition.Values.Remove(key);
                partition.Keys.Remove(key);
                OnCommitted(scriptKey, key, ValueChangeKind.Deleted);
                Publish(new UserScriptValueChangedEventArgs(
                    Interlocked.Increment(ref _sequence),
                    scriptKey,
                    key,
                    new StoredValue(true, existing),
                    StoredValue.Missing,
                    ValueChangeKind.Deleted));
                return true;
            }
            finally
            {
                partition.Gate.Release();
            }
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
        public async Task<IReadOnlyList<KeyValuePair<string, UserScriptValue>>> GetSnapshotAsync(
            string scriptKey,
            CancellationToken cancellationToken)
        {
            ValidateKey(scriptKey, nameof(scriptKey));
            var partition = GetPartition(scriptKey);
            await partition.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return partition.Keys.Select(key => new KeyValuePair<string, UserScriptValue>(key, partition.Values[key])).ToList().AsReadOnly();
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
            ValueChanged?.Invoke(this, args);
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
