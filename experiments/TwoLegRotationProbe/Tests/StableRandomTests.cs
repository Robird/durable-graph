using Atelia.TwoLegRotationProbe.Workloads.Generation;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class StableRandomTests {
    [Fact]
    public void Fork_matches_the_fixed_golden_vector() {
        RandomStream stream = new StableRandom(0x0123456789ABCDEF).Fork(
            RandomDomain.ObjectUpdate,
            stepIndex: 17,
            objectId: 42,
            lane: 3);

        ulong[] actual = [
            stream.NextUInt64(),
            stream.NextUInt64(),
            stream.NextUInt64(),
            stream.NextUInt64(),
        ];

        Assert.Equal(new ulong[] {
            0xC25EB298C9C92B1D,
            0x0CBD7892DF68F6C4,
            0xE152C9FD20D62927,
            0x7F4C9BA064649338,
        }, actual);
    }

    [Fact]
    public void Same_seed_and_key_reproduce_the_same_stream() {
        StableRandom root = new(123);
        RandomStream first = root.Fork(RandomDomain.ObjectCreate, 8, 55, 2);
        RandomStream second = root.Fork(RandomDomain.ObjectCreate, 8, 55, 2);

        Assert.Equal(first.NextUInt64(), second.NextUInt64());
        Assert.Equal(first.NextUInt64(), second.NextUInt64());
    }

    [Fact]
    public void Each_key_component_isolated_the_derived_stream() {
        StableRandom root = new(123);
        ulong baseline = root.Fork(RandomDomain.Lifecycle, 8, 55, 2).NextUInt64();

        Assert.NotEqual(baseline, root.Fork(RandomDomain.ObjectCreate, 8, 55, 2).NextUInt64());
        Assert.NotEqual(baseline, root.Fork(RandomDomain.Lifecycle, 9, 55, 2).NextUInt64());
        Assert.NotEqual(baseline, root.Fork(RandomDomain.Lifecycle, 8, 56, 2).NextUInt64());
        Assert.NotEqual(baseline, root.Fork(RandomDomain.Lifecycle, 8, 55, 3).NextUInt64());
        Assert.NotEqual(baseline, new StableRandom(124).Fork(RandomDomain.Lifecycle, 8, 55, 2).NextUInt64());
    }

    [Fact]
    public void Consuming_one_fork_does_not_advance_another_fork() {
        StableRandom root = new(123);
        RandomStream noisy = root.Fork(RandomDomain.ObjectUpdate, 1, 10);
        RandomStream untouched = root.Fork(RandomDomain.ObjectUpdate, 1, 11);
        ulong expected = root.Fork(RandomDomain.ObjectUpdate, 1, 11).NextUInt64();

        for (int i = 0; i < 100; i++) {
            noisy.NextUInt64();
        }

        Assert.Equal(expected, untouched.NextUInt64());
    }

    [Fact]
    public void NextInt_supports_single_value_and_full_int_ranges() {
        RandomStream singleValue = new StableRandom(1).Fork(RandomDomain.Lifecycle, 0);
        RandomStream fullRange = new StableRandom(2).Fork(RandomDomain.Lifecycle, 0);

        Assert.Equal(0, singleValue.NextInt(1));
        Assert.Equal(-7, singleValue.NextInt(-7, -6));
        int value = fullRange.NextInt(int.MinValue, int.MaxValue);
        Assert.InRange(value, int.MinValue, int.MaxValue - 1);
    }

    [Fact]
    public void NextInt_stays_within_requested_ranges() {
        RandomStream stream = new StableRandom(123).Fork(RandomDomain.Lifecycle, 0);

        for (int i = 0; i < 1_000; i++) {
            Assert.InRange(stream.NextInt(7), 0, 6);
            Assert.InRange(stream.NextInt(-20, -4), -20, -5);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NextInt_rejects_non_positive_exclusive_max(int exclusiveMax) {
        RandomStream stream = new StableRandom(1).Fork(RandomDomain.Lifecycle, 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => stream.NextInt(exclusiveMax));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    public void NextInt_rejects_empty_or_reversed_ranges(int inclusiveMin, int exclusiveMax) {
        RandomStream stream = new StableRandom(1).Fork(RandomDomain.Lifecycle, 0);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => stream.NextInt(inclusiveMin, exclusiveMax));
    }

    [Fact]
    public void Fork_rejects_unknown_domains_and_negative_coordinates() {
        StableRandom root = new(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => root.Fork((RandomDomain)0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => root.Fork(RandomDomain.Lifecycle, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => root.Fork(RandomDomain.Lifecycle, 0, lane: -1));
    }
}
