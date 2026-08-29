using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Rotation;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class PreparatoryBaseMigrationTests {
    private const int LargePayloadBytes = 140_000_000;

    [Fact]
    public void One_B_migration_preserves_state_and_lineage_while_reducing_A_debt() {
        SmallSource source = CreateSmallSource();
        ObjectVersionDictionaryMaterializationInspection sourceMap =
            ObjectVersionDictionaryReader.MaterializeLive(
                source.Store,
                source.PublishedRevisionAddress);
        IReadOnlyDictionary<uint, LogicalObjectState> sourceState =
            PhysicalStateOracle.Materialize(source.Store, sourceMap.Bindings);
        int debtBefore = CountPreviousFileDebt(
            source.Store,
            source.Previous.FileNumber,
            sourceMap.Bindings);

        PreparatoryBaseMigrationPlan plan = PreparatoryBaseMigrationPlanner.Create(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            [source.FirstAObjectId]);

        Assert.Equal(
            [source.FirstAObjectId],
            plan.MigrationRevision.Frame.ObjectVersions.Keys);
        ObjectVersionDictionary plannedDictionary = Assert.IsType<ObjectVersionDictionary>(
            plan.MigrationRevision.Frame.ObjectVersionDictionary);
        Assert.Equal(ObjectVersionDictionaryKind.Delta, plannedDictionary.Kind);
        Assert.Equal(
            Current(source.PublishedRevisionAddress.FrameTicket),
            plannedDictionary.ParentRevisionFrameTicket);
        ObjectVersion plannedBase =
            plan.MigrationRevision.Frame.ObjectVersions[source.FirstAObjectId];
        Assert.Equal(ObjectVersionKind.Base, plannedBase.Kind);
        Assert.Null(plannedBase.DeltaParentFrameTicket);
        Assert.Equal(new LogicalObjectState(10, 1), ToLogicalState(plannedBase));

        AbsoluteFrameAddress migrated =
            PreparatoryBaseMigrationAppender.AppendToCurrentFile(source.Store, plan);

        Assert.Equal(plan.MigrationRevision.Address, migrated);
        Assert.Equal(source.Current.FileNumber, migrated.FileNumber);
        ObjectVersionDictionaryMaterializationInspection currentMap =
            ObjectVersionDictionaryReader.MaterializeLive(source.Store, migrated);
        AssertExactState(
            sourceState,
            PhysicalStateOracle.Materialize(source.Store, currentMap.Bindings));
        Assert.Equal(migrated, currentMap.Bindings[source.FirstAObjectId]);
        Assert.Equal(
            source.PublishedRevisionAddress,
            currentMap.Bindings[source.CurrentObjectId]);
        Assert.Equal(
            debtBefore - 1,
            CountPreviousFileDebt(
                source.Store,
                source.Previous.FileNumber,
                currentMap.Bindings));

        ObjectReconstructionInspection reconstruction =
            PhysicalStateOracle.InspectObjectReconstruction(
                source.Store,
                source.FirstAObjectId,
                currentMap.Bindings[source.FirstAObjectId]);
        Assert.Equal([migrated], reconstruction.ReconstructionFrameAddresses);
        Assert.Equal(source.Current.FileNumber, reconstruction.BaseAddress.FileNumber);

        ObjectLineageInspection lineage = PhysicalStateOracle.InspectObjectLineage(
            source.Store,
            source.FirstAObjectId,
            currentMap.Bindings[source.FirstAObjectId]);
        Assert.Equal(
            [migrated, source.PreviousRevisionAddress],
            lineage.ObjectVersionLineageAddresses);
        Assert.Equal(source.PreviousRevisionAddress, lineage.RootAddress);
        Assert.Equal(
            [source.PublishedRevisionAddress, source.PreviousRevisionAddress],
            Assert.Single(lineage.BaseParentLookups).DictionaryRevisionAddresses);
    }

    [Fact]
    public void Migration_rebases_a_B_head_whose_reconstruction_Base_is_in_A() {
        const uint objectId = 77;
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        ObjectVersionDictionaryBuilder previousDictionary = new();
        previousDictionary.BindSelf(objectId);
        FrameBuilder previousBuilder = new() {
            ObjectVersionDictionary = previousDictionary,
        };
        AddBase(previousBuilder, objectId, payloadBytes: 10);
        AbsoluteFrameAddress a = AppendRevision(previous, previousBuilder);

        RbfFile current = store.CreateFile();
        ObjectVersionDictionaryBuilder currentDictionary = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = Previous(a.FrameTicket),
        };
        currentDictionary.BindSelf(objectId);
        FrameBuilder currentBuilder = new() {
            ObjectVersionDictionary = currentDictionary,
        };
        AddDelta(
            currentBuilder,
            objectId,
            Previous(a.FrameTicket),
            parentBasePayloadBytes: 10,
            resultBasePayloadBytes: 12,
            deltaPayloadBytes: 4,
            reconstructionPayloadBytes: 14,
            logicalVersionOrdinal: 2);
        AbsoluteFrameAddress b = AppendRevision(current, currentBuilder);

        PreparatoryBaseMigrationPlan plan = PreparatoryBaseMigrationPlanner.Create(
            store,
            current.FileNumber,
            b,
            [objectId]);

        ObjectVersion plannedBase = plan.MigrationRevision.Frame.ObjectVersions[objectId];
        Assert.Equal(ObjectVersionKind.Base, plannedBase.Kind);
        Assert.Equal(new LogicalObjectState(12, 2), ToLogicalState(plannedBase));

        AbsoluteFrameAddress migration =
            PreparatoryBaseMigrationAppender.AppendToCurrentFile(store, plan);
        AbsoluteFrameAddress head = ObjectVersionDictionaryReader.MaterializeLive(
            store,
            migration).Bindings[objectId];
        Assert.Equal(migration, head);

        ObjectReconstructionInspection reconstruction =
            PhysicalStateOracle.InspectObjectReconstruction(store, objectId, head);
        Assert.Equal(new LogicalObjectState(12, 2), reconstruction.State);
        Assert.Equal([migration], reconstruction.ReconstructionFrameAddresses);

        ObjectLineageInspection lineage = PhysicalStateOracle.InspectObjectLineage(
            store,
            objectId,
            head);
        Assert.Equal([migration, b, a], lineage.ObjectVersionLineageAddresses);
        Assert.Equal(a, lineage.RootAddress);
        Assert.Equal(
            [b],
            Assert.Single(lineage.BaseParentLookups).DictionaryRevisionAddresses);
    }

    [Fact]
    public void Three_large_A_bases_require_two_preparatory_B_batches_before_C_fits() {
        LargeSource source = CreateLargeSource();
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> sourceMap =
            ObjectVersionDictionaryReader.MaterializeLive(
                source.Store,
                source.PublishedRevisionAddress).Bindings;
        IReadOnlyDictionary<uint, LogicalObjectState> sourceState =
            PhysicalStateOracle.Materialize(source.Store, sourceMap);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ImmediateRotationPlanner.Create(
                source.Store,
                source.Current.FileNumber,
                source.PublishedRevisionAddress));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PreparatoryBaseMigrationPlanner.Create(
                source.Store,
                source.Current.FileNumber,
                source.PublishedRevisionAddress,
                [source.ObjectIds[0], source.ObjectIds[1]]));

        PreparatoryBaseMigrationPlan firstPlan =
            PreparatoryBaseMigrationPlanner.Create(
                source.Store,
                source.Current.FileNumber,
                source.PublishedRevisionAddress,
                [source.ObjectIds[0]]);
        AbsoluteFrameAddress firstMigration =
            PreparatoryBaseMigrationAppender.AppendToCurrentFile(
                source.Store,
                firstPlan);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ImmediateRotationPlanner.Create(
                source.Store,
                source.Current.FileNumber,
                firstMigration));

        PreparatoryBaseMigrationPlan secondPlan =
            PreparatoryBaseMigrationPlanner.Create(
                source.Store,
                source.Current.FileNumber,
                firstMigration,
                [source.ObjectIds[1]]);
        AbsoluteFrameAddress secondMigration =
            PreparatoryBaseMigrationAppender.AppendToCurrentFile(
                source.Store,
                secondPlan);

        ImmediateRotationPlan rotation = ImmediateRotationPlanner.Create(
            source.Store,
            source.Current.FileNumber,
            secondMigration);
        Assert.Equal([source.ObjectIds[2]], rotation.EvacuationObjectIds);
        AbsoluteFrameAddress c = ImmediateRotationAppender.AppendToFreshNextFile(
            source.Store,
            rotation);

        IReadOnlyDictionary<uint, AbsoluteFrameAddress> finalMap =
            ObjectVersionDictionaryReader.MaterializeLive(source.Store, c).Bindings;
        AssertExactState(
            sourceState,
            PhysicalStateOracle.Materialize(source.Store, finalMap));
        Assert.Equal(
            [firstMigration, secondMigration, c],
            source.ObjectIds.Select(objectId => finalMap[objectId]).ToArray());

        foreach (uint objectId in source.ObjectIds) {
            ObjectReconstructionInspection reconstruction =
                PhysicalStateOracle.InspectObjectReconstruction(
                    source.Store,
                    objectId,
                    finalMap[objectId]);
            Assert.All(
                reconstruction.ReconstructionFrameAddresses,
                address => Assert.Contains(
                    address.FileNumber,
                    new[] { source.Current.FileNumber, c.FileNumber }));

            ObjectLineageInspection lineage = PhysicalStateOracle.InspectObjectLineage(
                source.Store,
                objectId,
                finalMap[objectId]);
            Assert.Equal(source.Previous.FileNumber, lineage.RootAddress.FileNumber);
            Assert.Contains(
                lineage.ObjectVersionLineageAddresses,
                address => address.FileNumber == source.Previous.FileNumber);
        }
    }

    [Fact]
    public void Invalid_selection_is_rejected_without_mutating_the_store() {
        SmallSource source = CreateSmallSource();

        AssertRejectedSelection(source, []);
        AssertRejectedSelection(
            source,
            [source.FirstAObjectId, source.FirstAObjectId]);
        AssertRejectedSelection(source, [999_999]);
        AssertRejectedSelection(source, [source.CurrentObjectId]);
    }

    [Fact]
    public void Planner_rejects_corruption_in_an_unselected_live_object_without_mutation() {
        CorruptSource source = CreateUnselectedCorruptionSource(corrupt: true);
        StoreSnapshot before = CaptureStore(source.Store);

        Assert.Throws<InvalidDataException>(() =>
            PreparatoryBaseMigrationPlanner.Create(
                source.Store,
                source.Current.FileNumber,
                source.PublishedRevisionAddress,
                [source.SelectedObjectId]));

        AssertStoreSnapshot(before, CaptureStore(source.Store));
    }

    [Fact]
    public void Planner_rejects_unselected_reconstruction_outside_A_B_without_mutation() {
        EscapingSource source = CreateUnselectedEscapingSource();
        StoreSnapshot before = CaptureStore(source.Store);

        Assert.Throws<InvalidDataException>(() =>
            PreparatoryBaseMigrationPlanner.Create(
                source.Store,
                source.Current.FileNumber,
                source.PublishedRevisionAddress,
                [source.SelectedObjectId]));

        AssertStoreSnapshot(before, CaptureStore(source.Store));
    }

    [Fact]
    public void Appender_revalidates_unselected_source_objects_before_mutating_B() {
        CorruptSource healthy = CreateUnselectedCorruptionSource(corrupt: false);
        PreparatoryBaseMigrationPlan plan = PreparatoryBaseMigrationPlanner.Create(
            healthy.Store,
            healthy.Current.FileNumber,
            healthy.PublishedRevisionAddress,
            [healthy.SelectedObjectId]);
        CorruptSource corrupt = CreateUnselectedCorruptionSource(corrupt: true);
        Assert.Equal(
            plan.MigrationRevision.Estimate.RbfLayout.FrameStartOffsetBytes,
            corrupt.Current.TailOffsetBytes);
        StoreSnapshot before = CaptureStore(corrupt.Store);

        Assert.Throws<InvalidDataException>(() =>
            PreparatoryBaseMigrationAppender.AppendToCurrentFile(
                corrupt.Store,
                plan));

        AssertStoreSnapshot(before, CaptureStore(corrupt.Store));
    }

    [Fact]
    public void Stale_plan_is_rejected_at_the_B_tail_without_further_mutation() {
        SmallSource source = CreateSmallSource();
        PreparatoryBaseMigrationPlan stale = PreparatoryBaseMigrationPlanner.Create(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            [source.FirstAObjectId]);
        PreparatoryBaseMigrationPlan competing = PreparatoryBaseMigrationPlanner.Create(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            [source.SecondAObjectId]);
        _ = PreparatoryBaseMigrationAppender.AppendToCurrentFile(
            source.Store,
            competing);
        StoreSnapshot beforeRejectedAppend = CaptureStore(source.Store);

        Assert.Throws<InvalidDataException>(() =>
            PreparatoryBaseMigrationAppender.AppendToCurrentFile(
                source.Store,
                stale));

        AssertStoreSnapshot(beforeRejectedAppend, CaptureStore(source.Store));
    }

    [Fact]
    public void Migration_batch_is_canonicalized_by_ObjectId() {
        SmallSource source = CreateSmallSource();

        PreparatoryBaseMigrationPlan plan = PreparatoryBaseMigrationPlanner.Create(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            [source.SecondAObjectId, source.FirstAObjectId]);

        Assert.Equal(
            [source.FirstAObjectId, source.SecondAObjectId],
            plan.RelocatedObjectIds);
        Assert.Equal(
            [source.FirstAObjectId, source.SecondAObjectId],
            plan.MigrationRevision.Frame.ObjectVersions.Keys);
        Assert.Equal(
            [source.FirstAObjectId, source.SecondAObjectId],
            plan.MigrationRevision.Frame.ObjectVersionDictionary!.Entries.Keys);
    }

    private static SmallSource CreateSmallSource() {
        const uint firstAObjectId = 10;
        const uint secondAObjectId = 20;
        const uint currentObjectId = 30;

        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        ObjectVersionDictionaryBuilder previousDictionary = new();
        previousDictionary.BindSelf(firstAObjectId);
        previousDictionary.BindSelf(secondAObjectId);
        FrameBuilder previousBuilder = new() {
            ObjectVersionDictionary = previousDictionary,
        };
        AddBase(previousBuilder, firstAObjectId, payloadBytes: 10);
        AddBase(previousBuilder, secondAObjectId, payloadBytes: 20);
        AbsoluteFrameAddress previousRevision = AppendRevision(
            previous,
            previousBuilder);

        RbfFile current = store.CreateFile();
        ObjectVersionDictionaryBuilder currentDictionary = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = Previous(previousRevision.FrameTicket),
        };
        currentDictionary.BindSelf(currentObjectId);
        FrameBuilder currentBuilder = new() {
            ObjectVersionDictionary = currentDictionary,
        };
        AddBase(currentBuilder, currentObjectId, payloadBytes: 30);
        AbsoluteFrameAddress published = AppendRevision(current, currentBuilder);

        return new SmallSource(
            store,
            previous,
            current,
            previousRevision,
            published,
            firstAObjectId,
            secondAObjectId,
            currentObjectId);
    }

    private static LargeSource CreateLargeSource() {
        uint[] objectIds = [101, 202, 303];
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        AbsoluteFrameAddress? previousRevision = null;

        foreach (uint objectId in objectIds) {
            ObjectVersionDictionaryBuilder dictionary = new();
            if (previousRevision is AbsoluteFrameAddress parent) {
                dictionary.Kind = ObjectVersionDictionaryKind.Delta;
                dictionary.ParentRevisionFrameTicket = Current(parent.FrameTicket);
            }

            dictionary.BindSelf(objectId);
            FrameBuilder builder = new() { ObjectVersionDictionary = dictionary };
            AddBase(builder, objectId, LargePayloadBytes);
            previousRevision = AppendRevision(previous, builder);
        }

        RbfFile current = store.CreateFile();
        FrameBuilder currentBuilder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder {
                Kind = ObjectVersionDictionaryKind.Delta,
                ParentRevisionFrameTicket = Previous(
                    previousRevision!.Value.FrameTicket),
            },
        };
        AbsoluteFrameAddress published = AppendRevision(current, currentBuilder);
        return new LargeSource(
            store,
            previous,
            current,
            published,
            objectIds);
    }

    private static CorruptSource CreateUnselectedCorruptionSource(bool corrupt) {
        const uint selectedObjectId = 41;
        const uint corruptObjectId = 42;
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        ObjectVersionDictionaryBuilder previousDictionary = new();
        previousDictionary.BindSelf(selectedObjectId);
        previousDictionary.BindSelf(corruptObjectId);
        FrameBuilder previousBuilder = new() {
            ObjectVersionDictionary = previousDictionary,
        };
        AddBase(previousBuilder, selectedObjectId, payloadBytes: 10);
        AddBase(previousBuilder, corruptObjectId, payloadBytes: 20);
        AbsoluteFrameAddress previousRevision = AppendRevision(
            previous,
            previousBuilder);

        RbfFile current = store.CreateFile();
        ObjectVersionDictionaryBuilder currentDictionary = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = Previous(previousRevision.FrameTicket),
        };
        currentDictionary.BindSelf(corruptObjectId);
        FrameBuilder currentBuilder = new() {
            ObjectVersionDictionary = currentDictionary,
        };
        AddDelta(
            currentBuilder,
            corruptObjectId,
            Previous(previousRevision.FrameTicket),
            parentBasePayloadBytes: corrupt ? 999 : 20,
            resultBasePayloadBytes: 25,
            deltaPayloadBytes: 5,
            reconstructionPayloadBytes: 25,
            logicalVersionOrdinal: 2);
        AbsoluteFrameAddress published = AppendRevision(current, currentBuilder);
        return new CorruptSource(
            store,
            current,
            published,
            selectedObjectId);
    }

    private static EscapingSource CreateUnselectedEscapingSource() {
        const uint selectedObjectId = 51;
        const uint escapingObjectId = 52;
        RbfFileStore store = new();
        RbfFile older = store.CreateFile();
        ObjectVersionDictionaryBuilder olderDictionary = new();
        olderDictionary.BindSelf(escapingObjectId);
        FrameBuilder olderBuilder = new() {
            ObjectVersionDictionary = olderDictionary,
        };
        AddBase(olderBuilder, escapingObjectId, payloadBytes: 20);
        AbsoluteFrameAddress olderRevision = AppendRevision(older, olderBuilder);

        RbfFile previous = store.CreateFile();
        ObjectVersionDictionaryBuilder previousDictionary = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = Previous(olderRevision.FrameTicket),
        };
        previousDictionary.BindSelf(selectedObjectId);
        previousDictionary.BindSelf(escapingObjectId);
        FrameBuilder previousBuilder = new() {
            ObjectVersionDictionary = previousDictionary,
        };
        AddBase(previousBuilder, selectedObjectId, payloadBytes: 10);
        AddDelta(
            previousBuilder,
            escapingObjectId,
            Previous(olderRevision.FrameTicket),
            parentBasePayloadBytes: 20,
            resultBasePayloadBytes: 25,
            deltaPayloadBytes: 5,
            reconstructionPayloadBytes: 25,
            logicalVersionOrdinal: 2);
        AbsoluteFrameAddress previousRevision = AppendRevision(
            previous,
            previousBuilder);

        RbfFile current = store.CreateFile();
        FrameBuilder currentBuilder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder {
                Kind = ObjectVersionDictionaryKind.Delta,
                ParentRevisionFrameTicket = Previous(previousRevision.FrameTicket),
            },
        };
        AbsoluteFrameAddress published = AppendRevision(current, currentBuilder);
        return new EscapingSource(
            store,
            current,
            published,
            selectedObjectId);
    }

    private static void AssertRejectedSelection(
        SmallSource source,
        uint[] objectIds) {
        StoreSnapshot before = CaptureStore(source.Store);

        Assert.Throws<ArgumentException>(() =>
            PreparatoryBaseMigrationPlanner.Create(
                source.Store,
                source.Current.FileNumber,
                source.PublishedRevisionAddress,
                objectIds));

        AssertStoreSnapshot(before, CaptureStore(source.Store));
    }

    private static int CountPreviousFileDebt(
        RbfFileStore store,
        uint previousFileNumber,
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> stateMap) =>
        stateMap.Count(pair =>
            PhysicalStateOracle.InspectObjectReconstruction(
                store,
                pair.Key,
                pair.Value).BaseAddress.FileNumber == previousFileNumber);

    private static void AddBase(
        FrameBuilder builder,
        uint objectId,
        int payloadBytes) {
        ObjectVersionBuilder version = builder.Add(objectId);
        version.Kind = ObjectVersionKind.Base;
        version.PayloadBytes = payloadBytes;
        version.ReconstructionObjectPayloadBytes = payloadBytes;
        version.ResultBasePayloadBytes = payloadBytes;
        version.LogicalVersionOrdinal = 1;
    }

    private static void AddDelta(
        FrameBuilder builder,
        uint objectId,
        RelativeFrameTicket parent,
        int parentBasePayloadBytes,
        int resultBasePayloadBytes,
        int deltaPayloadBytes,
        long reconstructionPayloadBytes,
        int logicalVersionOrdinal) {
        ObjectVersionBuilder version = builder.Add(objectId);
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

    private static LogicalObjectState ToLogicalState(ObjectVersion version) => new(
        version.ResultBasePayloadBytes,
        version.LogicalVersionOrdinal);

    private static void AssertExactState(
        IReadOnlyDictionary<uint, LogicalObjectState> expected,
        IReadOnlyDictionary<uint, LogicalObjectState> actual) {
        Assert.Equal(expected.Count, actual.Count);
        foreach ((uint objectId, LogicalObjectState expectedState) in expected) {
            Assert.True(actual.TryGetValue(objectId, out LogicalObjectState actualState));
            Assert.Equal(expectedState, actualState);
        }
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

    private sealed record SmallSource(
        RbfFileStore Store,
        RbfFile Previous,
        RbfFile Current,
        AbsoluteFrameAddress PreviousRevisionAddress,
        AbsoluteFrameAddress PublishedRevisionAddress,
        uint FirstAObjectId,
        uint SecondAObjectId,
        uint CurrentObjectId);

    private sealed record LargeSource(
        RbfFileStore Store,
        RbfFile Previous,
        RbfFile Current,
        AbsoluteFrameAddress PublishedRevisionAddress,
        uint[] ObjectIds);

    private sealed record CorruptSource(
        RbfFileStore Store,
        RbfFile Current,
        AbsoluteFrameAddress PublishedRevisionAddress,
        uint SelectedObjectId);

    private sealed record EscapingSource(
        RbfFileStore Store,
        RbfFile Current,
        AbsoluteFrameAddress PublishedRevisionAddress,
        uint SelectedObjectId);

    private sealed record StoreSnapshot(int FileCount, IReadOnlyList<FileSnapshot> Files);

    private sealed record FileSnapshot(uint FileNumber, int FrameCount, long TailOffsetBytes);
}
