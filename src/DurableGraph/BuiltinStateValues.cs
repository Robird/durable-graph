using System.Buffers;
using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph;

// Only the cold binding path inspects CLR types. Generated member loops call typed static operations.
internal static class BuiltinStateValues {
    internal static bool TryBindCurrent(Type domainType, out StateValueBinding binding) {
        if (domainType == typeof(bool)) {
            binding = new(new(1, TypeTag.Boolean), typeof(bool), typeof(BooleanStateOps), domainType, typeof(IdentityValueProjection<bool>));
            return true;
        }
        if (domainType == typeof(byte)) {
            binding = new(new(1, TypeTag.Byte), typeof(byte), typeof(ByteStateOps), domainType, typeof(IdentityValueProjection<byte>));
            return true;
        }
        if (domainType == typeof(sbyte)) {
            binding = new(new(1, TypeTag.SByte), typeof(sbyte), typeof(SByteStateOps), domainType, typeof(IdentityValueProjection<sbyte>));
            return true;
        }
        if (domainType == typeof(short)) {
            binding = new(new(1, TypeTag.Int16), typeof(short), typeof(Int16StateOps), domainType, typeof(IdentityValueProjection<short>));
            return true;
        }
        if (domainType == typeof(ushort)) {
            binding = new(new(1, TypeTag.UInt16), typeof(ushort), typeof(UInt16StateOps), domainType, typeof(IdentityValueProjection<ushort>));
            return true;
        }
        if (domainType == typeof(int)) {
            binding = new(new(1, TypeTag.Int32), typeof(int), typeof(Int32StateOps), domainType, typeof(IdentityValueProjection<int>));
            return true;
        }
        if (domainType == typeof(uint)) {
            binding = new(new(1, TypeTag.UInt32), typeof(uint), typeof(UInt32StateOps), domainType, typeof(IdentityValueProjection<uint>));
            return true;
        }
        if (domainType == typeof(long)) {
            binding = new(new(1, TypeTag.Int64), typeof(long), typeof(Int64StateOps), domainType, typeof(IdentityValueProjection<long>));
            return true;
        }
        if (domainType == typeof(ulong)) {
            binding = new(new(1, TypeTag.UInt64), typeof(ulong), typeof(UInt64StateOps), domainType, typeof(IdentityValueProjection<ulong>));
            return true;
        }
        if (domainType == typeof(char)) {
            binding = new(new(1, TypeTag.Char), typeof(char), typeof(CharStateOps), domainType, typeof(IdentityValueProjection<char>));
            return true;
        }
        if (domainType == typeof(Half)) {
            binding = new(new(1, TypeTag.Half), typeof(Half), typeof(HalfStateOps), domainType, typeof(IdentityValueProjection<Half>));
            return true;
        }
        if (domainType == typeof(float)) {
            binding = new(new(1, TypeTag.Single), typeof(float), typeof(SingleStateOps), domainType, typeof(IdentityValueProjection<float>));
            return true;
        }
        if (domainType == typeof(double)) {
            binding = new(new(1, TypeTag.Double), typeof(double), typeof(DoubleStateOps), domainType, typeof(IdentityValueProjection<double>));
            return true;
        }
        if (domainType == typeof(string)) {
            binding = new(new(1, TypeTag.String), typeof(ObjectId), typeof(StringIdStateOps), domainType, typeof(StringValueProjection));
            return true;
        }
        binding = null!;
        return false;
    }

    internal static bool TryBindStored(DurableFieldInfo slot, out StateValueBinding binding) {
        binding = slot.TypeTag switch {
            TypeTag.Boolean => new(slot, typeof(bool), typeof(BooleanStateOps)),
            TypeTag.Byte => new(slot, typeof(byte), typeof(ByteStateOps)),
            TypeTag.SByte => new(slot, typeof(sbyte), typeof(SByteStateOps)),
            TypeTag.Int16 => new(slot, typeof(short), typeof(Int16StateOps)),
            TypeTag.UInt16 => new(slot, typeof(ushort), typeof(UInt16StateOps)),
            TypeTag.Int32 => new(slot, typeof(int), typeof(Int32StateOps)),
            TypeTag.UInt32 => new(slot, typeof(uint), typeof(UInt32StateOps)),
            TypeTag.Int64 => new(slot, typeof(long), typeof(Int64StateOps)),
            TypeTag.UInt64 => new(slot, typeof(ulong), typeof(UInt64StateOps)),
            TypeTag.Char => new(slot, typeof(char), typeof(CharStateOps)),
            TypeTag.Half => new(slot, typeof(Half), typeof(HalfStateOps)),
            TypeTag.Single => new(slot, typeof(float), typeof(SingleStateOps)),
            TypeTag.Double => new(slot, typeof(double), typeof(DoubleStateOps)),
            TypeTag.String => new(slot, typeof(ObjectId), typeof(StringIdStateOps)),
            TypeTag.DurableReference => new(slot, typeof(ObjectId), typeof(DurableIdStateOps)),
            _ => null!,
        };
        return binding is not null;
    }
}

