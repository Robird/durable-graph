using System.Collections.Immutable;

namespace Atelia.DurableGraph;

/// <summary>
/// Describes one wire-format-independent version of a durable schema.
/// </summary>
public sealed class DurableSchema : IEquatable<DurableSchema> {
    public DurableSchema(
        string schemaId,
        int version,
        params DurableFieldInfo[] fields) {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaId);

        if (version <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                version,
                "Schema versions must be positive.");
        }

        ArgumentNullException.ThrowIfNull(fields);

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
    }

    public string SchemaId { get; }

    public int Version { get; }

    public ImmutableArray<DurableFieldInfo> Fields { get; }

    public bool Equals(DurableSchema? other) {
        return other is not null &&
            StringComparer.Ordinal.Equals(SchemaId, other.SchemaId) &&
            Version == other.Version &&
            Fields.AsSpan().SequenceEqual(other.Fields.AsSpan());
    }

    public override bool Equals(object? obj) {
        return obj is DurableSchema other && Equals(other);
    }

    public override int GetHashCode() {
        HashCode hashCode = new();
        hashCode.Add(SchemaId, StringComparer.Ordinal);
        hashCode.Add(Version);

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
