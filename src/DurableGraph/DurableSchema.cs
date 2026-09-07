using System.Collections.Immutable;

namespace Atelia.DurableGraph;

/// <summary>
/// Describes one wire-format-independent version of a durable schema.
/// </summary>
public sealed class DurableSchema : IEquatable<DurableSchema> {
    private readonly int _hashCode;

    public DurableSchema(
        string schemaId,
        int version,
        params DurableFieldInfo[] fields)
        : this(schemaId, version, fields, baseSchema: null) {
    }

    public DurableSchema(
        string schemaId,
        int version,
        SchemaKind kind,
        params DurableFieldInfo[] fields)
        : this(schemaId, version, fields, baseSchema: null, kind) {
    }

    public DurableSchema(
        string schemaId,
        int version,
        DurableFieldInfo[] fields,
        DurableSchema? baseSchema,
        SchemaKind kind = SchemaKind.ReferenceObject) {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaId);

        if (version <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                version,
                "Schema versions must be positive.");
        }

        ArgumentNullException.ThrowIfNull(fields);
        if (!Enum.IsDefined(kind)) {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        if (baseSchema is not null &&
            (kind != SchemaKind.ReferenceObject || baseSchema.Kind != SchemaKind.ReferenceObject)) {
            throw new ArgumentException("Only reference object Schemas can have a reference object base.", nameof(baseSchema));
        }

        HashSet<string> ancestorIds = new(StringComparer.Ordinal) { schemaId };
        for (DurableSchema? ancestor = baseSchema; ancestor is not null; ancestor = ancestor.BaseSchema) {
            if (!ancestorIds.Add(ancestor.SchemaId)) {
                throw new ArgumentException(
                    "A schema identity cannot occur more than once in an inheritance chain.",
                    nameof(baseSchema));
            }
        }

        DurableFieldInfo[] canonicalFields = (DurableFieldInfo[])fields.Clone();
        Array.Sort(
            canonicalFields,
            static (left, right) => left.FieldId.CompareTo(right.FieldId));

        for (int index = 0; index < canonicalFields.Length; index++) {
            DurableFieldInfo field = canonicalFields[index];

            if (field.FieldId <= 0 ||
                field.TypeTag is TypeTag.Invalid ||
                !Enum.IsDefined(field.TypeTag)) {
                throw new ArgumentException(
                    "Every field must contain a valid field identifier and type tag.",
                    nameof(fields));
            }

            if (index > 0 && canonicalFields[index - 1].FieldId == field.FieldId) {
                throw new ArgumentException(
                    $"Field identifier {field.FieldId} is duplicated.",
                    nameof(fields));
            }
        }

        SchemaId = schemaId;
        Version = version;
        Kind = kind;
        Fields = ImmutableArray.CreateRange(canonicalFields);
        BaseSchema = baseSchema;

        // Immutable dependencies already have their hashes. Hashing a shared DAG
        // must not expand it repeatedly as a tree. This is never a persistent hash.
        HashCode hashCode = new();
        hashCode.Add(SchemaId, StringComparer.Ordinal);
        hashCode.Add(Version);
        hashCode.Add(Kind);
        hashCode.Add(BaseSchema);
        foreach (DurableFieldInfo field in Fields) { hashCode.Add(field); }
        _hashCode = hashCode.ToHashCode();
    }

    public string SchemaId { get; }

    public int Version { get; }

    public SchemaKind Kind { get; }

    /// <summary>Gets the fields declared by this schema, excluding inherited fields.</summary>
    public ImmutableArray<DurableFieldInfo> Fields { get; }

    /// <summary>Gets the exact base schema, including its immutable ancestor chain.</summary>
    public DurableSchema? BaseSchema { get; }

    public bool Equals(DurableSchema? other) {
        if (ReferenceEquals(this, other)) { return true; }
        if (other is null) { return false; }
        var pending = new Stack<(DurableSchema Left, DurableSchema Right)>();
        var seen = new HashSet<(DurableSchema, DurableSchema)>(SchemaPairComparer.Instance);
        pending.Push((this, other));
        while (pending.TryPop(out var pair)) {
            var (left, right) = pair;
            if (ReferenceEquals(left, right) || !seen.Add(pair)) { continue; }
            if (left.Version != right.Version || left.Kind != right.Kind ||
                !StringComparer.Ordinal.Equals(left.SchemaId, right.SchemaId) ||
                left.Fields.Length != right.Fields.Length) { return false; }
            if ((left.BaseSchema is null) != (right.BaseSchema is null)) { return false; }
            if (left.BaseSchema is not null) { pending.Push((left.BaseSchema, right.BaseSchema!)); }
            for (int index = 0; index < left.Fields.Length; index++) {
                DurableFieldInfo a = left.Fields[index], b = right.Fields[index];
                if (a.FieldId != b.FieldId || a.TypeTag != b.TypeTag ||
                    !StringComparer.Ordinal.Equals(a.TargetSchemaId, b.TargetSchemaId) ||
                    (a.InlineSchema is null) != (b.InlineSchema is null)) { return false; }
                if (a.InlineSchema is not null) { pending.Push((a.InlineSchema, b.InlineSchema!)); }
            }
        }
        return true;
    }

    public override bool Equals(object? obj) {
        return obj is DurableSchema other && Equals(other);
    }

    public override int GetHashCode() => _hashCode;

    internal void RequireReferenceObject() {
        if (Kind != SchemaKind.ReferenceObject) {
            throw new ArgumentException("An identity-bearing object requires a reference object Schema.");
        }
    }

    private sealed class SchemaPairComparer : IEqualityComparer<(DurableSchema, DurableSchema)> {
        internal static readonly SchemaPairComparer Instance = new();
        public bool Equals((DurableSchema, DurableSchema) x, (DurableSchema, DurableSchema) y) =>
            ReferenceEquals(x.Item1, y.Item1) && ReferenceEquals(x.Item2, y.Item2);
        public int GetHashCode((DurableSchema, DurableSchema) pair) => HashCode.Combine(
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pair.Item1),
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pair.Item2));
    }

    public static bool operator ==(DurableSchema? left, DurableSchema? right) {
        return Equals(left, right);
    }

    public static bool operator !=(DurableSchema? left, DurableSchema? right) {
        return !Equals(left, right);
    }
}
