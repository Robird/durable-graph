namespace Atelia.DurableGraph;

/// <summary>An object's identity in a revision or capture view. Zero denotes a null reference.</summary>
/// <remarks>
/// The same ID space serves all supported reference kinds. The ID alone does not identify
/// a repository, revision, target type or Schema version; those come from its enclosing view.
/// Numeric conversion is explicit through the constructor and <see cref="Value"/>.
/// </remarks>
public readonly record struct ObjectId(uint Value) : IComparable<ObjectId> {
    public bool IsNull => Value == 0;

    public int CompareTo(ObjectId other) => Value.CompareTo(other.Value);
}
