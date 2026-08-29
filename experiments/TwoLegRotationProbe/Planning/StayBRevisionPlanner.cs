using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Rotation;

namespace Atelia.TwoLegRotationProbe.Planning;

/// <summary>
/// Builds one pure B-local Revision candidate from normalized Save facts and
/// caller-explicit actions. It never appends or publishes.
/// </summary>
internal static class StayBRevisionPlanner {
    public static StayBRevisionPlan Create(
        RbfFileStore store,
        NormalizedSaveFacts facts,
        StayBSaveDecision decision) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(decision);

        RbfFile currentFile = ValidateSource(store, facts);
        SortedDictionary<uint, UpdateWriteMode> updateModes =
            ValidateUpdateDecisions(facts, decision);
        HashSet<uint> migrationObjectIds = ValidateMigrationDecisions(
            facts,
            decision);

        FileScope currentScope = new(facts.CurrentFileNumber);
        ObjectVersionDictionaryBuilder dictionary = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = currentScope.Relativize(
                facts.PublishedRevisionAddress),
        };
        FrameBuilder frame = new() { ObjectVersionDictionary = dictionary };

        foreach (NormalizedSaveFact fact in facts.AllFacts) {
            switch (fact) {
                case NormalizedInsertFact insert:
                    AddBase(
                        frame,
                        insert.ObjectId,
                        insert.ResultState.BasePayloadBytes,
                        insert.ResultState.LogicalVersionOrdinal);
                    dictionary.BindSelf(insert.ObjectId);
                    break;
                case NormalizedUpdateFact update:
                    AddUpdate(
                        frame,
                        currentScope,
                        update,
                        updateModes[update.ObjectId]);
                    dictionary.BindSelf(update.ObjectId);
                    break;
                case NormalizedRemoveFact remove:
                    dictionary.Remove(remove.ObjectId);
                    break;
                case NormalizedNoChangeFact noChange
                    when migrationObjectIds.Contains(noChange.ObjectId):
                    AddBase(
                        frame,
                        noChange.ObjectId,
                        noChange.Source.State.BasePayloadBytes,
                        noChange.Source.State.LogicalVersionOrdinal);
                    dictionary.BindSelf(noChange.ObjectId);
                    break;
                case NormalizedNoChangeFact:
                    break;
                default:
                    throw new InvalidDataException(
                        $"Unsupported normalized Save fact type '{fact.GetType().FullName}'.");
            }
        }

        PlannedRevisionV0 revision = new(
            facts.CurrentFileNumber,
            frame.Build(),
            currentFile.TailOffsetBytes);
        return new StayBRevisionPlan(facts, decision, revision);
    }

    private static RbfFile ValidateSource(
        RbfFileStore store,
        NormalizedSaveFacts facts) {
        if (facts.PreviousFileNumber == 0 ||
            facts.CurrentFileNumber != checked(facts.PreviousFileNumber + 1) ||
            facts.PublishedRevisionAddress.FileNumber != facts.CurrentFileNumber) {
            throw new InvalidDataException(
                "Stay-B planning requires normalized facts for one adjacent A/B scope " +
                "and a PublishedRevision in B.");
        }

        if ((uint)store.FileCount != facts.CurrentFileNumber) {
            throw new InvalidDataException(
                $"Current file {facts.CurrentFileNumber} must be the highest existing " +
                $"RBF file; the store currently has {store.FileCount} files.");
        }

        _ = store.GetFile(facts.PreviousFileNumber);
        RbfFile currentFile = store.GetFile(facts.CurrentFileNumber);
        try {
            _ = store.ReadFrame(facts.PublishedRevisionAddress);
        } catch (Exception exception) when (
            exception is ArgumentOutOfRangeException or KeyNotFoundException) {
            throw new InvalidDataException(
                $"Published revision {facts.PublishedRevisionAddress} is not readable.",
                exception);
        }

        foreach (SourceObjectFact source in facts.ParentLive.Values) {
            EnsureAddressInScope(facts, source.ObjectId, source.HeadAddress, "head");
            EnsureAddressInScope(facts, source.ObjectId, source.BaseAddress, "Base");
            foreach (AbsoluteFrameAddress reconstructionAddress in
                source.ReconstructionFrameAddresses) {
                EnsureAddressInScope(
                    facts,
                    source.ObjectId,
                    reconstructionAddress,
                    "reconstruction");
            }
        }

        return currentFile;
    }

    private static SortedDictionary<uint, UpdateWriteMode> ValidateUpdateDecisions(
        NormalizedSaveFacts facts,
        StayBSaveDecision decision) {
        uint[] expectedObjectIds = facts.Updates
            .Select(static update => update.ObjectId)
            .ToArray();
        uint[] actualObjectIds = decision.UpdateDecisions
            .Select(static update => update.ObjectId)
            .ToArray();
        if (!expectedObjectIds.SequenceEqual(actualObjectIds)) {
            throw new ArgumentException(
                "Update write decisions must cover exactly the normalized Update ObjectIds.",
                nameof(decision));
        }

        SortedDictionary<uint, UpdateWriteMode> result = [];
        foreach (UpdateWriteDecision update in decision.UpdateDecisions) {
            result.Add(update.ObjectId, update.Mode);
        }

        return result;
    }

    private static HashSet<uint> ValidateMigrationDecisions(
        NormalizedSaveFacts facts,
        StayBSaveDecision decision) {
        Dictionary<uint, NormalizedSaveFact> factsByObjectId = facts.AllFacts
            .ToDictionary(static fact => fact.ObjectId);
        HashSet<uint> result = [];
        foreach (uint objectId in decision.UnchangedMigrationObjectIds) {
            if (!factsByObjectId.TryGetValue(
                objectId,
                out NormalizedSaveFact? fact) ||
                fact is not NormalizedNoChangeFact noChange) {
                throw new ArgumentException(
                    $"Migration ObjectId {objectId} must be a normalized NoChange object.",
                    nameof(decision));
            }

            if (noChange.Source.BaseAddress.FileNumber != facts.PreviousFileNumber) {
                throw new ArgumentException(
                    $"Migration ObjectId {objectId} is already based in Current file " +
                    $"{facts.CurrentFileNumber}.",
                    nameof(decision));
            }

            _ = result.Add(objectId);
        }

        return result;
    }

    private static void EnsureAddressInScope(
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

    private static void AddUpdate(
        FrameBuilder frame,
        FileScope currentScope,
        NormalizedUpdateFact update,
        UpdateWriteMode mode) {
        switch (mode) {
            case UpdateWriteMode.Base:
                AddBase(
                    frame,
                    update.ObjectId,
                    update.ResultState.BasePayloadBytes,
                    update.ResultState.LogicalVersionOrdinal);
                break;
            case UpdateWriteMode.Delta:
                ObjectVersionBuilder version = frame.Add(update.ObjectId);
                version.Kind = ObjectVersionKind.Delta;
                version.PayloadBytes = update.DeltaPayloadBytes;
                version.ReconstructionObjectPayloadBytes = checked(
                    update.Source.HeadReconstructionObjectPayloadBytes +
                    update.DeltaPayloadBytes);
                version.ResultBasePayloadBytes = update.ResultState.BasePayloadBytes;
                version.ExpectedParentBasePayloadBytes =
                    update.Source.State.BasePayloadBytes;
                version.LogicalVersionOrdinal = update.ResultState.LogicalVersionOrdinal;
                version.DeltaParentFrameTicket = currentScope.Relativize(
                    update.Source.HeadAddress);
                break;
            default:
                throw new InvalidDataException(
                    $"Object {update.ObjectId} has unsupported Update write mode {mode}.");
        }
    }

    private static void AddBase(
        FrameBuilder frame,
        uint objectId,
        int payloadBytes,
        int logicalVersionOrdinal) {
        ObjectVersionBuilder version = frame.Add(objectId);
        version.Kind = ObjectVersionKind.Base;
        version.PayloadBytes = payloadBytes;
        version.ReconstructionObjectPayloadBytes = payloadBytes;
        version.ResultBasePayloadBytes = payloadBytes;
        version.LogicalVersionOrdinal = logicalVersionOrdinal;
    }
}
