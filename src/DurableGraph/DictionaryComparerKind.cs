namespace Atelia.DurableGraph;

/// <summary>Supported Dictionary comparison semantics, stored with the object contents.</summary>
public enum DictionaryComparerKind : byte {
    ScalarDefault = 0,
    StringOrdinal = 1,
    StringOrdinalIgnoreCase = 2,
    ReferenceIdentity = 3,
}
