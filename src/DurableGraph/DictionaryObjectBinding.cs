using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph;

/// <summary>Experimental DB-054 projection of exact BCL Dictionary contents and supported comparer semantics.</summary>
/// <remarks>Comparison policy is per instance. Arbitrary comparers and struct keys require a future explicit recovery contract.</remarks>
// TODO(DB-054): revisit composite keys and whether the BCL facade remains suitable;
// see docs/design-branches/0054-dictionary-content-object-slice.md sections 2.1 and 2.2.
public abstract class DictionaryObjectBinding : ObjectBinding {
    private protected DictionaryObjectBinding(Type domainType, DictionaryLayout layout, StateValueBinding key, StateValueBinding value,
        Func<DictionaryObjectBinding, ObjectStateRecord, ObjectStateRecord>? normalize)
        : base(domainType, ObjectLayout.ForDictionary(layout)) {
        DictionaryLayout = layout;
        KeyBinding = key;
        ValueBinding = value;
        NormalizeState = normalize;
    }

    public DictionaryLayout DictionaryLayout { get; }
    public StateValueBinding KeyBinding { get; }
    public StateValueBinding ValueBinding { get; }
    private protected Func<DictionaryObjectBinding, ObjectStateRecord, ObjectStateRecord>? NormalizeState { get; }

    public static DictionaryObjectBinding Create(Type domainType, DictionaryLayout layout, StateValueBinding key, StateValueBinding value,
        Func<DictionaryObjectBinding, ObjectStateRecord, ObjectStateRecord>? normalize = null) {
        ArgumentNullException.ThrowIfNull(domainType);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        if (!domainType.IsGenericType || domainType.GetGenericTypeDefinition() != typeof(Dictionary<,>) || domainType.ContainsGenericParameters ||
            domainType.GetGenericArguments()[0] != key.DomainType || domainType.GetGenericArguments()[1] != value.DomainType ||
            key.ProjectionType is null || value.ProjectionType is null ||
            layout.KeySlot != StateBindingContext.WithFieldId(key.Slot, 1) || layout.ValueSlot != StateBindingContext.WithFieldId(value.Slot, 2)) {
            throw new ArgumentException("The current Dictionary type and both bindings must match the complete layout.");
        }
        DictionaryKeyPolicy.RequireCurrentKey(key.DomainType!, layout.KeySlot);
        return (DictionaryObjectBinding)Activator.CreateInstance(typeof(DictionaryObjectBinding<,,,,,,,>).MakeGenericType(
            key.DomainType!, key.StateType, key.ProjectionType, key.StateOpsType,
            value.DomainType!, value.StateType, value.ProjectionType, value.StateOpsType), domainType, layout, key, value, normalize)!;
    }

    internal abstract ObjectStateRecord CreateStateRecord(ObjectId id, object frozen);
    internal abstract void ValidateLookupKeys(ObjectStateRecord state, Func<ObjectId, ObjectStateRecord> resolve);
}

