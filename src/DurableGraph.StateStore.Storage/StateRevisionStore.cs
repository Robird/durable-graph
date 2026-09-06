using Atelia.Data;
using Atelia.Rbf;
using Atelia.RbfSegmentStore;

namespace Atelia.DurableGraph.StateStore.Storage;

/// <summary>
/// Appends and reads State Revision Frames through an existing RBF Segment Store.
/// </summary>
/// <remarks>
/// The caller owns the lifetime and publication authority of the supplied Segment
/// Store. This type appends candidate data and never chooses or publishes a head.
/// </remarks>
public sealed class StateRevisionStore {
    private readonly IRbfSegmentStore _segmentStore;

    public StateRevisionStore(IRbfSegmentStore segmentStore) {
        ArgumentNullException.ThrowIfNull(segmentStore);
        _segmentStore = segmentStore;
    }

    public FrameAddress Append(StateRevision revision) {
        ArgumentNullException.ThrowIfNull(revision);

        using RbfSegmentWriterLease writer = _segmentStore.OpenActiveWriter();
        using RbfFrameBuilder builder = writer.File.BeginAppend();
        StateRevisionWireWriter.Write(
            builder.PayloadAndMeta,
            revision,
            new FileScope(writer.SegmentNumber));
        SizedPtr ticket = builder.EndAppend(
            StateRevisionWireFormat.RbfTag).Unwrap();
        return new FrameAddress(writer.SegmentNumber, ticket);
    }

    public StateRevision Read(FrameAddress address) {
        FrameAddressValidator.ValidateRequired(address, nameof(address));
        using RbfSegmentReaderLease reader = _segmentStore.OpenReader(
            address.FileNumber);
        using RbfPooledFrame frame = reader.File.ReadPooledFrame(
            address.FrameTicket).Unwrap();
        if (frame.IsTombstone) {
            throw new InvalidDataException(
                $"State Revision Frame {address} is a tombstone.");
        }

        if (frame.Tag != StateRevisionWireFormat.RbfTag) {
            throw new InvalidDataException(
                $"Frame {address} has tag 0x{frame.Tag:X8}; expected State Revision " +
                $"tag 0x{StateRevisionWireFormat.RbfTag:X8}.");
        }

        if (frame.TailMetaLength != 0) {
            throw new InvalidDataException(
                $"State Revision Frame {address} has unexpected TailMeta.");
        }

        return StateRevisionWireReader.Read(
            frame.PayloadAndMeta,
            new FileScope(address.FileNumber));
    }

    /// <summary>
    /// Reconstructs the membership-declared current head of every live Object.
    /// </summary>
    /// <remarks>
    /// Local ObjectIds map to the containing State Revision Frame. External
    /// ObjectIds keep the absolute address recorded by the completing Base. The
    /// returned map is immutable and enumerates in ascending ObjectId order.
    /// These shallow declarations are not dereferenced or validated as
    /// ObjectVersion records by this operation.
    /// </remarks>
    public IReadOnlyDictionary<uint, FrameAddress> ReadLiveObjectHeads(
        FrameAddress revisionHead) =>
        LiveObjectHeadMapMaterializer.Materialize(revisionHead, Read);

    /// <summary>
    /// Reads the complete Base body of an Object live in the specified Revision.
    /// The returned array is an independent copy owned by the caller.
    /// </summary>
    /// <remarks>
    /// Resolves membership from the exact Revision, then requires its declared
    /// containing Frame to hold a local Base record. An absent local record is
    /// invalid even if the containing Frame inherits that Object from its parent.
    /// This validates only the requested Object's locator and raw body; it does
    /// not validate types, other external heads, or graph references.
    /// </remarks>
    public byte[] ReadObjectBase(FrameAddress revisionHead, uint objectId) {
        FrameAddressValidator.ValidateRequired(revisionHead, nameof(revisionHead));
        if (objectId == 0) {
            throw new ArgumentOutOfRangeException(
                nameof(objectId), objectId, "ObjectId must be nonzero.");
        }

        IReadOnlyDictionary<uint, FrameAddress> heads = ReadLiveObjectHeads(
            revisionHead);
        if (!heads.TryGetValue(objectId, out FrameAddress containingFrame)) {
            throw new InvalidDataException(
                $"ObjectId {objectId} is not live in Revision {revisionHead}.");
        }

        StateRevision containingRevision = Read(containingFrame);
        foreach (BaseObjectRecord record in containingRevision.BaseObjects) {
            if (record.ObjectId == objectId) {
                return record.Body.ToArray();
            }
        }

        throw new InvalidDataException(
            $"Frame {containingFrame} has no local Base record for ObjectId {objectId}.");
    }
}
