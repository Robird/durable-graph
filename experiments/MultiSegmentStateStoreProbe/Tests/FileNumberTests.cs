using Atelia.MultiSegmentStateStoreProbe.Model;
using Atelia.MultiSegmentStateStoreProbe.Storage;

namespace Atelia.MultiSegmentStateStoreProbe.Tests;

public sealed class FileNumberTests {
    [Fact]
    public void File_numbers_are_one_based_and_checked() {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileNumber(0));
        Assert.Equal(new FileNumber(2), new FileNumber(1).Next());
        Assert.Throws<OverflowException>(() => new FileNumber(uint.MaxValue).Next());
    }

    [Fact]
    public void Default_file_number_fails_closed_at_use_boundaries() {
        FileNumber invalid = default;

        Assert.Throws<InvalidDataException>(() => invalid.Next());
        Assert.Throws<InvalidDataException>(() => FileNameConvention.Format(invalid));
        Assert.Throws<InvalidDataException>(() =>
            new AbsoluteFrameAddress(invalid, new FrameTicket(4, 24)));
    }

    [Theory]
    [InlineData(1u, "0000000001.rbf")]
    [InlineData(65_536u, "0000065536.rbf")]
    [InlineData(uint.MaxValue, "4294967295.rbf")]
    public void Canonical_filename_round_trips_without_a_catalog(
        uint value,
        string expectedFileName) {
        FileNumber fileNumber = new(value);

        Assert.Equal(expectedFileName, FileNameConvention.Format(fileNumber));
        Assert.Equal(fileNumber, FileNameConvention.Parse(expectedFileName));
    }

    [Theory]
    [InlineData("1.rbf")]
    [InlineData("0000000000.rbf")]
    [InlineData("0000000001.RBF")]
    [InlineData("0000000001.bin")]
    [InlineData("4294967296.rbf")]
    public void Non_canonical_filename_is_rejected(string fileName) {
        Assert.Throws<InvalidDataException>(() => FileNameConvention.Parse(fileName));
    }
}
