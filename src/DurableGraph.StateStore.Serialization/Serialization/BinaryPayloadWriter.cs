using System.Buffers;
using System.Buffers.Binary;

namespace Atelia.DurableGraph.StateStore.Serialization;

/// <summary>
/// Writes canonical payload primitives to a caller-owned buffer. Generated bodies
/// share this writer by reference; a failed write does not roll back prior output.
/// </summary>
public ref struct BinaryPayloadWriter {
    private readonly IBufferWriter<byte> _downstream;

    public BinaryPayloadWriter(IBufferWriter<byte> downstream) {
        ArgumentNullException.ThrowIfNull(downstream);
        _downstream = downstream;
    }

    public void WriteByte(byte value) {
        _downstream.GetSpan(1)[0] = value;
        _downstream.Advance(1);
    }

    public void WriteSByte(sbyte value) => WriteByte(unchecked((byte)value));

    public void WriteBoolean(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    public void WriteUInt16(ushort value) =>
        CanonicalVarInt.WriteUInt16(_downstream, value);

    /// <summary>Writes one UTF-16 code unit using canonical UInt16 encoding, including surrogates.</summary>
    public void WriteChar(char value) => WriteUInt16(value);

    public void WriteUInt32(uint value) =>
        CanonicalVarInt.WriteUInt32(_downstream, value);

    public void WriteUInt64(ulong value) =>
        CanonicalVarInt.WriteUInt64(_downstream, value);

    public void WriteInt16(short value) =>
        CanonicalVarInt.WriteInt16(_downstream, value);

    public void WriteInt32(int value) =>
        CanonicalVarInt.WriteInt32(_downstream, value);

    public void WriteInt64(long value) =>
        CanonicalVarInt.WriteInt64(_downstream, value);

    public void WriteHalf(Half value) {
        BinaryPrimitives.WriteHalfLittleEndian(_downstream.GetSpan(sizeof(ushort)), value);
        _downstream.Advance(sizeof(ushort));
    }

    public void WriteSingle(float value) {
        BinaryPrimitives.WriteSingleLittleEndian(_downstream.GetSpan(sizeof(float)), value);
        _downstream.Advance(sizeof(float));
    }

    public void WriteDouble(double value) {
        BinaryPrimitives.WriteDoubleLittleEndian(_downstream.GetSpan(sizeof(double)), value);
        _downstream.Advance(sizeof(double));
    }

    /// <summary>Writes all 128 Guid bits in big-endian order, without a length prefix.</summary>
    public void WriteGuid(Guid value) {
        value.TryWriteBytes(_downstream.GetSpan(16), bigEndian: true, out _);
        _downstream.Advance(16);
    }

    /// <summary>Writes decimal's lo, mid, hi and flags words as four little-endian Int32 values.</summary>
    public void WriteDecimal(decimal value) {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        Span<byte> destination = _downstream.GetSpan(16);
        for (int index = 0; index < bits.Length; index++) {
            BinaryPrimitives.WriteInt32LittleEndian(destination[(index * sizeof(int))..], bits[index]);
        }
        _downstream.Advance(16);
    }

    /// <summary>Writes signed ticks using canonical Int64 ZigZag encoding.</summary>
    public void WriteTimeSpan(TimeSpan value) => WriteInt64(value.Ticks);

    /// <summary>Writes already encoded body bytes verbatim, without a length prefix.</summary>
    public void WriteSpan(ReadOnlySpan<byte> value) {
        if (value.IsEmpty) {
            return;
        }

        value.CopyTo(_downstream.GetSpan(value.Length));
        _downstream.Advance(value.Length);
    }

    internal void WriteCount(int count) {
        if (count < 0) {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        CanonicalVarInt.WriteUInt32(_downstream, (uint)count);
    }

    internal void WriteBytes(ReadOnlySpan<byte> value) {
        WriteCount(value.Length);
        WriteSpan(value);
    }

    /// <summary>Writes canonical non-null string content; reference slots encode their ObjectId separately.</summary>
    public void WriteString(string value) {
        ArgumentNullException.ThrowIfNull(value);
        StringPayloadCodec.Write(_downstream, value);
    }

    internal void WriteNullableString(string? value) =>
        StringPayloadCodec.WriteNullable(_downstream, value);
}
