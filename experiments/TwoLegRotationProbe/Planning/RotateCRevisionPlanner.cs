using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Rotation;

namespace Atelia.TwoLegRotationProbe.Planning;

/// <summary>
/// Builds one pure first-Revision candidate for fresh file C. It consumes only frozen
/// normalized facts and never rematerializes, appends, creates a file, or publishes.
/// </summary>
internal static class RotateCRevisionPlanner {
    public static RotateCRevisionPlan Create(
        RbfFileStore store,
        NormalizedSaveFacts facts,
        RotateCSaveDecision decision) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(decision);

        uint nextFileNumber = ValidateSource(store, facts);
        SortedDictionary<uint, UpdateWriteMode> bContainedUpdateModes =
            ValidateUpdateDecisions(facts, decision);
        HashSet<uint> bContainedNoChangeBaseObjectIds =
            ValidateNoChangeBaseDecisions(facts, decision);

        FileScope nextScope = new(nextFileNumber);
        ObjectVersionDictionaryBuilder dictionary = new() {
            Kind = ObjectVersionDictionaryKind.Base,
            ParentRevisionFrameTicket = nextScope.Relativize(
                facts.PublishedRevisionAddress),
        };
        FrameBuilder frame = new() { ObjectVersionDictionary = dictionary };

        foreach (NormalizedSaveFact fact in facts.AllFacts) {
            switch (fact) {
                case NormalizedInsertFact insert:
                    RevisionCandidateRecordBuilder.AddBase(
                        frame,
                        insert.ObjectId,
                        insert.ResultState);
                    dictionary.BindSelf(insert.ObjectId);
                    break;
                case NormalizedUpdateFact update when IsADependent(facts, update.Source):
                    RevisionCandidateRecordBuilder.AddBase(
                        frame,
                        update.ObjectId,
                        update.ResultState);
                    dictionary.BindSelf(update.ObjectId);
                    break;
                case NormalizedUpdateFact update:
                    RevisionCandidateRecordBuilder.AddUpdate(
                        frame,
                        nextScope,
                        update,
                        bContainedUpdateModes[update.ObjectId]);
                    dictionary.BindSelf(update.ObjectId);
                    break;
                case NormalizedRemoveFact:
                    break;
                case NormalizedNoChangeFact noChange
                    when IsADependent(facts, noChange.Source) ||
                        bContainedNoChangeBaseObjectIds.Contains(noChange.ObjectId):
                    RevisionCandidateRecordBuilder.AddBase(
                        frame,
                        noChange.ObjectId,
                        noChange.Source.State);
                    dictionary.BindSelf(noChange.ObjectId);
                    break;
                case NormalizedNoChangeFact noChange:
                    dictionary.BindExternal(
                        noChange.ObjectId,
                        nextScope.Relativize(noChange.Source.HeadAddress));
                    break;
                default:
                    throw new InvalidDataException(
                        $"Unsupported normalized Save fact type '{fact.GetType().FullName}'.");
            }
        }

