using System.Collections.Immutable;

namespace Atelia.DurableGraph;

/// <summary>
/// Describes one wire-format-independent version of a durable schema.
/// </summary>
public sealed class DurableSchema : IEquatable<DurableSchema> {
    public DurableSchema(
        string schemaId,
        int version,
        params DurableFieldInfo[] fields)
        : this(schemaId, version, fields, baseSchema: null) {
    }

    public DurableSchema(
        string schemaId,
        int version,
        DurableFieldInfo[] fields,
        DurableSchema? baseSchema) {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaId);

        if (version <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                version,
                "Schema versions must be positive.");
        }

        ArgumentNullException.ThrowIfNull(fields);

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
        Fields = ImmutableArray.CreateRange(canonicalFields);
        BaseSchema = baseSchema;
    }

    public string SchemaId { get; }

    public int Version { get; }

    /// <summary>Gets the fields declared by this schema, excluding inherited fields.</summary>
    public ImmutableArray<DurableFieldInfo> Fields { get; }

    /// <summary>Gets the exact base schema, including its immutable ancestor chain.</summary>
    public DurableSchema? BaseSchema { get; }

    public bool Equals(DurableSchema? other) {
        return other is not null &&
            StringComparer.Ordinal.Equals(SchemaId, other.SchemaId) &&
            Version == other.Version &&
            Equals(BaseSchema, other.BaseSchema) &&
            Fields.AsSpan().SequenceEqual(other.Fields.AsSpan());
    }

    public override bool Equals(object? obj) {
        return obj is DurableSchema other && Equals(other);
    }

    public override int GetHashCode() {
        HashCode hashCode = new();
        hashCode.Add(SchemaId, StringComparer.Ordinal);
        hashCode.Add(Version);
        hashCode.Add(BaseSchema);

        foreach (DurableFieldInfo field in Fields) {
            hashCode.Add(field);
        }

        return hashCode.ToHashCode();
    }

    public static bool operator ==(DurableSchema? left, DurableSchema? right) {
        return Equals(left, right);
    }

    public static bool operator !=(DurableSchema? left, DurableSchema? right) {
        return !Equals(left, right);
    }
}
