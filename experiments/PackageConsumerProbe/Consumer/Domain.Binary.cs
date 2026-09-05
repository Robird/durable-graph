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
        ArrayBufferWriter<byte> buffer = new();
        BinaryPayloadWriter writer = new(buffer);
        __DurableBinaryBody.Write(ref writer, source);
        if (!buffer.WrittenSpan.SequenceEqual(new byte[] { 0x01, 0x21, 0x54 })) {
            throw new InvalidOperationException("Unexpected base-first binary body.");
        }

        Character target = new(false, 0, 0, 91);
        BinaryPayloadReader reader = new(buffer.WrittenSpan);
        __DurableBinaryBody.Read(ref reader, target);
        reader.EnsureFullyConsumed();
        bool restored = target.HasExpectedBase && target._total == 42 && target._sentinel == 91;
        return $"BinaryBody:{Convert.ToHexString(buffer.WrittenSpan)}:{restored}";
    }
}
