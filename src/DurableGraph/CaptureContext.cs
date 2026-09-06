namespace Atelia.DurableGraph;

/// <summary>
/// Registers typed roots and captures their state when sealed. Keep the domain view stable
/// from registration through Seal. Generated adapters pair the domain, schema and DTO types.
/// </summary>
public sealed class CaptureContext : IDisposable {
    private enum Phase { Registering, Capturing, Sealed, Resolved }

    private CaptureSession? _session;
    private Phase _phase;
    private Dictionary<object, uint> _bindings = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, RootCapture> _roots = new(ReferenceEqualityComparer.Instance);
    private readonly List<RootCapture> _queue = [];
    private readonly List<uint> _rootIds = [];
    private readonly List<CapturedObject> _objects = [];

    internal CaptureContext(CaptureSession session) {
        _session = session;
    }

    internal CapturedGraph? Candidate { get; private set; }

    /// <summary>Registers an exact concrete root; null contributes a zero root ID.</summary>
    public uint AddRoot<TDomain, TState>(
        TDomain? value,
        DurableSchema schema,
        Func<TDomain, CaptureContext, TState> capture)
        where TDomain : DurableBase
        where TState : unmanaged => AddRootCore(value, schema, capture, preparation: null);

    /// <summary>Registers an exact root together with stable frozen-state preparation operations.</summary>
    public uint AddRoot<TDomain, TState>(
        TDomain? value,
        DurableSchema schema,
        Func<TDomain, CaptureContext, TState> capture,
        CapturedStatePreparation<TState> preparation)
        where TDomain : DurableBase
        where TState : unmanaged {
        try {
            ArgumentNullException.ThrowIfNull(preparation);
            return AddRootCore(value, schema, capture, preparation);
        }
        catch {
            AbortBuild();
            throw;
        }
    }

    private uint AddRootCore<TDomain, TState>(
        TDomain? value,
        DurableSchema schema,
        Func<TDomain, CaptureContext, TState> capture,
        CapturedStatePreparation<TState>? preparation)
        where TDomain : DurableBase
        where TState : unmanaged {
        try {
            RequirePhase(Phase.Registering);
            ArgumentNullException.ThrowIfNull(schema);
            ArgumentNullException.ThrowIfNull(capture);
            if (preparation is not null && !schema.Equals(preparation.Schema)) {
                throw new ArgumentException("Preparation requires the registered root's exact Schema.", nameof(preparation));
            }
            if (value is null) {
                _rootIds.Add(0);
                return 0;
            }
            if (value.GetType() != typeof(TDomain)) {
                throw new ArgumentException("Root capture requires the exact concrete domain type.", nameof(value));
            }
            if (_roots.TryGetValue(value, out RootCapture? existing)) {
                if (existing is not RootCapture<TDomain, TState> typed ||
                    !typed.Schema.Equals(schema) || !typed.Capture.Equals(capture) ||
                    !ReferenceEquals(typed.Preparation, preparation)) {
                    throw new ArgumentException("A repeated root must use the same schema, DTO, capture and preparation binding.", nameof(capture));
                }
                _rootIds.Add(existing.Id);
                return existing.Id;
            }
            uint id = _session!.GetOrAllocateId(value);
            RootCapture<TDomain, TState> root = new(id, value, schema, capture, preparation);
            _bindings.Add(value, id);
            _roots.Add(value, root);
            _queue.Add(root);
            _rootIds.Add(id);
            return id;
        }
        catch {
            AbortBuild();
            throw;
        }
    }

    /// <summary>Captures a string reference. Empty strings share one identity; nonempty strings use reference identity.</summary>
    public uint CaptureString(string? value) {
        try {
            RequirePhase(Phase.Capturing);
            if (value is null) {
                return 0;
            }
            if (value.Length == 0) {
                value = string.Empty;
            }
            if (_bindings.TryGetValue(value, out uint id)) {
                return id;
            }
            id = _session!.GetOrAllocateId(value);
            _bindings.Add(value, id);
            _objects.Add(new CapturedObject(id, value));
            return id;
        }
        catch {
            AbortBuild();
            throw;
        }
    }

    /// <summary>Copies all registered roots into a complete immutable graph.</summary>
    public CapturedGraph Seal() {
        try {
            RequirePhase(Phase.Registering);
            _phase = Phase.Capturing;
            foreach (RootCapture root in _queue) {
                CapturedObject item = root.Invoke(this);
                // A callback may catch an illegal reentrant call. Such a failure still aborts the build.
                RequirePhase(Phase.Capturing);
                _objects.Add(item);
            }
            Candidate = new CapturedGraph(_rootIds, _objects);
            _phase = Phase.Sealed;
            ClearBuildData();
            return Candidate;
        }
        catch {
            AbortBuild();
            throw;
        }
    }

    /// <summary>Abandons an unfinished or unresolved capture. Safe after accept/discard and repeated calls.</summary>
    public void Dispose() => Resolve();

    internal Dictionary<object, uint> DetachBindings() {
        Dictionary<object, uint> result = _bindings;
        _bindings = new(ReferenceEqualityComparer.Instance);
        return result;
    }

    internal void Resolve() {
        _session?.RequireNotPreparing();
        _phase = Phase.Resolved;
        _bindings.Clear();
        ClearBuildData();
        Candidate = null;
        CaptureSession? session = _session;
        _session = null;
        session?.Release(this);
    }

    private void AbortBuild() {
        if (_phase is Phase.Registering or Phase.Capturing) {
            Resolve();
        }
    }

    private void RequirePhase(Phase phase) {
        if (_phase != phase) {
            throw new InvalidOperationException("The operation is not valid in the current capture phase.");
        }
    }

    private void ClearBuildData() {
        _roots.Clear();
        _queue.Clear();
        _rootIds.Clear();
        _objects.Clear();
    }

    private abstract class RootCapture(uint id) {
        public uint Id { get; } = id;
        public abstract CapturedObject Invoke(CaptureContext context);
    }

    private sealed class RootCapture<TDomain, TState>(
        uint id, TDomain source, DurableSchema schema, Func<TDomain, CaptureContext, TState> capture,
        CapturedStatePreparation<TState>? preparation)
        : RootCapture(id)
        where TDomain : DurableBase
        where TState : unmanaged {
        public DurableSchema Schema { get; } = schema;
        public Func<TDomain, CaptureContext, TState> Capture { get; } = capture;
        public CapturedStatePreparation<TState>? Preparation { get; } = preparation;

        public override CapturedObject Invoke(CaptureContext context) =>
            new(Id, Schema, Capture(source, context), Preparation);
    }
}
