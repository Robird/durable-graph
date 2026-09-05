namespace Atelia.DurableGraph.StateStore;

internal enum ObjectRepresentationMode {
    Base,
    Delta,
}

internal readonly record struct ObjectWriteDecision(
    uint ObjectId,
    ObjectRepresentationMode Mode);

/// <summary>
/// Sparse object content writes, ordered by ObjectId, for the caller's frozen save
/// view. Omitted NoChange objects retain their heads. This is not a complete live
/// object map or an appendable revision; removals and map checkpoints belong to
/// the later Save consumer. Changes to the save view require a new plan.
/// </summary>
internal sealed class ObjectRepresentationPlan {
    // Takes ownership of the policy's fresh array; the caller must not retain it.
    internal ObjectRepresentationPlan(ObjectWriteDecision[] writes) {
        Writes = Array.AsReadOnly(writes);
    }

    internal IReadOnlyList<ObjectWriteDecision> Writes { get; }
}
