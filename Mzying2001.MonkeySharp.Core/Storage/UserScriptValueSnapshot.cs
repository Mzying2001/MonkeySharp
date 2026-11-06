using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Mzying2001.MonkeySharp.Core.Storage
{
    /// <summary>Contains the committed events and exact partition watermark of an atomic mutation.</summary>
    public sealed class UserScriptValueCommit : ReadOnlyCollection<UserScriptValueChangedEventArgs>
    {
        /// <summary>Creates an immutable commit acknowledgement, including no-op batches.</summary>
        public UserScriptValueCommit(IEnumerable<UserScriptValueChangedEventArgs> changes, long sequence) : base(changes.ToList())
        { if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence)); Sequence = sequence; }
        /// <summary>Gets the watermark at the commit boundary.</summary>
        public long Sequence { get; }
    }

    /// <summary>Contains one atomic partition snapshot and the last included change sequence.</summary>
    public sealed class UserScriptValueSnapshot : ReadOnlyCollection<KeyValuePair<string, UserScriptValue>>
    {
        /// <summary>Creates an immutable snapshot in insertion order.</summary>
        public UserScriptValueSnapshot(IEnumerable<KeyValuePair<string, UserScriptValue>> values, long sequence)
            : base(values.ToList())
        { if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence)); Sequence = sequence; }
        /// <summary>Gets the partition watermark captured atomically with the values.</summary>
        public long Sequence { get; }
    }

    /// <summary>Describes one validated set or delete in an atomic storage batch.</summary>
    public sealed class UserScriptValueMutation
    {
        /// <summary>Creates a mutation; null value means delete, while typed undefined remains a stored value.</summary>
        public UserScriptValueMutation(string key, UserScriptValue value)
        { Key = key ?? throw new ArgumentNullException(nameof(key)); Value = value; }
        /// <summary>Gets the key.</summary>
        public string Key { get; }
        /// <summary>Gets the value to set, or null to delete.</summary>
        public UserScriptValue Value { get; }
        /// <summary>Validates and copies the entire batch before any effects.</summary>
        public static IReadOnlyList<UserScriptValueMutation> Validate(IEnumerable<UserScriptValueMutation> mutations)
        {
            if (mutations == null) throw new ArgumentNullException(nameof(mutations));
            var result = mutations.ToArray();
            if (result.Any(item => item == null) || result.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count() != result.Length)
                throw new ArgumentException("Mutations must be non-null and have distinct keys.", nameof(mutations));
            return Array.AsReadOnly(result);
        }
    }

    /// <summary>Identifies the request and execution that committed a storage batch.</summary>
    public sealed class UserScriptValueMutationOrigin
    {
        /// <summary>Creates authenticated mutation provenance.</summary>
        public UserScriptValueMutationOrigin(string mutationId, string executionId, string method)
        { MutationId = mutationId; ExecutionId = executionId; Method = method; }
        /// <summary>Gets the request identifier.</summary>
        public string MutationId { get; }
        /// <summary>Gets the originating execution.</summary>
        public string ExecutionId { get; }
        /// <summary>Gets the API method.</summary>
        public string Method { get; }
    }

    /// <summary>Contains a contiguous suffix after a watermark, or requests a new snapshot when retention has a gap.</summary>
    public sealed class UserScriptValueChangeBatch
    {
        /// <summary>Creates a replay result.</summary>
        public UserScriptValueChangeBatch(bool complete, long sequence, IEnumerable<UserScriptValueChangedEventArgs> changes)
        { Complete = complete; Sequence = sequence; Changes = Array.AsReadOnly(changes.ToArray()); }
        /// <summary>Gets whether every change since the requested watermark is present.</summary>
        public bool Complete { get; }
        /// <summary>Gets the partition's current watermark.</summary>
        public long Sequence { get; }
        /// <summary>Gets the ordered suffix, empty when a fresh snapshot is required.</summary>
        public IReadOnlyList<UserScriptValueChangedEventArgs> Changes { get; }
    }

    /// <summary>Maintains bounded per-partition replay history for value-store implementations.</summary>
    public sealed class UserScriptValueChangeJournal
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, Queue<UserScriptValueChangedEventArgs>> _changes = new Dictionary<string, Queue<UserScriptValueChangedEventArgs>>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _sequences = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly int _capacity;
        /// <summary>Creates a journal. A store must call it within the same serialization boundary as its values.</summary>
        public UserScriptValueChangeJournal(int capacity = 4096)
        { if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity)); _capacity = capacity; }
        /// <summary>Gets the current partition watermark.</summary>
        public long Sequence(string scriptKey)
        { lock (_sync) return _sequences.TryGetValue(scriptKey, out var sequence) ? sequence : 0; }
        /// <summary>Appends a committed change with its provenance.</summary>
        public UserScriptValueChangedEventArgs Append(string scriptKey, string key, StoredValue oldValue, StoredValue newValue, UserScriptValueMutationOrigin origin)
        {
            lock (_sync)
            {
                var change = new UserScriptValueChangedEventArgs(Sequence(scriptKey) + 1, scriptKey, key, oldValue, newValue,
                    newValue.Exists ? ValueChangeKind.Set : ValueChangeKind.Deleted, origin?.MutationId, origin?.ExecutionId, origin?.Method);
                if (!_changes.TryGetValue(scriptKey, out var changes)) _changes[scriptKey] = changes = new Queue<UserScriptValueChangedEventArgs>();
                changes.Enqueue(change);
                _sequences[scriptKey] = change.Sequence;
                while (changes.Count > _capacity) changes.Dequeue();
                return change;
            }
        }
        /// <summary>Replays changes strictly after the requested watermark without confusing other scripts' sequences.</summary>
        public UserScriptValueChangeBatch Read(string scriptKey, long afterSequence)
        {
            lock (_sync)
            {
                var current = Sequence(scriptKey);
                if (afterSequence < 0 || afterSequence > current ||
                    afterSequence < current && (!_changes.TryGetValue(scriptKey, out var history) || history.Peek().Sequence > afterSequence + 1))
                    return new UserScriptValueChangeBatch(false, current, Array.Empty<UserScriptValueChangedEventArgs>());
                return new UserScriptValueChangeBatch(true, current, _changes.TryGetValue(scriptKey, out var changes)
                    ? changes.Where(change => change.Sequence > afterSequence) : Enumerable.Empty<UserScriptValueChangedEventArgs>());
            }
        }
    }
}
