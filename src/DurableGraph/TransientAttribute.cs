namespace Atelia.DurableGraph;

/// <summary>
/// Marks a field as intentionally excluded from durable state.
/// Use field: for record struct property storage. This does not change the record's
/// generated equality, and restoration leaves the excluded storage at its default value.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class TransientAttribute : Attribute {
}
