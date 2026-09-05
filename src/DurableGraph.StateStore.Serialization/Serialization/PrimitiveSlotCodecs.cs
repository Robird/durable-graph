namespace Atelia.DurableGraph.StateStore.Serialization;

/// <summary>The fixed primitive bodies supported by the binary payload layer.</summary>
internal static class PrimitiveSlotCodecs {
    private static readonly Dictionary<Type, ValueSlotCodec> Codecs = new() {
        [typeof(bool)] = new ValueSlotCodec<bool>(
            static (ref BinaryPayloadReader reader, ref bool value) => value = reader.ReadBoolean(),
            static (ref BinaryPayloadWriter writer, ref bool value) => writer.WriteBoolean(value)),
        [typeof(byte)] = new ValueSlotCodec<byte>(
            static (ref BinaryPayloadReader reader, ref byte value) => value = reader.ReadByte(),
            static (ref BinaryPayloadWriter writer, ref byte value) => writer.WriteByte(value)),
        [typeof(sbyte)] = new ValueSlotCodec<sbyte>(
            static (ref BinaryPayloadReader reader, ref sbyte value) => value = reader.ReadSByte(),
            static (ref BinaryPayloadWriter writer, ref sbyte value) => writer.WriteSByte(value)),
        [typeof(short)] = new ValueSlotCodec<short>(
            static (ref BinaryPayloadReader reader, ref short value) => value = reader.ReadInt16(),
            static (ref BinaryPayloadWriter writer, ref short value) => writer.WriteInt16(value)),
        [typeof(ushort)] = new ValueSlotCodec<ushort>(
            static (ref BinaryPayloadReader reader, ref ushort value) => value = reader.ReadUInt16(),
            static (ref BinaryPayloadWriter writer, ref ushort value) => writer.WriteUInt16(value)),
        [typeof(int)] = new ValueSlotCodec<int>(
            static (ref BinaryPayloadReader reader, ref int value) => value = reader.ReadInt32(),
            static (ref BinaryPayloadWriter writer, ref int value) => writer.WriteInt32(value)),
        [typeof(uint)] = new ValueSlotCodec<uint>(
            static (ref BinaryPayloadReader reader, ref uint value) => value = reader.ReadUInt32(),
            static (ref BinaryPayloadWriter writer, ref uint value) => writer.WriteUInt32(value)),
        [typeof(long)] = new ValueSlotCodec<long>(
            static (ref BinaryPayloadReader reader, ref long value) => value = reader.ReadInt64(),
            static (ref BinaryPayloadWriter writer, ref long value) => writer.WriteInt64(value)),
        [typeof(ulong)] = new ValueSlotCodec<ulong>(
            static (ref BinaryPayloadReader reader, ref ulong value) => value = reader.ReadUInt64(),
            static (ref BinaryPayloadWriter writer, ref ulong value) => writer.WriteUInt64(value)),
        [typeof(char)] = new ValueSlotCodec<char>(
            static (ref BinaryPayloadReader reader, ref char value) => value = (char)reader.ReadUInt16(),
            static (ref BinaryPayloadWriter writer, ref char value) => writer.WriteUInt16(value)),
        [typeof(Half)] = new ValueSlotCodec<Half>(
            static (ref BinaryPayloadReader reader, ref Half value) => value = reader.ReadHalf(),
            static (ref BinaryPayloadWriter writer, ref Half value) => writer.WriteHalf(value)),
        [typeof(float)] = new ValueSlotCodec<float>(
            static (ref BinaryPayloadReader reader, ref float value) => value = reader.ReadSingle(),
            static (ref BinaryPayloadWriter writer, ref float value) => writer.WriteSingle(value)),
        [typeof(double)] = new ValueSlotCodec<double>(
            static (ref BinaryPayloadReader reader, ref double value) => value = reader.ReadDouble(),
            static (ref BinaryPayloadWriter writer, ref double value) => writer.WriteDouble(value)),
    };

    internal static ValueSlotCodec Get(Type type) {
        ArgumentNullException.ThrowIfNull(type);
        return Codecs.TryGetValue(type, out ValueSlotCodec? codec)
            ? codec
            : throw new NotSupportedException($"No primitive slot codec is defined for '{type}'.");
    }

    internal static ValueSlotCodec<T> Get<T>() where T : struct => (ValueSlotCodec<T>)Get(typeof(T));
}
