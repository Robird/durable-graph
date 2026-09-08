namespace Atelia.DurableGraph;

/// <summary>
/// Describes one persisted field in a durable schema.
/// </summary>
public readonly record struct DurableFieldInfo {
    public DurableFieldInfo(int fieldId, TypeTag typeTag, string? targetSchemaId = null, DurableSchema? inlineSchema = null)
        : this(fieldId, typeTag, targetSchemaId is null ? null : TypeExpr.Named(targetSchemaId), inlineSchema) {
    }

    private DurableFieldInfo(int fieldId, TypeTag typeTag, TypeExpr? targetType, DurableSchema? inlineSchema) {
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
            ArgumentNullException.ThrowIfNull(targetType);
            if (targetType.Kind != TypeExprKind.Named || !targetType.IsClosed) {
                throw new ArgumentException("A durable reference requires a closed nominal named type.", nameof(targetType));
            }
        }
        else if (targetType is not null) {
            throw new ArgumentException("Only durable references have a nominal target Schema identity.", nameof(targetType));
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
        TargetType = targetType;
        InlineSchema = inlineSchema;
    }

    public int FieldId { get; }

    public TypeTag TypeTag { get; }

    /// <summary>Gets the stable nominal target family for a durable reference, without binding its version.</summary>
    public string? TargetSchemaId => TargetType?.DefinitionId;

    /// <summary>Gets the complete closed nominal reference constraint, without binding a version.</summary>
    public TypeExpr? TargetType { get; }

    public static DurableFieldInfo Reference(int fieldId, TypeExpr targetType) =>
        new(fieldId, TypeTag.DurableReference, targetType, inlineSchema: null);

    /// <summary>Gets the immutable exact layout of an inline value, including its value dependencies.</summary>
    public DurableSchema? InlineSchema { get; }
}
