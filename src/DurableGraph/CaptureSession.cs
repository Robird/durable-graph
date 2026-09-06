namespace Atelia.DurableGraph;

/// <summary>
/// Owns one in-memory accepted graph and a monotonic ID allocator. Single-threaded;
/// only one capture may be in flight, including a sealed but unresolved candidate.
/// </summary>
public sealed class CaptureSession {
    private ulong _nextObjectId;
    private Dictionary<object, uint> _bindings = new(ReferenceEqualityComparer.Instance);
    private CaptureContext? _pending;

    public CaptureSession() : this(1) { }

    // Allows the finite ID domain to be tested without allocating billions of objects.
    internal CaptureSession(uint firstObjectId) {
        ArgumentOutOfRangeException.ThrowIfZero(firstObjectId);
        _nextObjectId = firstObjectId;
    }

    public CapturedGraph? Current { get; private set; }

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

    private CaptureContext RequireCandidate(CapturedGraph candidate) {
        ArgumentNullException.ThrowIfNull(candidate);
        if (_pending is null || !ReferenceEquals(_pending.Candidate, candidate)) {
            throw new InvalidOperationException("The candidate is not the session's current unresolved capture.");
        }
        return _pending;
    }
}
