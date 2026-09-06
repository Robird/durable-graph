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
/// object map or an appendable revision; ObjectRevisionPlanner supplies the
/// membership difference. Changes to the save view require a new plan.
/// </summary>
internal sealed class ObjectRepresentationPlan {
    internal ObjectRepresentationPlan(ObjectWriteDecision[] writes) {
        Writes = new FrozenList<ObjectWriteDecision>(writes);
    }

    internal IReadOnlyList<ObjectWriteDecision> Writes { get; }
}
