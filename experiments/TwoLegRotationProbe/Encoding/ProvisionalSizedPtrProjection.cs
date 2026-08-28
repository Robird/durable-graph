using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Encoding;

/// <summary>
/// Local projection of the current Atelia.Data.SizedPtr.Serialize shape.
/// It exists only to make this probe reproducible without taking a sibling-project dependency.
/// </summary>
internal static class ProvisionalSizedPtrProjection {
    private const int AlignmentShift = 2;
    private const int LengthPackedBits = 26;
    private const ulong LengthPackedMask = (1UL << LengthPackedBits) - 1UL;

    public static ulong Serialize(FrameTicket ticket) {
        ulong packed =
            (((ulong)ticket.OffsetBytes >> AlignmentShift) << LengthPackedBits) |
            (((ulong)ticket.LengthBytes >> AlignmentShift) & LengthPackedMask);
        ulong offset = packed >> LengthPackedBits;
        ulong length = packed & LengthPackedMask;

        ulong result = ((offset & 0x3F) << 2) | (length & 0x3);
        offset >>= 6;
        length >>= 2;

        result |= (((offset & 0x1F) << 3) | (length & 0x7)) << 8;
        offset >>= 5;
        length >>= 3;
        result |= (((offset & 0x1F) << 3) | (length & 0x7)) << 16;
        offset >>= 5;
        length >>= 3;
        result |= (((offset & 0x1F) << 3) | (length & 0x7)) << 24;
        offset >>= 5;
        length >>= 3;
        result |= (((offset & 0x1F) << 3) | (length & 0x7)) << 32;
        offset >>= 5;
        length >>= 3;

        result |= (((offset & 0xF) << 4) | (length & 0xF)) << 40;
        offset >>= 4;
        length >>= 4;
        result |= (((offset & 0xF) << 4) | (length & 0xF)) << 48;
        offset >>= 4;
        length >>= 4;
        result |= (((offset & 0xF) << 4) | (length & 0xF)) << 56;
        return result;
    }

    public static FrameTicket Deserialize(ulong serialized) {
        ulong offset = (serialized >> 60) & 0xF;
        ulong length = (serialized >> 56) & 0xF;
        offset = (offset << 4) | ((serialized >> 52) & 0xF);
        length = (length << 4) | ((serialized >> 48) & 0xF);
        offset = (offset << 4) | ((serialized >> 44) & 0xF);
        length = (length << 4) | ((serialized >> 40) & 0xF);

        offset = (offset << 5) | ((serialized >> 35) & 0x1F);
        length = (length << 3) | ((serialized >> 32) & 0x7);
        offset = (offset << 5) | ((serialized >> 27) & 0x1F);
        length = (length << 3) | ((serialized >> 24) & 0x7);
        offset = (offset << 5) | ((serialized >> 19) & 0x1F);
        length = (length << 3) | ((serialized >> 16) & 0x7);
        offset = (offset << 5) | ((serialized >> 11) & 0x1F);
        length = (length << 3) | ((serialized >> 8) & 0x7);

        offset = (offset << 6) | ((serialized >> 2) & 0x3F);
        length = (length << 2) | (serialized & 0x3);

        long offsetBytes = checked((long)(offset << AlignmentShift));
        int lengthBytes = checked((int)(length << AlignmentShift));
        return new FrameTicket(offsetBytes, lengthBytes);
    }
}
