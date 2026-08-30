using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class CanonicalTerminalSettlementPlannerTests {
    private const int LargePayloadBytes = 140_000_000;

    [Fact]
    public void Direct_Rotate_has_no_empty_Stay_and_preserves_the_caller_Store() {
        SettlementSource source = CreateSource((10U, 10));
        StoreSnapshot before = CaptureStore(source.Store);

        CanonicalTerminalSettlementProven proven =
            Assert.IsType<CanonicalTerminalSettlementProven>(
                CanonicalTerminalSettlementPlanner.TryCreate(
                    source.Store,
                    source.Cursor));

        AssertStoreSnapshot(before, CaptureStore(source.Store));
        CanonicalTerminalSettlementCertificate certificate = proven.Certificate;
        Assert.Same(source.Cursor, certificate.InitialCursor);
        Assert.Empty(certificate.MaintenanceStayBSteps);
        Assert.Equal(
            source.Cursor.PublishedRevisionAddress,
            certificate.FinalRotateC.Plan.Facts.PublishedRevisionAddress);

        int sourceCurrentFrameCount = source.Current.FrameCount;
        ProbeRevisionCursor result = ExplicitProbeRevisionApplier.ApplyRotateC(
            source.Store,
            certificate.InitialCursor,
            certificate.FinalRotateC);

        Assert.Equal(sourceCurrentFrameCount, source.Current.FrameCount);
        Assert.Equal(3, source.Store.FileCount);
        Assert.Equal(2U, result.FileScope.PreviousFileNumber);
        Assert.Equal(3U, result.FileScope.CurrentFileNumber);
        AssertRuntimeState(source.Store, result, source.ExpectedState);
    }

    [Fact]
    public void Large_A_debt_uses_two_ascending_migrations_then_Rotate() {
        SettlementSource source = CreateSource(
            (101U, LargePayloadBytes),
            (202U, LargePayloadBytes),
            (303U, LargePayloadBytes));
        StoreSnapshot before = CaptureStore(source.Store);

        CanonicalTerminalSettlementProven proven =
            Assert.IsType<CanonicalTerminalSettlementProven>(
                CanonicalTerminalSettlementPlanner.TryCreate(
                    source.Store,
                    source.Cursor));

        AssertStoreSnapshot(before, CaptureStore(source.Store));
        CanonicalTerminalSettlementCertificate certificate = proven.Certificate;
        Assert.Equal(
            [101U, 202U],
            certificate.MaintenanceStayBSteps
                .Select(step => Assert.Single(
                    step.Plan.Decision.UnchangedMigrationObjectIds))
                .ToArray());
        Assert.Equal(
            [303U],
            certificate.FinalRotateC.Plan.Revision.Frame.ObjectVersions.Keys);

        ProbeRevisionCursor result = certificate.InitialCursor;
        foreach (FeasibleCandidate<StayBRevisionPlan> maintenance in
            certificate.MaintenanceStayBSteps) {
            result = ExplicitProbeRevisionApplier.ApplyStayB(
                source.Store,
                result,
                maintenance);
        }

        result = ExplicitProbeRevisionApplier.ApplyRotateC(
            source.Store,
            result,
            certificate.FinalRotateC);
        Assert.Equal(2U, result.FileScope.PreviousFileNumber);
        Assert.Equal(3U, result.FileScope.CurrentFileNumber);
        AssertRuntimeState(source.Store, result, source.ExpectedState);
    }

    [Fact]
    public void Blocked_preparation_is_deterministic_Unproven_and_does_not_mutate() {
        SettlementSource source = CreateSource(
            (101U, LargePayloadBytes),
            (202U, LargePayloadBytes),
            (303U, LargePayloadBytes));
        AdvanceTailTo(
            source.Current,
            RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes);
        ProbeRevisionCursor cursor = new(
            source.Cursor.FileScope,
            source.Cursor.PublishedRevisionAddress,
            source.Current.TailOffsetBytes);
        StoreSnapshot before = CaptureStore(source.Store);

        CanonicalTerminalSettlementRejectedUnproven first =
            Assert.IsType<CanonicalTerminalSettlementRejectedUnproven>(
                CanonicalTerminalSettlementPlanner.TryCreate(
                    source.Store,
                    cursor));
        CanonicalTerminalSettlementRejectedUnproven second =
            Assert.IsType<CanonicalTerminalSettlementRejectedUnproven>(
                CanonicalTerminalSettlementPlanner.TryCreate(
                    source.Store,
                    cursor));

        Assert.Equal(first, second);
        Assert.Equal(
            CanPrepareAndRotateRejectionStage.PreparatoryMigration,
            first.Rejection.Stage);
        Assert.Equal(1, first.Rejection.CompletedMigrationCount);
        Assert.Equal(202U, first.Rejection.BlockingObjectId);
        Assert.Equal(
            RevisionCandidateCapacityLimit.TargetFrameStartRelative,
            first.Rejection.Capacity.Limit);
        AssertStoreSnapshot(before, CaptureStore(source.Store));
        Assert.Equal(2, source.Store.FileCount);
    }

    private static SettlementSource CreateSource(
        params (uint ObjectId, int PayloadBytes)[] objects) {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        AbsoluteFrameAddress? parentRevision = null;
        SortedDictionary<uint, LogicalObjectState> expectedState = [];
        foreach ((uint objectId, int payloadBytes) in objects) {
            ObjectVersionDictionaryBuilder dictionary = new();
            if (parentRevision is AbsoluteFrameAddress parentAddress) {
                dictionary.Kind = ObjectVersionDictionaryKind.Delta;
                dictionary.ParentRevisionFrameTicket = new RelativeFrameTicket(
                    IsPreviousFile: false,
                    parentAddress.FrameTicket);
            }

            dictionary.BindSelf(objectId);
            FrameBuilder builder = new() {
                ObjectVersionDictionary = dictionary,
            };
            AddBase(builder, objectId, payloadBytes);
            parentRevision = AppendExact(previous, builder);
            expectedState.Add(
                objectId,
                new LogicalObjectState(payloadBytes, LogicalVersionOrdinal: 1));
        }

        if (parentRevision is null) {
            parentRevision = AppendExact(
                previous,
                new FrameBuilder {
                    ObjectVersionDictionary = new ObjectVersionDictionaryBuilder(),
                });
        }

        RbfFile current = store.CreateFile();
        FrameBuilder publishedBuilder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder {
                Kind = ObjectVersionDictionaryKind.Delta,
                ParentRevisionFrameTicket = new RelativeFrameTicket(
                    IsPreviousFile: true,
                    parentRevision.Value.FrameTicket),
            },
        };
        AbsoluteFrameAddress published = AppendExact(current, publishedBuilder);
        ProbeRevisionCursor cursor = new(
            new FileScope(current.FileNumber),
            published,
            current.TailOffsetBytes);
        return new SettlementSource(
            store,
            current,
            cursor,
            expectedState);
    }

    private static void AdvanceTailTo(RbfFile file, long targetTailOffsetBytes) {
        while (file.TailOffsetBytes < targetTailOffsetBytes) {
            long remaining = targetTailOffsetBytes - file.TailOffsetBytes;
            int maximumAppendBytes = checked(
                RbfV040Layout.MaxFrameLengthBytes +
                RbfV040Layout.TrailingFenceBytes);
            int appendBytes = remaining > maximumAppendBytes
                ? maximumAppendBytes
                : checked((int)remaining);
            long after = remaining - appendBytes;
            int minimumAppendBytes = checked(
                RbfV040Layout.MinFrameLengthBytes +
                RbfV040Layout.TrailingFenceBytes);
            if (after > 0 && after < minimumAppendBytes) {
                appendBytes = checked(
                    appendBytes - (minimumAppendBytes - (int)after));
            }

            if (appendBytes < minimumAppendBytes ||
                (appendBytes & RbfV040Layout.AlignmentMask) != 0) {
                throw new InvalidOperationException(
                    $"Cannot advance file {file.FileNumber} exactly to " +
                    $"{targetTailOffsetBytes}.");
            }

            int payloadBytes = appendBytes - minimumAppendBytes;
            _ = file.Append(new FrameBuilder().Build(), payloadBytes);
        }

        Assert.Equal(targetTailOffsetBytes, file.TailOffsetBytes);
    }

    private static void AddBase(
        FrameBuilder frame,
        uint objectId,
        int payloadBytes) {
        ObjectVersionBuilder version = frame.Add(objectId);
        version.Kind = ObjectVersionKind.Base;
        version.PayloadBytes = payloadBytes;
        version.ReconstructionObjectPayloadBytes = payloadBytes;
        version.ResultBasePayloadBytes = payloadBytes;
        version.LogicalVersionOrdinal = 1;
    }

    private static AbsoluteFrameAddress AppendExact(
        RbfFile file,
        FrameBuilder builder) {
        Frame frame = builder.Build();
        ProvisionalRevisionV0Estimate estimate =
            ProvisionalRevisionV0Estimator.Estimate(
                frame,
                file.TailOffsetBytes);
        FrameTicket ticket = file.Append(
            frame,
            estimate.RbfLayout.PayloadLengthBytes,
            estimate.RbfLayout.TailMetaLengthBytes);
        return new AbsoluteFrameAddress(file.FileNumber, ticket);
    }

    private static void AssertRuntimeState(
        RbfFileStore store,
        ProbeRevisionCursor cursor,
        IReadOnlyDictionary<uint, LogicalObjectState> expected) {
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> bindings =
            ObjectVersionDictionaryReader.MaterializeLive(
                store,
                cursor.PublishedRevisionAddress).Bindings;
        IReadOnlyDictionary<uint, LogicalObjectState> actual =
            PhysicalStateOracle.Materialize(store, bindings);

        Assert.Equal(expected.Count, actual.Count);
        foreach ((uint objectId, LogicalObjectState state) in expected) {
            Assert.True(
                actual.TryGetValue(objectId, out LogicalObjectState actualState));
            Assert.Equal(state, actualState);
        }
    }

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

    private sealed record SettlementSource(
        RbfFileStore Store,
        RbfFile Current,
        ProbeRevisionCursor Cursor,
        IReadOnlyDictionary<uint, LogicalObjectState> ExpectedState);

    private sealed record StoreSnapshot(
        int FileCount,
        IReadOnlyList<FileSnapshot> Files);

    private sealed record FileSnapshot(
        uint FileNumber,
        int FrameCount,
        long TailOffsetBytes);
}
