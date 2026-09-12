using Atelia.DurableGraph.Runtime;

namespace Atelia.DurableGraph;

/// <summary>Receives generated declaration factories and retained history explicitly.</summary>
public interface IStateDefinitionRegistration {
    void Register(StateDefinitionBinding definition) =>
        throw new NotSupportedException("This registration sink does not accept definition templates.");

    /// <summary>Registers explicitly selected, immutable value conversion rules in the same code catalog.</summary>
    void Register(StateValueUpgradeRuleSet ruleSet) =>
        throw new NotSupportedException("This registration sink does not accept value upgrade rules.");
}
