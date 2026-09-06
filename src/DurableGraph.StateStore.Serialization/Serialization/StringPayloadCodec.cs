using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace Atelia.DurableGraph.StateStore.Serialization;

/// <summary>
/// Encodes value-semantic strings using the shorter of strict UTF-8 and UTF-16LE.
/// UTF-16LE wins ties and preserves unpaired UTF-16 surrogates.
/// </summary>
public static class StringPayloadCodec {
    private const uint Utf8FlagMask = 1;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    /// <summary>Prepares owned canonical non-null string content; reference slots encode ObjectId separately.</summary>
    public static PreparedBase PrepareBase(string value) {
        ArgumentNullException.ThrowIfNull(value);
        ArrayBufferWriter<byte> buffer = new();
        Write(buffer, value);
        return new PreparedBase(buffer.WrittenSpan);
    }

    /// <summary>
    /// Writes a canonical raw header followed by the selected string payload.
    /// An even header is the UTF-16LE byte count; an odd header contains the UTF-8 byte count shifted left by one.
    /// </summary>
    internal static void Write(IBufferWriter<byte> downstream, string value) {
        ArgumentNullException.ThrowIfNull(downstream);
        ArgumentNullException.ThrowIfNull(value);

        EncodingChoice choice = ChooseEncoding(value);
        CanonicalVarInt.WriteUInt32(downstream, choice.RawHeader);
        WritePayload(downstream, value, choice);
    }

    /// <summary>
    /// Writes zero for null; otherwise writes the canonical raw string header plus one, followed by its payload.
    /// </summary>
    internal static void WriteNullable(IBufferWriter<byte> downstream, string? value) {
        ArgumentNullException.ThrowIfNull(downstream);

        if (value is null) {
            CanonicalVarInt.WriteUInt32(downstream, NullablePayloadHeader.EncodeNull());
            return;
        }

        EncodingChoice choice = ChooseEncoding(value);
        CanonicalVarInt.WriteUInt32(downstream, NullablePayloadHeader.EncodePresent(choice.RawHeader));
        WritePayload(downstream, value, choice);
    }

    internal static string Read(ref BinaryPayloadReader reader) {
        uint rawHeader = reader.ReadUInt32();
        return ReadRaw(ref reader, rawHeader);
    }

    internal static string? ReadNullable(ref BinaryPayloadReader reader) {
        uint encodedHeader = reader.ReadUInt32();
        if (!NullablePayloadHeader.TryDecode(encodedHeader, out uint rawHeader)) { return null; }
        return ReadRaw(ref reader, rawHeader);
    }

    private static string ReadRaw(ref BinaryPayloadReader reader, uint rawHeader) {
        bool isUtf8 = (rawHeader & Utf8FlagMask) != 0;
        uint payloadByteCount = isUtf8 ? rawHeader >> 1 : rawHeader;
        if (payloadByteCount > int.MaxValue) {
            throw new InvalidDataException($"String payload byte count {payloadByteCount} exceeds Int32.MaxValue.");
        }

        int byteCount = (int)payloadByteCount;
        if (byteCount > reader.RemainingCount) {
            throw new InvalidDataException(
                $"String payload declares {byteCount} byte(s), but only {reader.RemainingCount} byte(s) remain."
            );
        }

        ReadOnlySpan<byte> payload = reader.ReadSpan(byteCount);
        string value = isUtf8 ? ReadUtf8(payload) : ReadUtf16Le(payload);
        EnsureCanonicalEncoding(value, isUtf8, byteCount);
        return value;
    }

    private static EncodingChoice ChooseEncoding(string value) {
        long utf16ByteCount = (long)value.Length * sizeof(char);
        int utf8ByteCount;

        try {
            utf8ByteCount = StrictUtf8.GetByteCount(value);
        } catch (EncoderFallbackException) {
            return ChooseUtf16(utf16ByteCount);
        } catch (OverflowException) {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value.Length,
                "String payload exceeds the supported encoded length.");
        }

