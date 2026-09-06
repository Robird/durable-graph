namespace Atelia.DurableGraph.StateStore;

// Expose only read interfaces, including through non-generic collection casts.
internal sealed class FrozenList<T> : IReadOnlyList<T> {
    private readonly T[] _items;

    internal FrozenList(IEnumerable<T> items) {
        _items = items.ToArray();
    }

    public int Count => _items.Length;
    public T this[int index] => _items[index];
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
