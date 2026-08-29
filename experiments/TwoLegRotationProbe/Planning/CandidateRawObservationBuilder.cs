using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Rotation;
using Atelia.TwoLegRotationProbe.Simulation;

namespace Atelia.TwoLegRotationProbe.Planning;

internal static class CandidateRawObservationBuilder {
    public static CandidateRawObservation Create(
        RbfFileStore store,
        NormalizedSaveFacts facts,
        CandidateTarget target,
        PlannedRevisionV0 candidate) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(candidate);
        if (!Enum.IsDefined(target)) {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        uint expectedFileNumber = target switch {
            CandidateTarget.StayB => facts.CurrentFileNumber,
            CandidateTarget.RotateC => checked(facts.CurrentFileNumber + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
        if (candidate.FileNumber != expectedFileNumber) {
            throw new InvalidDataException(
                $"{target} candidate targets file {candidate.FileNumber}, expected " +
                $"{expectedFileNumber}.");
        }

        (int foregroundRecordBytes, int maintenanceRecordBytes) =
            ClassifyDomainRecordBytes(facts, candidate.Estimate);
        CandidateReconstructionObservation reconstruction = ObserveReconstruction(
            store,
            facts,
            target,
            candidate);
        return new CandidateRawObservation(
            facts,
            target,
            candidate,
            foregroundRecordBytes,
            maintenanceRecordBytes,
            reconstruction);
    }

    private static (int ForegroundBytes, int MaintenanceBytes)
        ClassifyDomainRecordBytes(
            NormalizedSaveFacts facts,
            ProvisionalRevisionV0Estimate estimate) {
        Dictionary<uint, NormalizedSaveFact> factsByObjectId = facts.AllFacts
            .ToDictionary(static fact => fact.ObjectId);
        int foregroundBytes = 0;
        int maintenanceBytes = 0;
        foreach (ProvisionalDomainRecordEstimate record in estimate.DomainRecords) {
            if (!factsByObjectId.TryGetValue(
                record.ObjectId,
                out NormalizedSaveFact? fact)) {
                throw new InvalidDataException(
                    $"Candidate record {record.ObjectId} has no normalized Save fact.");
            }

            switch (fact) {
                case NormalizedInsertFact or NormalizedUpdateFact:
                    foregroundBytes = checked(
                        foregroundBytes + record.FullRecordBytes);
                    break;
                case NormalizedNoChangeFact:
                    maintenanceBytes = checked(
                        maintenanceBytes + record.FullRecordBytes);
                    break;
                default:
                    throw new InvalidDataException(
                        $"Candidate record {record.ObjectId} belongs to non-written fact " +
                        $"'{fact.GetType().Name}'.");
            }
        }

        return (foregroundBytes, maintenanceBytes);
    }

    private static CandidateReconstructionObservation ObserveReconstruction(
        RbfFileStore store,
        NormalizedSaveFacts facts,
        CandidateTarget target,
        PlannedRevisionV0 candidate) {
        FileScope resultScope = new(candidate.FileNumber);
        uint previousFileNumber = resultScope.PreviousFileNumber
            ?? throw new InvalidDataException(
                $"Candidate file {candidate.FileNumber} has no Previous file.");
        Frame frame = candidate.Frame;
        ObjectVersionDictionary dictionary = frame.ObjectVersionDictionary
            ?? throw new InvalidDataException(
                "A candidate observation requires an explicit runtime OVD.");

        HashSet<AbsoluteFrameAddress> uniqueFrameAddresses = [];
        HashSet<AbsoluteFrameAddress> previousFrameAddresses = [];
        List<uint> previousDependentObjectIds = [];
        long previousDependentBasePayloadBytes = 0;
        int requiredObjectVersionCount = 0;
        long requiredObjectPayloadBytes = 0;

        foreach ((uint objectId, _) in facts.PostLiveStates) {
            SourceObjectFact? source = facts.ParentLive.GetValueOrDefault(objectId);
            IReadOnlyList<AbsoluteFrameAddress> path;
            long objectRequiredPayloadBytes;
            if (frame.ObjectVersions.TryGetValue(
                objectId,
                out ObjectVersion? candidateVersion)) {
                ValidateSelfBinding(dictionary, objectId);
                if (candidateVersion.Kind == ObjectVersionKind.Base) {
                    path = [candidate.Address];
                    objectRequiredPayloadBytes = candidateVersion.PayloadBytes;
                } else if (candidateVersion.Kind == ObjectVersionKind.Delta) {
                    if (source is null) {
                        throw new InvalidDataException(
                            $"Candidate Delta {objectId} has no source object fact.");
                    }

                    ValidateExactDeltaParent(candidate, source, candidateVersion);
                    path = [candidate.Address, .. source.ReconstructionFrameAddresses];
                    objectRequiredPayloadBytes = checked(
                        candidateVersion.PayloadBytes +
                        source.HeadReconstructionObjectPayloadBytes);
                    if (candidateVersion.ReconstructionObjectPayloadBytes !=
                        objectRequiredPayloadBytes) {
                        throw new InvalidDataException(
                            $"Candidate Delta {objectId} reconstruction payload disagrees " +
                            "with its frozen source path.");
                    }
                } else {
                    throw new InvalidDataException(
                        $"Candidate object {objectId} has invalid version kind " +
                        $"{candidateVersion.Kind}.");
                }
            } else {
                if (source is null) {
                    throw new InvalidDataException(
                        $"PostLive object {objectId} has neither a candidate record nor a source fact.");
                }

                ValidateInheritedBinding(
                    target,
                    candidate,
                    dictionary,
                    source);
                path = source.ReconstructionFrameAddresses;
                objectRequiredPayloadBytes =
                    source.HeadReconstructionObjectPayloadBytes;
            }

            bool dependsOnPrevious = false;
            foreach (AbsoluteFrameAddress address in path) {
                EnsureAddressInResultScope(resultScope, objectId, address);
                _ = uniqueFrameAddresses.Add(address);
                if (address.FileNumber == previousFileNumber) {
                    dependsOnPrevious = true;
                    _ = previousFrameAddresses.Add(address);
                }
            }

            requiredObjectVersionCount = checked(
                requiredObjectVersionCount + path.Count);
            requiredObjectPayloadBytes = checked(
                requiredObjectPayloadBytes + objectRequiredPayloadBytes);
            if (dependsOnPrevious) {
                previousDependentObjectIds.Add(objectId);
                previousDependentBasePayloadBytes = checked(
                    previousDependentBasePayloadBytes +
                    facts.PostLiveStates[objectId].BasePayloadBytes);
            }
        }

        AbsoluteFrameAddress[] canonicalFrameAddresses = uniqueFrameAddresses
            .OrderBy(static address => address.FileNumber)
            .ThenBy(static address => address.FrameTicket.OffsetBytes)
            .ThenBy(static address => address.FrameTicket.LengthBytes)
            .ToArray();
        long objectPayloadBytesInUniqueFrames = 0;
        long modeledRbfFrameBytesRead = 0;
        long previousFileFrameBytes = 0;
        foreach (AbsoluteFrameAddress address in canonicalFrameAddresses) {
            Frame observedFrame;
            RbfFrameLayoutEstimate layout;
            if (address == candidate.Address) {
                observedFrame = candidate.Frame;
                layout = candidate.Estimate.RbfLayout;
            } else {
                observedFrame = store.ReadFrame(address);
                layout = store.ReadLayout(address);
                ProvisionalRevisionV0Estimate provenance;
                try {
                    provenance = ProvisionalRevisionV0Estimator.Estimate(
                        observedFrame,
                        layout.FrameStartOffsetBytes);
                } catch (RevisionCandidateCapacityException exception) {
                    throw new InvalidDataException(
                        $"Source frame {address} has missing or inconsistent " +
                        "ProvisionalRevisionV0 accounting provenance.",
                        exception);
                }

                if (provenance.RbfLayout != layout) {
                    throw new InvalidDataException(
                        $"Source frame {address} has missing or inconsistent " +
                        "ProvisionalRevisionV0 accounting provenance.");
                }
            }

            foreach (ObjectVersion version in observedFrame.ObjectVersions.Values) {
                objectPayloadBytesInUniqueFrames = checked(
                    objectPayloadBytesInUniqueFrames + version.PayloadBytes);
            }

            modeledRbfFrameBytesRead = checked(
                modeledRbfFrameBytesRead + layout.FrameLengthBytes);
            if (address.FileNumber == previousFileNumber) {
                previousFileFrameBytes = checked(
                    previousFileFrameBytes + layout.FrameLengthBytes);
            }
        }

        PostSaveReconstructionMetrics metrics = new(
            AccountingScope.ProvisionalRevisionV0,
            facts.PostLiveStates.Count,
            requiredObjectVersionCount,
            canonicalFrameAddresses.Length,
            requiredObjectPayloadBytes,
            objectPayloadBytesInUniqueFrames,
            modeledRbfFrameBytesRead);
        return new CandidateReconstructionObservation(
            resultScope,
            canonicalFrameAddresses,
            previousDependentObjectIds,
            previousDependentBasePayloadBytes,
            previousFrameAddresses.Count,
            previousFileFrameBytes,
            metrics);
    }

    private static void ValidateSelfBinding(
        ObjectVersionDictionary dictionary,
        uint objectId) {
        if (!dictionary.Entries.TryGetValue(
            objectId,
            out ObjectVersionDictionaryBinding binding) ||
            binding.Kind != ObjectVersionDictionaryBindingKind.Self) {
            throw new InvalidDataException(
                $"Candidate object record {objectId} is not bound to Self.");
        }
    }

    private static void ValidateExactDeltaParent(
        PlannedRevisionV0 candidate,
        SourceObjectFact source,
        ObjectVersion version) {
        if (version.DeltaParentFrameTicket is not RelativeFrameTicket parent ||
            new FileScope(candidate.FileNumber).Resolve(parent) != source.HeadAddress) {
            throw new InvalidDataException(
                $"Candidate Delta {source.ObjectId} does not point to its exact source head.");
        }
    }

    private static void ValidateInheritedBinding(
        CandidateTarget target,
        PlannedRevisionV0 candidate,
        ObjectVersionDictionary dictionary,
        SourceObjectFact source) {
        if (target == CandidateTarget.StayB) {
            if (dictionary.Kind != ObjectVersionDictionaryKind.Delta ||
                dictionary.Entries.ContainsKey(source.ObjectId)) {
                throw new InvalidDataException(
                    $"Inherited Stay-B object {source.ObjectId} is not an OVD Delta miss.");
            }

            return;
        }

        if (dictionary.Kind != ObjectVersionDictionaryKind.Base ||
            !dictionary.Entries.TryGetValue(
                source.ObjectId,
                out ObjectVersionDictionaryBinding binding) ||
            binding.Kind != ObjectVersionDictionaryBindingKind.External ||
            binding.ExternalFrameTicket is not RelativeFrameTicket external ||
            new FileScope(candidate.FileNumber).Resolve(external) != source.HeadAddress) {
            throw new InvalidDataException(
                $"Inherited Rotate-C object {source.ObjectId} is not bound to its exact source head.");
        }
    }

    private static void EnsureAddressInResultScope(
        FileScope resultScope,
        uint objectId,
        AbsoluteFrameAddress address) {
        if (address.FileNumber != resultScope.CurrentFileNumber &&
            address.FileNumber != resultScope.PreviousFileNumber) {
            throw new InvalidDataException(
                $"PostLive object {objectId} reconstruction address {address} is outside " +
                $"result scope {resultScope.PreviousFileNumber}/{resultScope.CurrentFileNumber}.");
        }
    }
}
