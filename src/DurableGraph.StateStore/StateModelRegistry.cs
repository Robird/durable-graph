namespace Atelia.DurableGraph.StateStore;

/// <summary>Explicit application-local current models and their exact historical readers.</summary>
/// <remarks>This code capability directory is not the authority for persisted Schema definitions.</remarks>
public sealed class StateModelRegistry : IStateModelRegistration {
    private readonly Dictionary<TypeExpr, StateModelBinding> _models = [];
    private readonly Dictionary<Type, StateModelBinding> _types = [];
    private readonly Dictionary<SchemaKey, StateReaderBinding> _readers = [];
    private readonly Dictionary<string, StateDefinitionBinding> _definitions = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, StateValueUpgradeRuleSet> _valueUpgradeRules = [];

    /// <summary>Registers one stable ruleset; repeated registration of that instance is harmless.</summary>
    public void Register(StateValueUpgradeRuleSet ruleSet) =>
        StateModelSnapshot.RegisterValueUpgradeRuleSet(_valueUpgradeRules, ruleSet);

    public void Register(StateDefinitionBinding definition) {
        ArgumentNullException.ThrowIfNull(definition);
        foreach (StateModelBinding model in _models.Values) { ValidateOwnership(definition, model); }
        StateModelSnapshot.RegisterDefinition(_definitions, definition);
    }

    /// <summary>Registers one stable model atomically; repeated registration of that instance is harmless.</summary>
    public void Register(StateModelBinding model) {
        ArgumentNullException.ThrowIfNull(model);
        foreach (StateDefinitionBinding definition in _definitions.Values) { ValidateOwnership(definition, model); }
        TypeExpr id = model.CurrentSchema.Type;
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
            SchemaKey key = new(reader.Schema.Type, reader.Schema.Version);
            if (_readers.TryGetValue(key, out StateReaderBinding? prior) && !ReferenceEquals(reader, prior)) {
                throw new InvalidOperationException($"A different reader is already registered for {key.SchemaId} v{key.Version}.");
            }
        }
        foreach (StateReaderBinding reader in model.Readers) {
            _readers.TryAdd(new(reader.Schema.Type, reader.Schema.Version), reader);
        }
        _models.Add(id, model);
        _types.Add(model.DomainType, model);
    }

    private static void ValidateOwnership(StateDefinitionBinding definition, StateModelBinding model) {
        Type declaration = model.DomainType.IsGenericType ? model.DomainType.GetGenericTypeDefinition() : model.DomainType;
        bool sameClr = declaration == definition.DomainTypeDefinition;
        bool sameFamily = model.CurrentSchema.SchemaId == definition.DefinitionId;
        if (sameClr != sameFamily || (sameFamily && (model.CurrentSchema.Kind != definition.Kind ||
            model.CurrentSchema.Type.Arguments.Length != definition.Arity || model.CurrentSchema.Version != definition.CurrentVersion))) {
            throw new InvalidOperationException("A definition and concrete model disagree about declaration ownership or current version.");
        }
        // A concrete stable binding may coexist with its own declaration catalog. It remains
        // the selected binding for that exact CLR closure; templates serve other closures.
    }

    // Freeze every index before any reader, Upgrade, Capture or Hydrate callback runs.
    internal StateModelSnapshot Snapshot(SchemaStore? schemas = null) => new(
        new Dictionary<TypeExpr, StateModelBinding>(_models),
        new Dictionary<Type, StateModelBinding>(_types),
        new Dictionary<SchemaKey, StateReaderBinding>(_readers),
        new Dictionary<string, StateDefinitionBinding>(_definitions, StringComparer.Ordinal), schemas,
        new Dictionary<Type, StateValueUpgradeRuleSet>(_valueUpgradeRules));
}
