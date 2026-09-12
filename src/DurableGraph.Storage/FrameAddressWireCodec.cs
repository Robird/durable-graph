using Atelia.Data;
using Atelia.DurableGraph.Serialization;

namespace Atelia.DurableGraph.Storage;

internal static class FrameAddressWireCodec {
    internal static void Write(
        ref BinaryPayloadWriter writer,
        FileScope scope,
        FrameAddress address) {
        scope.ValidateRequired(nameof(scope));
        FrameAddressValidator.ValidateRequired(address, nameof(address));

        uint backwardFileDistance = scope.ToBackwardFileDistance(
            address.FileNumber);
        ulong serializedTicket = address.FrameTicket.Serialize();

        writer.WriteUInt32(backwardFileDistance);
        writer.WriteUInt64(serializedTicket);
    }

    internal static FrameAddress Read(
        ref BinaryPayloadReader reader,
        FileScope scope) {
        scope.ValidateRequired(nameof(scope));

        BinaryPayloadReader candidate = reader;
        uint backwardFileDistance = candidate.ReadUInt32();
        ulong serializedTicket = candidate.ReadUInt64();
        SizedPtr frameTicket = SizedPtr.Deserialize(serializedTicket);
        if (frameTicket.Length == 0) {
            throw new InvalidDataException(
                "A required FrameAddress must contain a non-empty SizedPtr.");
        }
        FrameAddress address = new(
            scope.ToAbsoluteFileNumber(backwardFileDistance),
            frameTicket);
        reader = candidate;
        return address;
    }
}
