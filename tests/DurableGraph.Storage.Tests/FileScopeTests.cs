namespace Atelia.DurableGraph.Storage.Tests;

public sealed class FileScopeTests {
    [Theory]
    [InlineData(70_000u, 70_000u, 0u)]
    [InlineData(70_000u, 69_999u, 1u)]
    [InlineData(70_000u, 4_464u, 65_536u)]
    [InlineData(uint.MaxValue, 1u, uint.MaxValue - 1u)]
    public void Absolute_file_number_round_trips_through_backward_distance(
        uint currentFileNumber,
        uint absoluteFileNumber,
        uint expectedBackwardFileDistance) {
        FileScope scope = new(currentFileNumber);

        uint backwardFileDistance = scope.ToBackwardFileDistance(
            absoluteFileNumber);
        uint resolvedFileNumber = scope.ToAbsoluteFileNumber(
            backwardFileDistance);

        Assert.Equal(expectedBackwardFileDistance, backwardFileDistance);
        Assert.Equal(absoluteFileNumber, resolvedFileNumber);
    }

    [Fact]
    public void Current_file_number_is_one_based() {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileScope(0));
    }

    [Fact]
    public void Default_scope_is_rejected_by_both_conversions() {
        FileScope scope = default;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            scope.ToAbsoluteFileNumber(0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            scope.ToBackwardFileDistance(1));
    }

    [Fact]
    public void Absolute_file_number_is_one_based() {
        FileScope scope = new(10);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            scope.ToBackwardFileDistance(0));
    }

    [Fact]
    public void Future_absolute_file_is_rejected() {
        FileScope scope = new(10);

        Assert.Throws<InvalidDataException>(() =>
            scope.ToBackwardFileDistance(11));
    }

    [Theory]
    [InlineData(10u)]
    [InlineData(uint.MaxValue)]
    public void Distance_that_reaches_or_crosses_file_zero_is_rejected(
        uint backwardFileDistance) {
        FileScope scope = new(10);

        Assert.Throws<InvalidDataException>(() =>
            scope.ToAbsoluteFileNumber(backwardFileDistance));
    }
}
