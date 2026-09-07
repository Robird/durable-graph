namespace Atelia.DurableGraph.StateStore;

/// <summary>Explicit application-local current models and their exact historical readers.</summary>
/// <remarks>This code capability directory is not the authority for persisted Schema definitions.</remarks>
public sealed class StateModelRegistry : IStateModelRegistration {
    private readonly Dictionary<string, StateModelBinding> _models = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, StateModelBinding> _types = [];
    private readonly Dictionary<SchemaKey, StateReaderBinding> _readers = [];

    /// <summary>Registers one stable model atomically; repeated registration of that instance is harmless.</summary>
    public void Register(StateModelBinding model) {
        ArgumentNullException.ThrowIfNull(model);
        string id = model.CurrentSchema.SchemaId;
        if (_models.TryGetValue(id, out StateModelBinding? existing)) {
            if (!ReferenceEquals(existing, model)) {
                throw new InvalidOperationException($"A different current model is already registered for {id}.");
            }
            return;
        }
        if (_types.ContainsKey(model.DomainType)) {
            throw new InvalidOperationException($"A current model is already registered for exact domain type {model.DomainType}.");
        }
        foreach (StateReaderBinding reader in model.Readers) {
            SchemaKey key = new(reader.Schema.SchemaId, reader.Schema.Version);
            if (_readers.TryGetValue(key, out StateReaderBinding? prior) && !ReferenceEquals(reader, prior)) {
                throw new InvalidOperationException($"A different reader is already registered for {key.SchemaId} v{key.Version}.");
            }
        }
        foreach (StateReaderBinding reader in model.Readers) {
            _readers.TryAdd(new(reader.Schema.SchemaId, reader.Schema.Version), reader);
        }
        _models.Add(id, model);
        _types.Add(model.DomainType, model);
    }

    // Freeze every index before any reader, Upgrade, Capture or Hydrate callback runs.
    internal StateModelSnapshot Snapshot() => new(
        new Dictionary<string, StateModelBinding>(_models, StringComparer.Ordinal),
        new Dictionary<Type, StateModelBinding>(_types),
        new Dictionary<SchemaKey, StateReaderBinding>(_readers));
}

internal sealed record StateModelSnapshot(
    IReadOnlyDictionary<string, StateModelBinding> Models,
    IReadOnlyDictionary<Type, StateModelBinding> Types,
    IReadOnlyDictionary<SchemaKey, StateReaderBinding> Readers);
