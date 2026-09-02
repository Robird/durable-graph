using System.Collections.ObjectModel;
using Atelia.MultiSegmentStateStoreProbe.Model;

namespace Atelia.MultiSegmentStateStoreProbe.Normalization;

internal sealed class NormalizedSaveFacts {
    private readonly ReadOnlyDictionary<uint, SourceObjectFact> _parentLive;
    private readonly ReadOnlyCollection<NormalizedInsertFact> _inserts;
    private readonly ReadOnlyCollection<NormalizedUpdateFact> _updates;
    private readonly ReadOnlyCollection<NormalizedRemoveFact> _removes;
    private readonly ReadOnlyCollection<NormalizedNoChangeFact> _noChanges;
    private readonly ReadOnlyDictionary<uint, LogicalObjectState> _postLiveStates;

    internal NormalizedSaveFacts(
        AbsoluteFrameAddress? expectedParent,
        IEnumerable<SourceObjectFact> parentLive,
        IEnumerable<NormalizedInsertFact> inserts,
        IEnumerable<NormalizedUpdateFact> updates,
        IEnumerable<NormalizedRemoveFact> removes,
        IEnumerable<NormalizedNoChangeFact> noChanges) {
        ExpectedParent = expectedParent;
        SortedDictionary<uint, SourceObjectFact> canonicalParent = new(
            parentLive.ToDictionary(static source => source.ObjectId));
        NormalizedInsertFact[] canonicalInserts = inserts
            .OrderBy(static fact => fact.ObjectId).ToArray();
        NormalizedUpdateFact[] canonicalUpdates = updates
            .OrderBy(static fact => fact.ObjectId).ToArray();
        NormalizedRemoveFact[] canonicalRemoves = removes
            .OrderBy(static fact => fact.ObjectId).ToArray();
        NormalizedNoChangeFact[] canonicalNoChanges = noChanges
            .OrderBy(static fact => fact.ObjectId).ToArray();

        _parentLive = new(canonicalParent);
        _inserts = Array.AsReadOnly(canonicalInserts);
        _updates = Array.AsReadOnly(canonicalUpdates);
        _removes = Array.AsReadOnly(canonicalRemoves);
        _noChanges = Array.AsReadOnly(canonicalNoChanges);

        SortedDictionary<uint, LogicalObjectState> postLive = new(
            canonicalParent.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.State));
        foreach (NormalizedInsertFact insert in canonicalInserts) {
            postLive.Add(insert.ObjectId, insert.ResultState);
        }

        foreach (NormalizedUpdateFact update in canonicalUpdates) {
            postLive[update.ObjectId] = update.ResultState;
        }

        foreach (NormalizedRemoveFact remove in canonicalRemoves) {
            _ = postLive.Remove(remove.ObjectId);
        }

        _postLiveStates = new(postLive);
    }

    public AbsoluteFrameAddress? ExpectedParent { get; }

    public IReadOnlyDictionary<uint, SourceObjectFact> ParentLive => _parentLive;

    public IReadOnlyList<NormalizedInsertFact> Inserts => _inserts;

    public IReadOnlyList<NormalizedUpdateFact> Updates => _updates;

    public IReadOnlyList<NormalizedRemoveFact> Removes => _removes;

    public IReadOnlyList<NormalizedNoChangeFact> NoChanges => _noChanges;

    public IReadOnlyDictionary<uint, LogicalObjectState> PostLiveStates =>
        _postLiveStates;
}
