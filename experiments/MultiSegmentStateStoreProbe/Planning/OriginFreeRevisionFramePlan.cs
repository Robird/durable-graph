using System.Collections.ObjectModel;
using Atelia.MultiSegmentStateStoreProbe.Encoding;
using Atelia.MultiSegmentStateStoreProbe.Model;

namespace Atelia.MultiSegmentStateStoreProbe.Planning;

/// <summary>
/// Explicit G2 fixture description for semantic Revision content. It makes no policy
/// decision: all external addresses are supplied by the caller and remain absolute until
/// the existing G1 renderer selects an origin.
/// </summary>
internal sealed class OriginFreeRevisionFramePlan {
    private readonly ReadOnlyCollection<OriginFreeObjectVersion> _objectVersions;
    private readonly ReadOnlyCollection<OriginFreeObjectVersionDictionaryEntry> _ovdEntries;
    private readonly ReadOnlyCollection<AbsoluteFrameAddress> _externalReferences;
    private readonly int _semanticMetadataPayloadBytes;
    private readonly int _tailMetadataBytes;

    public OriginFreeRevisionFramePlan(
        AbsoluteFrameAddress? priorRevision,
        ObjectVersionDictionaryKind dictionaryKind,
        IEnumerable<OriginFreeObjectVersion> objectVersions,
        IEnumerable<OriginFreeObjectVersionDictionaryEntry> ovdEntries) {
        ArgumentNullException.ThrowIfNull(objectVersions);
        ArgumentNullException.ThrowIfNull(ovdEntries);
        if (!Enum.IsDefined(dictionaryKind)) {
            throw new ArgumentOutOfRangeException(nameof(dictionaryKind));
        }

        OriginFreeObjectVersion[] canonicalVersions = objectVersions
            .OrderBy(static version => version.ObjectId)
            .ToArray();
        OriginFreeObjectVersionDictionaryEntry[] canonicalEntries = ovdEntries
            .OrderBy(static entry => entry.ObjectId)
            .ToArray();
        EnsureUniqueObjectIds(canonicalVersions.Select(static version => version.ObjectId));
        EnsureUniqueObjectIds(canonicalEntries.Select(static entry => entry.ObjectId));

        PriorRevision = priorRevision;
        DictionaryKind = dictionaryKind;
        _objectVersions = Array.AsReadOnly(canonicalVersions);
        _ovdEntries = Array.AsReadOnly(canonicalEntries);

        List<AbsoluteFrameAddress> externalReferences = [];
        if (priorRevision is { } prior) {
            externalReferences.Add(prior);
        }

        externalReferences.AddRange(canonicalVersions
            .Where(static version => version.DeltaParent is not null)
            .Select(static version => version.DeltaParent!.Value));
        externalReferences.AddRange(canonicalEntries
            .Where(static entry => entry.ExternalAddress is not null)
            .Select(static entry => entry.ExternalAddress!.Value));
        _externalReferences = externalReferences.AsReadOnly();
        _semanticMetadataPayloadBytes = EstimateSemanticMetadata(
            priorRevision,
            canonicalVersions,
            canonicalEntries);
        _tailMetadataBytes = EstimateTailMetadata(canonicalVersions);
    }

    public AbsoluteFrameAddress? PriorRevision { get; }

    public ObjectVersionDictionaryKind DictionaryKind { get; }

    public IReadOnlyList<OriginFreeObjectVersion> ObjectVersions => _objectVersions;

    public IReadOnlyList<OriginFreeObjectVersionDictionaryEntry> OvdEntries => _ovdEntries;

    public IReadOnlyList<AbsoluteFrameAddress> ExternalReferences => _externalReferences;

    public long SyntheticPayloadBytes => _objectVersions.Aggregate(
        0L,
        static (total, version) => checked(total + version.PayloadBytes));

    public int SemanticMetadataPayloadBytes => _semanticMetadataPayloadBytes;

    public int TailMetadataBytes => _tailMetadataBytes;

    internal RevisionFrame Render(FileScope scope) {
        RelativeFrameTicket? prior = PriorRevision is { } priorRevision
            ? scope.Relativize(priorRevision)
            : null;
        ObjectVersion[] versions = _objectVersions
            .Select(version => version.Render(scope))
            .ToArray();
        KeyValuePair<uint, ObjectVersionDictionaryBinding>[] entries = _ovdEntries
            .Select(entry => new KeyValuePair<uint, ObjectVersionDictionaryBinding>(
                entry.ObjectId,
                entry.Render(scope)))
            .ToArray();
        ObjectVersionDictionary dictionary = new(DictionaryKind, entries);
        return new RevisionFrame(prior, dictionary, versions);
    }

    private static void EnsureUniqueObjectIds(IEnumerable<uint> objectIds) {
        uint? previous = null;
        foreach (uint objectId in objectIds) {
            ArgumentOutOfRangeException.ThrowIfZero(objectId);
            if (previous == objectId) {
                throw new ArgumentException(
                    $"Origin-free Revision content contains duplicate ObjectId {objectId}.");
            }

            previous = objectId;
        }
    }

