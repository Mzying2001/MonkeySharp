using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Storage
{
    public readonly struct StoredValue
    {
        public StoredValue(bool exists, string jsonValue)
        {
            if (exists && jsonValue == null)
                throw new ArgumentNullException(nameof(jsonValue));
            Exists = exists;
            JsonValue = jsonValue;
        }

        public bool Exists { get; }
        public string JsonValue { get; }
        public static StoredValue Missing => new StoredValue(false, null);
    }

    public enum ValueChangeKind
    {
        Set,
        Deleted
    }

    public sealed class UserScriptValueChangedEventArgs : EventArgs
    {
        public UserScriptValueChangedEventArgs(
            long sequence,
            string scriptKey,
            string key,
            StoredValue oldValue,
            StoredValue newValue,
            ValueChangeKind kind)
        {
            Sequence = sequence;
            ScriptKey = scriptKey;
            Key = key;
            OldValue = oldValue;
            NewValue = newValue;
            Kind = kind;
        }

        public long Sequence { get; }
        public string ScriptKey { get; }
        public string Key { get; }
        public StoredValue OldValue { get; }
        public StoredValue NewValue { get; }
        public ValueChangeKind Kind { get; }
    }

    public interface IUserScriptValueStore
    {
        Task<StoredValue> GetAsync(string scriptKey, string key, CancellationToken cancellationToken);
        Task SetAsync(string scriptKey, string key, string jsonValue, CancellationToken cancellationToken);
        Task<bool> DeleteAsync(string scriptKey, string key, CancellationToken cancellationToken);
        Task<IReadOnlyList<string>> ListKeysAsync(string scriptKey, CancellationToken cancellationToken);
        event EventHandler<UserScriptValueChangedEventArgs> ValueChanged;
    }

    public class InMemoryUserScriptValueStore : IUserScriptValueStore, IDisposable
    {
        private readonly object _partitionsLock = new object();
        private readonly Dictionary<string, Partition> _partitions =
            new Dictionary<string, Partition>(StringComparer.Ordinal);
        private long _sequence;
        private bool _disposed;

        public event EventHandler<UserScriptValueChangedEventArgs> ValueChanged;

        public async Task<StoredValue> GetAsync(string scriptKey, string key, CancellationToken cancellationToken)
        {
            ValidateKey(scriptKey, nameof(scriptKey));
            ValidateKey(key, nameof(key));
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

        public async Task SetAsync(string scriptKey, string key, string jsonValue, CancellationToken cancellationToken)
        {
            ValidateKey(scriptKey, nameof(scriptKey));
            ValidateKey(key, nameof(key));
            var canonicalValue = Canonicalize(jsonValue);
            var partition = GetPartition(scriptKey);
            await partition.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await BeforeCommitAsync(scriptKey, key, ValueChangeKind.Set, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var oldValue = partition.Values.TryGetValue(key, out var existing)
                    ? new StoredValue(true, existing)
                    : StoredValue.Missing;
                partition.Values[key] = canonicalValue;
                OnCommitted(scriptKey, key, ValueChangeKind.Set);
                Publish(new UserScriptValueChangedEventArgs(
                    Interlocked.Increment(ref _sequence),
                    scriptKey,
                    key,
                    oldValue,
                    new StoredValue(true, canonicalValue),
                    ValueChangeKind.Set));
            }
            finally
            {
                partition.Gate.Release();
            }
        }

        public async Task<bool> DeleteAsync(string scriptKey, string key, CancellationToken cancellationToken)
        {
            ValidateKey(scriptKey, nameof(scriptKey));
            ValidateKey(key, nameof(key));
            var partition = GetPartition(scriptKey);
            await partition.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await BeforeCommitAsync(scriptKey, key, ValueChangeKind.Deleted, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!partition.Values.TryGetValue(key, out var existing))
                    return false;
                partition.Values.Remove(key);
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

        public async Task<IReadOnlyList<string>> ListKeysAsync(string scriptKey, CancellationToken cancellationToken)
        {
            ValidateKey(scriptKey, nameof(scriptKey));
            var partition = GetPartition(scriptKey);
            await partition.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ReadOnlyCollection<string>(
                    partition.Values.Keys.OrderBy(item => item, StringComparer.Ordinal).ToList());
            }
            finally
            {
                partition.Gate.Release();
            }
        }

        protected virtual Task BeforeCommitAsync(
            string scriptKey,
            string key,
            ValueChangeKind kind,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        protected virtual void OnCommitted(string scriptKey, string key, ValueChangeKind kind)
        {
        }

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

        private static string Canonicalize(string jsonValue)
        {
            if (jsonValue == null)
                throw new ArgumentNullException(nameof(jsonValue));
            using (var document = JsonDocument.Parse(jsonValue))
                return JsonSerializer.Serialize(document.RootElement);
        }

        private static void ValidateKey(string value, string parameterName)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("The value cannot be null or empty.", parameterName);
        }

        private sealed class Partition
        {
            public SemaphoreSlim Gate { get; } = new SemaphoreSlim(1, 1);
            public Dictionary<string, string> Values { get; } =
                new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }
}
