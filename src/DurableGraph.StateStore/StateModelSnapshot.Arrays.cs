namespace Atelia.DurableGraph.StateStore;

internal sealed partial class StateModelSnapshot {
    private readonly Dictionary<Type, ArrayObjectBinding> _currentArrays = [];
    private readonly Dictionary<ArrayLayout, ObjectReaderBinding> _arrayReaders = [];

    public override bool TryGetCurrentObjectBinding(Type domainType, out ObjectBinding? binding) {
        RequireClosed(domainType);
        if (!domainType.IsArray) { return base.TryGetCurrentObjectBinding(domainType, out binding); }
        if (_currentArrays.TryGetValue(domainType, out ArrayObjectBinding? prior)) {
            CheckArrayLayout(prior.ArrayLayout);
            binding = prior;
            return true;
        }
        var active = ("array", (object)domainType);
        Begin(active);
        try {
            // A reference element binds only its slot. In particular, a struct containing an
            // array of itself does not recursively close that referenced array's body here.
            StateValueBinding element = ResolveCurrentValue(domainType.GetElementType()!);
            TypeExpr type = GetTypeExpr(domainType);
            ArrayLayout layout = new(type.Kind, element.Slot);
            ArrayObjectBinding result = ArrayObjectBinding.Create(domainType, layout, element,
                (target, source) => NormalizeArray(source, target));
            if (result.DomainType != domainType || !result.ArrayLayout.Equals(layout)) {
                throw new InvalidDataException("An array factory returned another current type or exact layout.");
            }
            CheckArrayLayout(layout);
            _currentArrays.Add(domainType, result);
            binding = result;
            return true;
        } finally { _closing.Remove(active); }
    }

    public override ObjectReaderBinding ResolveObjectReader(ObjectLayout layout) {
        ArgumentNullException.ThrowIfNull(layout);
        if (layout.Kind != ObjectStateKind.Array) { return base.ResolveObjectReader(layout); }
        ArrayLayout array = layout.Array!;
        if (_arrayReaders.TryGetValue(array, out ObjectReaderBinding? prior)) {
            CheckArrayLayout(array);
            return prior;
        }
        var active = ("array reader", (object)array);
        Begin(active);
        try {
            // Historical readers resolve retained element state operations only. A deleted
            // historical domain struct need not be present as a current CLR declaration.
            CheckArrayLayout(array);
            StateValueBinding element = ResolveStoredValue(array.ElementSlot);
            ObjectReaderBinding result = ArrayStateReader.Create(array, element);
            if (!result.Layout.Equals(layout)) {
                throw new InvalidDataException("An array reader factory returned another exact layout.");
            }
            CheckArrayLayout(array);
            _arrayReaders.Add(array, result);
            return result;
        } finally { _closing.Remove(active); }
    }

    private void CheckArrayLayout(ArrayLayout layout) {
        DurableFieldInfo element = layout.ElementSlot;
        CheckArrayNominalType(NominalType(element), element.TypeTag switch {
            TypeTag.ObjectReference => SchemaKind.ReferenceObject,
            TypeTag.InlineValue => SchemaKind.InlineValue,
            _ => null,
        });
        if (layout.ElementSlot.InlineSchema is { } inline) { CheckRegistered(inline); }
    }

    private void CheckArrayNominalType(TypeExpr type, SchemaKind? requiredKind = null) {
        if (!type.IsClosed) { throw new InvalidDataException("An array element requires a closed nominal type."); }
        if (type.IsArray) {
            if (requiredKind == SchemaKind.InlineValue) { throw new InvalidDataException("An array cannot be an inline value."); }
            // The referenced array carries its own exact element slot in its Base. Its
            // nominal element can be either a reference family or an inline family.
            CheckArrayNominalType(type.ElementType!);
            return;
        }
        if (type.Kind == TypeExprKind.Builtin) {
            if (requiredKind.HasValue) { throw new InvalidDataException("A builtin cannot use a named object or inline slot."); }
            return;
        }
        if (type.Kind != TypeExprKind.Named) { throw new InvalidDataException("Unsupported array element type constructor."); }

        (SchemaKind kind, int arity) = GetArrayNominalDefinition(type.DefinitionId!);
        if (arity != type.Arguments.Length || (requiredKind.HasValue && requiredKind.Value != kind)) {
            throw new InvalidDataException($"Array element type {type} has the wrong declaration kind or generic arity.");
        }
        foreach (TypeExpr argument in type.Arguments) { CheckArrayNominalType(argument); }
    }

    private (SchemaKind Kind, int Arity) GetArrayNominalDefinition(string definitionId) {
        if (_definitions.TryGetValue(definitionId, out StateDefinitionBinding? definition)) {
            return (definition.Kind, definition.Arity);
        }

        // Legacy explicit models/readers predate declaration templates. Their registered
        // exact Schemas still witness nominal kind and arity, without closing a CLR body
        // or demanding a current domain type for a retained historical reference.
        Stack<DurableSchema> pending = new(_models.Values.Select(static model => model.CurrentSchema)
            .Concat(_readers.Values.Select(static reader => reader.Schema)));
        HashSet<DurableSchema> seen = new(ReferenceEqualityComparer.Instance);
        (SchemaKind Kind, int Arity)? found = null;
        while (pending.TryPop(out DurableSchema? schema)) {
            if (!seen.Add(schema)) { continue; }
            if (schema.SchemaId == definitionId) {
                var metadata = (schema.Kind, schema.Type.Arguments.Length);
                if (found.HasValue && found.Value != metadata) {
                    throw new InvalidDataException($"Registered Schemas disagree about the nominal declaration {definitionId}.");
                }
                found = metadata;
            }
            if (schema.BaseSchema is { } ancestor) { pending.Push(ancestor); }
            foreach (DurableFieldInfo field in schema.Fields) {
                if (field.InlineSchema is { } inline) { pending.Push(inline); }
            }
        }
        return found ?? throw new InvalidDataException($"No retained declaration metadata is registered for array element {definitionId}.");
    }
}