/// <summary>Copies a supported scalar between its domain and frozen representation.</summary>
public readonly struct IdentityValueProjection<T> : IValueProjection<T, T> where T : unmanaged {
    public static T Capture(in T value, CaptureContext context, DurableFieldInfo slot) => value;
    public static void Hydrate(ref T target, in T state, ObjectReadTable objects, DurableFieldInfo slot) => target = state;
}

/// <summary>Projects string references through the capture and restore identity tables.</summary>
public readonly struct StringValueProjection : IValueProjection<string?, ObjectId> {
    public static ObjectId Capture(in string? value, CaptureContext context, DurableFieldInfo slot) => context.CaptureString(value);
    public static void Hydrate(ref string? target, in ObjectId state, ObjectReadTable objects, DurableFieldInfo slot) => target = objects.ResolveString(state);
}

/// <summary>Projects a durable reference without expanding the target object's body.</summary>
public readonly struct DurableValueProjection<TDomain> : IValueProjection<TDomain?, ObjectId> where TDomain : DurableBase {
    public static ObjectId Capture(in TDomain? value, CaptureContext context, DurableFieldInfo slot) => context.CaptureDurable(value, slot.TargetType!);
    public static void Hydrate(ref TDomain? target, in ObjectId state, ObjectReadTable objects, DurableFieldInfo slot) => target = objects.ResolveDurable<TDomain>(state);
}

internal static class ScalarDelta {
    internal static PreparedDeltaBody Prepare<TState, TOps>(in TState current, DurableFieldInfo slot, bool equal)
        where TState : unmanaged where TOps : IStateOps<TState> {
        if (equal) { return new(false, []); }
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        TOps.WriteBase(ref writer, in current, slot);
        return new(true, buffer.WrittenSpan);
    }

    internal static void RequireChange(bool equal) {
        if (equal) { throw new InvalidDataException("A changed scalar slot cannot repeat its prior value."); }
    }
}

/// <summary>Static Boolean operations used by an unresolved generic value slot.</summary>
public readonly struct BooleanStateOps : IStateOps<bool> {
    public static void WriteBase(ref BinaryPayloadWriter writer, in bool state, DurableFieldInfo slot) => writer.WriteBoolean(state);
    public static bool ReadBase(ref BinaryPayloadReader reader, DurableFieldInfo slot) => reader.ReadBoolean();
    public static PreparedDeltaBody PrepareDelta(in bool prior, in bool current, DurableFieldInfo slot) =>
        ScalarDelta.Prepare<bool, BooleanStateOps>(in current, slot, prior == current);
    public static bool ApplyDelta(ref BinaryPayloadReader reader, in bool prior, DurableFieldInfo slot) {
        bool current = ReadBase(ref reader, slot);
        ScalarDelta.RequireChange(prior == current);
        return current;
    }
    public static void VisitReferences(in bool state, IStateReferenceVisitor visitor, DurableFieldInfo slot) { }
}

/// <summary>Static Byte operations used by an unresolved generic value slot.</summary>
public readonly struct ByteStateOps : IStateOps<byte> {
    public static void WriteBase(ref BinaryPayloadWriter writer, in byte state, DurableFieldInfo slot) => writer.WriteByte(state);
    public static byte ReadBase(ref BinaryPayloadReader reader, DurableFieldInfo slot) => reader.ReadByte();
    public static PreparedDeltaBody PrepareDelta(in byte prior, in byte current, DurableFieldInfo slot) =>
        ScalarDelta.Prepare<byte, ByteStateOps>(in current, slot, prior == current);
    public static byte ApplyDelta(ref BinaryPayloadReader reader, in byte prior, DurableFieldInfo slot) {
        byte current = ReadBase(ref reader, slot);
        ScalarDelta.RequireChange(prior == current);
        return current;
    }
    public static void VisitReferences(in byte state, IStateReferenceVisitor visitor, DurableFieldInfo slot) { }
}

