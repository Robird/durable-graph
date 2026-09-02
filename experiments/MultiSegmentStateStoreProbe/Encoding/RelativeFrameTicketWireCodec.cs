using Atelia.MultiSegmentStateStoreProbe.Model;

namespace Atelia.MultiSegmentStateStoreProbe.Encoding;

/// <summary>
/// Probe-only framing: canonical distance, Frame start, and Frame length VarUInt values.
/// This is not the final SizedPtr record grammar.
/// </summary>
internal static class RelativeFrameTicketWireCodec {
    public const int MaxEncodedBytes =
        CanonicalUnsignedBase128.MaxUInt32Bytes +
        CanonicalUnsignedBase128.MaxUInt64Bytes +
        CanonicalUnsignedBase128.MaxUInt32Bytes;

    public static int GetEncodedLength(RelativeFrameTicket reference) {
        reference.ValidateRequired();
        return checked(
            CanonicalUnsignedBase128.GetEncodedWidth(reference.BackwardFileDistance) +
            CanonicalUnsignedBase128.GetEncodedWidth(
                checked((ulong)reference.FrameTicket.OffsetBytes)) +
            CanonicalUnsignedBase128.GetEncodedWidth(
                checked((uint)reference.FrameTicket.LengthBytes)));
    }

    public static int Write(
        Span<byte> destination,
        RelativeFrameTicket reference) {
        reference.ValidateRequired();
        int distanceBytes = CanonicalUnsignedBase128.WriteUInt32(
            destination,
            reference.BackwardFileDistance);
        int offsetBytes = CanonicalUnsignedBase128.WriteUInt64(
            destination[distanceBytes..],
            checked((ulong)reference.FrameTicket.OffsetBytes));
        int lengthBytes = CanonicalUnsignedBase128.WriteUInt32(
            destination[(distanceBytes + offsetBytes)..],
            checked((uint)reference.FrameTicket.LengthBytes));
        return checked(distanceBytes + offsetBytes + lengthBytes);
    }

    public static RelativeFrameTicket Read(
        ReadOnlySpan<byte> source,
        out int bytesRead) {
        uint distance = CanonicalUnsignedBase128.ReadUInt32(
            source,
            out int distanceBytes);
        ulong offset = CanonicalUnsignedBase128.ReadUInt64(
            source[distanceBytes..],
            out int offsetBytes);
        uint length = CanonicalUnsignedBase128.ReadUInt32(
            source[(distanceBytes + offsetBytes)..],
            out int lengthBytes);

        FrameTicket frameTicket;
        try {
            frameTicket = new FrameTicket(
                checked((long)offset),
                checked((int)length));
        } catch (Exception exception) when (
            exception is ArgumentOutOfRangeException or OverflowException) {
            throw new InvalidDataException(
                "Required relative Frame ticket has an invalid byte range.",
                exception);
        }

        bytesRead = checked(distanceBytes + offsetBytes + lengthBytes);
        return new RelativeFrameTicket(distance, frameTicket);
    }
}
