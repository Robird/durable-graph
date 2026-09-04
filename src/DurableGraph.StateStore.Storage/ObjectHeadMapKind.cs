namespace Atelia.DurableGraph.StateStore.Storage;

/// <summary>
/// Describes whether a State Revision contains a complete live-object head map or
/// a mutation relative to its exact parent Revision.
/// </summary>
public enum ObjectHeadMapKind : byte {
    Base = 1,
    Delta = 2,
}
