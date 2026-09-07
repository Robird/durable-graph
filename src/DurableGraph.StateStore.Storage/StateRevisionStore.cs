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

        ValidateDirectPriors(revision);

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
        // TODO(DB-028): Measure repeated reads, then consider operation/batch-scoped
        // decoded Frame caching. Cache owned revisions, never pooled Frame views.
        // This alone does not remove repeated full head-map materialization costs.
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
    /// Reconstructs the object head for every ObjectId declared live by the
    /// ObjectHeadMap at the specified StateRevision address.
    /// </summary>
    /// <remarks>
    /// This does not identify or publish a publication head. Local ObjectIds map
    /// to the containing Revision Frame. External
    /// ObjectIds keep the absolute address recorded by the completing Base. The
    /// returned map is immutable and enumerates in ascending ObjectId order.
    /// These shallow declarations are not dereferenced or validated as
    /// ObjectVersion records by this operation.
    /// </remarks>
    public IReadOnlyDictionary<uint, FrameAddress> ReadLiveObjectHeadMap(
        FrameAddress revisionAddress) =>
        LiveObjectHeadMapMaterializer.Materialize(revisionAddress, Read);

    /// <summary>
    /// Reads the complete Base body of an Object live in the specified StateRevision.
    /// The returned array is an independent copy owned by the caller.
    /// </summary>
    /// <remarks>
    /// Resolves membership from the exact Revision, then requires its declared
    /// containing Frame to hold a local Base record. An absent local record is
    /// invalid even if the containing Frame inherits that Object from its parent.
    /// This validates only the requested Object's locator and raw body; it does
    /// not validate types, other external heads, or graph references.
    /// </remarks>
    public byte[] ReadObjectBaseBody(FrameAddress revisionAddress, uint objectId) {
        FrameAddress head = ResolveObjectHead(revisionAddress, objectId);
        ObjectVersionRecord record = FindLocalRecord(Read(head), head, objectId);
        if (record.Kind != ObjectVersionKind.Base) {
            throw new InvalidDataException(
                $"ObjectId {objectId} at {head} has a Delta head, not a complete Base body.");
        }

        return record.Body.ToArray();
    }

    /// <summary>
    /// Reads one live Object's complete raw reconstruction chain, ordered from
    /// its most recent Base through its current Delta head.
    /// </summary>
    /// <remarks>
    /// Each Delta must name the object head selected by its exact Parent Revision.
    /// Records own their bytes and no pooled Frame lease escapes this operation.
    /// This does not interpret bodies or authenticate their Schema or producer.
    /// Complete maps retain their shallow external-head declaration contract.
    /// </remarks>
    public ObjectVersionChain ReadObjectVersionChain(
        FrameAddress revisionAddress,
        uint objectId) {
        FrameAddress head = ResolveObjectHead(revisionAddress, objectId);
        FrameAddress address = head;
        StateRevision revision = Read(address);
        ObjectVersionRecord record = FindLocalRecord(revision, address, objectId);
        List<ObjectVersionChainEntry> records = [];
        while (true) {
            records.Add(new ObjectVersionChainEntry(address, record));
            if (record.Kind == ObjectVersionKind.Base) {
                break;
            }

            FrameAddress parent = revision.ParentRevisionAddress
                ?? throw new InvalidDataException(
                    $"Delta ObjectId {objectId} at {address} has no Parent Revision.");
            FrameAddress prior = record.PriorAddress
                ?? throw new InvalidDataException(
                    $"Delta ObjectId {objectId} at {address} has no prior address.");
            FrameAddressValidator.EnsureStrictlyEarlier(address, parent);
            FrameAddressValidator.EnsureStrictlyEarlier(address, prior);
            RequireParentHead(ReadLiveObjectHeadMap(parent), objectId, prior);
            revision = Read(prior);
            record = FindLocalRecord(revision, prior, objectId);
            address = prior;
        }

        records.Reverse();
        return new ObjectVersionChain(objectId, head, records);
    }

    private void ValidateDirectPriors(StateRevision revision) {
        IReadOnlyDictionary<uint, FrameAddress>? parentHeads = null;
        foreach (ObjectVersionRecord record in revision.LocalObjects) {
            if (record.Kind != ObjectVersionKind.Delta) {
                continue;
            }

            FrameAddress parent = revision.ParentRevisionAddress
                ?? throw new InvalidDataException(
                    $"Delta ObjectId {record.ObjectId} requires a Parent Revision.");
            FrameAddress prior = record.PriorAddress
                ?? throw new InvalidDataException(
                    $"Delta ObjectId {record.ObjectId} requires a prior address.");
            parentHeads ??= ReadLiveObjectHeadMap(parent);
            RequireParentHead(parentHeads, record.ObjectId, prior);
            _ = FindLocalRecord(Read(prior), prior, record.ObjectId);
        }
    }

    private FrameAddress ResolveObjectHead(FrameAddress revisionAddress, uint objectId) {
        FrameAddressValidator.ValidateRequired(revisionAddress, nameof(revisionAddress));
        if (objectId == 0) {
            throw new ArgumentOutOfRangeException(
                nameof(objectId), objectId, "ObjectId must be nonzero.");
        }

        if (!ReadLiveObjectHeadMap(revisionAddress).TryGetValue(objectId, out FrameAddress head)) {
            throw new InvalidDataException(
                $"ObjectId {objectId} is not live in Revision {revisionAddress}.");
        }

        return head;
    }

    private static void RequireParentHead(
        IReadOnlyDictionary<uint, FrameAddress> parentHeads,
        uint objectId,
        FrameAddress prior) {
        if (!parentHeads.TryGetValue(objectId, out FrameAddress expected) || expected != prior) {
            throw new InvalidDataException(
                $"Delta ObjectId {objectId} prior {prior} is not the object head selected by its exact Parent Revision.");
        }
    }

    private static ObjectVersionRecord FindLocalRecord(
        StateRevision revision,
        FrameAddress address,
        uint objectId) {
        foreach (ObjectVersionRecord record in revision.LocalObjects) {
            if (record.ObjectId == objectId) {
                return record;
            }
        }

        throw new InvalidDataException(
            $"Frame {address} has no local ObjectVersion record for ObjectId {objectId}.");
    }
}
