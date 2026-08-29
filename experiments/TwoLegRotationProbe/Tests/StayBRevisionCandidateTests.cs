using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Rotation;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class StayBRevisionCandidateTests {
    private const uint MigratedNoChangeObjectId = 10;
    private const uint DeltaUpdatedObjectId = 20;
    private const uint BaseUpdatedObjectId = 21;
    private const uint RemovedObjectId = 30;
    private const uint CurrentNoChangeObjectId = 40;
    private const uint UnmigratedDebtObjectId = 60;
    private const uint InsertedObjectId = 70;

    [Fact]
    public void Mixed_foreground_save_and_selected_migration_build_one_B_revision() {
        SourceFixture source = CreateSource();
        SaveStep saveStep = CreateMixedSaveStep();
        WorkloadReplayCursor logicalReplay = new();
        _ = logicalReplay.Apply(CreatePreviousFileStep());
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
            [
                MigratedNoChangeObjectId,
                DeltaUpdatedObjectId,
                BaseUpdatedObjectId,
                RemovedObjectId,
                CurrentNoChangeObjectId,
                UnmigratedDebtObjectId,
                InsertedObjectId,
            ],
            facts.AllFacts.Select(static fact => fact.ObjectId).ToArray());
        Assert.Equal(
            [InsertedObjectId],
            facts.Inserts.Select(static fact => fact.ObjectId).ToArray());
        Assert.Equal(
            [DeltaUpdatedObjectId, BaseUpdatedObjectId],
            facts.Updates.Select(static fact => fact.ObjectId).ToArray());
        Assert.Equal(
            [RemovedObjectId],
            facts.Removes.Select(static fact => fact.ObjectId).ToArray());
        Assert.Equal(
            [
                MigratedNoChangeObjectId,
                CurrentNoChangeObjectId,
                UnmigratedDebtObjectId,
            ],
            facts.NoChanges.Select(static fact => fact.ObjectId).ToArray());
        AssertExactState(expectedParentState, facts.ParentLiveStates);
        AssertExactState(expectedPostState, facts.PostLiveStates);

        SourceObjectFact deltaSource = facts.ParentLive[DeltaUpdatedObjectId];
        Assert.Equal(source.PublishedRevisionAddress, deltaSource.HeadAddress);
        Assert.Equal(source.PreviousRevisionAddress, deltaSource.BaseAddress);
        Assert.Equal(25, deltaSource.HeadReconstructionObjectPayloadBytes);
        Assert.Equal(
            [source.PublishedRevisionAddress, source.PreviousRevisionAddress],
            deltaSource.ReconstructionFrameAddresses);

        StayBSaveDecision decision = CreateMixedDecision(
            [MigratedNoChangeObjectId]);
        StayBRevisionPlan plan = StayBRevisionPlanner.Create(
            source.Store,
            facts,
            decision);

        AssertStoreSnapshot(beforePlanning, CaptureStore(source.Store));
        Assert.Same(facts, plan.Facts);
        Assert.Same(decision, plan.Decision);
        Assert.Equal(source.Current.FileNumber, plan.Revision.FileNumber);
        Assert.Equal(source.Current.TailOffsetBytes,
            plan.Revision.Address.FrameTicket.OffsetBytes);

        Frame candidate = plan.Revision.Frame;
        Assert.Equal(
            [
                MigratedNoChangeObjectId,
                DeltaUpdatedObjectId,
                BaseUpdatedObjectId,
                InsertedObjectId,
            ],
            candidate.ObjectVersions.Keys.ToArray());
        Assert.False(candidate.ObjectVersions.ContainsKey(RemovedObjectId));
        Assert.False(candidate.ObjectVersions.ContainsKey(CurrentNoChangeObjectId));
        Assert.False(candidate.ObjectVersions.ContainsKey(UnmigratedDebtObjectId));

        ObjectVersionDictionary dictionary = Assert.IsType<ObjectVersionDictionary>(
            candidate.ObjectVersionDictionary);
        Assert.Equal(ObjectVersionDictionaryKind.Delta, dictionary.Kind);
        Assert.Equal(
            Current(source.PublishedRevisionAddress.FrameTicket),
            dictionary.ParentRevisionFrameTicket);
        Assert.Equal(
            [
                MigratedNoChangeObjectId,
                DeltaUpdatedObjectId,
                BaseUpdatedObjectId,
                RemovedObjectId,
                InsertedObjectId,
            ],
            dictionary.Entries.Keys.ToArray());
        Assert.Equal(
            ObjectVersionDictionaryBindingKind.Remove,
            dictionary.Entries[RemovedObjectId].Kind);
        Assert.Null(dictionary.Entries[RemovedObjectId].ExternalFrameTicket);
        foreach (uint objectId in new[] {
            MigratedNoChangeObjectId,
            DeltaUpdatedObjectId,
            BaseUpdatedObjectId,
            InsertedObjectId,
        }) {
            Assert.Equal(
                ObjectVersionDictionaryBindingKind.Self,
                dictionary.Entries[objectId].Kind);
            Assert.Null(dictionary.Entries[objectId].ExternalFrameTicket);
        }

        ObjectVersion delta = candidate.ObjectVersions[DeltaUpdatedObjectId];
        Assert.Equal(ObjectVersionKind.Delta, delta.Kind);
        Assert.Equal(5, delta.PayloadBytes);
        Assert.Equal(30, delta.ReconstructionObjectPayloadBytes);
        Assert.Equal(30, delta.ResultBasePayloadBytes);
        Assert.Equal(25, delta.ExpectedParentBasePayloadBytes);
        Assert.Equal(3, delta.LogicalVersionOrdinal);
        Assert.Equal(
            Current(source.PublishedRevisionAddress.FrameTicket),
            delta.DeltaParentFrameTicket);
        AssertBase(
            candidate.ObjectVersions[BaseUpdatedObjectId],
            payloadBytes: 19,
            logicalVersionOrdinal: 2);
        AssertBase(
            candidate.ObjectVersions[MigratedNoChangeObjectId],
            payloadBytes: 10,
            logicalVersionOrdinal: 1);
        AssertBase(
            candidate.ObjectVersions[InsertedObjectId],
            payloadBytes: 7,
            logicalVersionOrdinal: 1);

        IReadOnlyDictionary<uint, AbsoluteFrameAddress> parentBindings =
            ObjectVersionDictionaryReader.MaterializeLive(
                source.Store,
                source.PublishedRevisionAddress).Bindings;
        Assert.Equal(
            [
                MigratedNoChangeObjectId,
                DeltaUpdatedObjectId,
                BaseUpdatedObjectId,
                RemovedObjectId,
                UnmigratedDebtObjectId,
            ],
            GetPreviousFileDebtObjectIds(
                source.Store,
                source.Previous.FileNumber,
                parentBindings));

        ProvisionalRevisionV0Estimate independentlyEstimated =
            ProvisionalRevisionV0Estimator.Estimate(
                candidate,
                source.Current.TailOffsetBytes);
        AssertEquivalentEstimate(plan.Revision.Estimate, independentlyEstimated);

        FrameTicket appendedTicket = source.Current.Append(
            candidate,
            plan.Revision.Estimate.RbfLayout.PayloadLengthBytes,
            plan.Revision.Estimate.RbfLayout.TailMetaLengthBytes);
        AbsoluteFrameAddress appendedAddress = new(
            source.Current.FileNumber,
            appendedTicket);

        Assert.Equal(plan.Revision.Address, appendedAddress);
        Assert.Same(candidate, source.Current.Read(appendedTicket));
        Assert.Equal(
            plan.Revision.Estimate.RbfLayout,
            source.Current.ReadLayout(appendedTicket));
        AssertEquivalentEstimate(
            plan.Revision.Estimate,
            ProvisionalRevisionV0Estimator.Estimate(
                source.Current.Read(appendedTicket),
                appendedTicket.OffsetBytes));

        ObjectVersionDictionaryMaterializationInspection materialized =
            ObjectVersionDictionaryReader.MaterializeLive(
                source.Store,
                appendedAddress);
        Assert.Equal(
            [
                appendedAddress,
                source.PublishedRevisionAddress,
                source.PreviousRevisionAddress,
            ],
            materialized.DictionaryRevisionAddresses);
        Assert.Equal(
            [
                KeyValuePair.Create(MigratedNoChangeObjectId, appendedAddress),
                KeyValuePair.Create(DeltaUpdatedObjectId, appendedAddress),
                KeyValuePair.Create(BaseUpdatedObjectId, appendedAddress),
                KeyValuePair.Create(
                    CurrentNoChangeObjectId,
                    source.PublishedRevisionAddress),
                KeyValuePair.Create(
                    UnmigratedDebtObjectId,
                    source.PreviousRevisionAddress),
                KeyValuePair.Create(InsertedObjectId, appendedAddress),
            ],
            materialized.Bindings.ToArray());
        Assert.False(materialized.Bindings.ContainsKey(RemovedObjectId));
        AssertExactState(
            facts.PostLiveStates,
            PhysicalStateOracle.Materialize(source.Store, materialized.Bindings));

        ObjectVersionDictionaryLookupInspection removedLookup =
            ObjectVersionDictionaryReader.LookupLive(
                source.Store,
                appendedAddress,
                RemovedObjectId);
        Assert.Equal(
            ObjectVersionDictionaryLookupDisposition.Removed,
            removedLookup.Disposition);
        Assert.Equal(appendedAddress, removedLookup.DecisiveRevisionAddress);
        Assert.Equal(
            ObjectVersionDictionaryBindingKind.Remove,
            removedLookup.BindingKind);
        Assert.Null(removedLookup.ResolvedObjectVersionAddress);

        Assert.Equal(
            [DeltaUpdatedObjectId, UnmigratedDebtObjectId],
            GetPreviousFileDebtObjectIds(
                source.Store,
                source.Previous.FileNumber,
                materialized.Bindings));
        Assert.Equal(
            [appendedAddress, source.PublishedRevisionAddress,
                source.PreviousRevisionAddress],
            PhysicalStateOracle.InspectObjectReconstruction(
                source.Store,
                DeltaUpdatedObjectId,
                materialized.Bindings[DeltaUpdatedObjectId])
                .ReconstructionFrameAddresses);
        Assert.Equal(
            [appendedAddress],
            PhysicalStateOracle.InspectObjectReconstruction(
                source.Store,
                MigratedNoChangeObjectId,
                materialized.Bindings[MigratedNoChangeObjectId])
                .ReconstructionFrameAddresses);
        Assert.Equal(
            [source.PublishedRevisionAddress],
            PhysicalStateOracle.InspectObjectReconstruction(
                source.Store,
                CurrentNoChangeObjectId,
                materialized.Bindings[CurrentNoChangeObjectId])
                .ReconstructionFrameAddresses);
        Assert.Equal(
            [source.PreviousRevisionAddress],
            PhysicalStateOracle.InspectObjectReconstruction(
                source.Store,
                UnmigratedDebtObjectId,
                materialized.Bindings[UnmigratedDebtObjectId])
                .ReconstructionFrameAddresses);
    }

    [Fact]
    public void Equivalent_input_and_decision_orders_build_identical_candidates() {
        SourceFixture source = CreateSource();
        NormalizedSaveFacts firstFacts = SaveStepNormalizer.Normalize(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            CreateMixedSaveStep());
        NormalizedSaveFacts secondFacts = SaveStepNormalizer.Normalize(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            new SaveStep([
                new UpdateObject(DeltaUpdatedObjectId, 30, 5),
                new CreateObject(InsertedObjectId, 7),
                new RemoveObject(RemovedObjectId),
                new UpdateObject(BaseUpdatedObjectId, 19, 4),
            ]));
        StayBSaveDecision firstDecision = new(
            [
                new UpdateWriteDecision(DeltaUpdatedObjectId, UpdateWriteMode.Delta),
                new UpdateWriteDecision(BaseUpdatedObjectId, UpdateWriteMode.Base),
            ],
            [MigratedNoChangeObjectId, UnmigratedDebtObjectId]);
        StayBSaveDecision secondDecision = new(
            [
                new UpdateWriteDecision(BaseUpdatedObjectId, UpdateWriteMode.Base),
                new UpdateWriteDecision(DeltaUpdatedObjectId, UpdateWriteMode.Delta),
            ],
            [UnmigratedDebtObjectId, MigratedNoChangeObjectId]);
        StoreSnapshot before = CaptureStore(source.Store);

        StayBRevisionPlan first = StayBRevisionPlanner.Create(
            source.Store,
            firstFacts,
            firstDecision);
        StayBRevisionPlan second = StayBRevisionPlanner.Create(
            source.Store,
            secondFacts,
            secondDecision);

        AssertStoreSnapshot(before, CaptureStore(source.Store));
        Assert.Equal(DescribeFacts(firstFacts), DescribeFacts(secondFacts));
        Assert.Equal(
            firstDecision.UpdateDecisions,
            secondDecision.UpdateDecisions);
        Assert.Equal(
            firstDecision.UnchangedMigrationObjectIds,
            secondDecision.UnchangedMigrationObjectIds);
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
    public void Update_decisions_must_cover_updates_exactly_once_without_mutation() {
        SourceFixture source = CreateSource();
        NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            CreateMixedSaveStep());
        StoreSnapshot before = CaptureStore(source.Store);

        StayBSaveDecision missing = new(
            [new UpdateWriteDecision(DeltaUpdatedObjectId, UpdateWriteMode.Delta)],
            [MigratedNoChangeObjectId]);
        Assert.Throws<ArgumentException>(() =>
            StayBRevisionPlanner.Create(source.Store, facts, missing));

        StayBSaveDecision extra = new(
            [
                new UpdateWriteDecision(DeltaUpdatedObjectId, UpdateWriteMode.Delta),
                new UpdateWriteDecision(BaseUpdatedObjectId, UpdateWriteMode.Base),
                new UpdateWriteDecision(CurrentNoChangeObjectId, UpdateWriteMode.Base),
            ],
            [MigratedNoChangeObjectId]);
        Assert.Throws<ArgumentException>(() =>
            StayBRevisionPlanner.Create(source.Store, facts, extra));

        Assert.Throws<ArgumentException>(() => new StayBSaveDecision(
            [
                new UpdateWriteDecision(DeltaUpdatedObjectId, UpdateWriteMode.Delta),
                new UpdateWriteDecision(DeltaUpdatedObjectId, UpdateWriteMode.Base),
                new UpdateWriteDecision(BaseUpdatedObjectId, UpdateWriteMode.Base),
            ],
            [MigratedNoChangeObjectId]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StayBSaveDecision(
            [
                new UpdateWriteDecision(
                    DeltaUpdatedObjectId,
                    (UpdateWriteMode)int.MaxValue),
                new UpdateWriteDecision(BaseUpdatedObjectId, UpdateWriteMode.Base),
            ],
            [MigratedNoChangeObjectId]));

        AssertStoreSnapshot(before, CaptureStore(source.Store));
    }

    [Fact]
    public void Migration_accepts_only_unchanged_A_debt_without_mutation() {
        SourceFixture source = CreateSource();
        NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            CreateMixedSaveStep());
        StoreSnapshot before = CaptureStore(source.Store);

        foreach (uint rejectedObjectId in new[] {
            DeltaUpdatedObjectId,
            RemovedObjectId,
            InsertedObjectId,
            CurrentNoChangeObjectId,
            999U,
        }) {
            StayBSaveDecision decision = CreateMixedDecision([rejectedObjectId]);
            Assert.Throws<ArgumentException>(() =>
                StayBRevisionPlanner.Create(source.Store, facts, decision));
            AssertStoreSnapshot(before, CaptureStore(source.Store));
        }

        Assert.Throws<ArgumentException>(() => CreateMixedDecision(
            [MigratedNoChangeObjectId, MigratedNoChangeObjectId]));
        AssertStoreSnapshot(before, CaptureStore(source.Store));
    }

    [Fact]
    public void Oversized_candidate_failure_does_not_mutate_store() {
        SourceFixture source = CreateSource();
        NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            new SaveStep([
                new CreateObject(
                    999,
                    RbfV040Layout.MaxPayloadAndTailMetaLengthBytes),
            ]));
        StayBSaveDecision decision = new([], []);
        StoreSnapshot before = CaptureStore(source.Store);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StayBRevisionPlanner.Create(source.Store, facts, decision));

        AssertStoreSnapshot(before, CaptureStore(source.Store));
    }

    [Fact]
    public void Normalizer_rejects_source_OVD_chain_outside_AB_without_mutation() {
        RbfFileStore store = new();
        RbfFile older = store.CreateFile();
        FrameBuilder olderBuilder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder(),
        };
        AbsoluteFrameAddress olderRevision = AppendRevision(older, olderBuilder);

        RbfFile previous = store.CreateFile();
        ObjectVersionDictionaryBuilder previousDictionary = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = Previous(olderRevision.FrameTicket),
        };
        previousDictionary.BindSelf(objectId: 1);
        FrameBuilder previousBuilder = new() {
            ObjectVersionDictionary = previousDictionary,
        };
        AddBase(previousBuilder, objectId: 1, payloadBytes: 10,
            logicalVersionOrdinal: 1);
        AbsoluteFrameAddress previousRevision = AppendRevision(
            previous,
            previousBuilder);

        RbfFile current = store.CreateFile();
        FrameBuilder currentBuilder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder() {
                Kind = ObjectVersionDictionaryKind.Delta,
                ParentRevisionFrameTicket = Previous(previousRevision.FrameTicket),
            },
        };
        AbsoluteFrameAddress publishedRevision = AppendRevision(
            current,
            currentBuilder);
        StoreSnapshot before = CaptureStore(store);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SaveStepNormalizer.Normalize(
                store,
                current.FileNumber,
                publishedRevision,
                new SaveStep([new UpdateObject(1, 11, 1)])));

        Assert.Contains("OVD chain", exception.Message, StringComparison.Ordinal);
        AssertStoreSnapshot(before, CaptureStore(store));
    }

    private static SourceFixture CreateSource() {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        ObjectVersionDictionaryBuilder previousDictionary = new();
        foreach (uint objectId in new[] {
            MigratedNoChangeObjectId,
            DeltaUpdatedObjectId,
            BaseUpdatedObjectId,
            RemovedObjectId,
            UnmigratedDebtObjectId,
        }) {
            previousDictionary.BindSelf(objectId);
        }

        FrameBuilder previousBuilder = new() {
            ObjectVersionDictionary = previousDictionary,
        };
        AddBase(previousBuilder, MigratedNoChangeObjectId, 10, 1);
        AddBase(previousBuilder, DeltaUpdatedObjectId, 20, 1);
        AddBase(previousBuilder, BaseUpdatedObjectId, 21, 1);
        AddBase(previousBuilder, RemovedObjectId, 30, 1);
        AddBase(previousBuilder, UnmigratedDebtObjectId, 60, 1);
        AbsoluteFrameAddress previousRevisionAddress = AppendRevision(
            previous,
            previousBuilder);

        RbfFile current = store.CreateFile();
        ObjectVersionDictionaryBuilder currentDictionary = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = Previous(
                previousRevisionAddress.FrameTicket),
        };
        currentDictionary.BindSelf(DeltaUpdatedObjectId);
        currentDictionary.BindSelf(CurrentNoChangeObjectId);
        FrameBuilder currentBuilder = new() {
            ObjectVersionDictionary = currentDictionary,
        };
        AddDelta(
            currentBuilder,
            DeltaUpdatedObjectId,
            Previous(previousRevisionAddress.FrameTicket),
            parentBasePayloadBytes: 20,
            resultBasePayloadBytes: 25,
            deltaPayloadBytes: 5,
            reconstructionPayloadBytes: 25,
            logicalVersionOrdinal: 2);
        AddBase(currentBuilder, CurrentNoChangeObjectId, 40, 1);
        AbsoluteFrameAddress publishedRevisionAddress = AppendRevision(
            current,
            currentBuilder);

        return new SourceFixture(
            store,
            previous,
            current,
            previousRevisionAddress,
            publishedRevisionAddress);
    }

    private static SaveStep CreatePreviousFileStep() => new([
        new CreateObject(MigratedNoChangeObjectId, 10),
        new CreateObject(DeltaUpdatedObjectId, 20),
        new CreateObject(BaseUpdatedObjectId, 21),
        new CreateObject(RemovedObjectId, 30),
        new CreateObject(UnmigratedDebtObjectId, 60),
    ]);

    private static SaveStep CreatePublishedRevisionStep() => new([
        new UpdateObject(DeltaUpdatedObjectId, 25, 5),
        new CreateObject(CurrentNoChangeObjectId, 40),
    ]);

    private static SaveStep CreateMixedSaveStep() => new([
        new RemoveObject(RemovedObjectId),
        new UpdateObject(BaseUpdatedObjectId, 19, 4),
        new CreateObject(InsertedObjectId, 7),
        new UpdateObject(DeltaUpdatedObjectId, 30, 5),
    ]);

    private static StayBSaveDecision CreateMixedDecision(
        IEnumerable<uint> migrationObjectIds) => new(
        [
            new UpdateWriteDecision(DeltaUpdatedObjectId, UpdateWriteMode.Delta),
            new UpdateWriteDecision(BaseUpdatedObjectId, UpdateWriteMode.Base),
        ],
        migrationObjectIds);

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

    private static uint[] GetPreviousFileDebtObjectIds(
        RbfFileStore store,
        uint previousFileNumber,
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> bindings) => bindings
        .Where(pair => PhysicalStateOracle.InspectObjectReconstruction(
            store,
            pair.Key,
            pair.Value).BaseAddress.FileNumber == previousFileNumber)
        .Select(static pair => pair.Key)
        .Order()
        .ToArray();

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
        AbsoluteFrameAddress PublishedRevisionAddress);

    private sealed record StoreSnapshot(
        int FileCount,
        IReadOnlyList<FileSnapshot> Files);

    private sealed record FileSnapshot(
        uint FileNumber,
        int FrameCount,
        long TailOffsetBytes);
}
