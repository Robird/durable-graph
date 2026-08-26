namespace Atelia.DurableGraph;

/// <summary>
/// Converts one durable type to and from the prototype's boxed field state.
/// </summary>
/// <typeparam name="T">The durable type handled by this serializer.</typeparam>
public interface IDurableSerializer<T>
    where T : DurableBase {
    /// <summary>
    /// Gets the exact schema produced and consumed by this serializer.
    /// </summary>
    DurableSchema Schema { get; }

    /// <summary>
    /// Captures durable fields by their stable field identifiers.
    /// </summary>
    IReadOnlyDictionary<int, object?> Serialize(T value);

    /// <summary>
    /// Restores an instance from fields identified by their stable identifiers.
    /// </summary>
    T Deserialize(IReadOnlyDictionary<int, object?> fields);
}
