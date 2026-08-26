namespace Atelia.DurableGraph;

/// <summary>
/// Marks a field as intentionally excluded from durable state.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class TransientAttribute : Attribute {
}
