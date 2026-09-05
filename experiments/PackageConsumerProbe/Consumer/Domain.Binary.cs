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
        Character source = new(true, -17, 42, 8);
        var captured = __DurableBinaryBody.Capture(source);
        source._total = 999;
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        __DurableBinaryBody.Write(ref writer, in captured);
        if (!buffer.WrittenSpan.SequenceEqual(new byte[] { 0x01, 0x21, 0x54 })) {
            throw new InvalidOperationException("Unexpected base-first binary body.");
        }

        BinaryPayloadReader reader = new(buffer.WrittenSpan);
        var restored = __DurableBinaryBody.ReadV1(ref reader);
        reader.EnsureFullyConsumed();
        bool valid = restored.Segment0Field1 && restored.Segment0Field7 == -17 &&
            restored.Segment1Field1 == 42 && source._total == 999 && source._sentinel == 8 &&
            source.HasExpectedBase && ReferenceEquals(__DurableBinaryBody.V1.Schema, Schema);
        return $"BinaryBody:{Convert.ToHexString(buffer.WrittenSpan)}:{valid}";
    }
}
