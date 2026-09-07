namespace Atelia.DurableGraph;

/// <summary>Fills an unpublished domain instance from a current frozen DTO.</summary>
public delegate void StateHydrator<TDomain, TState>(TDomain domain, in TState state, StringReadTable strings)
    where TDomain : DurableBase where TState : unmanaged;

/// <summary>Receives explicitly selected generated model families.</summary>
public interface IStateModelRegistration {
    void Register(StateModelBinding model);
}

/// <summary>Code capabilities for one model family, independent of storage and publication.</summary>
/// <remarks>Use stable generated bindings. Callbacks must not publish partial objects or mutate input DTOs.</remarks>
public abstract class StateModelBinding {
    private readonly StateReaderBinding[] _readers;

    private protected StateModelBinding(DurableSchema currentSchema, Type domainType, IEnumerable<StateReaderBinding> readers) {
        ArgumentNullException.ThrowIfNull(currentSchema);
        ArgumentNullException.ThrowIfNull(domainType);
        ArgumentNullException.ThrowIfNull(readers);
        CurrentSchema = currentSchema;
        DomainType = domainType;
        _readers = readers.ToArray();
        HashSet<int> versions = [];
        foreach (StateReaderBinding reader in _readers) {
            if (reader is null || reader.Schema.SchemaId != currentSchema.SchemaId ||
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

    internal void RequireSource(CapturedObject source) {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Kind != CapturedObjectKind.Durable ||
            !_readers.Any(reader => reader.Schema.Equals(source.Schema))) {
            throw new InvalidDataException("The source does not match an exact Schema in this model family.");
        }
    }

    internal abstract CapturedObject Normalize(CapturedObject source);
    internal abstract void ValidateReferences(CapturedObject current, StringReadTable strings);
    internal abstract DurableBase Allocate();
    internal abstract void Hydrate(DurableBase domain, CapturedObject current, StringReadTable strings);
    internal abstract uint AddRoot(CaptureContext context, DurableBase domain);

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
    private readonly Func<CapturedObject, TState> _normalize;
    private readonly Func<TDomain> _allocate;
    private readonly StateHydrator<TDomain, TState> _hydrate;
    private readonly Func<TDomain, CaptureContext, TState> _capture;
    private readonly StateStringReferenceValidator<TState> _validateReferences;

    public StateModelBinding(
        CapturedStatePreparation<TState> preparation,
        IEnumerable<StateReaderBinding> readers,
        Func<CapturedObject, TState> normalize,
        Func<TDomain> allocate,
        StateHydrator<TDomain, TState> hydrate,
        Func<TDomain, CaptureContext, TState> capture,
        StateStringReferenceValidator<TState> validateReferences)
        : base((preparation ?? throw new ArgumentNullException(nameof(preparation))).Schema, typeof(TDomain), readers) {
        ArgumentNullException.ThrowIfNull(normalize);
        ArgumentNullException.ThrowIfNull(allocate);
        ArgumentNullException.ThrowIfNull(hydrate);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(validateReferences);
        _preparation = preparation;
        _normalize = normalize;
        _allocate = allocate;
        _hydrate = hydrate;
        _capture = capture;
        _validateReferences = validateReferences;
    }

    internal override CapturedObject Normalize(CapturedObject source) {
        RequireSource(source);
        return new CapturedObject(source.Id, CurrentSchema, _normalize(source), _preparation);
    }

    internal override void ValidateReferences(CapturedObject current, StringReadTable strings) {
        ((ICapturedStatePreparation)_preparation).Validate(current);
        TState state = current.GetState<TState>();
        _validateReferences(in state, strings);
    }

    internal override DurableBase Allocate() {
        TDomain domain = _allocate();
        RequireDomain(domain);
        return domain;
    }

    internal override void Hydrate(DurableBase domain, CapturedObject current, StringReadTable strings) {
        RequireDomain(domain);
        ValidateReferences(current, strings);
        TState state = current.GetState<TState>();
        _hydrate((TDomain)domain, in state, strings);
    }

    internal override uint AddRoot(CaptureContext context, DurableBase domain) {
        RequireDomain(domain);
        return context.AddRoot((TDomain)domain, CurrentSchema, _capture, _preparation);
    }

    private static void RequireDomain(DurableBase? domain) {
        if (domain is null || domain.GetType() != typeof(TDomain)) {
            throw new InvalidDataException("Restoration requires a non-null exact domain type.");
        }
    }
}
