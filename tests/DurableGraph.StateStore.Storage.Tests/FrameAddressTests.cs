using Atelia.Data;

namespace Atelia.DurableGraph.StateStore.Storage.Tests;

public sealed class FrameAddressTests {
    [Fact]
    public void Address_preserves_absolute_file_number_and_exact_frame_ticket() {
        SizedPtr frameTicket = SizedPtr.Create(64, 128);

        FrameAddress address = new(7, frameTicket);

        Assert.Equal(7u, address.FileNumber);
        Assert.Equal(frameTicket, address.FrameTicket);
    }

    [Fact]
    public void File_number_is_one_based() {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FrameAddress(0, SizedPtr.Create(64, 128)));
    }
}
