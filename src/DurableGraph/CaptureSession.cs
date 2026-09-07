using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph;

/// <summary>
/// Owns one in-memory accepted graph and a monotonic ID allocator. Single-threaded;
/// only one capture may be in flight, including a sealed but unresolved candidate.
/// </summary>
public sealed class CaptureSession {
    private ulong _nextObjectId;
    private Dictionary<object, uint> _bindings = new(ReferenceEqualityComparer.Instance);
    private CaptureContext? _pending;
    private bool _preparing;
    private bool _beginningCapture;

    public CaptureSession() : this(1) { }

    // Allows the finite ID domain to be tested without allocating billions of objects.
    internal CaptureSession(uint firstObjectId) {
        ArgumentOutOfRangeException.ThrowIfZero(firstObjectId);
        _nextObjectId = firstObjectId;
    }

    // Identity-only import for the controlled loader. Does not fabricate Capture provenance.
    internal CaptureSession(ulong firstObjectId, IReadOnlyDictionary<object, uint> bindings) {
        ArgumentNullException.ThrowIfNull(bindings);
        if (firstObjectId == 0 || firstObjectId > (ulong)uint.MaxValue + 1) {
            throw new ArgumentOutOfRangeException(nameof(firstObjectId));
        }
        HashSet<uint> ids = [];
        foreach ((object instance, uint id) in bindings) {
            if (id == 0 || id >= firstObjectId || !ids.Add(id)) {
                throw new ArgumentException("Imported IDs must be unique, nonzero and below the allocation cursor.", nameof(bindings));
            }
            _bindings.Add(instance, id);
        }
        _nextObjectId = firstObjectId;
    }

    public CapturedGraph? Current { get; private set; }

    /// <summary>
    /// Prepares all candidate bodies against Current without resolving the candidate or allocating IDs.
    /// The result identifies in-memory sources only; it does not certify a persistent Parent Revision.
    /// </summary>
    public PreparedCapturedGraph Prepare(CapturedGraph candidate) {
        RequireCandidate(candidate);
        _preparing = true;
        try {
            CapturedGraph? previous = Current;
            Dictionary<uint, CapturedObject> priorObjects = previous?.Objects.ToDictionary(static item => item.Id) ?? [];

            return new PreparedCapturedGraph(previous, candidate, PrepareObjects(candidate, priorObjects));
        }
        finally {
            _preparing = false;
        }
    }

    // Loaded baselines describe source-live rows, not a previously captured graph.
    internal IReadOnlyList<PreparedCapturedObject> PrepareAgainst(
        CapturedGraph candidate, IReadOnlyDictionary<uint, CapturedObject> previous) {
        RequireCandidate(candidate);
        ArgumentNullException.ThrowIfNull(previous);
        _preparing = true;
        try {
            return PrepareObjects(candidate, previous);
        }
        finally {
            _preparing = false;
        }
    }

    private static List<PreparedCapturedObject> PrepareObjects(
        CapturedGraph candidate, IReadOnlyDictionary<uint, CapturedObject> previous) {
        // Validate the entire comparison set before invoking any user body operation.
        foreach (CapturedObject current in candidate.Objects) {
            previous.TryGetValue(current.Id, out CapturedObject? prior);
            ValidatePreparation(current, prior);
        }
        List<PreparedCapturedObject> objects = new(candidate.Objects.Count);
        foreach (CapturedObject current in candidate.Objects) {
            previous.TryGetValue(current.Id, out CapturedObject? prior);
            PreparedBase body = current.Kind == CapturedObjectKind.String
                ? StringPayloadCodec.PrepareBase(current.StringContent)
                : current.Preparation!.PrepareBase(current);
            PreparedDelta? delta = prior is not null && current.Kind == CapturedObjectKind.Durable
                ? current.Preparation!.PrepareDelta(prior, current)
                : null;
            objects.Add(new PreparedCapturedObject(current, prior, body, delta));
        }
        return objects;
    }

    public CaptureContext BeginCapture() => BeginCapture([]);

    /// <summary>Begins a capture with an explicit, frozen directory of current model bindings.</summary>
    public CaptureContext BeginCapture(IEnumerable<StateModelBinding> models) {
        RequireNotPreparing();
        if (_pending is not null || _beginningCapture) {
            throw new InvalidOperationException("Resolve the current capture before beginning another.");
        }
        ArgumentNullException.ThrowIfNull(models);
        _beginningCapture = true;
        try {
            Dictionary<Type, StateModelBinding> types = [];
            Dictionary<string, StateModelBinding> families = new(StringComparer.Ordinal);
            foreach (StateModelBinding model in models) {
                ArgumentNullException.ThrowIfNull(model);
                if ((types.TryGetValue(model.DomainType, out StateModelBinding? byType) && !ReferenceEquals(byType, model)) ||
                    (families.TryGetValue(model.CurrentSchema.SchemaId, out StateModelBinding? byFamily) && !ReferenceEquals(byFamily, model))) {
                    throw new ArgumentException("Each exact domain type and Schema family requires one stable model binding.", nameof(models));
                }
                types.TryAdd(model.DomainType, model);
                families.TryAdd(model.CurrentSchema.SchemaId, model);
            }
            _pending = new CaptureContext(this, types);
            return _pending;
        } finally {
            _beginningCapture = false;
        }
    }

    /// <summary>Installs the actual candidate and its live bindings without recapturing domain data.</summary>
    public void Accept(CapturedGraph candidate) {
        CaptureContext context = RequireCandidate(candidate);
        Dictionary<object, uint> bindings = context.DetachBindings();
        _bindings = bindings;
        Current = candidate;
        context.Resolve();
    }

    /// <summary>Discards candidate bindings. IDs already allocated remain consumed.</summary>
    public void Discard(CapturedGraph candidate) {
        RequireCandidate(candidate).Resolve();
    }

    internal uint GetOrAllocateId(object source) {
        if (_bindings.TryGetValue(source, out uint id)) {
            return id;
        }
        if (_nextObjectId > uint.MaxValue) {
            throw new InvalidOperationException("The session's object ID space is exhausted.");
        }
        return (uint)_nextObjectId++;
    }

    internal void Release(CaptureContext context) {
        if (ReferenceEquals(_pending, context)) {
            _pending = null;
        }
    }

    internal void RequireNotPreparing() {
        if (_preparing) {
            throw new InvalidOperationException("The capture session cannot be changed or prepared recursively during preparation.");
        }
    }

    private static void ValidatePreparation(CapturedObject current, CapturedObject? prior) {
        if (prior is not null && current.Kind != prior.Kind) {
            throw new InvalidOperationException("An existing captured ID changed content kind.");
        }
        if (current.Kind == CapturedObjectKind.String) {
            if (prior is not null && !ReferenceEquals(current.StringContent, prior.StringContent)) {
                throw new InvalidOperationException("An existing string ID changed reference identity.");
            }
            return;
        }
        ICapturedStatePreparation preparation = current.Preparation
            ?? throw new InvalidOperationException("The captured durable object has no preparation binding.");
        preparation.Validate(current);
        if (prior is not null) {
            if (!ReferenceEquals(preparation, prior.Preparation)) {
                throw new InvalidOperationException("An existing durable object changed preparation binding.");
            }
            preparation.Validate(prior);
        }
    }

    private CaptureContext RequireCandidate(CapturedGraph candidate) {
        RequireNotPreparing();
        ArgumentNullException.ThrowIfNull(candidate);
        if (_pending is null || !ReferenceEquals(_pending.Candidate, candidate)) {
            throw new InvalidOperationException("The candidate is not the session's pending unresolved candidate.");
        }
        return _pending;
    }
}
