using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class FileScopeTests {
    [Fact]
    public void PreviousFileNumber_is_derived_from_current_file() {
        FileScope scope = new(1);

        Assert.Null(scope.PreviousFileNumber);

        scope.CurrentFileNumber = 2;

        Assert.Equal(1U, scope.PreviousFileNumber);
    }
}
