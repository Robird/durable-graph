using System.Buffers.Binary;

namespace Atelia.DurableGraph.StateStore.Serialization;

/// <summary>
/// Reads canonical payload primitives. Generated bodies share this cursor by reference;
/// callers own the payload boundary and any partially populated target after a failure.
/// </summary>
public ref struct BinaryPayloadReader {
    private ReadOnlySpan<byte> _remaining;
    private readonly int _initialLength;

    public BinaryPayloadReader(ReadOnlySpan<byte> source) {
        _remaining = source;
        _initialLength = source.Length;
    }

    public int ConsumedCount => _initialLength - _remaining.Length;
    public int RemainingCount => _remaining.Length;
    public bool End => _remaining.IsEmpty;

    public void EnsureFullyConsumed() {
        if (!End) {
            throw new InvalidDataException(
                $"Expected end of payload, but {RemainingCount} trailing byte(s) remain.");
        }
    }

    public byte ReadByte() {
        if (_remaining.IsEmpty) {
            throw new EndOfStreamException(
                "Binary payload is truncated while reading a byte.");
        }

        byte value = _remaining[0];
        _remaining = _remaining[1..];
        return value;
    }

    public sbyte ReadSByte() => unchecked((sbyte)ReadByte());

    public bool ReadBoolean() {
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

    public ushort ReadUInt16() {
        ushort value = CanonicalVarInt.ReadUInt16(_remaining, out int consumed);
        _remaining = _remaining[consumed..];
        return value;
    }

    /// <summary>Reads one UTF-16 code unit using canonical UInt16 encoding, including surrogates.</summary>
    public char ReadChar() => (char)ReadUInt16();

    public uint ReadUInt32() {
        uint value = CanonicalVarInt.ReadUInt32(_remaining, out int consumed);
        _remaining = _remaining[consumed..];
        return value;
    }

    public ulong ReadUInt64() {
        ulong value = CanonicalVarInt.ReadUInt64(_remaining, out int consumed);
        _remaining = _remaining[consumed..];
        return value;
    }

    public short ReadInt16() {
        short value = CanonicalVarInt.ReadInt16(_remaining, out int consumed);
        _remaining = _remaining[consumed..];
        return value;
    }

    public int ReadInt32() {
        int value = CanonicalVarInt.ReadInt32(_remaining, out int consumed);
        _remaining = _remaining[consumed..];
        return value;
    }

    public long ReadInt64() {
        long value = CanonicalVarInt.ReadInt64(_remaining, out int consumed);
        _remaining = _remaining[consumed..];
        return value;
    }

    public Half ReadHalf() =>
        BinaryPrimitives.ReadHalfLittleEndian(ReadSpan(sizeof(ushort)));

    public float ReadSingle() =>
        BinaryPrimitives.ReadSingleLittleEndian(ReadSpan(sizeof(float)));

    public double ReadDouble() =>
        BinaryPrimitives.ReadDoubleLittleEndian(ReadSpan(sizeof(double)));

    /// <summary>Reads all 128 Guid bits in big-endian order.</summary>
    public Guid ReadGuid() => new(ReadSpan(16), bigEndian: true);

    /// <summary>Reads the complete decimal representation, preserving scale and the sign of zero.</summary>
    public decimal ReadDecimal() {
        BinaryPayloadReader candidate = this;
        ReadOnlySpan<byte> source = candidate.ReadSpan(16);
        int lo = BinaryPrimitives.ReadInt32LittleEndian(source);
        int mid = BinaryPrimitives.ReadInt32LittleEndian(source[4..]);
        int hi = BinaryPrimitives.ReadInt32LittleEndian(source[8..]);
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(source[12..]);
        uint scale = (flags >> 16) & 0xff;
        if ((flags & 0x7f00ffffu) != 0 || scale > 28) {
            throw new InvalidDataException("Invalid decimal flags: reserved bits must be zero and scale must be 0 through 28.");
        }

        decimal value = new(lo, mid, hi, isNegative: (flags & 0x80000000u) != 0, scale: (byte)scale);
        this = candidate;
        return value;
    }

    /// <summary>Reads signed ticks using canonical Int64 ZigZag encoding.</summary>
    public TimeSpan ReadTimeSpan() => new(ReadInt64());

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

    /// <summary>Reads canonical non-null string content. Object identity belongs to the caller's loading context.</summary>
    public string ReadString() {
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
