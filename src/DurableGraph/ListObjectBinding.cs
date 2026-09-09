using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph;

/// <summary>Current BCL List projection assembled once from closed element capabilities.</summary>
public abstract class ListObjectBinding : ObjectBinding {
    private protected ListObjectBinding(Type domainType, ListLayout layout, StateValueBinding element,
        Func<ListObjectBinding, ObjectStateRecord, ObjectStateRecord>? normalize)
        : base(domainType, ObjectLayout.ForList(layout)) {
        ListLayout = layout;
        ElementBinding = element;
        NormalizeState = normalize;
    }

    public ListLayout ListLayout { get; }
    public StateValueBinding ElementBinding { get; }
    private protected Func<ListObjectBinding, ObjectStateRecord, ObjectStateRecord>? NormalizeState { get; }

    public static ListObjectBinding Create(Type domainListType, ListLayout layout, StateValueBinding element,
        Func<ListObjectBinding, ObjectStateRecord, ObjectStateRecord>? normalize = null) {
        ArgumentNullException.ThrowIfNull(domainListType);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(element);
        if (!domainListType.IsGenericType || domainListType.GetGenericTypeDefinition() != typeof(List<>) ||
            domainListType.ContainsGenericParameters || domainListType.GetGenericArguments()[0] != element.DomainType ||
            element.ProjectionType is null || layout.ElementSlot != StateBindingContext.WithFieldId(element.Slot, 1)) {
            throw new ArgumentException("The current List type and element binding must match the complete List layout.");
        }
        return (ListObjectBinding)Activator.CreateInstance(
            typeof(ListObjectBinding<,,,>).MakeGenericType(element.DomainType!, element.StateType,
                element.ProjectionType, element.StateOpsType), domainListType, layout, element, normalize)!;
    }

    internal abstract ObjectStateRecord CreateStateRecord(ObjectId id, object frozen);
}

internal sealed class ListObjectBinding<TDomain, TState, TProjection, TOps> : ListObjectBinding, ICapturedStatePreparation
    where TState : unmanaged where TProjection : IValueProjection<TDomain, TState> where TOps : IStateOps<TState> {
    public ListObjectBinding(Type domainType, ListLayout layout, StateValueBinding element,
        Func<ListObjectBinding, ObjectStateRecord, ObjectStateRecord>? normalize)
        : base(domainType, layout, element, normalize) { }

    internal override ObjectStateRecord Capture(ObjectId id, object domain, CaptureContext context) {
        List<TDomain> list = RequireDomain(domain);
        TState[] elements = new TState[list.Count];
        for (int index = 0; index < elements.Length; index++) {
            TDomain value = list[index];
            elements[index] = TProjection.Capture(in value, context, ListLayout.ElementSlot);
        }
        return CreateStateRecord(id, new FrozenListState<TState>(elements, takeOwnership: true));
    }

    internal override ObjectStateRecord Normalize(ObjectStateRecord source) {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Kind != ObjectStateKind.List || source.Layout.Type != CurrentLayout.Type) {
            throw new InvalidDataException("A List can only normalize within its nominal List type.");
        }
        if (NormalizeState is null && source.Layout.Equals(CurrentLayout)) {
            return CreateStateRecord(source.Id, source.GetListState<TState>());
        }
        if (NormalizeState is null) { throw new InvalidDataException("The historical List requires an explicit element upgrade capability."); }
        ObjectStateRecord result = NormalizeState(this, source);
        ((ICapturedStatePreparation)this).Validate(result);
        if (result.Id != source.Id || result.GetListState<TState>().Count != ((IFrozenListState)source.Content).Count) {
            throw new InvalidDataException("List normalization must preserve object identity and count.");
        }
        return result;
    }

    internal override ObjectStateRecord CreateStateRecord(ObjectId id, object frozen) {
        if (id.IsNull || frozen is not FrozenListState<TState> state) {
            throw new InvalidDataException("The List state does not match its target element representation.");
        }
        return new(id, ListLayout, state, this);
    }

    internal override void VisitReferences(ObjectStateRecord current, IStateReferenceVisitor visitor) {
        ((ICapturedStatePreparation)this).Validate(current);
        ListStateBody<TState, TOps>.VisitReferences(current.GetListState<TState>(), visitor, ListLayout);
    }

    internal override object Allocate(ObjectStateRecord current) {
        ((ICapturedStatePreparation)this).Validate(current);
        return new List<TDomain>(current.GetListState<TState>().Count);
    }

    internal override void Hydrate(object domain, ObjectStateRecord current, ObjectReadTable objects) {
        List<TDomain> list = RequireDomain(domain);
        ((ICapturedStatePreparation)this).Validate(current);
        if (list.Count != 0) { throw new InvalidDataException("List hydration requires an empty allocated instance."); }
        foreach (ref readonly TState element in current.GetListState<TState>().Elements) {
            TDomain value = default!;
            TProjection.Hydrate(ref value, in element, objects, ListLayout.ElementSlot);
            list.Add(value);
        }
    }

    void ICapturedStatePreparation.Validate(ObjectStateRecord item) {
        if (!CurrentLayout.Equals(item.Layout) || item.Content is not FrozenListState<TState>) {
            throw new InvalidOperationException("The preparation binding requires its exact List layout and state.");
        }
    }

    PreparedBaseBody ICapturedStatePreparation.PrepareBase(ObjectStateRecord current) {
        ((ICapturedStatePreparation)this).Validate(current);
        return ListStateBody<TState, TOps>.PrepareBase(current.GetListState<TState>(), ListLayout);
    }

    PreparedDeltaBody ICapturedStatePreparation.PrepareDelta(ObjectStateRecord previous, ObjectStateRecord current) {
        ((ICapturedStatePreparation)this).Validate(previous);
        ((ICapturedStatePreparation)this).Validate(current);
        return ListStateBody<TState, TOps>.PrepareDelta(previous.GetListState<TState>(), current.GetListState<TState>(), ListLayout);
    }

    private List<TDomain> RequireDomain(object domain) {
        if (domain is null || domain.GetType() != DomainType) {
            throw new InvalidDataException("List projection requires the exact registered BCL List type.");
        }
        return (List<TDomain>)domain;
    }
}
