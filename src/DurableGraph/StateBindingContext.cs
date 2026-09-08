using System.Collections.Immutable;

namespace Atelia.DurableGraph;

/// <summary>Resolves current models inside one frozen operation catalog.</summary>
public interface IStateModelResolver {
    bool TryGetCurrentModel(Type domainType, out StateModelBinding? model);
}

/// <summary>Cold-path closure services for generated factories in one immutable code catalog.</summary>
/// <remarks>Memoized bindings belong to this snapshot. No method registers application capabilities or allocates object IDs.</remarks>
public abstract partial class StateBindingContext : IStateModelResolver {
    private readonly Dictionary<(TypeExpr Type, int Version), StateSchemaBinding> _schemaBindings = [];

    public abstract bool TryGetCurrentModel(Type domainType, out StateModelBinding? model);
    public abstract StateValueBinding ResolveCurrentValue(Type domainType);
    public abstract StateValueBinding ResolveStoredValue(DurableFieldInfo slot);
    public abstract StateReaderBinding ResolveReader(DurableSchema schema);
    public abstract TypeExpr GetTypeExpr(Type domainType);
    public abstract Type GetDomainType(TypeExpr type);
    public abstract StateDefinitionBinding GetDefinition(string definitionId);
    public abstract bool TryGetSchema(TypeExpr type, int version, out DurableSchema? schema);

    public StateModelBinding ResolveCurrentModel(Type domainType) =>
        TryGetCurrentModel(domainType, out StateModelBinding? model) ? model! :
        throw new InvalidDataException($"No current model is registered for {domainType}.");

    public StateModelBinding ResolveCurrentModel(TypeExpr type) => ResolveCurrentModel(GetDomainType(type));

    public StateSchemaTemplate GetTemplate(string definitionId, int version) =>
        GetDefinition(definitionId).Templates.FirstOrDefault(template => template.Version == version) ??
        throw new InvalidDataException($"No retained Schema template exists for {definitionId} v{version}.");

    /// <summary>Checks stored layout against declaration patterns and derives exact free-value operands.</summary>
    public StateSchemaBinding BindSchema(DurableSchema schema) {
        ArgumentNullException.ThrowIfNull(schema);
        var key = (schema.Type, schema.Version);
        if (_schemaBindings.TryGetValue(key, out StateSchemaBinding? prior)) {
            if (!prior.Schema.Equals(schema)) { throw new InvalidDataException("The snapshot encountered conflicting complete Schemas for one key."); }
            CheckRegistered(schema);
            return prior;
        }
        CheckRegistered(schema);
        Dictionary<(TypeExpr Expression, int? Version), DurableFieldInfo> values = [];
        List<DurableFieldInfo> fields = [];
        TypeExpr[] parameters = Enumerable.Range(0, schema.Type.Arguments.Length).Select(TypeExpr.Parameter).ToArray();
        Match(schema, parameters, schema.Type.Arguments, values, fields, new(), 1);
        StateSchemaBinding result = new(this, schema, values, fields);
        _schemaBindings.Add(key, result);
        return result;
    }

