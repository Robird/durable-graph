namespace Atelia.DurableGraph;

/// <summary>Supported Dictionary comparison semantics, stored with the object contents.</summary>
public enum DictionaryComparerKind : byte {
    ScalarDefault = 0,
    StringOrdinal = 1,
    StringOrdinalIgnoreCase = 2,
    ReferenceIdentity = 3,
    /// <summary>Use the current domain key's default equality, without historical business lookup validation.</summary>
    CurrentDefault = 4,
    /// <summary>Use the current model snapshot's registered comparer for this closed Dictionary type.</summary>
    Application = 5,
}
