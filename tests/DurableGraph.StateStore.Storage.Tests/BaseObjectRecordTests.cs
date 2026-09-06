namespace Atelia.DurableGraph.StateStore.Storage.Tests;

public sealed class BaseObjectRecordTests {
    [Fact]
    public void Record_copies_input_and_exposes_only_readonly_content() {
        byte[] input = [1, 2, 3, 4];
        BaseObjectRecord record = new(7, input.AsSpan(1, 2));
        input.AsSpan().Fill(99);
        byte[] outputCopy = record.Body.ToArray();
        outputCopy[0] = 88;

        Assert.Equal(7u, record.ObjectId);
        Assert.Equal([2, 3], record.Body.ToArray());
    }

    [Fact]
    public void Empty_body_and_maximum_id_are_valid_but_zero_id_is_not() {
        BaseObjectRecord record = new(uint.MaxValue, []);
        Assert.Equal(uint.MaxValue, record.ObjectId);
        Assert.True(record.Body.IsEmpty);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BaseObjectRecord(0, [1]));
    }
}
