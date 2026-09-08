namespace Atelia.DurableGraph;

/// <summary>Fills an unpublished domain instance from a current frozen DTO.</summary>
public delegate void StateHydrator<TDomain, TState>(TDomain domain, in TState state, ObjectReadTable objects)
    where TDomain : DurableBase where TState : unmanaged;

/// <summary>Receives explicitly selected generated model families.</summary>
public interface IStateModelRegistration : IStateDefinitionRegistration {
    void Register(StateModelBinding model);
}

/// <summary>Code capabilities for one model family, independent of storage and publication.</summary>
/// <remarks>Use stable generated bindings. Callbacks must not publish partial objects or mutate input DTOs.</remarks>
public abstract class StateModelBinding {
    private readonly StateReaderBinding[] _readers;
    private readonly Func<DurableSchema, StateReaderBinding>? _sourceReaderResolver;

    private protected StateModelBinding(DurableSchema currentSchema, Type domainType, IEnumerable<StateReaderBinding> readers,
        Func<DurableSchema, StateReaderBinding>? sourceReaderResolver = null) {
        ArgumentNullException.ThrowIfNull(currentSchema);
        currentSchema.RequireReferenceObject();
        ArgumentNullException.ThrowIfNull(domainType);
        ArgumentNullException.ThrowIfNull(readers);
        CurrentSchema = currentSchema;
        DomainType = domainType;
        _sourceReaderResolver = sourceReaderResolver;
        _readers = readers.ToArray();
        HashSet<int> versions = [];
        foreach (StateReaderBinding reader in _readers) {
            if (reader is null || reader.Schema.Type != currentSchema.Type ||
                reader.Schema.Version > currentSchema.Version || !versions.Add(reader.Schema.Version)) {
                throw new ArgumentException("Model readers must have unique versions in the current Schema family.", nameof(readers));
            }
        }
        if (!_readers.Any(reader => reader.Schema.Equals(currentSchema))) {
            throw new ArgumentException("A model requires an exact current reader.", nameof(readers));
        }
        Readers = new ReaderList(_readers);
    }

    public DurableSchema CurrentSchema { get; }
    public Type DomainType { get; }
    public IReadOnlyList<StateReaderBinding> Readers { get; }

    internal void RequireSource(ObjectStateRecord source) {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Kind != ObjectStateKind.Durable || source.Schema!.Type != CurrentSchema.Type ||
            (!_readers.Any(reader => reader.Schema.Equals(source.Schema)) &&
             !(_sourceReaderResolver?.Invoke(source.Schema).Schema.Equals(source.Schema) ?? false))) {
            throw new InvalidDataException("The source does not match an exact Schema in this model family.");
        }
    }

    internal abstract ObjectStateRecord Normalize(ObjectStateRecord source);
    internal abstract void VisitReferences(ObjectStateRecord current, IStateReferenceVisitor visitor);
    internal abstract DurableBase Allocate();
    internal abstract void Hydrate(DurableBase domain, ObjectStateRecord current, ObjectReadTable objects);
    internal abstract ObjectId AddRoot(CaptureContext context, DurableBase domain);
    internal abstract ObjectStateRecord Capture(ObjectId id, DurableBase domain, CaptureContext context);
    internal abstract bool MatchesCapture(DurableSchema schema, Delegate capture, ICapturedStatePreparation? preparation);

    private sealed class ReaderList(StateReaderBinding[] readers) : IReadOnlyList<StateReaderBinding> {
        public int Count => readers.Length;
        public StateReaderBinding this[int index] => readers[index];
        public IEnumerator<StateReaderBinding> GetEnumerator() => ((IEnumerable<StateReaderBinding>)readers).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary>Strongly typed normalization, capture and restoration for one current model.</summary>
public sealed class StateModelBinding<TDomain, TState> : StateModelBinding
    where TDomain : DurableBase where TState : unmanaged {
    private readonly CapturedStatePreparation<TState> _preparation;
    private readonly Func<ObjectStateRecord, TState> _normalize;
    private readonly Func<TDomain> _allocate;
    private readonly StateHydrator<TDomain, TState> _hydrate;
    private readonly Func<TDomain, CaptureContext, TState> _capture;
    private readonly StateReferenceVisitor<TState> _visitReferences;

    public StateModelBinding(
        CapturedStatePreparation<TState> preparation,
        IEnumerable<StateReaderBinding> readers,
        Func<ObjectStateRecord, TState> normalize,
        Func<TDomain> allocate,
        StateHydrator<TDomain, TState> hydrate,
        Func<TDomain, CaptureContext, TState> capture,
        StateReferenceVisitor<TState> visitReferences,
        Func<DurableSchema, StateReaderBinding>? sourceReaderResolver = null)
        : base((preparation ?? throw new ArgumentNullException(nameof(preparation))).Schema, typeof(TDomain), readers, sourceReaderResolver) {
        ArgumentNullException.ThrowIfNull(normalize);
        ArgumentNullException.ThrowIfNull(allocate);
        ArgumentNullException.ThrowIfNull(hydrate);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(visitReferences);
        _preparation = preparation;
        _normalize = normalize;
        _allocate = allocate;
        _hydrate = hydrate;
        _capture = capture;
        _visitReferences = visitReferences;
    }

    internal override ObjectStateRecord Normalize(ObjectStateRecord source) {
        RequireSource(source);
        return new ObjectStateRecord(source.Id, CurrentSchema, _normalize(source), _preparation);
    }

    internal override void VisitReferences(ObjectStateRecord current, IStateReferenceVisitor visitor) {
        ArgumentNullException.ThrowIfNull(visitor);
        ((ICapturedStatePreparation)_preparation).Validate(current);
        TState state = current.GetState<TState>();
        _visitReferences(in state, visitor);
    }

    internal override DurableBase Allocate() {
        TDomain domain = _allocate();
        RequireDomain(domain);
        return domain;
    }

    internal override void Hydrate(DurableBase domain, ObjectStateRecord current, ObjectReadTable objects) {
        RequireDomain(domain);
        ArgumentNullException.ThrowIfNull(objects);
        ((ICapturedStatePreparation)_preparation).Validate(current);
        TState state = current.GetState<TState>();
        _hydrate((TDomain)domain, in state, objects);
    }

    internal override ObjectId AddRoot(CaptureContext context, DurableBase domain) {
        RequireDomain(domain);
        return context.AddRoot((TDomain)domain, CurrentSchema, _capture, _preparation);
    }

    internal override ObjectStateRecord Capture(ObjectId id, DurableBase domain, CaptureContext context) {
        RequireDomain(domain);
        return new ObjectStateRecord(id, CurrentSchema, _capture((TDomain)domain, context), _preparation);
    }

    internal override bool MatchesCapture(DurableSchema schema, Delegate capture, ICapturedStatePreparation? preparation) =>
        CurrentSchema.Equals(schema) && _capture.Equals(capture) && ReferenceEquals(_preparation, preparation);

    private static void RequireDomain(DurableBase? domain) {
        if (domain is null || domain.GetType() != typeof(TDomain)) {
            throw new InvalidDataException("Restoration requires a non-null exact domain type.");
        }
    }
}
