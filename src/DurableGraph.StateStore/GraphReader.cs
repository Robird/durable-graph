using Atelia.DurableGraph.StateStore.Storage;

namespace Atelia.DurableGraph.StateStore;

/// <summary>Restores explicit graph selections without owning a publication head or editing session.</summary>
internal static class GraphReader {
    internal static MaterializedGraph<T> Read<T>(StateRevisionStore store, SchemaStore schemas,
        FrameAddress revisionAddress, ObjectId rootId, StateModelSnapshot models,
        bool requireExactRootType = false) where T : DurableBase {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentOutOfRangeException.ThrowIfZero(rootId.Value, nameof(rootId));
        DecodedRevision decoded = RevisionDecoder.ReadSnapshot(store, schemas, revisionAddress, models);
        NormalizedRevision normalized = NormalizedRevision.Create(decoded, models);
        if (!normalized.Objects.TryGetValue(rootId, out NormalizedObject? root) ||
            root.Model is not StateModelBinding rootModel || rootModel.DomainType.IsAbstract ||
            (requireExactRootType ? rootModel.DomainType != typeof(T) : !typeof(T).IsAssignableFrom(rootModel.DomainType))) {
            throw new InvalidDataException("Root ID must select a durable object of the requested current domain type.");
        }
        ReachableVisitor visitor = new();
        visitor.Add(rootId);
        for (int index = 0; index < visitor.Ids.Count; index++) {
            NormalizedObject row = normalized.Objects[visitor.Ids[index]];
            row.Model.VisitReferences(row.Current, visitor);
        }
        Dictionary<ObjectId, object> instances = [];
        Dictionary<object, ObjectId> bindings = new(ReferenceEqualityComparer.Instance);
        // All allocations precede all hydration, preserving forward/shared/cyclic references.
        foreach (ObjectId id in visitor.Ids) {
            NormalizedObject row = normalized.Objects[id];
            ObjectBinding model = row.Model;
            object allocated = model.Allocate(row.Current);
            bool canonicalEmpty = row.Current.Kind == ObjectStateKind.String && row.Current.StringContent.Length == 0;
            if (allocated is null || allocated.GetType() != model.DomainType ||
                (!canonicalEmpty && !bindings.TryAdd(allocated, id))) {
                throw new InvalidDataException("Each nonempty object ID must allocate a distinct instance of its exact current type.");
            }
            instances.Add(id, allocated);
        }
        ObjectReadTable table = new(instances);
        foreach ((ObjectId id, object instance) in instances) {
            NormalizedObject row = normalized.Objects[id];
            row.Model.Hydrate(instance, row.Current, table);
        }
        // Keep full source string identity for a later editable import, including strings
        // made unreachable by Upgrade. Empty IDs share one instance: import the smallest ID
        // without rewriting any baseline DTO slots. Capture then observes necessary changes.
        foreach (NormalizedObject row in normalized.Objects.Values.OrderBy(static row => row.Current.Id)) {
            if (row.Current.Kind == ObjectStateKind.String) {
                bindings.TryAdd(row.Current.StringContent, row.Current.Id);
            }
        }
        ulong nextId = (ulong)normalized.Objects.Keys.Max().Value + 1;
        return new((T)instances[rootId], rootId, rootModel, normalized, nextId, bindings);
    }

    /// <summary>
    /// Experimental read-only snapshots in input order. Both restores must succeed before
    /// delivery; user callback side effects are not rolled back. Cross-graph instance sharing
    /// or separation is not a contract. A future shared reader must not feed editable imports.
    /// </summary>
    internal static (TFirst First, TSecond Second) ReadPair<TFirst, TSecond>(
        StateRevisionStore store, SchemaStore schemas,
        FrameAddress firstRevisionAddress, ObjectId firstRootId,
        FrameAddress secondRevisionAddress, ObjectId secondRootId,
        StateModelSnapshot models) where TFirst : DurableBase where TSecond : DurableBase {
        TFirst first = Read<TFirst>(store, schemas, firstRevisionAddress, firstRootId, models).Root;
        TSecond second = Read<TSecond>(store, schemas, secondRevisionAddress, secondRootId, models).Root;
        return (first, second);
    }

    // References have already been validated against the complete current directory.
    // This visitor computes reachability only; it owns no second field/type description.
    private sealed class ReachableVisitor : IStateReferenceVisitor {
        private readonly HashSet<ObjectId> _seen = [];
        internal List<ObjectId> Ids { get; } = [];
        internal void Add(ObjectId id) {
            if (!id.IsNull && _seen.Add(id)) {
                Ids.Add(id);
            }
        }
        public void VisitString(ObjectId objectId) => Add(objectId);
        public void VisitDurable(ObjectId objectId, string nominalSchemaId) => Add(objectId);
        public void VisitDurable(ObjectId objectId, TypeExpr nominalType) => Add(objectId);
        public void VisitObject(ObjectId objectId, TypeExpr declaredType) => Add(objectId);
    }
}

/// <summary>One independently restored graph and its complete source baseline for controlled editable import.</summary>
internal sealed class MaterializedGraph<T> where T : DurableBase {
    private readonly ulong _nextId;
    private readonly IReadOnlyDictionary<object, ObjectId> _bindings;

    internal MaterializedGraph(T root, ObjectId rootId, StateModelBinding rootModel,
        NormalizedRevision baseline, ulong nextId, IReadOnlyDictionary<object, ObjectId> bindings) {
        Root = root;
        RootId = rootId;
        RootModel = rootModel;
        Baseline = baseline;
        _nextId = nextId;
        _bindings = bindings;
    }

    internal T Root { get; }
    internal ObjectId RootId { get; }
    internal StateModelBinding RootModel { get; }
    internal NormalizedRevision Baseline { get; }
    internal CaptureSession CreateCaptureSession() => new(_nextId, _bindings);
}
