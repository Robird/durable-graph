using System.Buffers.Binary;

namespace Atelia.DurableGraph.StateStore.Serialization;

internal ref struct BinaryPayloadReader {
    private ReadOnlySpan<byte> _remaining;
    private readonly int _initialLength;

    internal BinaryPayloadReader(ReadOnlySpan<byte> source) {
        _remaining = source;
        _initialLength = source.Length;
    }

    internal int ConsumedCount => _initialLength - _remaining.Length;
    internal int RemainingCount => _remaining.Length;
    internal bool End => _remaining.IsEmpty;

    internal void EnsureFullyConsumed() {
        if (!End) {
            throw new InvalidDataException(
                $"Expected end of payload, but {RemainingCount} trailing byte(s) remain.");
        }
    }

    internal byte ReadByte() {
        if (_remaining.IsEmpty) {
            throw new EndOfStreamException(
                "Binary payload is truncated while reading a byte.");
        }

        byte value = _remaining[0];
        _remaining = _remaining[1..];
        return value;
    }

    internal sbyte ReadSByte() => unchecked((sbyte)ReadByte());

    internal bool ReadBoolean() {
        if (_remaining.IsEmpty) {
            throw new EndOfStreamException(
                "Binary payload is truncated while reading a Boolean.");
        }

        bool value = _remaining[0] switch {
            0 => false,
            1 => true,
            byte invalid => throw new InvalidDataException(
                $"Invalid Boolean byte 0x{invalid:X2}; expected 0x00 or 0x01."),
        };
        _remaining = _remaining[1..];
        return value;
    }

    internal ushort ReadUInt16() {
        ushort value = CanonicalVarInt.ReadUInt16(_remaining, out int consumed);
        _remaining = _remaining[consumed..];
        return value;
    }

    internal uint ReadUInt32() {
        uint value = CanonicalVarInt.ReadUInt32(_remaining, out int consumed);
        _remaining = _remaining[consumed..];
        return value;
    }

    internal ulong ReadUInt64() {
        ulong value = CanonicalVarInt.ReadUInt64(_remaining, out int consumed);
        _remaining = _remaining[consumed..];
        return value;
    }

    internal short ReadInt16() {
        short value = CanonicalVarInt.ReadInt16(_remaining, out int consumed);
        _remaining = _remaining[consumed..];
        return value;
    }

    internal int ReadInt32() {
        int value = CanonicalVarInt.ReadInt32(_remaining, out int consumed);
        _remaining = _remaining[consumed..];
        return value;
    }

    internal long ReadInt64() {
        long value = CanonicalVarInt.ReadInt64(_remaining, out int consumed);
        _remaining = _remaining[consumed..];
        return value;
    }

    internal Half ReadHalf() =>
        BinaryPrimitives.ReadHalfLittleEndian(ReadSpan(sizeof(ushort)));

    internal float ReadSingle() =>
        BinaryPrimitives.ReadSingleLittleEndian(ReadSpan(sizeof(float)));

    internal double ReadDouble() =>
        BinaryPrimitives.ReadDoubleLittleEndian(ReadSpan(sizeof(double)));

    internal ReadOnlySpan<byte> ReadSpan(int length) {
        if (length < 0) {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        if (_remaining.Length < length) {
            throw new EndOfStreamException(
                $"Binary payload is truncated while reading {length} byte(s).");
        }

        ReadOnlySpan<byte> value = _remaining[..length];
        _remaining = _remaining[length..];
        return value;
    }

    internal int ReadCount() {
        BinaryPayloadReader candidate = this;
        uint value = candidate.ReadUInt32();
        if (value > int.MaxValue) {
            throw new InvalidDataException(
                $"Payload count {value} exceeds Int32.MaxValue.");
        }

        this = candidate;
        return (int)value;
    }

    internal ReadOnlySpan<byte> ReadBytes() {
        BinaryPayloadReader candidate = this;
        int length = candidate.ReadCount();
        ReadOnlySpan<byte> value = candidate.ReadSpan(length);
        this = candidate;
        return value;
    }

    internal string ReadString() {
        BinaryPayloadReader candidate = this;
        string value = StringPayloadCodec.Read(ref candidate);
        this = candidate;
        return value;
    }

    internal string? ReadNullableString() {
        BinaryPayloadReader candidate = this;
        string? value = StringPayloadCodec.ReadNullable(ref candidate);
        this = candidate;
        return value;
    }
}
