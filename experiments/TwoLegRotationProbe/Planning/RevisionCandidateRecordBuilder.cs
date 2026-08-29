using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Planning;

/// <summary>
/// Mechanical construction of modeled domain records. Target-specific eligibility and
/// policy decisions remain with the candidate planner.
/// </summary>
internal static class RevisionCandidateRecordBuilder {
    public static void AddBase(
        FrameBuilder frame,
        uint objectId,
        LogicalObjectState state) {
        ArgumentNullException.ThrowIfNull(frame);

        ObjectVersionBuilder version = frame.Add(objectId);
        version.Kind = ObjectVersionKind.Base;
        version.PayloadBytes = state.BasePayloadBytes;
        version.ReconstructionObjectPayloadBytes = state.BasePayloadBytes;
        version.ResultBasePayloadBytes = state.BasePayloadBytes;
        version.LogicalVersionOrdinal = state.LogicalVersionOrdinal;
    }

    public static void AddUpdate(
        FrameBuilder frame,
        FileScope targetScope,
        NormalizedUpdateFact update,
        UpdateWriteMode mode) {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(targetScope);
        ArgumentNullException.ThrowIfNull(update);

        switch (mode) {
            case UpdateWriteMode.Base:
                AddBase(frame, update.ObjectId, update.ResultState);
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
                version.DeltaParentFrameTicket = targetScope.Relativize(
                    update.Source.HeadAddress);
                break;
            default:
                throw new InvalidDataException(
                    $"Object {update.ObjectId} has unsupported Update write mode {mode}.");
        }
    }
}