    private static int EstimateSemanticMetadata(
        AbsoluteFrameAddress? priorRevision,
        IEnumerable<OriginFreeObjectVersion> versions,
        IEnumerable<OriginFreeObjectVersionDictionaryEntry> entries) {
        int bytes = 2; // Revision tag plus OVD kind.
        bytes = checked(bytes + (priorRevision is null ? 1 : 2));
        foreach (OriginFreeObjectVersion version in versions) {
            bytes = checked(
                bytes +
                CanonicalUnsignedBase128.GetEncodedWidth(version.ObjectId) +
                1 +
                CanonicalUnsignedBase128.GetEncodedWidth(
                    checked((ulong)version.PayloadBytes)) +
                CanonicalUnsignedBase128.GetEncodedWidth(
                    checked((ulong)version.ResultState.BasePayloadBytes)) +
                CanonicalUnsignedBase128.GetEncodedWidth(
                    checked((ulong)version.ResultState.LogicalVersionOrdinal)) +
                sizeof(int)); // synthetic state value
            if (version.Kind == ObjectVersionKind.Delta) {
                LogicalObjectState expected = version.ExpectedParentState!.Value;
                bytes = checked(
                    bytes +
                    CanonicalUnsignedBase128.GetEncodedWidth(
                        checked((ulong)expected.BasePayloadBytes)) +
                    CanonicalUnsignedBase128.GetEncodedWidth(
                        checked((ulong)expected.LogicalVersionOrdinal)) +
                    sizeof(int)); // expected synthetic state value
            }
        }

        foreach (OriginFreeObjectVersionDictionaryEntry entry in entries) {
            bytes = checked(
                bytes +
                CanonicalUnsignedBase128.GetEncodedWidth(entry.ObjectId) +
                1);
        }

        return bytes;
    }

    private static int EstimateTailMetadata(
        IReadOnlyCollection<OriginFreeObjectVersion> versions) {
        int bytes = CanonicalUnsignedBase128.GetEncodedWidth(
            checked((ulong)versions.Count));
        foreach (OriginFreeObjectVersion version in versions) {
            bytes = checked(
                bytes +
                CanonicalUnsignedBase128.GetEncodedWidth(version.ObjectId) +
                1); // provisional record-offset width
        }

        return bytes;
    }
}

internal sealed class OriginFreeObjectVersion {
    private OriginFreeObjectVersion(
        uint objectId,
        ObjectVersionKind kind,
        int payloadBytes,
        LogicalObjectState resultState,
        LogicalObjectState? expectedParentState,
        AbsoluteFrameAddress? deltaParent) {
        ArgumentOutOfRangeException.ThrowIfZero(objectId);
        if (!Enum.IsDefined(kind)) {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(payloadBytes);
        if (kind == ObjectVersionKind.Base &&
            (expectedParentState is not null || deltaParent is not null)) {
            throw new ArgumentException("A Base plan cannot carry a direct parent.");
        }

        if (kind == ObjectVersionKind.Delta &&
            (payloadBytes == 0 || expectedParentState is null || deltaParent is null)) {
            throw new ArgumentException(
                "A Delta plan requires a positive payload, expected state, and exact parent.");
        }

        ObjectId = objectId;
        Kind = kind;
        PayloadBytes = payloadBytes;
        ResultState = resultState;
        ExpectedParentState = expectedParentState;
        DeltaParent = deltaParent;
    }

    public uint ObjectId { get; }

    public ObjectVersionKind Kind { get; }

    public int PayloadBytes { get; }

    public LogicalObjectState ResultState { get; }

    public LogicalObjectState? ExpectedParentState { get; }

    public AbsoluteFrameAddress? DeltaParent { get; }

    public static OriginFreeObjectVersion Base(
        uint objectId,
        LogicalObjectState state) =>
        new(
            objectId,
            ObjectVersionKind.Base,
            state.BasePayloadBytes,
            state,
            expectedParentState: null,
            deltaParent: null);

    public static OriginFreeObjectVersion Delta(
        uint objectId,
        int payloadBytes,
        LogicalObjectState expectedParentState,
        LogicalObjectState resultState,
        AbsoluteFrameAddress deltaParent) =>
        new(
            objectId,
            ObjectVersionKind.Delta,
            payloadBytes,
            resultState,
            expectedParentState,
            deltaParent);

    internal ObjectVersion Render(FileScope scope) => new(
        ObjectId,
        Kind,
        PayloadBytes,
        ResultState,
        ExpectedParentState,
        DeltaParent is { } parent ? scope.Relativize(parent) : null);
}

internal readonly record struct OriginFreeObjectVersionDictionaryEntry {
    private OriginFreeObjectVersionDictionaryEntry(
        uint objectId,
        ObjectVersionDictionaryBindingKind kind,
        AbsoluteFrameAddress? externalAddress) {
        ArgumentOutOfRangeException.ThrowIfZero(objectId);
        ObjectId = objectId;
        Kind = kind;
        ExternalAddress = externalAddress;
    }

    public uint ObjectId { get; }

    public ObjectVersionDictionaryBindingKind Kind { get; }

    public AbsoluteFrameAddress? ExternalAddress { get; }

    public static OriginFreeObjectVersionDictionaryEntry BindSelf(uint objectId) =>
        new(objectId, ObjectVersionDictionaryBindingKind.BindSelf, null);

    public static OriginFreeObjectVersionDictionaryEntry External(
        uint objectId,
        AbsoluteFrameAddress address) =>
        new(objectId, ObjectVersionDictionaryBindingKind.External, address);

    public static OriginFreeObjectVersionDictionaryEntry Remove(uint objectId) =>
        new(objectId, ObjectVersionDictionaryBindingKind.Remove, null);

    internal ObjectVersionDictionaryBinding Render(FileScope scope) => Kind switch {
        ObjectVersionDictionaryBindingKind.BindSelf =>
            ObjectVersionDictionaryBinding.BindSelf(),
        ObjectVersionDictionaryBindingKind.External
            when ExternalAddress is { } address =>
            ObjectVersionDictionaryBinding.External(scope.Relativize(address)),
        ObjectVersionDictionaryBindingKind.Remove =>
            ObjectVersionDictionaryBinding.Remove(),
        _ => throw new InvalidDataException(
            $"ObjectVersionDictionary entry {ObjectId} has an invalid origin-free shape."),
    };
}
