using Atelia.DurableGraph.StateStore.Storage;

namespace Atelia.DurableGraph.StateStore;

/// <summary>Loads one explicitly selected World after complete exact decoding and current DTO normalization.</summary>
public static class LoadedWorld {
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
        DurableBase allocated = model.Allocate();
        if (allocated is not TWorld world || allocated.GetType() != typeof(TWorld)) {
            throw new InvalidDataException("World allocation did not return the requested exact domain type.");
        }
        model.Hydrate(world, root.Current, normalized.Strings);

        Dictionary<object, uint> bindings = new(ReferenceEqualityComparer.Instance) { [world] = worldId };
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
        return new(store, schemas, world, worldId, model, normalized, new(nextId, bindings));
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
    private readonly NormalizedRevision _baseline;
    private readonly IReadOnlyDictionary<uint, CapturedObject> _currentStates;
    private readonly CaptureSession _capture;
    private bool _preparing;

    internal LoadedWorld(StateRevisionStore store, SchemaStore schemas, TWorld world, uint worldId,
        StateModelBinding model, NormalizedRevision baseline, CaptureSession capture) {
        _store = store;
        _schemas = schemas;
        World = world;
        WorldId = worldId;
        _model = model;
        _baseline = baseline;
        _currentStates = baseline.Objects.ToDictionary(static pair => pair.Key, static pair => pair.Value.Current);
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
            using CaptureContext context = _capture.BeginCapture();
            uint id = _model.AddRoot(context, World);
            if (id != WorldId) {
                throw new InvalidOperationException("Capture did not preserve the loaded World ID.");
            }
            CapturedGraph candidate = context.Seal();
            try {
                IReadOnlyList<PreparedCapturedObject> contents = _capture.PrepareAgainst(candidate, _currentStates);
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