internal sealed class DictionaryObjectBinding<KDomain, K, KProjection, KOps, VDomain, V, VProjection, VOps>
    : DictionaryObjectBinding, ICapturedStatePreparation
    where KDomain : notnull where K : unmanaged where V : unmanaged
    where KProjection : IValueProjection<KDomain, K> where KOps : IStateOps<K>
    where VProjection : IValueProjection<VDomain, V> where VOps : IStateOps<V> {
    public DictionaryObjectBinding(Type domainType, DictionaryLayout layout, StateValueBinding key, StateValueBinding value,
        Func<DictionaryObjectBinding, ObjectStateRecord, ObjectStateRecord>? normalize) : base(domainType, layout, key, value, normalize) { }

    internal override ObjectStateRecord Capture(ObjectId id, object domain, CaptureContext context) {
        Dictionary<KDomain, VDomain> dictionary = RequireDomain(domain);
        DictionaryComparerKind kind = DictionaryKeyPolicy.Identify(dictionary.Comparer, DictionaryLayout.KeySlot);
        DictionaryEntryState<K, V>[] entries = new DictionaryEntryState<K, V>[dictionary.Count];
        int index = 0;
        foreach (KeyValuePair<KDomain, VDomain> entry in dictionary) {
            KDomain key = entry.Key;
            VDomain value = entry.Value;
            K keyState = KProjection.Capture(in key, context, DictionaryLayout.KeySlot);
            V valueState = VProjection.Capture(in value, context, DictionaryLayout.ValueSlot);
            entries[index++] = new(keyState, valueState);
        }
        return CreateStateRecord(id, new FrozenDictionaryState<K, V>(kind, entries, takeOwnership: true));
    }

    internal override ObjectStateRecord Normalize(ObjectStateRecord source) {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Kind != ObjectStateKind.Dictionary || source.Layout.Type != CurrentLayout.Type) {
            throw new InvalidDataException("A Dictionary can only normalize within its nominal Dictionary type.");
        }
        if (NormalizeState is null && source.Layout.Equals(CurrentLayout)) {
            return CreateStateRecord(source.Id, source.GetDictionaryState<K, V>());
        }
        if (NormalizeState is null) { throw new InvalidDataException("The historical Dictionary requires explicit key/value upgrade capabilities."); }
        ObjectStateRecord result = NormalizeState(this, source);
        ((ICapturedStatePreparation)this).Validate(result);
        IFrozenDictionaryState oldState = (IFrozenDictionaryState)source.Content;
        FrozenDictionaryState<K, V> newState = result.GetDictionaryState<K, V>();
        if (result.Id != source.Id || newState.Count != oldState.Count || newState.ComparerKind != oldState.ComparerKind) {
            throw new InvalidDataException("Dictionary normalization must preserve identity, count and comparison policy.");
        }
        return result;
    }

    internal override ObjectStateRecord CreateStateRecord(ObjectId id, object frozen) {
        if (id.IsNull || frozen is not FrozenDictionaryState<K, V> state) {
            throw new InvalidDataException("Dictionary state does not match its target key/value representation.");
        }
        DictionaryStateBody<K, V, KOps, VOps>.ValidateLocal(state, DictionaryLayout);
        return new(id, DictionaryLayout, state, this);
    }

    internal override void VisitReferences(ObjectStateRecord current, IStateReferenceVisitor visitor) {
        ((ICapturedStatePreparation)this).Validate(current);
        DictionaryStateBody<K, V, KOps, VOps>.VisitReferences(current.GetDictionaryState<K, V>(), visitor, DictionaryLayout);
    }

    internal override void ValidateLookupKeys(ObjectStateRecord state, Func<ObjectId, ObjectStateRecord> resolve) {
        ((ICapturedStatePreparation)this).Validate(state);
        DictionaryStateBody<K, V, KOps, VOps>.ValidateLookupKeys(state.GetDictionaryState<K, V>(), DictionaryLayout, resolve);
    }

    internal override object Allocate(ObjectStateRecord current) {
        ((ICapturedStatePreparation)this).Validate(current);
        FrozenDictionaryState<K, V> state = current.GetDictionaryState<K, V>();
        return new Dictionary<KDomain, VDomain>(state.Count, DictionaryKeyPolicy.CreateComparer<KDomain>(state.ComparerKind, DictionaryLayout.KeySlot));
    }

    internal override void Hydrate(object domain, ObjectStateRecord current, ObjectReadTable objects) {
        Dictionary<KDomain, VDomain> dictionary = RequireDomain(domain);
        ((ICapturedStatePreparation)this).Validate(current);
        FrozenDictionaryState<K, V> state = current.GetDictionaryState<K, V>();
        if (dictionary.Count != 0 || DictionaryKeyPolicy.Identify(dictionary.Comparer, DictionaryLayout.KeySlot) != state.ComparerKind) {
            throw new InvalidDataException("Dictionary hydration requires an empty instance with the recorded comparison policy.");
        }
        foreach (ref readonly DictionaryEntryState<K, V> entry in state.Entries) {
            KDomain key = default!;
            VDomain value = default!;
            K keyState = entry.Key;
            V valueState = entry.Value;
            KProjection.Hydrate(ref key, in keyState, objects, DictionaryLayout.KeySlot);
            VProjection.Hydrate(ref value, in valueState, objects, DictionaryLayout.ValueSlot);
            if (key is null || !dictionary.TryAdd(key, value)) { throw new InvalidDataException("Dictionary restoration encountered a null or duplicate lookup key."); }
        }
    }

    void ICapturedStatePreparation.Validate(ObjectStateRecord item) {
        if (!CurrentLayout.Equals(item.Layout) || item.Content is not FrozenDictionaryState<K, V> state) {
            throw new InvalidOperationException("Preparation requires the exact Dictionary layout and state.");
        }
        // Keep whole-candidate metadata preflight free of body calls. Capture/read,
        // preparation and complete graph validation perform content validation.
        DictionaryKeyPolicy.RequireStoredPolicy(state.ComparerKind, DictionaryLayout.KeySlot);
    }

    PreparedBaseBody ICapturedStatePreparation.PrepareBase(ObjectStateRecord current) {
        ((ICapturedStatePreparation)this).Validate(current);
        return DictionaryStateBody<K, V, KOps, VOps>.PrepareBase(current.GetDictionaryState<K, V>(), DictionaryLayout);
    }

    PreparedDeltaBody ICapturedStatePreparation.PrepareDelta(ObjectStateRecord previous, ObjectStateRecord current) {
        ((ICapturedStatePreparation)this).Validate(previous);
        ((ICapturedStatePreparation)this).Validate(current);
        return DictionaryStateBody<K, V, KOps, VOps>.PrepareDelta(previous.GetDictionaryState<K, V>(), current.GetDictionaryState<K, V>(), DictionaryLayout);
    }

    private Dictionary<KDomain, VDomain> RequireDomain(object domain) {
        if (domain is null || domain.GetType() != DomainType) { throw new InvalidDataException("Dictionary projection requires the exact registered BCL type."); }
        return (Dictionary<KDomain, VDomain>)domain;
    }
}
