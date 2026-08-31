using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Planning;

namespace Atelia.TwoLegRotationProbe.Arena;

/// <summary>
/// Canonical, immutable, payload-only strategy input for one Save step.
/// It contains no physical address, future workload, feasibility, or metric facts.
/// </summary>
public sealed class StrategyStepViewV1 {
    private readonly ReadOnlyCollection<StrategyObjectFactV1> _objects;

    public StrategyStepViewV1(IEnumerable<StrategyObjectFactV1> objects) {
        ArgumentNullException.ThrowIfNull(objects);

        StrategyObjectFactV1[] canonicalObjects = objects
            .Select(static fact => fact ?? throw new ArgumentException(
                "Strategy facts cannot contain null.",
                nameof(objects)))
            .OrderBy(static fact => fact.ObjectId)
            .ToArray();
        for (int index = 1; index < canonicalObjects.Length; index++) {
            if (canonicalObjects[index - 1].ObjectId == canonicalObjects[index].ObjectId) {
                throw new ArgumentException(
                    $"Strategy facts contain duplicate ObjectId " +
                    $"{canonicalObjects[index].ObjectId}.",
                    nameof(objects));
            }
        }

        long graphBasePayloadBytes = 0;
        long evacuationBasePayloadBytes = 0;
        bool hasParentPreviousDebt = false;
        foreach (StrategyObjectFactV1 fact in canonicalObjects) {
            if (fact.ResultBasePayloadBytes is int resultBasePayloadBytes) {
                graphBasePayloadBytes = checked(
                    graphBasePayloadBytes + resultBasePayloadBytes);
                if (fact.SourceIsPreviousDependent is true) {
                    evacuationBasePayloadBytes = checked(
                        evacuationBasePayloadBytes + resultBasePayloadBytes);
                }
            }

            hasParentPreviousDebt |= fact.SourceIsPreviousDependent is true;
        }

        _objects = Array.AsReadOnly(canonicalObjects);
        PostLiveGraphBasePayloadBytes = graphBasePayloadBytes;
        ADependentEvacuationBasePayloadBytes = evacuationBasePayloadBytes;
        HasParentPreviousDebt = hasParentPreviousDebt;
    }

    public IReadOnlyList<StrategyObjectFactV1> Objects => _objects;

    /// <summary>
    /// Sum of post-Save Base payload bytes for Insert, Update, and NoChange (G).
    /// </summary>
    public long PostLiveGraphBasePayloadBytes { get; }

    /// <summary>
    /// Sum of post-Save Base payload bytes for live objects whose source Base is
    /// in the Previous file (E). Removed objects do not contribute.
    /// </summary>
    public long ADependentEvacuationBasePayloadBytes { get; }

    /// <summary>
    /// Whether any parent-live object depends on the Previous file. Unlike E,
    /// this includes an A-dependent object removed by this Save.
    /// </summary>
    public bool HasParentPreviousDebt { get; }

    internal static StrategyStepViewV1 Create(NormalizedSaveFacts facts) {
        ArgumentNullException.ThrowIfNull(facts);

        StrategyObjectFactV1[] objects = facts.AllFacts
            .Select(fact => ProjectFact(facts, fact))
            .ToArray();
        return new StrategyStepViewV1(objects);
    }

    private static StrategyObjectFactV1 ProjectFact(
        NormalizedSaveFacts facts,
        NormalizedSaveFact fact) => fact switch {
            NormalizedInsertFact insert => StrategyObjectFactV1.Insert(
                insert.ObjectId,
                insert.ResultState.BasePayloadBytes),
            NormalizedUpdateFact update => StrategyObjectFactV1.Update(
                update.ObjectId,
                IsPreviousDependent(facts, update.Source),
                update.Source.State.BasePayloadBytes,
                update.Source.HeadReconstructionObjectPayloadBytes,
                update.ResultState.BasePayloadBytes,
                update.DeltaPayloadBytes),
            NormalizedRemoveFact remove => StrategyObjectFactV1.Remove(
                remove.ObjectId,
                IsPreviousDependent(facts, remove.Source),
                remove.Source.State.BasePayloadBytes,
                remove.Source.HeadReconstructionObjectPayloadBytes),
            NormalizedNoChangeFact noChange => StrategyObjectFactV1.NoChange(
                noChange.ObjectId,
                IsPreviousDependent(facts, noChange.Source),
                noChange.Source.State.BasePayloadBytes,
                noChange.Source.HeadReconstructionObjectPayloadBytes),
            _ => throw new InvalidDataException(
                $"Unsupported normalized Save fact type '{fact.GetType().FullName}'."),
        };

    private static bool IsPreviousDependent(
        NormalizedSaveFacts facts,
        SourceObjectFact source) {
        if (source.BaseAddress.FileNumber == facts.PreviousFileNumber) {
            return true;
        }

        if (source.BaseAddress.FileNumber == facts.CurrentFileNumber) {
            return false;
        }

        throw new InvalidDataException(
            $"Object {source.ObjectId} terminating Base {source.BaseAddress} is outside " +
            $"source files {facts.PreviousFileNumber}/{facts.CurrentFileNumber}.");
    }
}