        PlannedRevisionV0 revision = new(
            nextFileNumber,
            frame.Build(),
            RbfV040Layout.InitialTailOffsetBytes);
        return new RotateCRevisionPlan(facts, decision, revision);
    }

    private static uint ValidateSource(
        RbfFileStore store,
        NormalizedSaveFacts facts) {
        if (facts.PreviousFileNumber == 0 ||
            facts.CurrentFileNumber != checked(facts.PreviousFileNumber + 1) ||
            facts.PublishedRevisionAddress.FileNumber != facts.CurrentFileNumber) {
            throw new InvalidDataException(
                "Rotate-C planning requires normalized facts for one adjacent A/B scope " +
                "and a PublishedRevision in B.");
        }

        if ((uint)store.FileCount != facts.CurrentFileNumber) {
            throw new InvalidDataException(
                $"Current file {facts.CurrentFileNumber} must be the highest existing " +
                $"RBF file; the store currently has {store.FileCount} files.");
        }

        _ = store.GetFile(facts.PreviousFileNumber);
        _ = store.GetFile(facts.CurrentFileNumber);
        try {
            _ = store.ReadFrame(facts.PublishedRevisionAddress);
        } catch (Exception exception) when (
            exception is ArgumentOutOfRangeException or KeyNotFoundException) {
            throw new InvalidDataException(
                $"Published revision {facts.PublishedRevisionAddress} is not readable.",
                exception);
        }

        foreach (SourceObjectFact source in facts.ParentLive.Values) {
            EnsureAddressInSourceScope(facts, source.ObjectId, source.HeadAddress, "head");
            EnsureAddressInSourceScope(facts, source.ObjectId, source.BaseAddress, "Base");
            foreach (AbsoluteFrameAddress reconstructionAddress in
                source.ReconstructionFrameAddresses) {
                EnsureAddressInSourceScope(
                    facts,
                    source.ObjectId,
                    reconstructionAddress,
                    "reconstruction");
            }

            if (source.BaseAddress.FileNumber == facts.CurrentFileNumber &&
                source.ReconstructionFrameAddresses.Any(address =>
                    address.FileNumber != facts.CurrentFileNumber)) {
                throw new InvalidDataException(
                    $"Source object {source.ObjectId} claims a B-contained Base but its " +
                    "reconstruction escapes Current file B.");
            }
        }

        return checked(facts.CurrentFileNumber + 1);
    }

    private static SortedDictionary<uint, UpdateWriteMode> ValidateUpdateDecisions(
        NormalizedSaveFacts facts,
        RotateCSaveDecision decision) {
        uint[] expectedObjectIds = facts.Updates
            .Where(update => !IsADependent(facts, update.Source))
            .Select(static update => update.ObjectId)
            .ToArray();
        uint[] actualObjectIds = decision.BContainedUpdateDecisions
            .Select(static update => update.ObjectId)
            .ToArray();
        if (!expectedObjectIds.SequenceEqual(actualObjectIds)) {
            throw new ArgumentException(
                "B-contained Update decisions must cover exactly the normalized " +
                "B-contained Update ObjectIds.",
                nameof(decision));
        }

        SortedDictionary<uint, UpdateWriteMode> result = [];
        foreach (UpdateWriteDecision update in decision.BContainedUpdateDecisions) {
            result.Add(update.ObjectId, update.Mode);
        }

        return result;
    }

    private static HashSet<uint> ValidateNoChangeBaseDecisions(
        NormalizedSaveFacts facts,
        RotateCSaveDecision decision) {
        Dictionary<uint, NormalizedSaveFact> factsByObjectId = facts.AllFacts
            .ToDictionary(static fact => fact.ObjectId);
        HashSet<uint> result = [];
        foreach (uint objectId in decision.BContainedNoChangeBaseObjectIds) {
            if (!factsByObjectId.TryGetValue(
                objectId,
                out NormalizedSaveFact? fact) ||
                fact is not NormalizedNoChangeFact noChange) {
                throw new ArgumentException(
                    $"Optional Base ObjectId {objectId} must be a normalized NoChange object.",
                    nameof(decision));
            }

            if (IsADependent(facts, noChange.Source)) {
                throw new ArgumentException(
                    $"NoChange ObjectId {objectId} is A-dependent and therefore already " +
                    "requires a Base in C.",
                    nameof(decision));
            }

            _ = result.Add(objectId);
        }

        return result;
    }

    private static bool IsADependent(
        NormalizedSaveFacts facts,
        SourceObjectFact source) =>
        source.BaseAddress.FileNumber == facts.PreviousFileNumber;

    private static void EnsureAddressInSourceScope(
        NormalizedSaveFacts facts,
        uint objectId,
        AbsoluteFrameAddress address,
        string role) {
        if (address.FileNumber != facts.PreviousFileNumber &&
            address.FileNumber != facts.CurrentFileNumber) {
            throw new InvalidDataException(
                $"Source object {objectId} {role} address {address} is outside files " +
                $"{facts.PreviousFileNumber}/{facts.CurrentFileNumber}.");
        }
    }
}
