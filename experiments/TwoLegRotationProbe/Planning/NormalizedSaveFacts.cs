using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Planning;

/// <summary>
/// Canonical, immutable single-Save facts tied to one source PublishedRevision.
/// The typed partitions are views derived from the same ObjectId-sorted fact sequence.
/// </summary>
internal sealed class NormalizedSaveFacts {
    private readonly ReadOnlyDictionary<uint, SourceObjectFact> _parentLive;
    private readonly ReadOnlyCollection<NormalizedSaveFact> _allFacts;
    private readonly ReadOnlyCollection<NormalizedInsertFact> _inserts;
    private readonly ReadOnlyCollection<NormalizedUpdateFact> _updates;
    private readonly ReadOnlyCollection<NormalizedRemoveFact> _removes;
    private readonly ReadOnlyCollection<NormalizedNoChangeFact> _noChanges;
    private readonly ReadOnlyDictionary<uint, LogicalObjectState> _parentLiveStates;
    private readonly ReadOnlyDictionary<uint, LogicalObjectState> _postLiveStates;

    internal NormalizedSaveFacts(
        uint previousFileNumber,
        uint currentFileNumber,
        AbsoluteFrameAddress publishedRevisionAddress,
        IEnumerable<SourceObjectFact> parentLive,
        IEnumerable<NormalizedSaveFact> allFacts) {
        ArgumentNullException.ThrowIfNull(parentLive);
        ArgumentNullException.ThrowIfNull(allFacts);

        if (previousFileNumber == 0 ||
            currentFileNumber != checked(previousFileNumber + 1) ||
            publishedRevisionAddress.FileNumber != currentFileNumber) {
            throw new ArgumentException(
                "Normalized Save facts require an adjacent A/B scope and a PublishedRevision in B.");
        }

        SortedDictionary<uint, SourceObjectFact> canonicalParentLive = [];
        foreach (SourceObjectFact source in parentLive) {
            ArgumentNullException.ThrowIfNull(source);
            if (!canonicalParentLive.TryAdd(source.ObjectId, source)) {
                throw new ArgumentException(
                    $"Parent live facts contain duplicate ObjectId {source.ObjectId}.",
                    nameof(parentLive));
            }
        }

        NormalizedSaveFact[] canonicalAllFacts = allFacts
            .Select(static fact => fact ?? throw new ArgumentException(
                "Normalized Save facts cannot contain null.",
                nameof(allFacts)))
            .OrderBy(static fact => fact.ObjectId)
            .ToArray();
        for (int index = 1; index < canonicalAllFacts.Length; index++) {
            if (canonicalAllFacts[index - 1].ObjectId == canonicalAllFacts[index].ObjectId) {
                throw new ArgumentException(
                    $"Normalized Save facts contain duplicate ObjectId " +
                    $"{canonicalAllFacts[index].ObjectId}.",
                    nameof(allFacts));
            }
        }

        List<NormalizedInsertFact> inserts = [];
        List<NormalizedUpdateFact> updates = [];
        List<NormalizedRemoveFact> removes = [];
        List<NormalizedNoChangeFact> noChanges = [];
        foreach (NormalizedSaveFact fact in canonicalAllFacts) {
            switch (fact) {
                case NormalizedInsertFact insert:
                    inserts.Add(insert);
                    break;
                case NormalizedUpdateFact update:
                    updates.Add(update);
                    break;
                case NormalizedRemoveFact remove:
                    removes.Add(remove);
                    break;
                case NormalizedNoChangeFact noChange:
                    noChanges.Add(noChange);
                    break;
                default:
                    throw new ArgumentException(
                        $"Unsupported normalized Save fact type '{fact.GetType().FullName}'.",
                        nameof(allFacts));
            }
        }

        ValidatePartitions(canonicalParentLive, inserts, updates, removes, noChanges);

        SortedDictionary<uint, LogicalObjectState> parentLiveStates = new(
            canonicalParentLive.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.State));
        SortedDictionary<uint, LogicalObjectState> postLiveStates = new(parentLiveStates);
        foreach (NormalizedSaveFact fact in canonicalAllFacts) {
            switch (fact) {
                case NormalizedInsertFact insert:
                    postLiveStates.Add(insert.ObjectId, insert.ResultState);
                    break;
                case NormalizedUpdateFact update:
                    postLiveStates[update.ObjectId] = update.ResultState;
                    break;
                case NormalizedRemoveFact remove:
                    _ = postLiveStates.Remove(remove.ObjectId);
                    break;
            }
        }

