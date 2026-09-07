namespace Atelia.DurableGraph;

/// <summary>A sealed capture result, independent of mutable domain instances and session bindings.</summary>
public sealed class CapturedGraph {
    internal CapturedGraph(IEnumerable<uint> rootIds, IEnumerable<ObjectStateRecord> objects) {
        RootIds = new FrozenList<uint>(rootIds);
        Objects = new FrozenList<ObjectStateRecord>(objects.OrderBy(static item => item.Id));
    }

    /// <summary>Root IDs in registration order, including duplicates and zero for null roots.</summary>
    public IReadOnlyList<uint> RootIds { get; }

    /// <summary>The complete candidate object set in ascending ID order.</summary>
    public IReadOnlyList<ObjectStateRecord> Objects { get; }

    // Array.AsReadOnly leaks its backing array through ICollection.SyncRoot.
    // Expose only the read operations, with no non-generic collection side channel.
    private sealed class FrozenList<T>(IEnumerable<T> source) : IReadOnlyList<T> {
        private readonly T[] _items = source.ToArray();
        public int Count => _items.Length;
        public T this[int index] => _items[index];
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
