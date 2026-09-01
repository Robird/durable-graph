namespace Atelia.MultiSegmentStateStoreProbe.Encoding;

internal static class CanonicalUnsignedBase128 {
    public const int MaxUInt32Bytes = 5;
    public const int MaxUInt64Bytes = 10;

    public static int WriteUInt32(Span<byte> destination, uint value) =>
        WriteUInt64(destination, value);

    public static int WriteUInt64(Span<byte> destination, ulong value) {
        int width = GetEncodedWidth(value);
        if (destination.Length < width) {
            throw new ArgumentException(
                $"Destination length {destination.Length} is smaller than encoded width {width}.",
                nameof(destination));
        }

        int index = 0;
        do {
            byte current = (byte)(value & 0x7f);
            value >>= 7;
            if (value != 0) {
                current |= 0x80;
            }

            destination[index++] = current;
        } while (value != 0);

        return index;
    }

    public static uint ReadUInt32(
        ReadOnlySpan<byte> source,
        out int bytesRead) {
        ulong value = ReadCore(source, MaxUInt32Bytes, uint.MaxValue, out bytesRead);
        return checked((uint)value);
    }

    public static ulong ReadUInt64(
        ReadOnlySpan<byte> source,
        out int bytesRead) =>
        ReadCore(source, MaxUInt64Bytes, ulong.MaxValue, out bytesRead);

    public static int GetEncodedWidth(ulong value) {
        int width = 1;
        while (value >= 0x80) {
            value >>= 7;
            width++;
        }

        return width;
    }

    private static ulong ReadCore(
        ReadOnlySpan<byte> source,
        int maxBytes,
        ulong maxValue,
        out int bytesRead) {
        ulong value = 0;
        for (int index = 0; index < maxBytes; index++) {
            if (index >= source.Length) {
                throw new EndOfStreamException("Unsigned Base128 value is truncated.");
            }

            byte current = source[index];
            ulong payload = (ulong)(current & 0x7f);
            int shift = checked(index * 7);
            if (shift >= 64 || payload > (maxValue >> shift)) {
                throw new InvalidDataException("Unsigned Base128 value overflows its target type.");
            }

            value |= payload << shift;
            if ((current & 0x80) == 0) {
                bytesRead = index + 1;
                if (bytesRead != GetEncodedWidth(value)) {
                    throw new InvalidDataException("Unsigned Base128 value is not canonical.");
                }

                return value;
            }
        }

        throw new InvalidDataException("Unsigned Base128 value exceeds its maximum width.");
    }
}
