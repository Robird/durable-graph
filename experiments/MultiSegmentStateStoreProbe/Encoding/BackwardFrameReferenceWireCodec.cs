using Atelia.MultiSegmentStateStoreProbe.Model;

namespace Atelia.MultiSegmentStateStoreProbe.Encoding;

/// <summary>
/// Probe-only framing: canonical VarUInt32 distance followed by canonical VarUInt64
/// opaque FrameTicketCode. This is not the final SizedPtr record grammar.
/// </summary>
internal static class BackwardFrameReferenceWireCodec {
    public static int Write(
        Span<byte> destination,
        BackwardFrameReference reference) {
        reference.ValidateRequired();
        int distanceBytes = CanonicalUnsignedBase128.WriteUInt32(
            destination,
            reference.BackwardFileDistance);
        int ticketBytes = CanonicalUnsignedBase128.WriteUInt64(
            destination[distanceBytes..],
            reference.FrameTicketCode);
        return checked(distanceBytes + ticketBytes);
    }

    public static BackwardFrameReference Read(
        ReadOnlySpan<byte> source,
        out int bytesRead) {
        uint distance = CanonicalUnsignedBase128.ReadUInt32(
            source,
            out int distanceBytes);
        ulong frameTicketCode = CanonicalUnsignedBase128.ReadUInt64(
            source[distanceBytes..],
            out int ticketBytes);
        if (frameTicketCode == 0) {
            throw new InvalidDataException(
                "A required BackwardFrameReference must have a non-zero FrameTicketCode.");
        }

        bytesRead = checked(distanceBytes + ticketBytes);
        return new BackwardFrameReference(distance, frameTicketCode);
    }
}
