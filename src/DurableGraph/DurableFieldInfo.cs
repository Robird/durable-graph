namespace Atelia.DurableGraph;

/// <summary>
/// Describes one persisted field in a durable schema.
/// </summary>
public readonly record struct DurableFieldInfo {
    public DurableFieldInfo(int fieldId, TypeTag typeTag) {
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

        FieldId = fieldId;
        TypeTag = typeTag;
    }

    public int FieldId { get; }

    public TypeTag TypeTag { get; }
}
