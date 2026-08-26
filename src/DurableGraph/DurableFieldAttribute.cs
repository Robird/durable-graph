namespace Atelia.DurableGraph;

/// <summary>
/// Marks a field as persisted and assigns its stable identity within a durable type.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class DurableFieldAttribute : Attribute {
    public DurableFieldAttribute(int fieldId) {
        if (fieldId <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(fieldId),
                fieldId,
                "Field identifiers must be positive.");
        }

        FieldId = fieldId;
    }

    public int FieldId { get; }
}
