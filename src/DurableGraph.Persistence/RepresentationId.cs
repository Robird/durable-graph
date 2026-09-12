namespace Atelia.DurableGraph.Persistence;

/// <summary>A repository-local, persistent identity in the closed representation catalog.</summary>
/// <remarks>
/// Zero is invalid. IDs are never reused or interpreted across repositories.
/// Class, inline Schema, array, and List nodes share this namespace. Only class, containers,
/// and built-in string nodes may be used as object Base representations.
/// </remarks>
public readonly record struct RepresentationId(uint Value) {
    /// <summary>The format-defined string representation, present even in an empty SchemaStore.</summary>
    public static RepresentationId String { get; } = new(1);
}