/// <summary>Static SByte operations used by an unresolved generic value slot.</summary>
public readonly struct SByteStateOps : IStateOps<sbyte> {
    public static void WriteBase(ref BinaryPayloadWriter writer, in sbyte state, DurableFieldInfo slot) => writer.WriteSByte(state);
    public static sbyte ReadBase(ref BinaryPayloadReader reader, DurableFieldInfo slot) => reader.ReadSByte();
    public static PreparedDeltaBody PrepareDelta(in sbyte prior, in sbyte current, DurableFieldInfo slot) =>
        ScalarDelta.Prepare<sbyte, SByteStateOps>(in current, slot, prior == current);
    public static sbyte ApplyDelta(ref BinaryPayloadReader reader, in sbyte prior, DurableFieldInfo slot) {
        sbyte current = ReadBase(ref reader, slot);
        ScalarDelta.RequireChange(prior == current);
        return current;
    }
    public static void VisitReferences(in sbyte state, IStateReferenceVisitor visitor, DurableFieldInfo slot) { }
}

/// <summary>Static Int16 operations used by an unresolved generic value slot.</summary>
public readonly struct Int16StateOps : IStateOps<short> {
    public static void WriteBase(ref BinaryPayloadWriter writer, in short state, DurableFieldInfo slot) => writer.WriteInt16(state);
    public static short ReadBase(ref BinaryPayloadReader reader, DurableFieldInfo slot) => reader.ReadInt16();
    public static PreparedDeltaBody PrepareDelta(in short prior, in short current, DurableFieldInfo slot) =>
        ScalarDelta.Prepare<short, Int16StateOps>(in current, slot, prior == current);
    public static short ApplyDelta(ref BinaryPayloadReader reader, in short prior, DurableFieldInfo slot) {
        short current = ReadBase(ref reader, slot);
        ScalarDelta.RequireChange(prior == current);
        return current;
    }
    public static void VisitReferences(in short state, IStateReferenceVisitor visitor, DurableFieldInfo slot) { }
}

/// <summary>Static UInt16 operations used by an unresolved generic value slot.</summary>
public readonly struct UInt16StateOps : IStateOps<ushort> {
    public static void WriteBase(ref BinaryPayloadWriter writer, in ushort state, DurableFieldInfo slot) => writer.WriteUInt16(state);
    public static ushort ReadBase(ref BinaryPayloadReader reader, DurableFieldInfo slot) => reader.ReadUInt16();
    public static PreparedDeltaBody PrepareDelta(in ushort prior, in ushort current, DurableFieldInfo slot) =>
        ScalarDelta.Prepare<ushort, UInt16StateOps>(in current, slot, prior == current);
    public static ushort ApplyDelta(ref BinaryPayloadReader reader, in ushort prior, DurableFieldInfo slot) {
        ushort current = ReadBase(ref reader, slot);
        ScalarDelta.RequireChange(prior == current);
        return current;
    }
    public static void VisitReferences(in ushort state, IStateReferenceVisitor visitor, DurableFieldInfo slot) { }
}

/// <summary>Static Int32 operations used by an unresolved generic value slot.</summary>
public readonly struct Int32StateOps : IStateOps<int> {
    public static void WriteBase(ref BinaryPayloadWriter writer, in int state, DurableFieldInfo slot) => writer.WriteInt32(state);
    public static int ReadBase(ref BinaryPayloadReader reader, DurableFieldInfo slot) => reader.ReadInt32();
    public static PreparedDeltaBody PrepareDelta(in int prior, in int current, DurableFieldInfo slot) =>
        ScalarDelta.Prepare<int, Int32StateOps>(in current, slot, prior == current);
    public static int ApplyDelta(ref BinaryPayloadReader reader, in int prior, DurableFieldInfo slot) {
        int current = ReadBase(ref reader, slot);
        ScalarDelta.RequireChange(prior == current);
        return current;
    }
    public static void VisitReferences(in int state, IStateReferenceVisitor visitor, DurableFieldInfo slot) { }
}

/// <summary>Static UInt32 operations used by an unresolved generic value slot.</summary>
public readonly struct UInt32StateOps : IStateOps<uint> {
    public static void WriteBase(ref BinaryPayloadWriter writer, in uint state, DurableFieldInfo slot) => writer.WriteUInt32(state);
    public static uint ReadBase(ref BinaryPayloadReader reader, DurableFieldInfo slot) => reader.ReadUInt32();
    public static PreparedDeltaBody PrepareDelta(in uint prior, in uint current, DurableFieldInfo slot) =>
        ScalarDelta.Prepare<uint, UInt32StateOps>(in current, slot, prior == current);
    public static uint ApplyDelta(ref BinaryPayloadReader reader, in uint prior, DurableFieldInfo slot) {
        uint current = ReadBase(ref reader, slot);
        ScalarDelta.RequireChange(prior == current);
        return current;
    }
    public static void VisitReferences(in uint state, IStateReferenceVisitor visitor, DurableFieldInfo slot) { }
}

