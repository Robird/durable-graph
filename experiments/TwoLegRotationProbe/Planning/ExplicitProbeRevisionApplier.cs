using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Rotation;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Planning;

/// <summary>
/// Explicit mutation boundary for the single-threaded in-memory probe. It appends one
/// already-selected exact candidate but does not publish a durable StateStore head.
/// </summary>
internal static class ExplicitProbeRevisionApplier {
    public static ProbeRevisionCursor ApplyStayB(
        RbfFileStore store,
        ProbeRevisionCursor expectedSource,
        FeasibleCandidate<StayBRevisionPlan> selected) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(expectedSource);
        ArgumentNullException.ThrowIfNull(selected);

        StayBRevisionPlan plan = selected.Plan ?? throw new ArgumentException(
            "A feasible Stay-B selection requires a plan.",
            nameof(selected));
        PlannedRevisionV0 candidate = ValidateFeasibleIdentity(
            selected.Observation,
            plan.Facts,
            plan.Revision,
            CandidateTarget.StayB,
            nameof(selected));
        RbfFile currentFile = ValidateSource(
            store,
            expectedSource,
            plan.Facts);
        CandidateRawObservation revalidatedObservation =
            ValidateCandidate(
                store,
                plan.Facts,
                CandidateTarget.StayB,
                candidate,
                ObjectVersionDictionaryKind.Delta);
        ValidateStayBTarget(
            plan.Facts,
            expectedSource,
            candidate,
            revalidatedObservation);

        ProbeRevisionCursor result = new(
            new FileScope(plan.Facts.CurrentFileNumber),
            candidate.Address,
            candidate.Estimate.RbfLayout.TailOffsetAfterBytes);