        PreviousFileNumber = previousFileNumber;
        CurrentFileNumber = currentFileNumber;
        PublishedRevisionAddress = publishedRevisionAddress;
        _parentLive = new ReadOnlyDictionary<uint, SourceObjectFact>(canonicalParentLive);
        _allFacts = Array.AsReadOnly(canonicalAllFacts);
        _inserts = inserts.AsReadOnly();
        _updates = updates.AsReadOnly();
        _removes = removes.AsReadOnly();
        _noChanges = noChanges.AsReadOnly();
        _parentLiveStates = new ReadOnlyDictionary<uint, LogicalObjectState>(
            parentLiveStates);
        _postLiveStates = new ReadOnlyDictionary<uint, LogicalObjectState>(
            postLiveStates);
    }

    public uint PreviousFileNumber { get; }

    public uint CurrentFileNumber { get; }

    public AbsoluteFrameAddress PublishedRevisionAddress { get; }

    public IReadOnlyDictionary<uint, SourceObjectFact> ParentLive => _parentLive;

    public IReadOnlyList<NormalizedSaveFact> AllFacts => _allFacts;

    public IReadOnlyList<NormalizedInsertFact> Inserts => _inserts;

    public IReadOnlyList<NormalizedUpdateFact> Updates => _updates;

    public IReadOnlyList<NormalizedRemoveFact> Removes => _removes;

    public IReadOnlyList<NormalizedNoChangeFact> NoChanges => _noChanges;

    public IReadOnlyDictionary<uint, LogicalObjectState> ParentLiveStates =>
        _parentLiveStates;

    public IReadOnlyDictionary<uint, LogicalObjectState> PostLiveStates =>
        _postLiveStates;

    private static void ValidatePartitions(
        IReadOnlyDictionary<uint, SourceObjectFact> parentLive,
        IEnumerable<NormalizedInsertFact> inserts,
        IEnumerable<NormalizedUpdateFact> updates,
        IEnumerable<NormalizedRemoveFact> removes,
        IEnumerable<NormalizedNoChangeFact> noChanges) {
        HashSet<uint> coveredParentObjectIds = [];
        foreach (NormalizedInsertFact insert in inserts) {
            if (parentLive.ContainsKey(insert.ObjectId)) {
                throw new ArgumentException(
                    $"Insert ObjectId {insert.ObjectId} is already live in the parent Revision.");
            }
        }

        foreach (SourceObjectFact source in updates.Select(static fact => fact.Source)
            .Concat(removes.Select(static fact => fact.Source))
            .Concat(noChanges.Select(static fact => fact.Source))) {
            if (!parentLive.TryGetValue(source.ObjectId, out SourceObjectFact? parent) ||
                !ReferenceEquals(parent, source)) {
                throw new ArgumentException(
                    $"ObjectId {source.ObjectId} does not use its canonical parent-live fact.");
            }

            if (!coveredParentObjectIds.Add(source.ObjectId)) {
                throw new ArgumentException(
                    $"Parent ObjectId {source.ObjectId} occurs in more than one Save partition.");
            }
        }

        if (coveredParentObjectIds.Count != parentLive.Count) {
            throw new ArgumentException(
                "Update, Remove, and NoChange partitions must cover every parent-live object exactly once.");
        }
    }
}
