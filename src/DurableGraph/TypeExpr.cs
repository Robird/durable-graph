using System.Collections.Immutable;

namespace Atelia.DurableGraph;

/// <summary>Identifies a supported nominal type, or a declaration-scoped type parameter.</summary>
/// <remarks>Versions belong to exact Schemas, not to nominal type expressions.</remarks>
public sealed class TypeExpr : IEquatable<TypeExpr>, IComparable<TypeExpr> {
    public const int MaximumDepth = 64;
    public const int MaximumNodeCount = 4096;
    public const int MaximumArity = 32;

    private readonly int _hashCode;
    private readonly int _depth;
    private readonly int _nodeCount;

    private TypeExpr(TypeExprKind kind, TypeTag builtinTag, string? definitionId,
        ImmutableArray<TypeExpr> arguments, int parameterOrdinal) {
        Kind = kind;
        BuiltinTag = builtinTag;
        DefinitionId = definitionId;
        Arguments = arguments;
        ParameterOrdinal = parameterOrdinal;
        IsClosed = kind != TypeExprKind.Parameter;
        _depth = 1;
        _nodeCount = 1;
        HashCode hash = new();
        hash.Add(kind);
        hash.Add(builtinTag);
        hash.Add(definitionId, StringComparer.Ordinal);
        hash.Add(parameterOrdinal);
        foreach (TypeExpr argument in arguments) {
            ArgumentNullException.ThrowIfNull(argument);
            _depth = Math.Max(_depth, argument._depth + 1);
            _nodeCount += argument._nodeCount;
            IsClosed &= argument.IsClosed;
            hash.Add(argument);
        }
        if (_depth > MaximumDepth || _nodeCount > MaximumNodeCount) {
            throw new ArgumentException("The type expression exceeds its depth or expanded node limit.", nameof(arguments));
        }
        _hashCode = hash.ToHashCode();
    }

    public TypeExprKind Kind { get; }
    public TypeTag BuiltinTag { get; }
    public string? DefinitionId { get; }
    public ImmutableArray<TypeExpr> Arguments { get; }
    public int ParameterOrdinal { get; }
    public bool IsClosed { get; }

    public static TypeExpr Builtin(TypeTag tag) {
        if (tag is < TypeTag.Boolean or > TypeTag.Double) {
            throw new ArgumentOutOfRangeException(nameof(tag), "Only the supported scalar and string tags are built-in types.");
        }
        return new(TypeExprKind.Builtin, tag, null, ImmutableArray<TypeExpr>.Empty, -1);
    }

    public static TypeExpr Named(string definitionId, params TypeExpr[] arguments) {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Length > MaximumArity) {
            throw new ArgumentOutOfRangeException(nameof(arguments), "A type definition exceeds the supported generic arity.");
        }
        return new(TypeExprKind.Named, TypeTag.Invalid, definitionId, ImmutableArray.CreateRange(arguments), -1);
    }

    public static TypeExpr Parameter(int ordinal) {
        if (ordinal is < 0 or >= MaximumArity) {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }
        return new(TypeExprKind.Parameter, TypeTag.Invalid, null, ImmutableArray<TypeExpr>.Empty, ordinal);
    }

    public bool Equals(TypeExpr? other) => CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is TypeExpr expression && Equals(expression);
    public override int GetHashCode() => _hashCode;

    /// <summary>Compares the canonical structure, using ordinal definition identifiers.</summary>
    public int CompareTo(TypeExpr? other) {
        if (ReferenceEquals(this, other)) { return 0; }
        if (other is null) { return 1; }
        int comparison = Kind.CompareTo(other.Kind);
        if (comparison != 0) { return comparison; }
        if (Kind == TypeExprKind.Builtin) { return BuiltinTag.CompareTo(other.BuiltinTag); }
        if (Kind == TypeExprKind.Parameter) { return ParameterOrdinal.CompareTo(other.ParameterOrdinal); }
        comparison = StringComparer.Ordinal.Compare(DefinitionId, other.DefinitionId);
        if (comparison != 0) { return comparison; }
        comparison = Arguments.Length.CompareTo(other.Arguments.Length);
        if (comparison != 0) { return comparison; }
        for (int index = 0; index < Arguments.Length; index++) {
            comparison = Arguments[index].CompareTo(other.Arguments[index]);
            if (comparison != 0) { return comparison; }
        }
        return 0;
    }

    public override string ToString() => Kind switch {
        TypeExprKind.Builtin => BuiltinTag.ToString(),
        TypeExprKind.Parameter => $"!{ParameterOrdinal}",
        _ => Arguments.IsEmpty ? DefinitionId! : $"{DefinitionId}<{string.Join(",", Arguments)}>",
    };

    public static bool operator ==(TypeExpr? left, TypeExpr? right) => Equals(left, right);
    public static bool operator !=(TypeExpr? left, TypeExpr? right) => !Equals(left, right);
}

public enum TypeExprKind : byte {
    Builtin = 1,
    Named = 2,
    Parameter = 3,
}
