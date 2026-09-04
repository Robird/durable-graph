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
}
