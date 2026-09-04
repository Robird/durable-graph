using System.Collections.ObjectModel;

namespace Atelia.DurableGraph.StateStore.Storage;

/// <summary>
/// Immutable semantic content of one StateStore Revision's live-object metadata.
/// </summary>
public sealed class StateRevision {
    private readonly ReadOnlyCollection<uint> _baseObjectIds;
    private readonly ReadOnlyCollection<uint> _deltaObjectIds;
    private readonly ReadOnlyDictionary<uint, FrameAddress> _externalObjectHeads;
    private readonly ReadOnlyCollection<uint> _removedObjectIds;

    private StateRevision(
        FrameAddress? parentRevisionAddress,
        ObjectHeadMapKind objectHeadMapKind,
        IEnumerable<uint> baseObjectIds,
        IEnumerable<uint> deltaObjectIds,
        IEnumerable<KeyValuePair<uint, FrameAddress>> externalObjectHeads,
        IEnumerable<uint> removedObjectIds) {
        ArgumentNullException.ThrowIfNull(baseObjectIds);
        ArgumentNullException.ThrowIfNull(deltaObjectIds);
        ArgumentNullException.ThrowIfNull(externalObjectHeads);
        ArgumentNullException.ThrowIfNull(removedObjectIds);

        ValidateOptionalAddress(parentRevisionAddress, nameof(parentRevisionAddress));
        uint[] frozenBaseIds = FreezeObjectIds(baseObjectIds, nameof(baseObjectIds));
        uint[] frozenDeltaIds = FreezeObjectIds(deltaObjectIds, nameof(deltaObjectIds));
        EnsureDisjoint(frozenBaseIds, frozenDeltaIds, "Base and Delta ObjectIds");

        SortedDictionary<uint, FrameAddress> frozenExternalHeads =
            FreezeExternalHeads(externalObjectHeads);
        uint[] frozenRemovedIds = FreezeObjectIds(
            removedObjectIds,
            nameof(removedObjectIds));
        uint[] localObjectIds = frozenBaseIds.Concat(frozenDeltaIds).ToArray();

        switch (objectHeadMapKind) {
            case ObjectHeadMapKind.Base:
                if (parentRevisionAddress is null && frozenDeltaIds.Length != 0) {
                    throw new ArgumentException(
                        "A genesis State Revision cannot contain Delta ObjectVersions.",
                        nameof(deltaObjectIds));
                }

                if (frozenRemovedIds.Length != 0) {
                    throw new ArgumentException(
                        "An ObjectHeadMap Base cannot contain removed ObjectIds.",
                        nameof(removedObjectIds));
                }

                EnsureDisjoint(
                    localObjectIds,
                    frozenExternalHeads.Keys,
                    "Local and external ObjectIds");
                break;
            case ObjectHeadMapKind.Delta:
                if (parentRevisionAddress is null) {
                    throw new ArgumentException(
                        "An ObjectHeadMap Delta requires an exact parent Revision address.",
                        nameof(parentRevisionAddress));
                }

                if (frozenExternalHeads.Count != 0) {
                    throw new ArgumentException(
                        "An ObjectHeadMap Delta cannot contain external Object heads.",
                        nameof(externalObjectHeads));
                }

                EnsureDisjoint(
                    localObjectIds,
                    frozenRemovedIds,
                    "Local and removed ObjectIds");
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(objectHeadMapKind),
                    objectHeadMapKind,
                    "Unknown ObjectHeadMap kind.");
        }

