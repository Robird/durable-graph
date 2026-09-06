using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.StateStore.Serialization.Tests;

public sealed class PreparedDeltaTests {
    [Fact]
    public void OwnsItsInputSliceAndDoesNotExposeWritableStorage() {
        byte[] source = [0xFF, 0x02, 0x16, 0xFF];
        PreparedDelta prepared = new(true, source.AsSpan(1, 2));
        Array.Clear(source);
        byte[] copy = prepared.Payload.ToArray();
        Array.Clear(copy);

        Assert.True(prepared.HasChanges);
        Assert.Equal<byte>([0x02, 0x16], prepared.Payload.ToArray());
        Assert.Equal(typeof(ReadOnlySpan<byte>), typeof(PreparedDelta).GetProperty(nameof(PreparedDelta.Payload))!.PropertyType);
        Assert.Null(typeof(PreparedDelta).GetProperty(nameof(PreparedDelta.HasChanges))!.SetMethod);
        Assert.Empty(typeof(PreparedDelta).GetFields());
    }

    [Fact]
    public void UnchangedDecisionDoesNotDependOnPayloadBeingEmpty() {
        PreparedDelta emptyLayout = new(false, []);
        PreparedDelta nineSlots = new(false, [0, 0]);

        Assert.False(emptyLayout.HasChanges);
        Assert.True(emptyLayout.Payload.IsEmpty);
        Assert.False(nineSlots.HasChanges);
        Assert.Equal<byte>([0, 0], nineSlots.Payload.ToArray());
    }
}
