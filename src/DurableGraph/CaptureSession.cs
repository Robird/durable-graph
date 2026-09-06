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

    public CaptureSession() : this(1) { }

    // Allows the finite ID domain to be tested without allocating billions of objects.
    internal CaptureSession(uint firstObjectId) {
        ArgumentOutOfRangeException.ThrowIfZero(firstObjectId);
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

            // Validate the entire comparison set before invoking any user body operation.
            foreach (CapturedObject current in candidate.Objects) {
                priorObjects.TryGetValue(current.Id, out CapturedObject? prior);
                ValidatePreparation(current, prior);
            }

            List<PreparedCapturedObject> objects = new(candidate.Objects.Count);
            foreach (CapturedObject current in candidate.Objects) {
                priorObjects.TryGetValue(current.Id, out CapturedObject? prior);
                PreparedBase body = current.Kind == CapturedObjectKind.String
                    ? StringPayloadCodec.PrepareBase(current.StringContent)
                    : current.Preparation!.PrepareBase(current);
                PreparedDelta? delta = prior is not null && current.Kind == CapturedObjectKind.Durable
                    ? current.Preparation!.PrepareDelta(prior, current)
                    : null;
                objects.Add(new PreparedCapturedObject(current, prior, body, delta));
            }
            return new PreparedCapturedGraph(previous, candidate, objects);
        }
        finally {
            _preparing = false;
        }
    }

    public CaptureContext BeginCapture() {
        if (_pending is not null) {
            throw new InvalidOperationException("Resolve the current capture before beginning another.");
        }
        _pending = new CaptureContext(this);
        return _pending;
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
            throw new InvalidOperationException("The candidate is not the session's current unresolved capture.");
        }
        return _pending;
    }
}
