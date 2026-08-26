using System.Collections.ObjectModel;

namespace Atelia.DurableGraph;

/// <summary>
/// Stores prototype boxed state in memory and gates access through exact schemas.
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
    /// Loads boxed state only after its registered schema exactly matches the
    /// serializer's schema identity, version, and shape.
    /// </summary>
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
                expectedSchema.SchemaId) ||
            storedSchema.Version != expectedSchema.Version) {
            throw new StateSchemaMismatchException(
                storedSchema,
                expectedSchema);
        }

        if (!storedSchema.Equals(expectedSchema)) {
            throw new SchemaConflictException(storedSchema, expectedSchema);
        }

        return serializer.Deserialize(state.Fields);
    }

    private sealed record StoredState(
        string SchemaId,
        int Version,
        IReadOnlyDictionary<int, object?> Fields);
}
