using System.Buffers;

namespace Atelia.DurableGraph.StateStore.Serialization;

internal static class CanonicalVarInt {
    private const int MaxUInt16Bytes = 3;
    private const int MaxUInt32Bytes = 5;
    private const int MaxUInt64Bytes = 10;

    internal static void WriteUInt16(IBufferWriter<byte> writer, ushort value) =>
        WriteCore(writer, value, MaxUInt16Bytes);

    internal static void WriteUInt32(IBufferWriter<byte> writer, uint value) =>
        WriteCore(writer, value, MaxUInt32Bytes);

    internal static void WriteUInt64(IBufferWriter<byte> writer, ulong value) =>
        WriteCore(writer, value, MaxUInt64Bytes);

    private static void WriteCore(
        IBufferWriter<byte> writer,
        ulong value,
        int maxBytes) {
        ArgumentNullException.ThrowIfNull(writer);

        Span<byte> destination = writer.GetSpan(maxBytes);
        int written = 0;
        do {
            byte current = (byte)(value & 0x7f);
            value >>= 7;
            if (value != 0) {
                current |= 0x80;
            }

            destination[written++] = current;
        } while (value != 0);

        writer.Advance(written);
    }

    internal static void WriteInt16(IBufferWriter<byte> writer, short value) =>
        WriteUInt16(writer, EncodeZigZag(value));

    internal static void WriteInt32(IBufferWriter<byte> writer, int value) =>
        WriteUInt32(writer, EncodeZigZag(value));

    internal static void WriteInt64(IBufferWriter<byte> writer, long value) =>
        WriteUInt64(writer, EncodeZigZag(value));

    internal static ushort ReadUInt16(ReadOnlySpan<byte> source, out int consumed) {
        ulong value = ReadCore(source, MaxUInt16Bytes, ushort.MaxValue, out consumed);
        return (ushort)value;
    }

    internal static uint ReadUInt32(ReadOnlySpan<byte> source, out int consumed) {
        ulong value = ReadCore(source, MaxUInt32Bytes, uint.MaxValue, out consumed);
        return (uint)value;
    }

    internal static ulong ReadUInt64(ReadOnlySpan<byte> source, out int consumed) =>
        ReadCore(source, MaxUInt64Bytes, ulong.MaxValue, out consumed);

    internal static short ReadInt16(ReadOnlySpan<byte> source, out int consumed) =>
        DecodeZigZag(ReadUInt16(source, out consumed));

    internal static int ReadInt32(ReadOnlySpan<byte> source, out int consumed) =>
        DecodeZigZag(ReadUInt32(source, out consumed));

    internal static long ReadInt64(ReadOnlySpan<byte> source, out int consumed) =>
        DecodeZigZag(ReadUInt64(source, out consumed));

    private static ulong ReadCore(
        ReadOnlySpan<byte> source,
        int maxBytes,
        ulong maxValue,
        out int consumed) {
        ulong value = 0;
        for (int index = 0; index < maxBytes; index++) {
            if ((uint)index >= (uint)source.Length) {
                throw new EndOfStreamException(
                    "Canonical variable-length integer is truncated.");
            }

            byte current = source[index];
            ulong payload = (ulong)(current & 0x7f);
            int shift = index * 7;
            if (payload > (maxValue >> shift)) {
                throw new InvalidDataException(
                    "Canonical variable-length integer overflows its target type.");
            }

            value |= payload << shift;
            if ((current & 0x80) == 0) {
                consumed = index + 1;
                if (consumed > 1 && payload == 0) {
                    throw new InvalidDataException(
                        "Canonical variable-length integer uses an overlong encoding.");
                }

                return value;
            }
        }

        throw new InvalidDataException(
            "Canonical variable-length integer exceeds its maximum width.");
    }

    private static ushort EncodeZigZag(short value) =>
        (ushort)((value << 1) ^ (value >> 15));

    private static uint EncodeZigZag(int value) =>
        (uint)((value << 1) ^ (value >> 31));

    private static ulong EncodeZigZag(long value) =>
        (ulong)((value << 1) ^ (value >> 63));

    private static short DecodeZigZag(ushort value) =>
        (short)((value >> 1) ^ -(value & 1));

    private static int DecodeZigZag(uint value) =>
        (int)(value >> 1) ^ -(int)(value & 1);

    private static long DecodeZigZag(ulong value) =>
        (long)(value >> 1) ^ -(long)(value & 1);
}
