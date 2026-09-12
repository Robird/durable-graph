using Atelia.DurableGraph.Storage;

namespace Atelia.DurableGraph.Persistence;

/// <summary>An immutable candidate; creating it neither appends nor publishes a Revision.</summary>
internal sealed class PreparedObjectRevision {
    internal PreparedObjectRevision(StateRevision revision, IEnumerable<ObjectSaveEstimate> estimates,
        ObjectRepresentationPlan representationPlan) {
        Revision = revision;
        Estimates = new FrozenList<ObjectSaveEstimate>(estimates);
        RepresentationPlan = representationPlan;
    }

    internal StateRevision Revision { get; }
    internal IReadOnlyList<ObjectSaveEstimate> Estimates { get; }
    internal ObjectRepresentationPlan RepresentationPlan { get; }
}