    private void Match(DurableSchema actual, IReadOnlyList<TypeExpr> symbolicArguments,
        IReadOnlyList<TypeExpr> rootArguments,
        Dictionary<(TypeExpr Expression, int? Version), DurableFieldInfo> values,
        List<DurableFieldInfo>? flattened, HashSet<(DurableSchema Schema, TypeExpr Scope)> matched, int depth) {
        if (depth > 256) { throw new InvalidDataException("Exact Schema template traversal exceeded its depth bound."); }
        // Repeated fields can share a complete inline DAG. Revisit only when the
        // declaration variables have a different meaning in the owner's scope.
        if (!matched.Add((actual, TypeExpr.Named(actual.SchemaId, symbolicArguments.ToArray())))) { return; }
        StateSchemaTemplate template = GetTemplate(actual.SchemaId, actual.Version);
        if (template.Kind != actual.Kind || template.Arity != actual.Type.Arguments.Length ||
            template.Arity != symbolicArguments.Count || template.Fields.Length != actual.Fields.Length) {
            throw new InvalidDataException("Stored Schema kind, arity or declared fields do not match retained history.");
        }
        if ((template.BaseSchema is null) != (actual.BaseSchema is null)) {
            throw new InvalidDataException("Stored Schema ancestry does not match retained history.");
        }
        if (template.BaseSchema is { } basePattern) {
            TypeExpr symbolicBase = Substitute(basePattern.Type, symbolicArguments);
            DurableSchema actualBase = actual.BaseSchema!;
            if (actualBase.Type != Substitute(symbolicBase, rootArguments) || actualBase.Version != basePattern.Version) {
                throw new InvalidDataException("Stored exact base does not match its retained declaration pattern.");
            }
            Match(actualBase, symbolicBase.Arguments, rootArguments, values, flattened, matched, depth + 1);
        }
        for (int index = 0; index < template.Fields.Length; index++) {
            StateFieldTemplate pattern = template.Fields[index];
            DurableFieldInfo slot = actual.Fields[index];
            TypeExpr expression = Substitute(pattern.ValueType, symbolicArguments);
            TypeExpr nominal = Substitute(expression, rootArguments);
            if (slot.FieldId != pattern.FieldId || NominalType(slot) != nominal) {
                throw new InvalidDataException("A stored field does not match its retained type pattern.");
            }
            if (nominal.Kind == TypeExprKind.Named) {
                StateDefinitionBinding valueDefinition = GetDefinition(nominal.DefinitionId!);
                if (valueDefinition.Arity != nominal.Arguments.Length ||
                    (valueDefinition.Kind == SchemaKind.InlineValue) != (slot.TypeTag == TypeTag.InlineValue)) {
                    throw new InvalidDataException("A parameter or named field has the wrong declaration kind or generic arity.");
                }
            }
            if (pattern.InlineVersion is { } exactVersion) {
                if (slot.InlineSchema is not { } inline || inline.Version != exactVersion) {
                    throw new InvalidDataException("A stored inline dependency has the wrong exact version.");
                }
            } else if (pattern.ValueType.Kind != TypeExprKind.Parameter && slot.TypeTag == TypeTag.InlineValue) {
                throw new InvalidDataException("A named inline field requires a fixed historical version.");
            }
            // A parameter means the entire exact slot. Named dependent values also propagate
            // their inner variables through the callee's declaration scope.
            int? bindingVersion = expression.Kind == TypeExprKind.Parameter ? null : pattern.InlineVersion;
            var valueKey = (expression, bindingVersion);
            DurableFieldInfo canonical = WithFieldId(slot, 1);
            if (values.TryGetValue(valueKey, out DurableFieldInfo seen) && seen != canonical) {
                throw new InvalidDataException("Repeated use of one value expression has inconsistent exact slot semantics.");
            }
            values[valueKey] = canonical;
            if (slot.InlineSchema is { } nested) {
                if (expression.Kind == TypeExprKind.Named) {
                    Match(nested, expression.Arguments, rootArguments, values, null, matched, depth + 1);
                } else {
                    // A single T can itself be a generic struct. Its internal declaration
                    // variables belong to that value, not to the enclosing owner's ordinals.
                    BindSchema(nested);
                }
            }
            flattened?.Add(slot);
        }
    }

