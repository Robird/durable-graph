namespace Atelia.DurableGraph;

/// <summary>Distinguishes an identity-bearing object from an inline value layout.</summary>
public enum SchemaKind {
    ReferenceObject = 1,
    InlineValue = 2,
}
