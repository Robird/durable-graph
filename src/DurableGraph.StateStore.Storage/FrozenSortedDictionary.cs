using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Atelia.DurableGraph.StateStore.Storage;

/// <summary>Owned sorted keys and corresponding values, frozen from an operation-local builder.</summary>
internal sealed class FrozenSortedDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, TValue> where TKey : notnull {
    private readonly TKey[] _keys;
    private readonly TValue[] _values;

    internal FrozenSortedDictionary(SortedDictionary<TKey, TValue> items) {
        ArgumentNullException.ThrowIfNull(items);
        if (!ReferenceEquals(items.Comparer, Comparer<TKey>.Default)) {
            throw new ArgumentException("Only the default key comparer is supported.", nameof(items));
        }

        _keys = new TKey[items.Count];
        _values = new TValue[items.Count];
        int index = 0;
        foreach ((TKey key, TValue value) in items) {
            if (index > 0 && Comparer<TKey>.Default.Compare(_keys[index - 1], key) >= 0) {
                throw new ArgumentException("Keys must be in strictly ascending order.", nameof(items));
            }

            _keys[index] = key;
            _values[index] = value;
            index++;
        }
    }

    public int Count => _keys.Length;

    public TValue this[TKey key] => TryGetValue(key, out TValue? value)
        ? value
        : throw new KeyNotFoundException();

    public IEnumerable<TKey> Keys {
        get {
            for (int index = 0; index < _keys.Length; index++) {
                yield return _keys[index];
            }
        }
    }

    public IEnumerable<TValue> Values {
        get {
            for (int index = 0; index < _values.Length; index++) {
                yield return _values[index];
            }
        }
    }

    public bool ContainsKey(TKey key) => FindIndex(key) >= 0;

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) {
        int index = FindIndex(key);
        if (index >= 0) {
            value = _values[index];
            return true;
        }

        value = default;
        return false;
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() {
        for (int index = 0; index < _keys.Length; index++) {
            yield return new(_keys[index], _values[index]);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private int FindIndex(TKey key) {
        ArgumentNullException.ThrowIfNull(key);
        int low = 0;
        int high = _keys.Length - 1;
        while (low <= high) {
            int middle = low + ((high - low) >> 1);
            int comparison = Comparer<TKey>.Default.Compare(_keys[middle], key);
            if (comparison == 0) {
                return middle;
            }

            if (comparison < 0) {
                low = middle + 1;
            } else {
                high = middle - 1;
            }
        }

        return -1;
    }
}
