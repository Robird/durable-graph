using Atelia.TwoLegRotationProbe.Benchmarking;

namespace Atelia.TwoLegRotationProbe.Arena;

/// <summary>
/// Organizer-owned adapter for one strategy assembly. A strategy may use any internal
/// code shape; the adapter binds its identity to one complete Arena run.
/// </summary>
public sealed class StrategyBindingV1 {
    private readonly Func<Func<StrategyRunContextV1, StrategyRunProductV1>>
        _createExecutor;

    public StrategyBindingV1(
        BenchmarkComponentIdentityV1 identity,
        string caseIdSuffix,
        Func<Func<StrategyRunContextV1, StrategyRunProductV1>> createExecutor) {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        BenchmarkV1Text.ValidateId(caseIdSuffix, nameof(caseIdSuffix));
        CaseIdSuffix = caseIdSuffix;
        _createExecutor = createExecutor ??
            throw new ArgumentNullException(nameof(createExecutor));
    }

    public BenchmarkComponentIdentityV1 Identity { get; }

    public string CaseIdSuffix { get; }

    internal Delegate ExecutorFactory => _createExecutor;

    internal StrategyRunProductV1 Execute(StrategyRunContextV1 context) {
        Func<StrategyRunContextV1, StrategyRunProductV1> executor =
            _createExecutor() ?? throw new InvalidDataException(
                $"Strategy '{Identity.Id}/{Identity.Version}' returned no executor.");
        return executor(context) ?? throw new InvalidDataException(
            $"Strategy '{Identity.Id}/{Identity.Version}' returned no run product.");
    }
}
