namespace Atelia.DurableGraph;

/// <summary>
/// Describes one persisted field in a durable schema.
/// </summary>
public readonly record struct DurableFieldInfo {
    public DurableFieldInfo(int fieldId, TypeTag typeTag, string? targetSchemaId = null, DurableSchema? inlineSchema = null) {
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

        if (typeTag == TypeTag.InlineValue) {
            ArgumentNullException.ThrowIfNull(inlineSchema);
            if (inlineSchema.Kind != SchemaKind.InlineValue) {
                throw new ArgumentException("An inline field requires an exact inline value Schema.", nameof(inlineSchema));
            }
        }
        else if (inlineSchema is not null) {
            throw new ArgumentException("Only inline values have an exact inline Schema.", nameof(inlineSchema));
        }

        FieldId = fieldId;
        TypeTag = typeTag;
        TargetSchemaId = targetSchemaId;
        InlineSchema = inlineSchema;
    }

    public int FieldId { get; }

    public TypeTag TypeTag { get; }

    /// <summary>Gets the stable nominal target family for a durable reference, without binding its version.</summary>
    public string? TargetSchemaId { get; }

    /// <summary>Gets the immutable exact layout of an inline value, including its value dependencies.</summary>
    public DurableSchema? InlineSchema { get; }
}
