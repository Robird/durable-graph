using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.StateStore.Storage;

/// <summary>
/// Encodes the provisional v1 State Revision membership payload. Object contents are
/// intentionally outside this format slice.
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

        WriteObjectIds(ref writer, revision.BaseObjectIds);
        WriteObjectIds(ref writer, revision.DeltaObjectIds);
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
        ValidateCount(revision.BaseObjectIds.Count, nameof(revision.BaseObjectIds));
        ValidateCount(revision.DeltaObjectIds.Count, nameof(revision.DeltaObjectIds));
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
