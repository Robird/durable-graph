using Atelia.DurableGraph.Serialization;

namespace Atelia.DurableGraph.Serialization.Tests;

public sealed class PreparedDeltaBodyTests {
    [Fact]
    public void OwnsItsInputSliceAndDoesNotExposeWritableStorage() {
        byte[] source = [0xFF, 0x02, 0x16, 0xFF];
        PreparedDeltaBody prepared = new(true, source.AsSpan(1, 2));
        Array.Clear(source);
        byte[] copy = prepared.Body.ToArray();
        Array.Clear(copy);

        Assert.True(prepared.HasChanges);
        Assert.Equal<byte>([0x02, 0x16], prepared.Body.ToArray());
        Assert.Equal(typeof(ReadOnlySpan<byte>), typeof(PreparedDeltaBody).GetProperty(nameof(PreparedDeltaBody.Body))!.PropertyType);
        Assert.Null(typeof(PreparedDeltaBody).GetProperty(nameof(PreparedDeltaBody.HasChanges))!.SetMethod);
        Assert.Empty(typeof(PreparedDeltaBody).GetFields());
    }

    [Fact]
    public void UnchangedDecisionDoesNotDependOnBodyBeingEmpty() {
        PreparedDeltaBody emptyLayout = new(false, []);
        PreparedDeltaBody nineSlots = new(false, [0, 0]);

        Assert.False(emptyLayout.HasChanges);
        Assert.True(emptyLayout.Body.IsEmpty);
        Assert.False(nineSlots.HasChanges);
        Assert.Equal<byte>([0, 0], nineSlots.Body.ToArray());
    }
}
