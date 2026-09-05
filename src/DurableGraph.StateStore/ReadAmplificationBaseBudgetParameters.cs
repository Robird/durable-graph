namespace Atelia.DurableGraph.StateStore;

/// <summary>
/// Integer read amplification limit (at least 1) and optional Base budget percent
/// (1 through 100). For example, (3, 5) means 3 times and 5 percent.
/// The budget is soft and does not cap total or physical write bytes.
/// </summary>
internal readonly record struct ReadAmplificationBaseBudgetParameters(
    int ReadAmplificationLimit,
    int BaseBudgetPercent);
