using Atelia.DurableGraph.StateStore.Storage;

namespace Atelia.DurableGraph.StateStore;

/// <summary>Loads one explicitly selected World after complete exact decoding and current DTO normalization.</summary>
public static class LoadedWorld {
    /// <summary>
    /// Freezes a new World graph into a no-Parent plan. May persist Schema registrations;
    /// does not append State, run its persistence barrier, publish, or install a baseline.
    /// The host must keep the graph stable throughout this synchronous operation.
    /// </summary>
    public static PreparedWorldRevision PrepareNew<TWorld>(
        StateRevisionStore store,
        SchemaStore schemas,
        TWorld world,
        StateModelRegistry models,
        ReadAmplificationBaseBudgetParameters parameters) where TWorld : DurableBase {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(models);
        StateModelSnapshot snapshot = models.Snapshot();
        if (world.GetType() != typeof(TWorld) || !snapshot.Types.TryGetValue(typeof(TWorld), out StateModelBinding? model)) {
            throw new ArgumentException("World must have its requested exact domain type registered.", nameof(world));
        }
        CaptureSession capture = new();
        using CaptureContext context = capture.BeginCapture(snapshot.Models.Values);
        uint worldId = model.AddRoot(context, world);
        CapturedGraph candidate = context.Seal();
        try {
            PreparedCapturedGraph contents = capture.Prepare(candidate);
            PreparedObjectRevision prepared = CapturedRevisionPlanner.PrepareRevision(store, schemas, null, contents, parameters);
            return new(worldId, prepared.Revision);
        } finally {
            capture.Discard(candidate);
        }
    }

    /// <summary>
    /// Creates a fixed-Parent editable view. Stores must belong to the same repository and remain
    /// usable and stable for Prepare. No instance constructors, Transient hook, write or publication runs here.
    /// </summary>
    public static LoadedWorld<TWorld> Load<TWorld>(
        StateRevisionStore store,
        SchemaStore schemas,
        FrameAddress revisionAddress,
        uint worldId,
        StateModelRegistry models) where TWorld : DurableBase {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentOutOfRangeException.ThrowIfZero(worldId);
        StateModelSnapshot snapshot = models.Snapshot();
        DecodedRevision decoded = RevisionDecoder.ReadSnapshot(store, schemas, revisionAddress, snapshot.Readers);
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
            if (row.Current.Kind == CapturedObjectKind.String) {
                // Distinct Empty IDs have one CLR instance. Choose the smallest ID, but never
                // rewrite the baseline DTO's original ID slots; Capture must observe the change.
                bindings.TryAdd(row.Current.StringContent, row.Current.Id);
            }
        }
        ulong nextId = (ulong)normalized.Objects.Keys.Max() + 1;
        return new(store, schemas, world, worldId, model, snapshot, normalized, new(nextId, bindings));
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
    }
}

/// <summary>One loaded World and its immutable Parent-bound comparison baseline.</summary>
/// <remarks>
/// Single-threaded. Prepare never advances this baseline. The host appends its owned plan,
/// then loads the returned address to obtain a new baseline. This is not Commit or publication.
/// </remarks>
public sealed class LoadedWorld<TWorld> where TWorld : DurableBase {
    private readonly StateRevisionStore _store;
    private readonly SchemaStore _schemas;
    private readonly StateModelBinding _model;
    private readonly StateModelSnapshot _models;
    private readonly NormalizedRevision _baseline;
    private readonly IReadOnlyDictionary<uint, CapturedObject> _baselineCurrentDtos;
    private readonly CaptureSession _capture;
    private bool _preparing;

    internal LoadedWorld(StateRevisionStore store, SchemaStore schemas, TWorld world, uint worldId,
        StateModelBinding model, StateModelSnapshot models, NormalizedRevision baseline, CaptureSession capture) {
        _store = store;
        _schemas = schemas;
        World = world;
        WorldId = worldId;
        _model = model;
        _models = models;
        _baseline = baseline;
        _baselineCurrentDtos = baseline.Objects.ToDictionary(static pair => pair.Key, static pair => pair.Value.Current);
        _capture = capture;
    }

    public TWorld World { get; }
    public uint WorldId { get; }
    public FrameAddress ParentRevisionAddress => _baseline.RevisionAddress;

    /// <summary>
    /// Freezes current content against the original Parent. May persist Schema registrations;
    /// never appends State, publishes, or accepts a new Parent. Failed captures can consume IDs.
    /// </summary>
    public PreparedWorldRevision Prepare(ReadAmplificationBaseBudgetParameters parameters) {
        if (_preparing) {
            throw new InvalidOperationException("The loaded World cannot be prepared recursively.");
        }
        _preparing = true;
        try {
            using CaptureContext context = _capture.BeginCapture(_models.Models.Values);
            uint id = _model.AddRoot(context, World);
            if (id != WorldId) {
                throw new InvalidOperationException("Capture did not preserve the loaded World ID.");
            }
            CapturedGraph candidate = context.Seal();
            try {
                IReadOnlyList<PreparedCapturedObject> contents = _capture.PrepareAgainst(candidate, _baselineCurrentDtos);
                PreparedObjectRevision prepared = LoadedRevisionPlanner.Prepare(_store, _schemas, _baseline, contents, parameters);
                return new(WorldId, prepared.Revision);
            } finally {
                _capture.Discard(candidate);
            }
        } finally {
            _preparing = false;
        }
    }
}

/// <summary>Owned frozen State contents with a fixed Parent and the selected World ID.</summary>
/// <remarks>Pass Revision to Storage.Append; the returned address still needs host publication.</remarks>
public sealed class PreparedWorldRevision {
    internal PreparedWorldRevision(uint worldId, StateRevision revision) {
        WorldId = worldId;
        Revision = revision;
    }

    public uint WorldId { get; }
    public StateRevision Revision { get; }
}
