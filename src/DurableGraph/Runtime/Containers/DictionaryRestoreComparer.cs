namespace Atelia.DurableGraph.Runtime;

// Retains the persisted choice through Load -> Capture, even when the selected inner
// comparer is now a standard comparer. The wrapper adds no graph/DTO/repository
// capture; the application controls its inner comparer's lifetime and captures.
internal sealed class DictionaryRestoreComparer<TKey> : IEqualityComparer<TKey> where TKey : notnull {
    internal DictionaryRestoreComparer(DictionaryComparerKind kind, IEqualityComparer<TKey> inner) {
        if (kind is not (DictionaryComparerKind.CurrentDefault or DictionaryComparerKind.Application)) {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        ArgumentNullException.ThrowIfNull(inner);
        Kind = kind;
        Inner = inner;
    }

    internal DictionaryComparerKind Kind { get; }
    internal IEqualityComparer<TKey> Inner { get; }
    public bool Equals(TKey? x, TKey? y) => Inner.Equals(x, y);
    public int GetHashCode(TKey obj) => Inner.GetHashCode(obj);
}