    internal void CheckRegistered(DurableSchema schema) {
        Dictionary<DurableSchema, int> heights = new(ReferenceEqualityComparer.Instance);
        Visit(schema, 1);

        int Visit(DurableSchema item, int depth) {
            if (depth > 256) { throw new InvalidDataException("Exact Schema layout exceeded its depth bound."); }
            if (heights.TryGetValue(item, out int knownHeight)) {
                if (depth + knownHeight - 1 > 256) { throw new InvalidDataException("Exact Schema layout exceeded its depth bound."); }
                return knownHeight;
            }
            if (TryGetSchema(item.Type, item.Version, out DurableSchema? registered) && !item.Equals(registered)) {
                throw new InvalidDataException("A derived Schema disagrees with the repository's registered exact definition.");
            }
            int height = 1;
            if (item.BaseSchema is { } ancestor) { height = Math.Max(height, Visit(ancestor, depth + 1) + 1); }
            foreach (DurableFieldInfo field in item.Fields) {
                if (field.InlineSchema is { } inline) { height = Math.Max(height, Visit(inline, depth + 1) + 1); }
            }
            heights.Add(item, height);
            return height;
        }
    }

    public static TypeExpr Substitute(TypeExpr expression, IReadOnlyList<TypeExpr> arguments) {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(arguments);
        if (expression.Kind == TypeExprKind.Parameter) {
            if (expression.ParameterOrdinal >= arguments.Count) { throw new InvalidDataException("A parameter is outside its declaration scope."); }
            return arguments[expression.ParameterOrdinal];
        }
        if (expression.Kind == TypeExprKind.Builtin || expression.Arguments.IsEmpty) { return expression; }
        return TypeExpr.Named(expression.DefinitionId!, expression.Arguments.Select(argument => Substitute(argument, arguments)).ToArray());
    }

    public static DurableFieldInfo WithFieldId(DurableFieldInfo slot, int fieldId) => slot.TypeTag switch {
        TypeTag.DurableReference => DurableFieldInfo.Reference(fieldId, slot.TargetType!),
        TypeTag.InlineValue => new(fieldId, TypeTag.InlineValue, inlineSchema: slot.InlineSchema),
        _ => new(fieldId, slot.TypeTag),
    };

    public static TypeExpr NominalType(DurableFieldInfo slot) => slot.TypeTag switch {
        TypeTag.DurableReference => slot.TargetType!,
        TypeTag.InlineValue => slot.InlineSchema!.Type,
        _ => TypeExpr.Builtin(slot.TypeTag),
    };
}

/// <summary>Validated exact layout and its declaration-scoped free-value bindings.</summary>
public sealed class StateSchemaBinding {
    private readonly StateBindingContext _context;
    private readonly IReadOnlyDictionary<(TypeExpr Expression, int? Version), DurableFieldInfo> _values;

    internal StateSchemaBinding(StateBindingContext context, DurableSchema schema,
        Dictionary<(TypeExpr Expression, int? Version), DurableFieldInfo> values, IEnumerable<DurableFieldInfo> fields) {
        _context = context;
        Schema = schema;
        _values = values;
        FieldSlots = fields.ToImmutableArray();
    }

    public DurableSchema Schema { get; }
    public ImmutableArray<DurableFieldInfo> FieldSlots { get; }
    public StateBindingContext Context => _context;
    internal IReadOnlyDictionary<(TypeExpr Expression, int? Version), DurableFieldInfo> ValueSlots => _values;

    public StateValueBinding GetValue(TypeExpr expression, int? inlineVersion = null) {
        ArgumentNullException.ThrowIfNull(expression);
        if (_values.TryGetValue((expression, inlineVersion), out DurableFieldInfo slot)) {
            return _context.ResolveStoredValue(slot);
        }
        if (!inlineVersion.HasValue) {
            DurableFieldInfo[] candidates = _values.Where(pair => pair.Key.Expression == expression).Select(static pair => pair.Value).Distinct().ToArray();
            if (candidates.Length == 1) { return _context.ResolveStoredValue(candidates[0]); }
        }
        throw new InvalidDataException("The exact Schema does not uniquely bind this value expression.");
    }

    internal bool TryGetSlot(TypeExpr expression, int? version, out DurableFieldInfo slot) =>
        _values.TryGetValue((expression, version), out slot);
}
