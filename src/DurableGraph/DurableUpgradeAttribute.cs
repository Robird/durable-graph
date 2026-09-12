namespace Atelia.DurableGraph;

/// <summary>Declares a generic-definition or closed-owner adjacent DTO conversion for generated registration.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DurableUpgradeAttribute : Attribute {
    public DurableUpgradeAttribute(Type owner, int fromVersion) {
        ArgumentNullException.ThrowIfNull(owner);
        if (!owner.IsClass || (owner.ContainsGenericParameters && !owner.IsGenericTypeDefinition)) {
            throw new ArgumentException("An upgrade owner must be a class or an open generic class definition.", nameof(owner));
        }
        if (fromVersion is <= 0 or int.MaxValue) { throw new ArgumentOutOfRangeException(nameof(fromVersion)); }
        Owner = owner;
        FromVersion = fromVersion;
    }

    public Type Owner { get; }
    public int FromVersion { get; }
}
