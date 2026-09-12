namespace Atelia.DurableGraph;

/// <summary>Registers one inline family conversion without requiring its old domain CLR type.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DurableValueUpgradeAttribute : Attribute {
    public DurableValueUpgradeAttribute(Type ruleSet, string definitionId, int fromVersion, int toVersion) {
        ArgumentNullException.ThrowIfNull(ruleSet);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fromVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(toVersion);
        RuleSet = ruleSet;
        DefinitionId = definitionId;
        FromVersion = fromVersion;
        ToVersion = toVersion;
    }

    public Type RuleSet { get; }
    public string DefinitionId { get; }
    public int FromVersion { get; }
    public int ToVersion { get; }
}
