using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.StateStore.Storage;

/// <summary>
/// Decodes the provisional v2 State Revision Base contents and membership, and immediately
/// normalizes every persisted reference to an absolute <see cref="FrameAddress"/>.
/// </summary>
internal static class StateRevisionWireReader {
    internal static StateRevision Read(
        ReadOnlySpan<byte> source,
        FileScope scope) {
        scope.ValidateRequired(nameof(scope));
        BinaryPayloadReader reader = new(source);
        byte version = reader.ReadByte();
        if (version != StateRevisionWireFormat.Version) {
            throw new InvalidDataException(
                $"Unsupported State Revision wire version {version}.");
        }

        ObjectHeadMapKind kind = reader.ReadByte() switch {
            (byte)ObjectHeadMapKind.Base => ObjectHeadMapKind.Base,
            (byte)ObjectHeadMapKind.Delta => ObjectHeadMapKind.Delta,
            byte value => throw new InvalidDataException(
                $"Unknown ObjectHeadMap kind {value} in State Revision wire data."),
        };
        FrameAddress? parent = reader.ReadByte() switch {
            0 => null,
            1 => FrameAddressWireCodec.Read(
                ref reader,
                scope),
            byte value => throw new InvalidDataException(
                $"Invalid parent-presence marker {value}."),
        };
        if (kind == ObjectHeadMapKind.Delta && parent is null) {
            throw new InvalidDataException(
                "An ObjectHeadMap Delta has no parent Revision address.");
        }

        BaseObjectRecord[] baseObjects = ReadBaseObjects(ref reader);
        StateRevision revision;
        try {
            revision = kind switch {
                ObjectHeadMapKind.Base => StateRevision.CreateBase(
                    parent,
                    baseObjects,
                    ReadExternalObjectHeads(
                        ref reader,
                        scope)),
                ObjectHeadMapKind.Delta when parent is { } parentAddress =>
                    StateRevision.CreateDelta(
                        parentAddress,
                        baseObjects,
                        ReadObjectIds(ref reader)),
                _ => throw new InvalidDataException(
                    $"Unknown ObjectHeadMap kind {kind}."),
            };
        } catch (ArgumentException exception) {
            throw new InvalidDataException(
                "State Revision wire data violates the semantic model.",
                exception);
        }

        reader.EnsureFullyConsumed();

        return revision;
    }

    private static KeyValuePair<uint, FrameAddress>[] ReadExternalObjectHeads(
        ref BinaryPayloadReader reader,
        FileScope scope) {
        int count = ReadCount(ref reader, minimumEntryBytes: 3);
        KeyValuePair<uint, FrameAddress>[] entries = new KeyValuePair<uint, FrameAddress>[count];
        uint previous = 0;
        for (int index = 0; index < count; index++) {
            uint objectId = ReadNextObjectId(ref reader, previous);
            previous = objectId;
            FrameAddress address = FrameAddressWireCodec.Read(
                ref reader,
                scope);
            entries[index] = new(objectId, address);
        }

        return entries;
    }

    private static uint[] ReadObjectIds(ref BinaryPayloadReader reader) {
        int count = ReadCount(ref reader, minimumEntryBytes: 1);
        uint[] objectIds = new uint[count];
        uint previous = 0;
        for (int index = 0; index < count; index++) {
            uint objectId = ReadNextObjectId(ref reader, previous);
            objectIds[index] = objectId;
            previous = objectId;
        }

        return objectIds;
    }

    private static BaseObjectRecord[] ReadBaseObjects(ref BinaryPayloadReader reader) {
        int count = ReadCount(ref reader, minimumEntryBytes: 2);
        BaseObjectRecord[] records = new BaseObjectRecord[count];
        uint previous = 0;
        for (int index = 0; index < count; index++) {
            uint objectId = ReadNextObjectId(ref reader, previous);
            // ReadBytes validates canonical Int32 length and remaining bounds before copying.
            records[index] = new BaseObjectRecord(objectId, reader.ReadBytes());
            previous = objectId;
        }

        return records;
    }

    private static int ReadCount(ref BinaryPayloadReader reader, int minimumEntryBytes) {
        uint encoded = reader.ReadUInt32();
        if (encoded > StateRevisionWireFormat.MaxCollectionCount) {
            throw new InvalidDataException(
                $"Collection count {encoded} exceeds the provisional limit " +
                $"{StateRevisionWireFormat.MaxCollectionCount}.");
        }

        if (encoded > (uint)(reader.RemainingCount / minimumEntryBytes)) {
            throw new InvalidDataException(
                $"Collection count {encoded} exceeds the remaining payload bounds.");
        }

        return checked((int)encoded);
    }

    private static uint ReadNextObjectId(
        ref BinaryPayloadReader reader,
        uint previous) {
        uint objectId = reader.ReadUInt32();
        if (objectId == 0) {
            throw new InvalidDataException("ObjectId 0 is reserved.");
        }

        if (objectId <= previous) {
            throw new InvalidDataException(
                "ObjectIds must be encoded in strictly ascending order.");
        }

        return objectId;
    }
}