/// <summary>Static Int64 operations used by an unresolved generic value slot.</summary>
public readonly struct Int64StateOps : IStateOps<long> {
    public static void WriteBase(ref BinaryPayloadWriter writer, in long state, DurableFieldInfo slot) => writer.WriteInt64(state);
    public static long ReadBase(ref BinaryPayloadReader reader, DurableFieldInfo slot) => reader.ReadInt64();
    public static PreparedDeltaBody PrepareDelta(in long prior, in long current, DurableFieldInfo slot) =>
        ScalarDelta.Prepare<long, Int64StateOps>(in current, slot, prior == current);
    public static long ApplyDelta(ref BinaryPayloadReader reader, in long prior, DurableFieldInfo slot) {
        long current = ReadBase(ref reader, slot);
        ScalarDelta.RequireChange(prior == current);
        return current;
    }
    public static void VisitReferences(in long state, IStateReferenceVisitor visitor, DurableFieldInfo slot) { }
}

/// <summary>Static UInt64 operations used by an unresolved generic value slot.</summary>
public readonly struct UInt64StateOps : IStateOps<ulong> {
    public static void WriteBase(ref BinaryPayloadWriter writer, in ulong state, DurableFieldInfo slot) => writer.WriteUInt64(state);
    public static ulong ReadBase(ref BinaryPayloadReader reader, DurableFieldInfo slot) => reader.ReadUInt64();
    public static PreparedDeltaBody PrepareDelta(in ulong prior, in ulong current, DurableFieldInfo slot) =>
        ScalarDelta.Prepare<ulong, UInt64StateOps>(in current, slot, prior == current);
    public static ulong ApplyDelta(ref BinaryPayloadReader reader, in ulong prior, DurableFieldInfo slot) {
        ulong current = ReadBase(ref reader, slot);
        ScalarDelta.RequireChange(prior == current);
        return current;
    }
    public static void VisitReferences(in ulong state, IStateReferenceVisitor visitor, DurableFieldInfo slot) { }
}

/// <summary>Static Char operations used by an unresolved generic value slot.</summary>
public readonly struct CharStateOps : IStateOps<char> {
    public static void WriteBase(ref BinaryPayloadWriter writer, in char state, DurableFieldInfo slot) => writer.WriteChar(state);
    public static char ReadBase(ref BinaryPayloadReader reader, DurableFieldInfo slot) => reader.ReadChar();
    public static PreparedDeltaBody PrepareDelta(in char prior, in char current, DurableFieldInfo slot) =>
        ScalarDelta.Prepare<char, CharStateOps>(in current, slot, prior == current);
    public static char ApplyDelta(ref BinaryPayloadReader reader, in char prior, DurableFieldInfo slot) {
        char current = ReadBase(ref reader, slot);
        ScalarDelta.RequireChange(prior == current);
        return current;
    }
    public static void VisitReferences(in char state, IStateReferenceVisitor visitor, DurableFieldInfo slot) { }
}

/// <summary>Static Half operations used by an unresolved generic value slot.</summary>
public readonly struct HalfStateOps : IStateOps<Half> {
    public static void WriteBase(ref BinaryPayloadWriter writer, in Half state, DurableFieldInfo slot) => writer.WriteHalf(state);
    public static Half ReadBase(ref BinaryPayloadReader reader, DurableFieldInfo slot) => reader.ReadHalf();
    public static PreparedDeltaBody PrepareDelta(in Half prior, in Half current, DurableFieldInfo slot) =>
        ScalarDelta.Prepare<Half, HalfStateOps>(in current, slot, BitConverter.HalfToUInt16Bits(prior) == BitConverter.HalfToUInt16Bits(current));
    public static Half ApplyDelta(ref BinaryPayloadReader reader, in Half prior, DurableFieldInfo slot) {
        Half current = ReadBase(ref reader, slot);
        ScalarDelta.RequireChange(BitConverter.HalfToUInt16Bits(prior) == BitConverter.HalfToUInt16Bits(current));
        return current;
    }
    public static void VisitReferences(in Half state, IStateReferenceVisitor visitor, DurableFieldInfo slot) { }
}

