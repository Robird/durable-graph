using System.Collections.ObjectModel;

namespace Atelia.DurableGraph;

/// <summary>
/// Stores prototype boxed state in memory and gates access through registered
/// schemas.
/// </summary>
/// <remarks>
/// Slots are demo-only addresses, not durable object identifiers. This type does
/// not provide persistence or thread-safety.
/// </remarks>
public sealed class InMemoryStateStore {
    private readonly Dictionary<string, StoredState> _states =
        new(StringComparer.Ordinal);

    public InMemoryStateStore()
        : this(new InMemorySchemaStore()) {
    }

    public InMemoryStateStore(InMemorySchemaStore schemaStore) {
        ArgumentNullException.ThrowIfNull(schemaStore);
        SchemaStore = schemaStore;
    }

    public InMemorySchemaStore SchemaStore { get; }

    /// <summary>
    /// Saves boxed state after registering the serializer's exact schema.
    /// </summary>
    public void Save<T>(
        string slot,
        T value,
        IDurableSerializer<T> serializer)
        where T : DurableBase {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(serializer);

        DurableSchema schema = serializer.Schema;
        ArgumentNullException.ThrowIfNull(schema);

        SchemaStore.Register(schema);

        IReadOnlyDictionary<int, object?> serializedFields =
            serializer.Serialize(value);
        ArgumentNullException.ThrowIfNull(serializedFields);

        Dictionary<int, object?> copiedFields = new(serializedFields);
        ReadOnlyDictionary<int, object?> storedFields = new(copiedFields);
        StoredState state = new(schema.SchemaId, schema.Version, storedFields);

        _states[slot] = state;
    }

    /// <summary>
    /// Loads boxed state after resolving its exact registered schema and
    /// confirming that it belongs to the serializer's schema identity.
    /// </summary>
    /// <remarks>
    /// Historical version and shape validation belongs to the serializer. A
    /// load does not register the serializer's current schema or rewrite the
    /// stored state after an in-memory upgrade.
    /// </remarks>
    public T Load<T>(string slot, IDurableSerializer<T> serializer)
        where T : DurableBase {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(serializer);

        if (!_states.TryGetValue(slot, out StoredState? state)) {
            throw new KeyNotFoundException($"State slot '{slot}' is not stored.");
        }

        DurableSchema storedSchema = SchemaStore.GetRequired(
            state.SchemaId,
            state.Version);
        DurableSchema expectedSchema = serializer.Schema;
        ArgumentNullException.ThrowIfNull(expectedSchema);

        if (!StringComparer.Ordinal.Equals(
                storedSchema.SchemaId,
                expectedSchema.SchemaId)) {
            throw new StateSchemaMismatchException(
                storedSchema,
                expectedSchema);
        }

        return serializer.Deserialize(storedSchema, state.Fields);
    }

    private sealed record StoredState(
        string SchemaId,
        int Version,
        IReadOnlyDictionary<int, object?> Fields);
}
