namespace Atelia.DurableGraph.StateStore.Storage;

/// <summary>
/// Immutable local Base contents and live-object metadata of one StateStore Revision.
/// </summary>
public sealed class StateRevision {
    private readonly FrozenList<BaseObjectRecord> _baseObjects;
    private readonly FrozenList<uint> _baseObjectIds;
    private readonly FrozenDictionary<uint, FrameAddress> _externalObjectHeads;
    private readonly FrozenList<uint> _removedObjectIds;

    // TODO(DB-026): Add actual ObjectVersion Delta payloads with exact prior locators
    // and reconstruction-chain validation. A reused ObjectId's new occupant must start
    // from Base. ObjectHeadMap Delta below only changes membership and current heads.
    private StateRevision(
        FrameAddress? parentRevisionAddress,
        ObjectHeadMapKind objectHeadMapKind,
        IEnumerable<BaseObjectRecord> baseObjects,
        IEnumerable<KeyValuePair<uint, FrameAddress>> externalObjectHeads,
        IEnumerable<uint> removedObjectIds) {
        ArgumentNullException.ThrowIfNull(baseObjects);
        ArgumentNullException.ThrowIfNull(externalObjectHeads);
        ArgumentNullException.ThrowIfNull(removedObjectIds);
        if (parentRevisionAddress is { } parent) {
            FrameAddressValidator.ValidateRequired(parent, nameof(parentRevisionAddress));
        }

        BaseObjectRecord[] frozenObjects = baseObjects.ToArray();
        foreach (BaseObjectRecord record in frozenObjects) {
            if (record is null) {
                throw new ArgumentException("A local Base record cannot be null.", nameof(baseObjects));
            }
        }

        Array.Sort(frozenObjects, static (left, right) => left.ObjectId.CompareTo(right.ObjectId));
        uint[] frozenBaseIds = FreezeObjectIds(frozenObjects.Select(static item => item.ObjectId), nameof(baseObjects));
        SortedDictionary<uint, FrameAddress> frozenExternalHeads = FreezeExternalHeads(externalObjectHeads);
        uint[] frozenRemovedIds = FreezeObjectIds(removedObjectIds, nameof(removedObjectIds));

        switch (objectHeadMapKind) {
            case ObjectHeadMapKind.Base:
                if (frozenRemovedIds.Length != 0) {
                    throw new ArgumentException("An ObjectHeadMap Base cannot contain removed ObjectIds.", nameof(removedObjectIds));
                }

                EnsureDisjoint(frozenBaseIds, frozenExternalHeads.Keys, "Local and external ObjectIds");
                break;
            case ObjectHeadMapKind.Delta:
                if (parentRevisionAddress is null) {
                    throw new ArgumentException("An ObjectHeadMap Delta requires an exact parent Revision address.", nameof(parentRevisionAddress));
                }

                if (frozenExternalHeads.Count != 0) {
                    throw new ArgumentException("An ObjectHeadMap Delta cannot contain external Object heads.", nameof(externalObjectHeads));
                }

                EnsureDisjoint(frozenBaseIds, frozenRemovedIds, "Local and removed ObjectIds");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(objectHeadMapKind), objectHeadMapKind, "Unknown ObjectHeadMap kind.");
        }

