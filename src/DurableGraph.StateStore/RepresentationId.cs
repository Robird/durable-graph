namespace Atelia.DurableGraph.StateStore;

/// <summary>A repository-local, persistent identity for one complete object representation.</summary>
/// <remarks>Zero is invalid. IDs are never reused or interpreted across repositories.</remarks>
public readonly record struct RepresentationId(uint Value) {
    /// <summary>The format-defined string representation, present even in an empty SchemaStore.</summary>
    public static RepresentationId String { get; } = new(1);
}
