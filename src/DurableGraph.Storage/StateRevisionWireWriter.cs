using System.Buffers;
using Atelia.DurableGraph.Serialization;

namespace Atelia.DurableGraph.Storage;

/// <summary>
/// Encodes the provisional v3 StateRevision's object-version records and
/// ObjectHeadMap membership representation.
/// </summary>
internal static class StateRevisionWireWriter {
    internal static void Write(
        IBufferWriter<byte> destination,
        StateRevision revision,
        FileScope scope) {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(revision);
        scope.ValidateRequired(nameof(scope));
        ValidateCollectionCounts(revision);

        BinaryPayloadWriter writer = new(destination);
        writer.WriteByte(StateRevisionWireFormat.Version);
        writer.WriteByte((byte)revision.ObjectHeadMapKind);
        if (revision.ParentRevisionAddress is { } parent) {
            writer.WriteByte(1);
            FrameAddressWireCodec.Write(
                ref writer,
                scope,
                parent);
        } else {
            writer.WriteByte(0);
        }

        WriteCount(ref writer, revision.LocalObjects.Count);
        foreach (ObjectVersionRecord record in revision.LocalObjects) {
            writer.WriteUInt32(record.ObjectId);
            writer.WriteByte((byte)record.Kind);
            if (record.PriorAddress is { } prior) {
                FrameAddressWireCodec.Write(ref writer, scope, prior);
            }
            writer.WriteBytes(record.Body);
        }
        switch (revision.ObjectHeadMapKind) {
            case ObjectHeadMapKind.Base:
                WriteCount(ref writer, revision.ExternalObjectHeads.Count);
                foreach ((uint objectId, FrameAddress address) in
                    revision.ExternalObjectHeads) {
                    writer.WriteUInt32(objectId);
                    FrameAddressWireCodec.Write(
                        ref writer,
                        scope,
                        address);
                }

                break;
            case ObjectHeadMapKind.Delta:
                WriteObjectIds(ref writer, revision.RemovedObjectIds);
                break;
            default:
                throw new InvalidDataException(
                    $"Unknown ObjectHeadMap kind {revision.ObjectHeadMapKind}.");
        }
    }

    private static void WriteObjectIds(
        ref BinaryPayloadWriter writer,
        IReadOnlyCollection<uint> objectIds) {
        WriteCount(ref writer, objectIds.Count);
        foreach (uint objectId in objectIds) {
            writer.WriteUInt32(objectId);
        }
    }

    private static void WriteCount(ref BinaryPayloadWriter writer, int count) {
        ValidateCount(count, "collection");
        writer.WriteCount(count);
    }

    private static void ValidateCollectionCounts(StateRevision revision) {
        ValidateCount(revision.LocalObjects.Count, nameof(revision.LocalObjects));
        if (revision.ObjectHeadMapKind == ObjectHeadMapKind.Base) {
            ValidateCount(
                revision.ExternalObjectHeads.Count,
                nameof(revision.ExternalObjectHeads));
        } else {
            ValidateCount(
                revision.RemovedObjectIds.Count,
                nameof(revision.RemovedObjectIds));
        }
    }

    private static void ValidateCount(int count, string role) {
        if ((uint)count > StateRevisionWireFormat.MaxCollectionCount) {
            throw new InvalidDataException(
                $"{role} count {count} exceeds the provisional limit " +
                $"{StateRevisionWireFormat.MaxCollectionCount}.");
        }
    }
}