/// <summary>Static Single operations used by an unresolved generic value slot.</summary>
public readonly struct SingleStateOps : IStateOps<float> {
    public static void WriteBase(ref BinaryPayloadWriter writer, in float state, DurableFieldInfo slot) => writer.WriteSingle(state);
    public static float ReadBase(ref BinaryPayloadReader reader, DurableFieldInfo slot) => reader.ReadSingle();
    public static PreparedDeltaBody PrepareDelta(in float prior, in float current, DurableFieldInfo slot) =>
        ScalarDelta.Prepare<float, SingleStateOps>(in current, slot, BitConverter.SingleToInt32Bits(prior) == BitConverter.SingleToInt32Bits(current));
    public static float ApplyDelta(ref BinaryPayloadReader reader, in float prior, DurableFieldInfo slot) {
        float current = ReadBase(ref reader, slot);
        ScalarDelta.RequireChange(BitConverter.SingleToInt32Bits(prior) == BitConverter.SingleToInt32Bits(current));
        return current;
    }
    public static void VisitReferences(in float state, IStateReferenceVisitor visitor, DurableFieldInfo slot) { }
}

/// <summary>Static Double operations used by an unresolved generic value slot.</summary>
public readonly struct DoubleStateOps : IStateOps<double> {
    public static void WriteBase(ref BinaryPayloadWriter writer, in double state, DurableFieldInfo slot) => writer.WriteDouble(state);
    public static double ReadBase(ref BinaryPayloadReader reader, DurableFieldInfo slot) => reader.ReadDouble();
    public static PreparedDeltaBody PrepareDelta(in double prior, in double current, DurableFieldInfo slot) =>
        ScalarDelta.Prepare<double, DoubleStateOps>(in current, slot, BitConverter.DoubleToInt64Bits(prior) == BitConverter.DoubleToInt64Bits(current));
    public static double ApplyDelta(ref BinaryPayloadReader reader, in double prior, DurableFieldInfo slot) {
        double current = ReadBase(ref reader, slot);
        ScalarDelta.RequireChange(BitConverter.DoubleToInt64Bits(prior) == BitConverter.DoubleToInt64Bits(current));
        return current;
    }
    public static void VisitReferences(in double state, IStateReferenceVisitor visitor, DurableFieldInfo slot) { }
}

/// <summary>Object ID slots retain their string semantics.</summary>
public readonly struct StringIdStateOps : IStateOps<ObjectId> {
    public static void WriteBase(ref BinaryPayloadWriter writer, in ObjectId state, DurableFieldInfo slot) => writer.WriteUInt32(state.Value);
    public static ObjectId ReadBase(ref BinaryPayloadReader reader, DurableFieldInfo slot) => new(reader.ReadUInt32());
    public static PreparedDeltaBody PrepareDelta(in ObjectId prior, in ObjectId current, DurableFieldInfo slot) =>
        ScalarDelta.Prepare<ObjectId, StringIdStateOps>(in current, slot, prior == current);
    public static ObjectId ApplyDelta(ref BinaryPayloadReader reader, in ObjectId prior, DurableFieldInfo slot) {
        ObjectId current = ReadBase(ref reader, slot);
        ScalarDelta.RequireChange(prior == current);
        return current;
    }
    public static void VisitReferences(in ObjectId state, IStateReferenceVisitor visitor, DurableFieldInfo slot) => visitor.VisitString(state);
}

/// <summary>Object ID slots retain their constructed nominal semantics.</summary>
public readonly struct DurableIdStateOps : IStateOps<ObjectId> {
    public static void WriteBase(ref BinaryPayloadWriter writer, in ObjectId state, DurableFieldInfo slot) => writer.WriteUInt32(state.Value);
    public static ObjectId ReadBase(ref BinaryPayloadReader reader, DurableFieldInfo slot) => new(reader.ReadUInt32());
    public static PreparedDeltaBody PrepareDelta(in ObjectId prior, in ObjectId current, DurableFieldInfo slot) =>
        StringIdStateOps.PrepareDelta(in prior, in current, slot);
    public static ObjectId ApplyDelta(ref BinaryPayloadReader reader, in ObjectId prior, DurableFieldInfo slot) =>
        StringIdStateOps.ApplyDelta(ref reader, in prior, slot);
    public static void VisitReferences(in ObjectId state, IStateReferenceVisitor visitor, DurableFieldInfo slot) => visitor.VisitDurable(state, slot.TargetType!);
}
