using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Rotation;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class RotateCRevisionCandidateTests {
    private const uint ADependentNoChangeObjectId = 10;
    private const uint ADependentUpdateObjectId = 20;
    private const uint RemovedObjectId = 30;
    private const uint BContainedBaseUpdateObjectId = 40;
    private const uint BContainedDeltaUpdateObjectId = 41;
    private const uint ExternalNoChangeObjectId = 50;
    private const uint RelocatedNoChangeObjectId = 51;
    private const uint InsertedObjectId = 70;

    [Fact]
    public void Mixed_save_builds_one_full_C_revision_with_B_C_reconstruction_closure() {
        SourceFixture source = CreateSource();
        SaveStep saveStep = CreateMixedSaveStep(insertPayloadBytes: 7);
        WorkloadReplayCursor logicalReplay = new();
        _ = logicalReplay.Apply(CreatePreviousFileStep());
        _ = logicalReplay.Apply(CreateBLocalRevisionStep());
        IReadOnlyDictionary<uint, LogicalObjectState> expectedParentState =
            logicalReplay.Apply(CreatePublishedRevisionStep());
        IReadOnlyDictionary<uint, LogicalObjectState> expectedPostState =
            logicalReplay.Apply(saveStep);
        StoreSnapshot beforePlanning = CaptureStore(source.Store);

        NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            saveStep);

        Assert.Equal(
            [InsertedObjectId],
            facts.Inserts.Select(static fact => fact.ObjectId).ToArray());
        Assert.Equal(
            [
                ADependentUpdateObjectId,
                BContainedBaseUpdateObjectId,
                BContainedDeltaUpdateObjectId,
            ],
            facts.Updates.Select(static fact => fact.ObjectId).ToArray());
        Assert.Equal(
            [RemovedObjectId],
            facts.Removes.Select(static fact => fact.ObjectId).ToArray());
        Assert.Equal(
            [
                ADependentNoChangeObjectId,
                ExternalNoChangeObjectId,
                RelocatedNoChangeObjectId,
            ],
            facts.NoChanges.Select(static fact => fact.ObjectId).ToArray());
        AssertExactState(expectedParentState, facts.ParentLiveStates);
        AssertExactState(expectedPostState, facts.PostLiveStates);
        AssertHeadInBBaseInA(
            source,
            facts.ParentLive[ADependentNoChangeObjectId]);
        AssertHeadInBBaseInA(
            source,
            facts.ParentLive[ADependentUpdateObjectId]);
        Assert.Equal(
            source.BLocalRevisionAddress,
            facts.ParentLive[BContainedDeltaUpdateObjectId].HeadAddress);
        Assert.Equal(
            source.BLocalRevisionAddress,
            facts.ParentLive[ExternalNoChangeObjectId].HeadAddress);

        RotateCSaveDecision decision = CreateMixedDecision(
            [RelocatedNoChangeObjectId]);
        var plan = RotateCRevisionPlanner.Create(
            source.Store,
            facts,
            decision);

        AssertStoreSnapshot(beforePlanning, CaptureStore(source.Store));
        Assert.Equal(source.Current.FileNumber + 1, plan.Revision.FileNumber);
        Assert.Equal(
            RbfV040Layout.InitialTailOffsetBytes,
            plan.Revision.Address.FrameTicket.OffsetBytes);

        Frame candidate = plan.Revision.Frame;
        Assert.Equal(
            [
                ADependentNoChangeObjectId,
                ADependentUpdateObjectId,
                BContainedBaseUpdateObjectId,
                BContainedDeltaUpdateObjectId,
                RelocatedNoChangeObjectId,
                InsertedObjectId,
            ],
            candidate.ObjectVersions.Keys.ToArray());
        Assert.False(candidate.ObjectVersions.ContainsKey(RemovedObjectId));
        Assert.False(candidate.ObjectVersions.ContainsKey(ExternalNoChangeObjectId));

        AssertBase(
            candidate.ObjectVersions[ADependentNoChangeObjectId],
            payloadBytes: 12,
            logicalVersionOrdinal: 2);
        AssertBase(
            candidate.ObjectVersions[ADependentUpdateObjectId],
            payloadBytes: 30,
            logicalVersionOrdinal: 3);
        AssertBase(
            candidate.ObjectVersions[BContainedBaseUpdateObjectId],
            payloadBytes: 44,
            logicalVersionOrdinal: 2);
        AssertBase(
            candidate.ObjectVersions[RelocatedNoChangeObjectId],
            payloadBytes: 51,
            logicalVersionOrdinal: 1);
        AssertBase(
            candidate.ObjectVersions[InsertedObjectId],
            payloadBytes: 7,
            logicalVersionOrdinal: 1);

        ObjectVersion delta =
            candidate.ObjectVersions[BContainedDeltaUpdateObjectId];
        Assert.Equal(ObjectVersionKind.Delta, delta.Kind);
        Assert.Equal(2, delta.PayloadBytes);
        Assert.Equal(43, delta.ReconstructionObjectPayloadBytes);
        Assert.Equal(43, delta.ResultBasePayloadBytes);
        Assert.Equal(41, delta.ExpectedParentBasePayloadBytes);
        Assert.Equal(2, delta.LogicalVersionOrdinal);
        Assert.Equal(
            Previous(source.BLocalRevisionAddress.FrameTicket),
            delta.DeltaParentFrameTicket);

        ObjectVersionDictionary dictionary = Assert.IsType<ObjectVersionDictionary>(
            candidate.ObjectVersionDictionary);
        Assert.Equal(ObjectVersionDictionaryKind.Base, dictionary.Kind);
        Assert.Equal(
            Previous(source.PublishedRevisionAddress.FrameTicket),
            dictionary.ParentRevisionFrameTicket);
        Assert.Equal(
            source.PublishedRevisionAddress,
            new FileScope(plan.Revision.FileNumber).Resolve(
                Assert.IsType<RelativeFrameTicket>(
                    dictionary.ParentRevisionFrameTicket)));
        Assert.Equal(
            [
                ADependentNoChangeObjectId,
                ADependentUpdateObjectId,
                BContainedBaseUpdateObjectId,
                BContainedDeltaUpdateObjectId,
                ExternalNoChangeObjectId,
                RelocatedNoChangeObjectId,
                InsertedObjectId,
            ],
            dictionary.Entries.Keys.ToArray());
        Assert.DoesNotContain(RemovedObjectId, dictionary.Entries.Keys);
        foreach (uint objectId in new[] {
            ADependentNoChangeObjectId,
            ADependentUpdateObjectId,
            BContainedBaseUpdateObjectId,
            BContainedDeltaUpdateObjectId,
            RelocatedNoChangeObjectId,
            InsertedObjectId,
        }) {
            Assert.Equal(
                ObjectVersionDictionaryBindingKind.Self,
                dictionary.Entries[objectId].Kind);
            Assert.Null(dictionary.Entries[objectId].ExternalFrameTicket);
        }

        ObjectVersionDictionaryBinding external =
            dictionary.Entries[ExternalNoChangeObjectId];
        Assert.Equal(ObjectVersionDictionaryBindingKind.External, external.Kind);
        Assert.Equal(
            Previous(source.BLocalRevisionAddress.FrameTicket),
            external.ExternalFrameTicket);
        Assert.Equal(
            source.BLocalRevisionAddress,
            new FileScope(plan.Revision.FileNumber).Resolve(
                Assert.IsType<RelativeFrameTicket>(external.ExternalFrameTicket)));

        AssertEquivalentEstimate(
            plan.Revision.Estimate,
            ProvisionalRevisionV0Estimator.Estimate(
                candidate,
                RbfV040Layout.InitialTailOffsetBytes));

        (RbfFile next, FrameTicket appendedTicket) =
            source.Store.CreateFileWithFirstFrame(
                candidate,
                plan.Revision.Estimate.RbfLayout);
        AbsoluteFrameAddress appendedAddress = new(next.FileNumber, appendedTicket);

        Assert.Equal(plan.Revision.Address, appendedAddress);
        Assert.Same(candidate, next.Read(appendedTicket));
        Assert.Equal(
            plan.Revision.Estimate.RbfLayout,
            next.ReadLayout(appendedTicket));
        AssertEquivalentEstimate(
            plan.Revision.Estimate,
            ProvisionalRevisionV0Estimator.Estimate(
                next.Read(appendedTicket),
                appendedTicket.OffsetBytes));

        ObjectVersionDictionaryMaterializationInspection materialized =
            ObjectVersionDictionaryReader.MaterializeLive(
                source.Store,
                appendedAddress);
        Assert.Equal([appendedAddress], materialized.DictionaryRevisionAddresses);
        Assert.Equal(
            [
                KeyValuePair.Create(ADependentNoChangeObjectId, appendedAddress),
                KeyValuePair.Create(ADependentUpdateObjectId, appendedAddress),
                KeyValuePair.Create(BContainedBaseUpdateObjectId, appendedAddress),
                KeyValuePair.Create(BContainedDeltaUpdateObjectId, appendedAddress),
                KeyValuePair.Create(
                    ExternalNoChangeObjectId,
                    source.BLocalRevisionAddress),
                KeyValuePair.Create(RelocatedNoChangeObjectId, appendedAddress),
                KeyValuePair.Create(InsertedObjectId, appendedAddress),
            ],
            materialized.Bindings.ToArray());
        AssertExactState(
            facts.PostLiveStates,
            PhysicalStateOracle.Materialize(source.Store, materialized.Bindings));

        ObjectVersionDictionaryLookupInspection removedLookup =
            ObjectVersionDictionaryReader.LookupLive(
                source.Store,
                appendedAddress,
                RemovedObjectId);
        Assert.Equal(
            ObjectVersionDictionaryLookupDisposition.AbsentAtBase,
            removedLookup.Disposition);
        Assert.Equal(appendedAddress, removedLookup.DecisiveRevisionAddress);
        Assert.Null(removedLookup.BindingKind);
        Assert.Null(removedLookup.ResolvedObjectVersionAddress);

        AssertReconstructionFiles(
            source,
            materialized.Bindings,
            next.FileNumber,
            new Dictionary<uint, AbsoluteFrameAddress[]> {
                [ADependentNoChangeObjectId] = [appendedAddress],
                [ADependentUpdateObjectId] = [appendedAddress],
                [BContainedBaseUpdateObjectId] = [appendedAddress],
                [BContainedDeltaUpdateObjectId] =
                    [appendedAddress, source.BLocalRevisionAddress],
                [ExternalNoChangeObjectId] = [source.BLocalRevisionAddress],
                [RelocatedNoChangeObjectId] = [appendedAddress],
                [InsertedObjectId] = [appendedAddress],
            });

        ObjectLineageInspection aDependentLineage =
            PhysicalStateOracle.InspectObjectLineage(
                source.Store,
                ADependentNoChangeObjectId,
                materialized.Bindings[ADependentNoChangeObjectId]);
        Assert.Equal(
            [
                appendedAddress,
                source.PublishedRevisionAddress,
                source.PreviousRevisionAddress,
            ],
            aDependentLineage.ObjectVersionLineageAddresses);
        Assert.Equal(
            source.PreviousRevisionAddress,
            aDependentLineage.RootAddress);
    }

    [Fact]
    public void Equivalent_input_and_decision_orders_build_identical_C_candidates() {
        SourceFixture source = CreateSource();
        NormalizedSaveFacts firstFacts = SaveStepNormalizer.Normalize(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            CreateMixedSaveStep(insertPayloadBytes: 7));
        NormalizedSaveFacts secondFacts = SaveStepNormalizer.Normalize(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            new SaveStep([
                new UpdateObject(BContainedDeltaUpdateObjectId, 43, 2),
                new CreateObject(InsertedObjectId, 7),
                new UpdateObject(ADependentUpdateObjectId, 30, 5),
                new RemoveObject(RemovedObjectId),
                new UpdateObject(BContainedBaseUpdateObjectId, 44, 4),
            ]));
        RotateCSaveDecision firstDecision = new(
            [
                new UpdateWriteDecision(
                    BContainedBaseUpdateObjectId,
                    UpdateWriteMode.Base),
                new UpdateWriteDecision(
                    BContainedDeltaUpdateObjectId,
                    UpdateWriteMode.Delta),
            ],
            [ExternalNoChangeObjectId, RelocatedNoChangeObjectId]);
        RotateCSaveDecision secondDecision = new(
            [
                new UpdateWriteDecision(
                    BContainedDeltaUpdateObjectId,
                    UpdateWriteMode.Delta),
                new UpdateWriteDecision(
                    BContainedBaseUpdateObjectId,
                    UpdateWriteMode.Base),
            ],
            [RelocatedNoChangeObjectId, ExternalNoChangeObjectId]);
        StoreSnapshot before = CaptureStore(source.Store);

        var first = RotateCRevisionPlanner.Create(
            source.Store,
            firstFacts,
            firstDecision);
        var second = RotateCRevisionPlanner.Create(
            source.Store,
            secondFacts,
            secondDecision);

        AssertStoreSnapshot(before, CaptureStore(source.Store));
        Assert.Equal(DescribeFacts(firstFacts), DescribeFacts(secondFacts));
        Assert.Equal(first.Revision.Address, second.Revision.Address);
        Assert.Equal(
            DescribeObjectVersions(first.Revision.Frame),
            DescribeObjectVersions(second.Revision.Frame));
        Assert.Equal(
            DescribeDictionary(first.Revision.Frame),
            DescribeDictionary(second.Revision.Frame));
        AssertEquivalentEstimate(first.Revision.Estimate, second.Revision.Estimate);
    }

    [Fact]
    public void B_contained_update_decisions_must_cover_the_choice_set_exactly() {
        SourceFixture source = CreateSource();
        NormalizedSaveFacts facts = NormalizeMixed(source, insertPayloadBytes: 7);
        StoreSnapshot before = CaptureStore(source.Store);

        RotateCSaveDecision missing = new(
            [new UpdateWriteDecision(
                BContainedBaseUpdateObjectId,
                UpdateWriteMode.Base)],
            [RelocatedNoChangeObjectId]);
        Assert.Throws<ArgumentException>(() =>
            RotateCRevisionPlanner.Create(source.Store, facts, missing));

        foreach (uint extraObjectId in new[] {
            ADependentUpdateObjectId,
            ExternalNoChangeObjectId,
            999U,
        }) {
            RotateCSaveDecision extra = new(
                [
                    new UpdateWriteDecision(
                        BContainedBaseUpdateObjectId,
                        UpdateWriteMode.Base),
                    new UpdateWriteDecision(
                        BContainedDeltaUpdateObjectId,
                        UpdateWriteMode.Delta),
                    new UpdateWriteDecision(extraObjectId, UpdateWriteMode.Base),
                ],
                [RelocatedNoChangeObjectId]);
            Assert.Throws<ArgumentException>(() =>
                RotateCRevisionPlanner.Create(source.Store, facts, extra));
        }

        Assert.Throws<ArgumentException>(() => new RotateCSaveDecision(
            [
                new UpdateWriteDecision(
                    BContainedBaseUpdateObjectId,
                    UpdateWriteMode.Base),
                new UpdateWriteDecision(
                    BContainedBaseUpdateObjectId,
                    UpdateWriteMode.Delta),
                new UpdateWriteDecision(
                    BContainedDeltaUpdateObjectId,
                    UpdateWriteMode.Delta),
            ],
            [RelocatedNoChangeObjectId]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotateCSaveDecision(
            [
                new UpdateWriteDecision(
                    BContainedBaseUpdateObjectId,
                    (UpdateWriteMode)int.MaxValue),
                new UpdateWriteDecision(
                    BContainedDeltaUpdateObjectId,
                    UpdateWriteMode.Delta),
            ],
            [RelocatedNoChangeObjectId]));

        AssertStoreSnapshot(before, CaptureStore(source.Store));
    }

    [Fact]
    public void Optional_nochange_Base_selection_accepts_only_B_contained_NoChange() {
        SourceFixture source = CreateSource();
        NormalizedSaveFacts facts = NormalizeMixed(source, insertPayloadBytes: 7);
        StoreSnapshot before = CaptureStore(source.Store);

        foreach (uint rejectedObjectId in new[] {
            ADependentNoChangeObjectId,
            ADependentUpdateObjectId,
            RemovedObjectId,
            BContainedBaseUpdateObjectId,
            InsertedObjectId,
            999U,
        }) {
            RotateCSaveDecision decision = CreateMixedDecision([rejectedObjectId]);
            Assert.Throws<ArgumentException>(() =>
                RotateCRevisionPlanner.Create(source.Store, facts, decision));
            AssertStoreSnapshot(before, CaptureStore(source.Store));
        }

        Assert.Throws<ArgumentException>(() => CreateMixedDecision(
            [ExternalNoChangeObjectId, ExternalNoChangeObjectId]));
        AssertStoreSnapshot(before, CaptureStore(source.Store));
    }

    [Fact]
    public void Oversized_C_candidate_failure_does_not_mutate_store() {
        SourceFixture source = CreateSource();
        NormalizedSaveFacts facts = NormalizeMixed(
            source,
            RbfV040Layout.MaxPayloadAndTailMetaLengthBytes);
        RotateCSaveDecision decision = CreateMixedDecision(
            [RelocatedNoChangeObjectId]);
        StoreSnapshot before = CaptureStore(source.Store);

        RevisionCandidateCapacityException exception =
            Assert.Throws<RevisionCandidateCapacityException>(() =>
            RotateCRevisionPlanner.Create(source.Store, facts, decision));
        Assert.Equal(
            RevisionCandidateCapacityLimit.PayloadAndTailMetaLength,
            exception.Rejection.Limit);
        Assert.True(
            exception.Rejection.AttemptedValue > exception.Rejection.MaximumValue);
        Assert.Equal(
            RbfV040Layout.MaxPayloadAndTailMetaLengthBytes,
            exception.Rejection.MaximumValue);

        AssertStoreSnapshot(before, CaptureStore(source.Store));
    }

    [Fact]
    public void Maintenance_only_default_decision_matches_immediate_rotation_candidate() {
        SourceFixture source = CreateSource();
        NormalizedSaveFacts facts = SaveStepNormalizer.NormalizeMaintenanceOnly(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress);
        RotateCSaveDecision decision = new([], []);
        StoreSnapshot before = CaptureStore(source.Store);

        var unified = RotateCRevisionPlanner.Create(
            source.Store,
            facts,
            decision);
        ImmediateRotationPlan immediate = ImmediateRotationPlanner.Create(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress);

        AssertStoreSnapshot(before, CaptureStore(source.Store));
        Assert.Equal(
            immediate.EvacuationRevision.Address,
            unified.Revision.Address);
        Assert.Equal(
            DescribeObjectVersions(immediate.EvacuationRevision.Frame),
            DescribeObjectVersions(unified.Revision.Frame));
        Assert.Equal(
            DescribeDictionary(immediate.EvacuationRevision.Frame),
            DescribeDictionary(unified.Revision.Frame));
        AssertEquivalentEstimate(
            immediate.EvacuationRevision.Estimate,
            unified.Revision.Estimate);
    }

    private static SourceFixture CreateSource() {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        ObjectVersionDictionaryBuilder previousDictionary = new();
        previousDictionary.BindSelf(ADependentNoChangeObjectId);
        previousDictionary.BindSelf(ADependentUpdateObjectId);
        previousDictionary.BindSelf(RemovedObjectId);
        FrameBuilder previousBuilder = new() {
            ObjectVersionDictionary = previousDictionary,
        };
        AddBase(previousBuilder, ADependentNoChangeObjectId, 10, 1);
        AddBase(previousBuilder, ADependentUpdateObjectId, 20, 1);
        AddBase(previousBuilder, RemovedObjectId, 30, 1);
        AbsoluteFrameAddress previousRevisionAddress = AppendRevision(
            previous,
            previousBuilder);

        RbfFile current = store.CreateFile();
        ObjectVersionDictionaryBuilder bLocalDictionary = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = Previous(
                previousRevisionAddress.FrameTicket),
        };
        foreach (uint objectId in new[] {
            BContainedBaseUpdateObjectId,
            BContainedDeltaUpdateObjectId,
            ExternalNoChangeObjectId,
            RelocatedNoChangeObjectId,
        }) {
            bLocalDictionary.BindSelf(objectId);
        }

        FrameBuilder bLocalBuilder = new() {
            ObjectVersionDictionary = bLocalDictionary,
        };
        AddBase(bLocalBuilder, BContainedBaseUpdateObjectId, 40, 1);
        AddBase(bLocalBuilder, BContainedDeltaUpdateObjectId, 41, 1);
        AddBase(bLocalBuilder, ExternalNoChangeObjectId, 50, 1);
        AddBase(bLocalBuilder, RelocatedNoChangeObjectId, 51, 1);
        AbsoluteFrameAddress bLocalRevisionAddress = AppendRevision(
            current,
            bLocalBuilder);

        ObjectVersionDictionaryBuilder publishedDictionary = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = Current(bLocalRevisionAddress.FrameTicket),
        };
        publishedDictionary.BindSelf(ADependentNoChangeObjectId);
        publishedDictionary.BindSelf(ADependentUpdateObjectId);
        FrameBuilder publishedBuilder = new() {
            ObjectVersionDictionary = publishedDictionary,
        };
        AddDelta(
            publishedBuilder,
            ADependentNoChangeObjectId,
            Previous(previousRevisionAddress.FrameTicket),
            parentBasePayloadBytes: 10,
            resultBasePayloadBytes: 12,
            deltaPayloadBytes: 2,
            reconstructionPayloadBytes: 12,
            logicalVersionOrdinal: 2);
        AddDelta(
            publishedBuilder,
            ADependentUpdateObjectId,
            Previous(previousRevisionAddress.FrameTicket),
            parentBasePayloadBytes: 20,
            resultBasePayloadBytes: 25,
            deltaPayloadBytes: 5,
            reconstructionPayloadBytes: 25,
            logicalVersionOrdinal: 2);
        AbsoluteFrameAddress publishedRevisionAddress = AppendRevision(
            current,
            publishedBuilder);

        return new SourceFixture(
            store,
            previous,
            current,
            previousRevisionAddress,
            bLocalRevisionAddress,
            publishedRevisionAddress);
    }

    private static NormalizedSaveFacts NormalizeMixed(
        SourceFixture source,
        int insertPayloadBytes) => SaveStepNormalizer.Normalize(
        source.Store,
        source.Current.FileNumber,
        source.PublishedRevisionAddress,
        CreateMixedSaveStep(insertPayloadBytes));

    private static SaveStep CreatePreviousFileStep() => new([
        new CreateObject(ADependentNoChangeObjectId, 10),
        new CreateObject(ADependentUpdateObjectId, 20),
        new CreateObject(RemovedObjectId, 30),
    ]);

    private static SaveStep CreateBLocalRevisionStep() => new([
        new CreateObject(BContainedBaseUpdateObjectId, 40),
        new CreateObject(BContainedDeltaUpdateObjectId, 41),
        new CreateObject(ExternalNoChangeObjectId, 50),
        new CreateObject(RelocatedNoChangeObjectId, 51),
    ]);

    private static SaveStep CreatePublishedRevisionStep() => new([
        new UpdateObject(ADependentNoChangeObjectId, 12, 2),
        new UpdateObject(ADependentUpdateObjectId, 25, 5),
    ]);

    private static SaveStep CreateMixedSaveStep(int insertPayloadBytes) => new([
        new RemoveObject(RemovedObjectId),
        new UpdateObject(BContainedDeltaUpdateObjectId, 43, 2),
        new CreateObject(InsertedObjectId, insertPayloadBytes),
        new UpdateObject(ADependentUpdateObjectId, 30, 5),
        new UpdateObject(BContainedBaseUpdateObjectId, 44, 4),
    ]);

    private static RotateCSaveDecision CreateMixedDecision(
        IEnumerable<uint> noChangeBaseObjectIds) => new(
        [
            new UpdateWriteDecision(
                BContainedBaseUpdateObjectId,
                UpdateWriteMode.Base),
            new UpdateWriteDecision(
                BContainedDeltaUpdateObjectId,
                UpdateWriteMode.Delta),
        ],
        noChangeBaseObjectIds);

    private static void AssertHeadInBBaseInA(
        SourceFixture source,
        SourceObjectFact fact) {
        Assert.Equal(source.PublishedRevisionAddress, fact.HeadAddress);
        Assert.Equal(source.PreviousRevisionAddress, fact.BaseAddress);
        Assert.Equal(
            [source.PublishedRevisionAddress, source.PreviousRevisionAddress],
            fact.ReconstructionFrameAddresses);
    }

    private static void AssertReconstructionFiles(
        SourceFixture source,
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> bindings,
        uint nextFileNumber,
        IReadOnlyDictionary<uint, AbsoluteFrameAddress[]> expectedAddresses) {
        foreach ((uint objectId, AbsoluteFrameAddress headAddress) in bindings) {
            ObjectReconstructionInspection inspection =
                PhysicalStateOracle.InspectObjectReconstruction(
                    source.Store,
                    objectId,
                    headAddress);
            Assert.Equal(expectedAddresses[objectId], inspection.ReconstructionFrameAddresses);
            Assert.All(
                inspection.ReconstructionFrameAddresses,
                address => Assert.Contains(
                    address.FileNumber,
                    new[] { source.Current.FileNumber, nextFileNumber }));
            Assert.DoesNotContain(
                inspection.ReconstructionFrameAddresses,
                address => address.FileNumber == source.Previous.FileNumber);
        }
    }

    private static void AddBase(
        FrameBuilder frame,
        uint objectId,
        int payloadBytes,
        int logicalVersionOrdinal) {
        RevisionCandidateRecordBuilder.AddBase(
            frame,
            objectId,
            new LogicalObjectState(payloadBytes, logicalVersionOrdinal));
    }

    private static void AddDelta(
        FrameBuilder frame,
        uint objectId,
        RelativeFrameTicket parent,
        int parentBasePayloadBytes,
        int resultBasePayloadBytes,
        int deltaPayloadBytes,
        long reconstructionPayloadBytes,
        int logicalVersionOrdinal) {
        ObjectVersionBuilder version = frame.Add(objectId);
        version.Kind = ObjectVersionKind.Delta;
        version.PayloadBytes = deltaPayloadBytes;
        version.ReconstructionObjectPayloadBytes = reconstructionPayloadBytes;
        version.ResultBasePayloadBytes = resultBasePayloadBytes;
        version.ExpectedParentBasePayloadBytes = parentBasePayloadBytes;
        version.LogicalVersionOrdinal = logicalVersionOrdinal;
        version.DeltaParentFrameTicket = parent;
    }

    private static AbsoluteFrameAddress AppendRevision(
        RbfFile file,
        FrameBuilder builder) {
        Frame frame = builder.Build();
        ProvisionalRevisionV0Estimate estimate =
            ProvisionalRevisionV0Estimator.Estimate(frame, file.TailOffsetBytes);
        FrameTicket ticket = file.Append(
            frame,
            estimate.RbfLayout.PayloadLengthBytes,
            estimate.RbfLayout.TailMetaLengthBytes);
        Assert.Equal(estimate.RbfLayout.Ticket, ticket);
        return new AbsoluteFrameAddress(file.FileNumber, ticket);
    }

    private static void AssertBase(
        ObjectVersion version,
        int payloadBytes,
        int logicalVersionOrdinal) {
        Assert.Equal(ObjectVersionKind.Base, version.Kind);
        Assert.Equal(payloadBytes, version.PayloadBytes);
        Assert.Equal(payloadBytes, version.ReconstructionObjectPayloadBytes);
        Assert.Equal(payloadBytes, version.ResultBasePayloadBytes);
        Assert.Null(version.ExpectedParentBasePayloadBytes);
        Assert.Equal(logicalVersionOrdinal, version.LogicalVersionOrdinal);
        Assert.Null(version.DeltaParentFrameTicket);
    }

    private static void AssertExactState(
        IReadOnlyDictionary<uint, LogicalObjectState> expected,
        IReadOnlyDictionary<uint, LogicalObjectState> actual) {
        Assert.Equal(expected.Count, actual.Count);
        foreach ((uint objectId, LogicalObjectState expectedState) in expected) {
            Assert.True(actual.TryGetValue(
                objectId,
                out LogicalObjectState actualState));
            Assert.Equal(expectedState, actualState);
        }
    }

    private static void AssertEquivalentEstimate(
        ProvisionalRevisionV0Estimate expected,
        ProvisionalRevisionV0Estimate actual) {
        Assert.Equal(expected.DomainRecords, actual.DomainRecords);
        Assert.Equal(
            expected.SyntheticObjectPayloadBytes,
            actual.SyntheticObjectPayloadBytes);
        Assert.Equal(expected.DomainRecordHeaderBytes, actual.DomainRecordHeaderBytes);
        Assert.Equal(
            expected.ObjectVersionDictionaryRecordBytes,
            actual.ObjectVersionDictionaryRecordBytes);
        Assert.Equal(expected.TailMetaDirectoryBytes, actual.TailMetaDirectoryBytes);
        Assert.Equal(expected.AddressTokenBytes, actual.AddressTokenBytes);
        Assert.Equal(
            expected.ObjectVersionDictionaryPayloadOffsetBytes,
            actual.ObjectVersionDictionaryPayloadOffsetBytes);
        Assert.Equal(expected.RbfLayout, actual.RbfLayout);
    }

    private static string[] DescribeFacts(NormalizedSaveFacts facts) => facts.AllFacts
        .Select(static fact => fact switch {
            NormalizedInsertFact insert =>
                $"I:{insert.ObjectId}:{insert.ResultState}",
            NormalizedUpdateFact update =>
                $"U:{update.ObjectId}:{update.Source.State}:{update.Source.HeadAddress}:" +
                $"{update.Source.BaseAddress}:{update.ResultState}:" +
                $"{update.DeltaPayloadBytes}",
            NormalizedRemoveFact remove =>
                $"R:{remove.ObjectId}:{remove.Source.State}:" +
                $"{remove.Source.HeadAddress}:{remove.Source.BaseAddress}",
            NormalizedNoChangeFact noChange =>
                $"N:{noChange.ObjectId}:{noChange.Source.State}:" +
                $"{noChange.Source.HeadAddress}:{noChange.Source.BaseAddress}",
            _ => throw new InvalidOperationException(),
        })
        .ToArray();

    private static string[] DescribeObjectVersions(Frame frame) => frame.ObjectVersions
        .Select(static pair =>
            $"{pair.Key}:{pair.Value.Kind}:{pair.Value.PayloadBytes}:" +
            $"{pair.Value.ReconstructionObjectPayloadBytes}:" +
            $"{pair.Value.ResultBasePayloadBytes}:" +
            $"{pair.Value.ExpectedParentBasePayloadBytes}:" +
            $"{pair.Value.LogicalVersionOrdinal}:" +
            $"{pair.Value.DeltaParentFrameTicket}")
        .ToArray();

    private static string[] DescribeDictionary(Frame frame) {
        ObjectVersionDictionary dictionary = Assert.IsType<ObjectVersionDictionary>(
            frame.ObjectVersionDictionary);
        return
        [
            $"{dictionary.Kind}:{dictionary.ParentRevisionFrameTicket}",
            .. dictionary.Entries.Select(static pair =>
                $"{pair.Key}:{pair.Value.Kind}:{pair.Value.ExternalFrameTicket}"),
        ];
    }

    private static RelativeFrameTicket Previous(FrameTicket ticket) =>
        new(IsPreviousFile: true, ticket);

    private static RelativeFrameTicket Current(FrameTicket ticket) =>
        new(IsPreviousFile: false, ticket);

    private static StoreSnapshot CaptureStore(RbfFileStore store) => new(
        store.FileCount,
        Enumerable.Range(1, store.FileCount)
            .Select(index => store.GetFile((uint)index))
            .Select(static file => new FileSnapshot(
                file.FileNumber,
                file.FrameCount,
                file.TailOffsetBytes))
            .ToArray());

    private static void AssertStoreSnapshot(
        StoreSnapshot expected,
        StoreSnapshot actual) {
        Assert.Equal(expected.FileCount, actual.FileCount);
        Assert.Equal(expected.Files, actual.Files);
    }

    private sealed record SourceFixture(
        RbfFileStore Store,
        RbfFile Previous,
        RbfFile Current,
        AbsoluteFrameAddress PreviousRevisionAddress,
        AbsoluteFrameAddress BLocalRevisionAddress,
        AbsoluteFrameAddress PublishedRevisionAddress);

    private sealed record StoreSnapshot(
        int FileCount,
        IReadOnlyList<FileSnapshot> Files);

    private sealed record FileSnapshot(
        uint FileNumber,
        int FrameCount,
        long TailOffsetBytes);
}
