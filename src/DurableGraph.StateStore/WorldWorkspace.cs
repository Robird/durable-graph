using Atelia.DurableGraph.StateStore.Storage;

namespace Atelia.DurableGraph.StateStore;

/// <summary>Private single-World capture and comparison owner shared by both outer APIs.</summary>
internal sealed class WorldWorkspace<TWorld> where TWorld : DurableBase {
    private readonly StateRevisionStore _store;
    private readonly SchemaStore _schemas;
    private readonly StateModelBinding _model;
    private readonly StateModelSnapshot _models;
    private readonly CaptureSession _capture;
    private NormalizedRevision? _baseline;
    private PreparedWorldSave<TWorld>? _pending;
    private bool _staging;

    private WorldWorkspace(StateRevisionStore store, SchemaStore schemas, TWorld world, uint worldId,
        StateModelBinding model, StateModelSnapshot models, NormalizedRevision? baseline, CaptureSession capture) {
        _store = store;
        _schemas = schemas;
        World = world;
        WorldId = worldId;
        _model = model;
        _models = models;
        _baseline = baseline;
        _capture = capture;
    }

    internal TWorld World { get; }
    internal uint WorldId { get; private set; }
    internal FrameAddress? ParentRevisionAddress => _baseline?.RevisionAddress;

    internal static WorldWorkspace<TWorld> Create(StateRevisionStore store, SchemaStore schemas,
        TWorld world, StateModelRegistry models) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(models);
        StateModelSnapshot snapshot = models.Snapshot(schemas);
        if (world.GetType() != typeof(TWorld) || !snapshot.TryGetCurrentModel(typeof(TWorld), out StateModelBinding? model)) {
            throw new ArgumentException("World must have its requested exact domain type registered.", nameof(world));
        }
        return new(store, schemas, world, 0, model!, snapshot, null, new());
    }

    internal static WorldWorkspace<TWorld> Load(StateRevisionStore store, SchemaStore schemas,
        FrameAddress revisionAddress, uint worldId, StateModelRegistry models) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentOutOfRangeException.ThrowIfZero(worldId);
        StateModelSnapshot snapshot = models.Snapshot(schemas);
        DecodedRevision decoded = RevisionDecoder.ReadSnapshot(store, schemas, revisionAddress, snapshot);
        NormalizedRevision normalized = NormalizedRevision.Create(decoded, snapshot);
        if (!normalized.Objects.TryGetValue(worldId, out NormalizedObject? root) ||
            root.Model is not { } model || model.DomainType != typeof(TWorld) || typeof(TWorld).IsAbstract) {
            throw new InvalidDataException("World ID must select a durable object of the requested exact current domain type.");
        }
        IReadOnlyList<uint> reachable = FindReachable(normalized, worldId);
        Dictionary<uint, DurableBase> instances = [];
        Dictionary<object, uint> bindings = new(ReferenceEqualityComparer.Instance);
        // Allocate the complete reachable durable set before any field assignment. This
        // preserves forward/shared/cyclic references without recursive materialization.
        foreach (uint id in reachable) {
            if (normalized.Objects[id].Model is not { } actualModel) {
                continue;
            }
            DurableBase allocated = actualModel.Allocate();
            if (allocated is null || allocated.GetType() != actualModel.DomainType || !bindings.TryAdd(allocated, id)) {
                throw new InvalidDataException("Each durable ID must allocate a distinct instance of its exact current domain type.");
            }
            instances.Add(id, allocated);
        }
        ObjectReadTable table = new(normalized.Strings, instances);
        foreach ((uint id, DurableBase instance) in instances) {
            NormalizedObject row = normalized.Objects[id];
            row.Model!.Hydrate(instance, row.Current, table);
        }
        TWorld world = (TWorld)instances[worldId];

        // Retain lookup entries for source strings already owned by the normalized baseline.
        // Capture still emits only reachable strings; this map is not post-save membership.
        foreach (NormalizedObject row in normalized.Objects.Values.OrderBy(static row => row.Current.Id)) {
            if (row.Current.Kind == ObjectStateKind.String) {
                // Distinct Empty IDs have one CLR instance. Choose the smallest ID, but never
                // rewrite the baseline DTO's original ID slots; Capture must observe the change.
                bindings.TryAdd(row.Current.StringContent, row.Current.Id);
            }
        }
        ulong nextId = (ulong)normalized.Objects.Keys.Max() + 1;
        return new(store, schemas, world, worldId, model, snapshot, normalized, new(nextId, bindings));
    }

    internal PreparedWorldSave<TWorld> Stage(ReadAmplificationBaseBudgetParameters parameters) {
        if (_staging || _pending is not null) {
            throw new InvalidOperationException("Resolve the current World save before preparing another.");
        }
        _staging = true;
        CaptureContext? context = null;
        try {
            context = _capture.BeginCapture(_models);
            uint worldId = _model.AddRoot(context, World);
            if (WorldId != 0 && worldId != WorldId) {
                throw new InvalidOperationException("Capture did not preserve the World ID.");
            }
            CapturedGraph candidate = context.Seal();
            PreparedObjectRevision prepared = _baseline is null
                ? CapturedRevisionPlanner.PrepareRevision(_store, _schemas, null, _capture.Prepare(candidate), parameters)
                : LoadedRevisionPlanner.Prepare(_store, _schemas, _baseline,
                    _capture.PrepareAgainst(candidate, _baseline.CurrentDtos), parameters);
            NormalizedRevision next = NormalizedRevision.FromCandidate(candidate, _models);
            _pending = new(this, context, candidate, worldId, prepared.Revision, next);
            return _pending;
        } catch {
            context?.Dispose();
            throw;
        } finally {
            _staging = false;
        }
    }

    internal void Install(PreparedWorldSave<TWorld> pending, CapturedGraph candidate, NormalizedRevision baseline) {
        RequirePending(pending);
        // Accept transfers the already prepared live dictionary. Current is only a shared-row
        // projection: all subsequent comparisons use this baseline's full current DTO directory.
        _capture.Accept(candidate);
        _baseline = baseline;
        WorldId = pending.WorldId;
        _pending = null;
    }

    internal void Discard(PreparedWorldSave<TWorld> pending, CaptureContext context) {
        RequirePending(pending);
        context.Dispose();
        _pending = null;
    }

    private void RequirePending(PreparedWorldSave<TWorld> pending) {
        if (!ReferenceEquals(_pending, pending)) {
            throw new InvalidOperationException("The pending save does not belong to this workspace.");
        }
    }

    private static IReadOnlyList<uint> FindReachable(NormalizedRevision normalized, uint worldId) {
        ReachableVisitor visitor = new();
        visitor.Add(worldId);
        for (int index = 0; index < visitor.Ids.Count; index++) {
            NormalizedObject row = normalized.Objects[visitor.Ids[index]];
            row.Model?.VisitReferences(row.Current, visitor);
        }
        return visitor.Ids;
    }

    // All references were already checked against the complete current directory.
    // This visitor only computes membership; it owns no second field/type description.
    private sealed class ReachableVisitor : IStateReferenceVisitor {
        private readonly HashSet<uint> _seen = [];
        internal List<uint> Ids { get; } = [];
        internal void Add(uint id) {
            if (id != 0 && _seen.Add(id)) {
                Ids.Add(id);
            }
        }
        public void VisitString(uint objectId) => Add(objectId);
        public void VisitDurable(uint objectId, string nominalSchemaId) => Add(objectId);
        public void VisitDurable(uint objectId, TypeExpr nominalType) => Add(objectId);
    }
}
