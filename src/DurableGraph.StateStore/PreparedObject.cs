using Atelia.DurableGraph.StateStore.Serialization;
using Atelia.DurableGraph.StateStore.Storage;

namespace Atelia.DurableGraph.StateStore;

/// <summary>
/// Owned current content and the producer's classification against an exact prior.
/// The producer owns the DTO/Schema correspondence; this row cannot authenticate it.
/// </summary>
internal sealed class PreparedObject {
    private PreparedObject(uint objectId, FrameAddress? priorAddress, PreparedBase baseContent,
        PreparedDelta? deltaContent, ObjectSaveChangeKind changeKind) {
        ArgumentOutOfRangeException.ThrowIfZero(objectId);
        ArgumentNullException.ThrowIfNull(baseContent);
        if (priorAddress is { } prior && (prior.FileNumber == 0 || prior.FrameTicket.Length == 0)) {
            throw new ArgumentOutOfRangeException(nameof(priorAddress), "The prior must be a valid Frame address.");
        }
        ObjectId = objectId;
        PriorAddress = priorAddress;
        BaseContent = baseContent;
        DeltaContent = deltaContent;
        ChangeKind = changeKind;
    }

    internal uint ObjectId { get; }
    internal FrameAddress? PriorAddress { get; }
    internal PreparedBase BaseContent { get; }
    internal PreparedDelta? DeltaContent { get; }
    internal ObjectSaveChangeKind ChangeKind { get; }

    internal static PreparedObject New(uint id, PreparedBase content) =>
        new(id, null, content, null, ObjectSaveChangeKind.Insert);

    internal static PreparedObject Unchanged(uint id, FrameAddress prior, PreparedBase content) =>
        new(id, prior, content, null, ObjectSaveChangeKind.NoChange);

    internal static PreparedObject Compared(uint id, FrameAddress prior, PreparedBase content, PreparedDelta delta) {
        ArgumentNullException.ThrowIfNull(delta);
        return new(id, prior, content, delta,
            delta.HasChanges ? ObjectSaveChangeKind.Update : ObjectSaveChangeKind.NoChange);
    }

    internal static PreparedObject BaseOnlyUpdate(uint id, FrameAddress prior, PreparedBase content) =>
        new(id, prior, content, null, ObjectSaveChangeKind.BaseOnlyUpdate);
}
