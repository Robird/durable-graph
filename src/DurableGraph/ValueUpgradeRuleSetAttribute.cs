namespace Atelia.DurableGraph;

/// <summary>Marks an application-local, explicitly registered collection of value conversion rules.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ValueUpgradeRuleSetAttribute : Attribute {
    public bool AllowKeepExact { get; set; }
    /// <summary>Allows this rule set's child conversion to preserve Nullable presence around that conversion.</summary>
    public bool AllowNullableLifting { get; set; }
}