        _ = currentFile.Append(
            candidate.Frame,
            candidate.Estimate.RbfLayout.PayloadLengthBytes,
            candidate.Estimate.RbfLayout.TailMetaLengthBytes);
        return result;
    }

    public static ProbeRevisionCursor ApplyRotateC(
        RbfFileStore store,
        ProbeRevisionCursor expectedSource,
        FeasibleCandidate<RotateCRevisionPlan> selected) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(expectedSource);
        ArgumentNullException.ThrowIfNull(selected);

        RotateCRevisionPlan plan = selected.Plan ?? throw new ArgumentException(
            "A feasible Rotate-C selection requires a plan.",
            nameof(selected));
        PlannedRevisionV0 candidate = ValidateFeasibleIdentity(
            selected.Observation,
            plan.Facts,
            plan.Revision,
            CandidateTarget.RotateC,
            nameof(selected));
        _ = ValidateSource(store, expectedSource, plan.Facts);
        CandidateRawObservation revalidatedObservation =
            ValidateCandidate(
                store,
                plan.Facts,
                CandidateTarget.RotateC,
                candidate,
                ObjectVersionDictionaryKind.Base);
        ValidateRotateCTarget(
            store,
            plan.Facts,
            candidate,
            revalidatedObservation);

        ProbeRevisionCursor result = new(
            new FileScope(candidate.FileNumber),
            candidate.Address,
            candidate.Estimate.RbfLayout.TailOffsetAfterBytes);

        _ = store.CreateFileWithFirstFrame(
            candidate.Frame,
            candidate.Estimate.RbfLayout);
        return result;
    }

    private static PlannedRevisionV0 ValidateFeasibleIdentity(
        CandidateRawObservation? observation,
        NormalizedSaveFacts facts,
        PlannedRevisionV0 candidate,
        CandidateTarget expectedTarget,
        string parameterName) {
        if (observation is null ||
            observation.Target != expectedTarget ||
            !ReferenceEquals(observation.Facts, facts) ||
            !ReferenceEquals(observation.Candidate, candidate)) {
            throw new ArgumentException(
                $"The feasible {expectedTarget} selection does not retain its exact candidate identity.",
                parameterName);
        }

        return candidate;
    }

    private static RbfFile ValidateSource(
        RbfFileStore store,
        ProbeRevisionCursor expectedSource,
        NormalizedSaveFacts facts) {
        if (facts.PreviousFileNumber == 0 ||
            facts.CurrentFileNumber != checked(facts.PreviousFileNumber + 1) ||
            expectedSource.FileScope.CurrentFileNumber != facts.CurrentFileNumber ||
            expectedSource.FileScope.PreviousFileNumber != facts.PreviousFileNumber ||
            expectedSource.PublishedRevisionAddress != facts.PublishedRevisionAddress) {
            throw new InvalidDataException(
                "The caller cursor and normalized source A/B PublishedRevision disagree.");
        }

        if ((uint)store.FileCount != facts.CurrentFileNumber) {
            throw new InvalidDataException(
                $"The source store must end at Current file {facts.CurrentFileNumber}; " +
                $"it currently contains {store.FileCount} files.");
        }

        _ = store.GetFile(facts.PreviousFileNumber);
        RbfFile currentFile = store.GetFile(facts.CurrentFileNumber);
        if (currentFile.TailOffsetBytes != expectedSource.CurrentFileTailOffsetBytes) {
            throw new InvalidDataException(
                $"Current file {facts.CurrentFileNumber} tail is " +
                $"{currentFile.TailOffsetBytes}, but the caller expects " +
                $"{expectedSource.CurrentFileTailOffsetBytes}.");
        }

        try {
            _ = store.ReadFrame(facts.PublishedRevisionAddress);
            _ = store.ReadLayout(facts.PublishedRevisionAddress);
        } catch (Exception exception) when (
            exception is ArgumentOutOfRangeException or KeyNotFoundException) {
            throw new InvalidDataException(
                $"Published revision {facts.PublishedRevisionAddress} is not readable.",
                exception);
        }

        IReadOnlyDictionary<uint, AbsoluteFrameAddress> actualBindings =
            ObjectVersionDictionaryReader.MaterializeLive(
                store,
                facts.PublishedRevisionAddress).Bindings;
        if (actualBindings.Count != facts.ParentLive.Count ||
            facts.ParentLive.Any(pair =>
                !actualBindings.TryGetValue(
                    pair.Key,
                    out AbsoluteFrameAddress actualAddress) ||
                actualAddress != pair.Value.HeadAddress)) {
            throw new InvalidDataException(
                "The source PublishedRevision live bindings no longer match the normalized facts.");
        }

        foreach ((uint objectId, SourceObjectFact source) in facts.ParentLive) {
            ObjectReconstructionInspection actual =
                PhysicalStateOracle.InspectObjectReconstruction(
                    store,
                    objectId,
                    source.HeadAddress);
            Frame headFrame = store.ReadFrame(source.HeadAddress);
            if (!headFrame.ObjectVersions.TryGetValue(
                objectId,
                out ObjectVersion? headVersion) ||
                actual.State != source.State ||
                actual.HeadAddress != source.HeadAddress ||
                actual.BaseAddress != source.BaseAddress ||
                headVersion.ReconstructionObjectPayloadBytes !=
                    source.HeadReconstructionObjectPayloadBytes ||
                !actual.ReconstructionFrameAddresses.SequenceEqual(
                    source.ReconstructionFrameAddresses)) {
                throw new InvalidDataException(
                    $"Source object {objectId} no longer matches its frozen normalized facts.");
            }
        }

        return currentFile;
    }

    private static CandidateRawObservation ValidateCandidate(
        RbfFileStore store,
        NormalizedSaveFacts facts,
        CandidateTarget target,
        PlannedRevisionV0 candidate,
        ObjectVersionDictionaryKind expectedDictionaryKind) {
        RbfFrameLayoutEstimate retainedLayout = candidate.Estimate.RbfLayout;
        RbfFrameLayoutEstimate envelope = RbfV040Layout.Estimate(
            retainedLayout.FrameStartOffsetBytes,
            retainedLayout.PayloadLengthBytes,
            retainedLayout.TailMetaLengthBytes);
        if (envelope != retainedLayout ||
            candidate.Address != new AbsoluteFrameAddress(
                candidate.FileNumber,
                retainedLayout.Ticket)) {
            throw new InvalidDataException(
                "The retained candidate address and RBF layout identity disagree.");
        }

        ObjectVersionDictionary dictionary = candidate.Frame.ObjectVersionDictionary
            ?? throw new InvalidDataException(
                "An applied candidate requires an explicit runtime OVD.");
        if (dictionary.Kind != expectedDictionaryKind ||
            dictionary.ParentRevisionFrameTicket is not RelativeFrameTicket parent ||
            ResolveRelative(candidate.FileNumber, parent) !=
                facts.PublishedRevisionAddress) {
            throw new InvalidDataException(
                $"The {target} candidate does not carry the required shared source anchor.");
        }

        ValidateCandidatePostLiveStates(facts, candidate.Frame);

        return CandidateRawObservationBuilder.Create(
            store,
            facts,
            target,
            candidate);
    }

    private static void ValidateCandidatePostLiveStates(
        NormalizedSaveFacts facts,
        Frame candidateFrame) {
        foreach ((uint objectId, ObjectVersion version) in
            candidateFrame.ObjectVersions) {
            if (!facts.PostLiveStates.TryGetValue(
                objectId,
                out LogicalObjectState expectedState) ||
                version.ResultBasePayloadBytes != expectedState.BasePayloadBytes ||
                version.LogicalVersionOrdinal != expectedState.LogicalVersionOrdinal) {
                throw new InvalidDataException(
                    $"Candidate record {objectId} does not produce its normalized PostLive state.");
            }
        }

        foreach ((uint objectId, LogicalObjectState expectedState) in
            facts.PostLiveStates) {
            if (candidateFrame.ObjectVersions.ContainsKey(objectId)) {
                continue;
            }

            if (!facts.ParentLive.TryGetValue(
                objectId,
                out SourceObjectFact? source) ||
                source.State != expectedState) {
                throw new InvalidDataException(
                    $"Unwritten candidate object {objectId} does not inherit its normalized PostLive state.");
            }
        }
    }

    private static void ValidateStayBTarget(
        NormalizedSaveFacts facts,
        ProbeRevisionCursor expectedSource,
        PlannedRevisionV0 candidate,
        CandidateRawObservation observation) {
        RbfFrameLayoutEstimate layout = candidate.Estimate.RbfLayout;
        if (candidate.FileNumber != facts.CurrentFileNumber ||
            candidate.Address.FileNumber != facts.CurrentFileNumber ||
            layout.FrameStartOffsetBytes !=
                expectedSource.CurrentFileTailOffsetBytes ||
            candidate.Address.FrameTicket != layout.Ticket) {
            throw new InvalidDataException(
                "The Stay-B candidate does not target the expected Current-file tail.");
        }

        ValidateResultScope(
            observation.PostLiveReconstruction.ResultScope,
            facts.PreviousFileNumber,
            facts.CurrentFileNumber,
            CandidateTarget.StayB);
    }

    private static void ValidateRotateCTarget(
        RbfFileStore store,
        NormalizedSaveFacts facts,
        PlannedRevisionV0 candidate,
        CandidateRawObservation observation) {
        uint nextFileNumber = checked(facts.CurrentFileNumber + 1);
        RbfFrameLayoutEstimate layout = candidate.Estimate.RbfLayout;
        if ((uint)store.FileCount != facts.CurrentFileNumber ||
            candidate.FileNumber != nextFileNumber ||
            candidate.Address.FileNumber != nextFileNumber ||
            layout.FrameStartOffsetBytes != RbfV040Layout.InitialTailOffsetBytes ||
            candidate.Address.FrameTicket != layout.Ticket) {
            throw new InvalidDataException(
                "The Rotate-C candidate does not target the first Frame of a fresh Next file.");
        }

        ValidateResultScope(
            observation.PostLiveReconstruction.ResultScope,
            facts.CurrentFileNumber,
            nextFileNumber,
            CandidateTarget.RotateC);
    }

    private static void ValidateResultScope(
        FileScope resultScope,
        uint expectedPreviousFileNumber,
        uint expectedCurrentFileNumber,
        CandidateTarget target) {
        if (resultScope.PreviousFileNumber != expectedPreviousFileNumber ||
            resultScope.CurrentFileNumber != expectedCurrentFileNumber) {
            throw new InvalidDataException(
                $"The {target} observation has an unexpected result FileScope.");
        }
    }

    private static AbsoluteFrameAddress ResolveRelative(
        uint originFileNumber,
        RelativeFrameTicket ticket) {
        try {
            return new FileScope(originFileNumber).Resolve(ticket);
        } catch (InvalidOperationException exception) {
            throw new InvalidDataException(
                $"File {originFileNumber} cannot resolve relative ticket {ticket}.",
                exception);
        }
    }
}