        return utf8ByteCount < utf16ByteCount
            ? EncodingChoice.Utf8(utf8ByteCount)
            : ChooseUtf16(utf16ByteCount);
    }

    private static EncodingChoice ChooseUtf16(long byteCount) {
        if (byteCount > int.MaxValue) {
            throw new ArgumentOutOfRangeException(
                "value",
                byteCount / sizeof(char),
                "String payload exceeds the supported encoded length.");
        }

        return EncodingChoice.Utf16((int)byteCount);
    }

    private static void WritePayload(IBufferWriter<byte> downstream, string value, EncodingChoice choice) {
        if (choice.ByteCount == 0) { return; }

        Span<byte> destination = downstream.GetSpan(choice.ByteCount)[..choice.ByteCount];
        if (choice.IsUtf8) {
            int written = StrictUtf8.GetBytes(value.AsSpan(), destination);
            if (written != choice.ByteCount) {
                throw new InvalidOperationException("UTF-8 byte count changed between sizing and encoding.");
            }
        } else if (BitConverter.IsLittleEndian) {
            MemoryMarshal.AsBytes(value.AsSpan()).CopyTo(destination);
        } else {
            for (int i = 0; i < value.Length; i++) {
                BinaryPrimitives.WriteUInt16LittleEndian(destination[(i * sizeof(char))..], value[i]);
            }
        }

        downstream.Advance(choice.ByteCount);
    }

    private static string ReadUtf8(ReadOnlySpan<byte> payload) {
        try {
            return StrictUtf8.GetString(payload);
        } catch (DecoderFallbackException ex) {
            throw new InvalidDataException("String payload contains invalid UTF-8.", ex);
        } catch (OverflowException ex) {
            throw new InvalidDataException("UTF-8 string payload exceeds the supported decoded length.", ex);
        }
    }

    private static string ReadUtf16Le(ReadOnlySpan<byte> payload) {
        if ((payload.Length & 1) != 0) {
            throw new InvalidDataException("UTF-16LE string payload has an odd byte count.");
        }
        if (payload.IsEmpty) { return string.Empty; }
        if (BitConverter.IsLittleEndian) {
            return new string(MemoryMarshal.Cast<byte, char>(payload));
        }

        byte[] bytes = payload.ToArray();
        return string.Create(bytes.Length / sizeof(char), bytes, static (destination, source) => {
            for (int i = 0; i < destination.Length; i++) {
                destination[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(i * sizeof(char)));
            }
        });
    }

    private static void EnsureCanonicalEncoding(string value, bool encodedAsUtf8, int encodedByteCount) {
        long utf16ByteCount = (long)value.Length * sizeof(char);
        int utf8ByteCount;

        try {
            utf8ByteCount = StrictUtf8.GetByteCount(value);
        } catch (EncoderFallbackException) {
            if (encodedAsUtf8) {
                throw new InvalidDataException("A UTF-8 payload decoded to a value that cannot be encoded as strict UTF-8.");
            }
            return;
        } catch (OverflowException ex) {
            throw new InvalidDataException("Decoded string exceeds the supported encoded length.", ex);
        }

        bool shouldUseUtf8 = utf8ByteCount < utf16ByteCount;
        long expectedByteCount = shouldUseUtf8 ? utf8ByteCount : utf16ByteCount;
        if (encodedAsUtf8 != shouldUseUtf8 || encodedByteCount != expectedByteCount) {
            string expectedEncoding = shouldUseUtf8 ? "UTF-8" : "UTF-16LE";
            throw new InvalidDataException(
                $"String payload is not canonical; value must use {expectedEncoding} with {expectedByteCount} byte(s)."
            );
        }
    }

    private readonly record struct EncodingChoice(bool IsUtf8, int ByteCount, uint RawHeader) {
        internal static EncodingChoice Utf8(int byteCount) {
            uint rawHeader = checked(((uint)byteCount * 2u) | Utf8FlagMask);
            return new EncodingChoice(true, byteCount, rawHeader);
        }

        internal static EncodingChoice Utf16(int byteCount) {
            return new EncodingChoice(false, byteCount, checked((uint)byteCount));
        }
    }
}
