namespace Atelia.DurableGraph.Storage;

/// <summary>
/// Describes whether a StateRevision contains an ObjectHeadMap Base checkpoint or
/// an ObjectHeadMap Delta relative to its exact Parent Revision.
/// </summary>
public enum ObjectHeadMapKind : byte {
    Base = 1,
    Delta = 2,
}
