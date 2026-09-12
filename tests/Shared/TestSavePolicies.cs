using Atelia.DurableGraph.StateStore;

namespace Atelia.DurableGraph.Testing;

internal static class TestSavePolicies {
    // A stable test baseline, independent of the library's evolving default policy.
    internal static readonly ReadAmplificationBaseBudgetParameters Baseline = new(3, 5);
}
