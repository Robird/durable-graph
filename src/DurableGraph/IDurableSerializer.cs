namespace Atelia.DurableGraph;

/// <summary>
/// Converts one durable type to and from the prototype's boxed field state.
/// </summary>
/// <typeparam name="T">The durable type handled by this serializer.</typeparam>
public interface IDurableSerializer<T>
    where T : DurableBase {
    /// <summary>
    /// Gets the current schema produced when this serializer writes state.
    /// </summary>
    DurableSchema Schema { get; }

    /// <summary>
    /// Captures durable fields by their stable field identifiers.
    /// </summary>
    IReadOnlyDictionary<int, object?> Serialize(T value);

    /// <summary>
    /// Restores a current instance from fields written with the supplied stored
    /// schema.
    /// </summary>
    /// <remarks>
    /// Implementations are responsible for validating the complete stored
    /// schema and upgrading supported historical versions before materializing
    /// the current durable type.
    /// </remarks>
    T Deserialize(
        DurableSchema storedSchema,
        IReadOnlyDictionary<int, object?> fields);
}
