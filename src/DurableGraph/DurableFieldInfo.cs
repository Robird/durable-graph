namespace Atelia.DurableGraph;

/// <summary>
/// Describes one persisted field in a durable schema.
/// </summary>
public readonly record struct DurableFieldInfo {
    public DurableFieldInfo(int fieldId, TypeTag typeTag, string? targetSchemaId = null) {
        if (fieldId <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(fieldId),
                fieldId,
                "Field identifiers must be positive.");
        }

        if (typeTag is TypeTag.Invalid || !Enum.IsDefined(typeTag)) {
            throw new ArgumentOutOfRangeException(
                nameof(typeTag),
                typeTag,
                "The type tag must identify a supported durable field type.");
        }

        if (typeTag == TypeTag.DurableReference) {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetSchemaId);
        }
        else if (targetSchemaId is not null) {
            throw new ArgumentException("Only durable references have a nominal target Schema identity.", nameof(targetSchemaId));
        }

        FieldId = fieldId;
        TypeTag = typeTag;
        TargetSchemaId = targetSchemaId;
    }

    public int FieldId { get; }

    public TypeTag TypeTag { get; }

    /// <summary>Gets the stable nominal target family for a durable reference, without binding its version.</summary>
    public string? TargetSchemaId { get; }
}