        ParentRevisionAddress = parentRevisionAddress;
        ObjectHeadMapKind = objectHeadMapKind;
        _baseObjects = new(frozenObjects);
        _baseObjectIds = new(frozenBaseIds);
        _externalObjectHeads = new(frozenExternalHeads);
        _removedObjectIds = new(frozenRemovedIds);
    }

    public FrameAddress? ParentRevisionAddress { get; }
    public ObjectHeadMapKind ObjectHeadMapKind { get; }

    /// <summary>Complete same-Frame Base records in ascending ObjectId order.</summary>
    public IReadOnlyList<BaseObjectRecord> BaseObjects => _baseObjects;

    /// <summary>IDs derived from the local Base records. Each head is its containing Revision Frame.</summary>
    public IReadOnlyList<uint> BaseObjectIds => _baseObjectIds;

    /// <summary>Earlier current heads completing an ObjectHeadMap Base; empty for a map Delta.</summary>
    public IReadOnlyDictionary<uint, FrameAddress> ExternalObjectHeads => _externalObjectHeads;

    /// <summary>IDs removed by a map Delta; a map Base expresses non-live objects by absence.</summary>
    public IReadOnlyList<uint> RemovedObjectIds => _removedObjectIds;

    public static StateRevision CreateBase(
        FrameAddress? parentRevisionAddress,
        IEnumerable<BaseObjectRecord> baseObjects,
        IEnumerable<KeyValuePair<uint, FrameAddress>> externalObjectHeads) =>
        new(parentRevisionAddress, ObjectHeadMapKind.Base, baseObjects, externalObjectHeads, []);

    public static StateRevision CreateDelta(
        FrameAddress parentRevisionAddress,
        IEnumerable<BaseObjectRecord> baseObjects,
        IEnumerable<uint> removedObjectIds) =>
        new(parentRevisionAddress, ObjectHeadMapKind.Delta, baseObjects, [], removedObjectIds);

    internal IEnumerable<uint> LocalObjectIds => _baseObjectIds;

    private static uint[] FreezeObjectIds(IEnumerable<uint> objectIds, string parameterName) {
        uint[] frozen = objectIds.Order().ToArray();
        uint? previous = null;
        foreach (uint objectId in frozen) {
            if (objectId == 0) {
                throw new ArgumentOutOfRangeException(parameterName, objectId, "ObjectId 0 is reserved.");
            }

            if (previous == objectId) {
                throw new ArgumentException($"ObjectId {objectId} occurs more than once.", parameterName);
            }

            previous = objectId;
        }

        return frozen;
    }

    private static SortedDictionary<uint, FrameAddress> FreezeExternalHeads(
        IEnumerable<KeyValuePair<uint, FrameAddress>> externalObjectHeads) {
        SortedDictionary<uint, FrameAddress> frozen = [];
        foreach ((uint objectId, FrameAddress address) in externalObjectHeads) {
            if (objectId == 0) {
                throw new ArgumentOutOfRangeException(nameof(externalObjectHeads), objectId, "ObjectId 0 is reserved.");
            }

            FrameAddressValidator.ValidateRequired(address, nameof(externalObjectHeads));
            if (!frozen.TryAdd(objectId, address)) {
                throw new ArgumentException($"ObjectId {objectId} occurs more than once in external heads.", nameof(externalObjectHeads));
            }
        }

        return frozen;
    }

    private static void EnsureDisjoint(IEnumerable<uint> left, IEnumerable<uint> right, string role) {
        HashSet<uint> seen = new(left);
        foreach (uint objectId in right) {
            if (!seen.Add(objectId)) {
                throw new ArgumentException($"{role} overlap at ObjectId {objectId}.");
            }
        }
    }

    // Only read interfaces are exposed; ICollection.SyncRoot must not reveal backing storage.
    private sealed class FrozenList<T>(T[] items) : IReadOnlyList<T> {
        public int Count => items.Length;
        public T this[int index] => items[index];
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)items).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class FrozenDictionary<TKey, TValue>(SortedDictionary<TKey, TValue> items)
        : IReadOnlyDictionary<TKey, TValue> where TKey : notnull {
        public int Count => items.Count;
        public TValue this[TKey key] => items[key];
        public IEnumerable<TKey> Keys {
            get {
                foreach (TKey key in items.Keys) {
                    yield return key;
                }
            }
        }
        public IEnumerable<TValue> Values {
            get {
                foreach (TValue value in items.Values) {
                    yield return value;
                }
            }
        }
        public bool ContainsKey(TKey key) => items.ContainsKey(key);
        public bool TryGetValue(TKey key, out TValue value) => items.TryGetValue(key, out value!);
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
