using System.Reflection;

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

/// <summary>A selected adjacent owner conversion over generated historical DTO types.</summary>
/// <remarks>Metadata describes one execution capability; it does not select a graph-wide migration path.</remarks>
public sealed class StateUpgradeProvider {
    public StateUpgradeProvider(string definitionId, int fromVersion, MethodInfo method,
        TypeExpr? closedOwner = null, bool allowLegacyTwoParameter = false) {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        if (fromVersion is <= 0 or int.MaxValue) { throw new ArgumentOutOfRangeException(nameof(fromVersion)); }
        ArgumentNullException.ThrowIfNull(method);
        if (!method.IsStatic || method.ReturnType != typeof(void) || method.DeclaringType?.ContainsGenericParameters == true) {
            throw new ArgumentException("An upgrade requires a static void method on a closed CLR host.", nameof(method));
        }
        ParameterInfo[] parameters = method.GetParameters();
        bool legacy = allowLegacyTwoParameter && !method.IsGenericMethod && parameters.Length == 2;
        if ((!legacy && parameters.Length != 3) ||
            !parameters[0].ParameterType.IsByRef || !parameters[0].IsIn || parameters[0].IsOut ||
            !parameters[1].ParameterType.IsByRef || !parameters[1].IsOut ||
            (!legacy && parameters[2].ParameterType != typeof(UpgradeContext))) {
            throw new ArgumentException("An upgrade requires (in prior, out next, UpgradeContext).", nameof(method));
        }
        if (closedOwner is not null && (closedOwner.Kind != TypeExprKind.Named || !closedOwner.IsClosed ||
            closedOwner.DefinitionId != definitionId || method.ContainsGenericParameters)) {
            throw new ArgumentException("A closed owner provider requires that closed nominal family and concrete state parameters.", nameof(closedOwner));
        }
        DefinitionId = definitionId;
        FromVersion = fromVersion;
        Method = method;
        ClosedOwner = closedOwner;
        IsLegacyTwoParameter = legacy;
    }

    public string DefinitionId { get; }
    public int FromVersion { get; }
    public int ToVersion => FromVersion + 1;
    public MethodInfo Method { get; }
    public TypeExpr? ClosedOwner { get; }
    public bool IsLegacyTwoParameter { get; }

    internal Type PriorType => Method.GetParameters()[0].ParameterType.GetElementType()!;
    internal Type NextType => Method.GetParameters()[1].ParameterType.GetElementType()!;

    internal bool IsSameCapability(StateUpgradeProvider other) =>
        DefinitionId == other.DefinitionId && FromVersion == other.FromVersion && ClosedOwner == other.ClosedOwner &&
        Method.Equals(other.Method) && IsLegacyTwoParameter == other.IsLegacyTwoParameter;
}
