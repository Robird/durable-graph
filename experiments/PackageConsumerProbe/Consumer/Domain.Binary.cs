using System.Buffers;
using Atelia.DurableGraph;
using Atelia.DurableGraph.StateStore.Serialization;

namespace PackageConsumerProbe;

[DurableType("package.body-base", 1, SchemaOnly = true, GenerateBinaryBody = true)]
public abstract partial class BinaryBase : DurableBase {
    [DurableField(7)] private int _count;
    [DurableField(1)] private bool _enabled;

    protected BinaryBase(bool enabled, int count) {
        _enabled = enabled;
        _count = count;
    }

    protected bool HasExpectedBase => _enabled && _count == -17;
}

[DurableType("package.body-leaf", 1, SchemaOnly = true, GenerateBinaryBody = true)]
public sealed partial class Character : BinaryBase {
    [DurableField(1)] private long _total;
    [Transient] private int _sentinel;

    private Character(bool enabled, int count, long total, int sentinel) : base(enabled, count) {
        _total = total;
        _sentinel = sentinel;
    }

    public static string ExerciseGeneratedSnapshots() {
        ScalarValues.Exercise();
        ReferenceCaptureExercise.Run();
        StringDecodingExercise.Run();
        ExercisePreparedDelta();
        Character source = new(true, -17, 42, 8);
        var captured = __DurableBinaryBody.Capture(source);
        source._total = 999;
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        __DurableBinaryBody.Write(ref writer, in captured);
        if (!buffer.WrittenSpan.SequenceEqual(new byte[] { 0x01, 0x21, 0x54 })) {
            throw new InvalidOperationException("Unexpected base-first binary body.");
        }

        // The result and prebuilt string helper arrive through the single runtime package reference.
        PreparedBase prepared = __DurableBinaryBody.PrepareBase(in captured);
        byte[] externalCopy = prepared.Payload.ToArray();
        Array.Clear(externalCopy);
        source._total = 1001;
        _ = __DurableBinaryBody.PrepareBase(in captured);
        if (!prepared.Payload.SequenceEqual(new byte[] { 0x01, 0x21, 0x54 }) ||
            !StringPayloadCodec.PrepareBase("A").Payload.SequenceEqual(new byte[] { 0x03, 0x41 }) ||
            !StringPayloadCodec.PrepareBase(string.Empty).Payload.SequenceEqual(new byte[] { 0x00 })) {
            throw new InvalidOperationException("Packaged Base preparation lost canonical bytes or frozen content.");
        }

        BinaryPayloadReader reader = new(buffer.WrittenSpan);
        var restored = __DurableBinaryBody.ReadV1(ref reader);
        reader.EnsureFullyConsumed();
        bool valid = restored.Segment0Field1 && restored.Segment0Field7 == -17 &&
            restored.Segment1Field1 == 42 && source._total == 1001 && source._sentinel == 8 &&
            source.HasExpectedBase && ReferenceEquals(__DurableBinaryBody.V1.Schema, Schema);
        return $"BinaryBody:{Convert.ToHexString(buffer.WrittenSpan)}:{valid}:ReferenceCapture:True:StringDecoding:True:PreparedDelta:True:PreparedBase:True";
    }
}

[DurableType("package.scalar-values", 1, SchemaOnly = true, GenerateBinaryBody = true)]
public sealed partial class ScalarValues : DurableBase {
    [DurableField(1)] private byte _byte = byte.MaxValue;
    [DurableField(2)] private sbyte _sbyte = sbyte.MinValue;
    [DurableField(3)] private short _short = -1;
    [DurableField(4)] private ushort _ushort = 128;
    [DurableField(5)] private uint _uint = 16384;
    [DurableField(6)] private ulong _ulong = ulong.MaxValue;
    [DurableField(7)] private char _char = '\uD800';
    [DurableField(8)] private Half _half = BitConverter.UInt16BitsToHalf(0x8000);
    [DurableField(9)] private float _single = BitConverter.UInt32BitsToSingle(0x7FC12345);
    [DurableField(10)] private double _double = BitConverter.UInt64BitsToDouble(0xFFF8123456789ABC);

    internal static void Exercise() {
        ScalarValues source = new();
        var captured = __DurableBinaryBody.Capture(source);
        source._half = (Half)1;
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        __DurableBinaryBody.Write(ref writer, in captured);
        const string golden = "FF80018001808001FFFFFFFFFFFFFFFFFF0180B00300804523C17FBC9A78563412F8FF";
        if (Convert.ToHexString(buffer.WrittenSpan) != golden) {
            throw new InvalidOperationException("Unexpected scalar DTO bytes.");
        }

        BinaryPayloadReader reader = new(buffer.WrittenSpan);
        var restored = __DurableBinaryBody.ReadV1(ref reader);
        reader.EnsureFullyConsumed();
        if (restored.Segment0Field1 != byte.MaxValue || restored.Segment0Field2 != sbyte.MinValue ||
            restored.Segment0Field3 != -1 || restored.Segment0Field4 != 128 ||
            restored.Segment0Field5 != 16384 || restored.Segment0Field6 != ulong.MaxValue ||
            restored.Segment0Field7 != '\uD800' ||
            BitConverter.HalfToUInt16Bits(restored.Segment0Field8) != 0x8000 ||
            BitConverter.SingleToUInt32Bits(restored.Segment0Field9) != 0x7FC12345 ||
            BitConverter.DoubleToUInt64Bits(restored.Segment0Field10) != 0xFFF8123456789ABC ||
            !ReferenceEquals(__DurableBinaryBody.V1.Schema, Schema)) {
            throw new InvalidOperationException("Packaged scalar DTO did not preserve field values and floating-point bits.");
        }
    }
}
