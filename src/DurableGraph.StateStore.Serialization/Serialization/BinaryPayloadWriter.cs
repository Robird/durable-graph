using System.Buffers;
using System.Buffers.Binary;

namespace Atelia.DurableGraph.StateStore.Serialization;

internal ref struct BinaryPayloadWriter {
    private readonly IBufferWriter<byte> _downstream;

    internal BinaryPayloadWriter(IBufferWriter<byte> downstream) {
        ArgumentNullException.ThrowIfNull(downstream);
        _downstream = downstream;
    }

    internal void WriteByte(byte value) {
        _downstream.GetSpan(1)[0] = value;
        _downstream.Advance(1);
    }

    internal void WriteSByte(sbyte value) => WriteByte(unchecked((byte)value));

    internal void WriteBoolean(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    internal void WriteUInt16(ushort value) =>
        CanonicalVarInt.WriteUInt16(_downstream, value);

    internal void WriteUInt32(uint value) =>
        CanonicalVarInt.WriteUInt32(_downstream, value);

    internal void WriteUInt64(ulong value) =>
        CanonicalVarInt.WriteUInt64(_downstream, value);

    internal void WriteInt16(short value) =>
        CanonicalVarInt.WriteInt16(_downstream, value);

    internal void WriteInt32(int value) =>
        CanonicalVarInt.WriteInt32(_downstream, value);

    internal void WriteInt64(long value) =>
        CanonicalVarInt.WriteInt64(_downstream, value);

    internal void WriteHalf(Half value) {
        BinaryPrimitives.WriteHalfLittleEndian(_downstream.GetSpan(sizeof(ushort)), value);
        _downstream.Advance(sizeof(ushort));
    }

    internal void WriteSingle(float value) {
        BinaryPrimitives.WriteSingleLittleEndian(_downstream.GetSpan(sizeof(float)), value);
        _downstream.Advance(sizeof(float));
    }

    internal void WriteDouble(double value) {
        BinaryPrimitives.WriteDoubleLittleEndian(_downstream.GetSpan(sizeof(double)), value);
        _downstream.Advance(sizeof(double));
    }

    internal void WriteSpan(ReadOnlySpan<byte> value) {
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

    internal void WriteString(string value) {
        ArgumentNullException.ThrowIfNull(value);
        StringPayloadCodec.Write(_downstream, value);
    }

    internal void WriteNullableString(string? value) =>
        StringPayloadCodec.WriteNullable(_downstream, value);
}