        ParentRevisionAddress = parentRevisionAddress;
        ObjectHeadMapKind = objectHeadMapKind;
        _baseObjectIds = Array.AsReadOnly(frozenBaseIds);
        _deltaObjectIds = Array.AsReadOnly(frozenDeltaIds);
        _externalObjectHeads = new(frozenExternalHeads);
        _removedObjectIds = Array.AsReadOnly(frozenRemovedIds);
    }

    public FrameAddress? ParentRevisionAddress { get; }

    public ObjectHeadMapKind ObjectHeadMapKind { get; }

    /// <summary>
    /// ObjectIds whose same-Frame ObjectVersion will contain a full Base payload.
    /// Every entry implicitly publishes its containing Revision Frame as current head.
    /// </summary>
    public IReadOnlyList<uint> BaseObjectIds => _baseObjectIds;

    /// <summary>
    /// ObjectIds whose same-Frame ObjectVersion will contain a Delta payload.
    /// Every entry implicitly publishes its containing Revision Frame as current head.
    /// </summary>
    public IReadOnlyList<uint> DeltaObjectIds => _deltaObjectIds;

    /// <summary>
    /// Earlier current heads needed to complete an ObjectHeadMap Base.
    /// Empty for ObjectHeadMap Delta Revisions.
    /// </summary>
    public IReadOnlyDictionary<uint, FrameAddress> ExternalObjectHeads =>
        _externalObjectHeads;

    /// <summary>
    /// ObjectIds removed by an ObjectHeadMap Delta. Empty for ObjectHeadMap Base
    /// Revisions, where absence already means not live.
    /// </summary>
    public IReadOnlyList<uint> RemovedObjectIds => _removedObjectIds;

    public static StateRevision CreateBase(
        FrameAddress? parentRevisionAddress,
        IEnumerable<uint> baseObjectIds,
        IEnumerable<uint> deltaObjectIds,
        IEnumerable<KeyValuePair<uint, FrameAddress>> externalObjectHeads) =>
        new(
            parentRevisionAddress,
            ObjectHeadMapKind.Base,
            baseObjectIds,
            deltaObjectIds,
            externalObjectHeads,
            []);

    public static StateRevision CreateDelta(
        FrameAddress parentRevisionAddress,
        IEnumerable<uint> baseObjectIds,
        IEnumerable<uint> deltaObjectIds,
        IEnumerable<uint> removedObjectIds) =>
        new(
            parentRevisionAddress,
            ObjectHeadMapKind.Delta,
            baseObjectIds,
            deltaObjectIds,
            [],
            removedObjectIds);

    internal IEnumerable<uint> LocalObjectIds =>
        _baseObjectIds.Concat(_deltaObjectIds);

    private static uint[] FreezeObjectIds(
        IEnumerable<uint> objectIds,
        string parameterName) {
        uint[] frozen = objectIds.Order().ToArray();
        uint? previous = null;
        foreach (uint objectId in frozen) {
            if (objectId == 0) {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    objectId,
                    "ObjectId 0 is reserved.");
            }

            if (previous == objectId) {
                throw new ArgumentException(
                    $"ObjectId {objectId} occurs more than once.",
                    parameterName);
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
                throw new ArgumentOutOfRangeException(
                    nameof(externalObjectHeads),
                    objectId,
                    "ObjectId 0 is reserved.");
            }

            ValidateAddress(address, nameof(externalObjectHeads));
            if (!frozen.TryAdd(objectId, address)) {
                throw new ArgumentException(
                    $"ObjectId {objectId} occurs more than once in external heads.",
                    nameof(externalObjectHeads));
            }
        }

        return frozen;
    }

    private static void EnsureDisjoint(
        IEnumerable<uint> left,
        IEnumerable<uint> right,
        string role) {
        HashSet<uint> seen = new(left);
        foreach (uint objectId in right) {
            if (!seen.Add(objectId)) {
                throw new ArgumentException(
                    $"{role} overlap at ObjectId {objectId}.");
            }
        }
    }

    private static void ValidateOptionalAddress(
        FrameAddress? address,
        string parameterName) {
        if (address is { } value) {
            ValidateAddress(value, parameterName);
        }
    }

    private static void ValidateAddress(FrameAddress address, string parameterName) {
        FrameAddressValidator.ValidateRequired(address, parameterName);
    }
}
