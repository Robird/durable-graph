using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class CompletionCertificateTests {
    private const uint SmallDebtObjectId = 10;
    private const uint InsertObjectId = 404;
    private const int LargePayloadBytes = 140_000_000;

    [Fact]
    public void Direct_completion_has_zero_migrations_and_an_exact_final_chain() {
        CertificateSource source = CreateSmallSource();
        InitialSelection initial = CreateInitialSelection(source);
        StoreSnapshot before = CaptureStore(source.Store);

        CanPrepareAndRotateProven proven = Assert.IsType<CanPrepareAndRotateProven>(
            CanPrepareAndRotateCertificatePlanner.TryCreate(
                source.Store,
                initial.Cursor,
                initial.StayB));

        AssertStoreSnapshot(before, CaptureStore(source.Store));
        CanPrepareAndRotateCertificate certificate = proven.Certificate;
        Assert.Same(initial.Cursor, certificate.InitialCursor);
        Assert.Same(initial.StayB, certificate.InitialStayB);
        Assert.Empty(certificate.MaintenanceStayBSteps);
        Assert.Equal(
            initial.StayB.Plan.Revision.Address,
            certificate.FinalRotateC.Plan.Facts.PublishedRevisionAddress);
        Assert.Equal(
            initial.StayB.Plan.Revision.Estimate.RbfLayout.TailOffsetAfterBytes,
            certificate.FinalRotateC.Plan.Facts.ParentLive[InsertObjectId]
                .HeadAddress.FrameTicket.EndOffsetExclusive +
                RbfV040Layout.TrailingFenceBytes);
        Assert.Equal(CandidateTarget.RotateC, certificate.FinalRotateC.Observation.Target);

        ProbeRevisionCursor cursor = ExplicitProbeRevisionApplier.ApplyStayB(
            source.Store,
            certificate.InitialCursor,
            certificate.InitialStayB);
        cursor = ExplicitProbeRevisionApplier.ApplyRotateC(
            source.Store,
            cursor,
            certificate.FinalRotateC);

        Assert.Equal(source.Current.FileNumber, cursor.FileScope.PreviousFileNumber);
        Assert.Equal(source.Current.FileNumber + 1, cursor.FileScope.CurrentFileNumber);
        AssertRuntimeState(
            source.Store,
            cursor.PublishedRevisionAddress,
            initial.StayB.Plan.Facts.PostLiveStates);
    }

    [Fact]
    public void Three_large_A_debts_replay_two_single_object_migrations_then_rotate() {
        CertificateSource source = CreateLargeSource();
        InitialSelection initial = CreateInitialSelection(source);
        CapacityRejectedCandidate<RotateCRevisionPlan> directRotate =
            Assert.IsType<CapacityRejectedCandidate<RotateCRevisionPlan>>(
                initial.Pair.RotateCAttempt);
        Assert.Equal(
            RevisionCandidateCapacityLimit.PayloadAndTailMetaLength,
            directRotate.Rejection.Limit);
        StoreSnapshot beforeCertificate = CaptureStore(source.Store);

        CanPrepareAndRotateProven proven = Assert.IsType<CanPrepareAndRotateProven>(
            CanPrepareAndRotateCertificatePlanner.TryCreate(
                source.Store,
                initial.Cursor,
                initial.StayB));

        AssertStoreSnapshot(beforeCertificate, CaptureStore(source.Store));
        CanPrepareAndRotateCertificate certificate = proven.Certificate;
        Assert.Same(initial.StayB, certificate.InitialStayB);
        Assert.Equal(2, certificate.MaintenanceStayBSteps.Count);
        Assert.Equal(
            [101U, 202U],
            certificate.MaintenanceStayBSteps
                .Select(step => Assert.Single(
                    step.Plan.Decision.UnchangedMigrationObjectIds))
                .ToArray());
        Assert.All(certificate.MaintenanceStayBSteps, step => {
            Assert.Empty(step.Plan.Decision.UpdateDecisions);
            Assert.Single(step.Plan.Revision.Frame.ObjectVersions);
        });
        Assert.Equal(
            [303U],
            certificate.FinalRotateC.Plan.Revision.Frame.ObjectVersions.Keys);
        Assert.Equal(
            certificate.MaintenanceStayBSteps[^1].Plan.Revision.Address,
            certificate.FinalRotateC.Plan.Facts.PublishedRevisionAddress);

        ProbeRevisionCursor cursor = ExplicitProbeRevisionApplier.ApplyStayB(
            source.Store,
            certificate.InitialCursor,
            certificate.InitialStayB);
        foreach (FeasibleCandidate<StayBRevisionPlan> step in
            certificate.MaintenanceStayBSteps) {
            Assert.Equal(cursor.PublishedRevisionAddress,
                step.Plan.Facts.PublishedRevisionAddress);
            Assert.Equal(cursor.CurrentFileTailOffsetBytes,
                step.Plan.Revision.Estimate.RbfLayout.FrameStartOffsetBytes);
            cursor = ExplicitProbeRevisionApplier.ApplyStayB(
                source.Store,
                cursor,
                step);
        }

        Assert.Equal(cursor.PublishedRevisionAddress,
            certificate.FinalRotateC.Plan.Facts.PublishedRevisionAddress);
        cursor = ExplicitProbeRevisionApplier.ApplyRotateC(
            source.Store,
            cursor,
            certificate.FinalRotateC);

        Assert.Equal(source.Current.FileNumber, cursor.FileScope.PreviousFileNumber);
        Assert.Equal(source.Current.FileNumber + 1, cursor.FileScope.CurrentFileNumber);
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> bindings =
            ObjectVersionDictionaryReader.MaterializeLive(
                source.Store,
                cursor.PublishedRevisionAddress).Bindings;
        AssertExactState(
            initial.StayB.Plan.Facts.PostLiveStates,
            PhysicalStateOracle.Materialize(source.Store, bindings));
        foreach ((uint objectId, AbsoluteFrameAddress address) in bindings) {
            ObjectReconstructionInspection reconstruction =
                PhysicalStateOracle.InspectObjectReconstruction(
                    source.Store,
                    objectId,
                    address);
            Assert.All(
                reconstruction.ReconstructionFrameAddresses,
                frameAddress => Assert.Contains(
                    frameAddress.FileNumber,
                    new[] {
                        cursor.FileScope.PreviousFileNumber!.Value,
                        cursor.FileScope.CurrentFileNumber,
                    }));
        }
    }

    [Fact]
    public void Blocked_next_migration_is_deterministic_RejectedUnproven() {
        CertificateSource source = CreateLargeSource();
        AdvanceTailTo(
            source.Current,
            RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes);
        InitialSelection initial = CreateInitialSelection(source);
        Assert.IsType<FeasibleCandidate<StayBRevisionPlan>>(
            initial.Pair.StayBAttempt);
        StoreSnapshot before = CaptureStore(source.Store);

        CanPrepareAndRotateRejectedUnproven first =
            Assert.IsType<CanPrepareAndRotateRejectedUnproven>(
                CanPrepareAndRotateCertificatePlanner.TryCreate(
                    source.Store,
                    initial.Cursor,
                    initial.StayB));
        CanPrepareAndRotateRejectedUnproven second =
            Assert.IsType<CanPrepareAndRotateRejectedUnproven>(
                CanPrepareAndRotateCertificatePlanner.TryCreate(
                    source.Store,
                    initial.Cursor,
                    initial.StayB));

        Assert.Equal(first, second);
        Assert.Equal(
            CanPrepareAndRotateRejectionStage.PreparatoryMigration,
            first.Rejection.Stage);
        Assert.Equal(0, first.Rejection.CompletedMigrationCount);
        Assert.Equal(101U, first.Rejection.BlockingObjectId);
        Assert.Equal(
            RevisionCandidateCapacityLimit.TargetFrameStartRelative,
            first.Rejection.Capacity.Limit);
        Assert.True(
            first.Rejection.Capacity.AttemptedValue >
                first.Rejection.Capacity.MaximumValue);
        AssertStoreSnapshot(before, CaptureStore(source.Store));
    }

    [Fact]
    public void Probe_fork_preserves_existing_identity_and_layout_but_isolates_appends() {
        CertificateSource source = CreateSmallSource();
        RbfFileStore fork = source.Store.ForkForProbe();

        Assert.Equal(source.Store.FileCount, fork.FileCount);
        foreach (AbsoluteFrameAddress address in source.RevisionAddresses) {
            Assert.Same(source.Store.ReadFrame(address), fork.ReadFrame(address));
            Assert.Equal(source.Store.ReadLayout(address), fork.ReadLayout(address));
        }
        for (uint fileNumber = 1; fileNumber <= source.Store.FileCount; fileNumber++) {
            Assert.Equal(
                source.Store.GetFile(fileNumber).FrameCount,
                fork.GetFile(fileNumber).FrameCount);
            Assert.Equal(
                source.Store.GetFile(fileNumber).TailOffsetBytes,
                fork.GetFile(fileNumber).TailOffsetBytes);
        }

        RbfFile originalCurrent = source.Store.GetFile(source.Current.FileNumber);
        RbfFile forkCurrent = fork.GetFile(source.Current.FileNumber);
        int originalFrameCount = originalCurrent.FrameCount;
        long originalTail = originalCurrent.TailOffsetBytes;
        _ = forkCurrent.Append(new FrameBuilder().Build());
        Assert.Equal(originalFrameCount, originalCurrent.FrameCount);
        Assert.Equal(originalTail, originalCurrent.TailOffsetBytes);
        int forkFrameCount = forkCurrent.FrameCount;
        long forkTail = forkCurrent.TailOffsetBytes;

        _ = originalCurrent.Append(new FrameBuilder().Build());
        Assert.Equal(forkFrameCount, forkCurrent.FrameCount);
        Assert.Equal(forkTail, forkCurrent.TailOffsetBytes);
    }

    [Fact]
    public void Stale_cursor_and_mismatched_identity_throw_instead_of_Unproven() {
        CertificateSource staleSource = CreateSmallSource();
        InitialSelection staleInitial = CreateInitialSelection(staleSource);
        ProbeRevisionCursor staleCursor = new(
            staleInitial.Cursor.FileScope,
            staleInitial.Cursor.PublishedRevisionAddress,
            staleInitial.Cursor.CurrentFileTailOffsetBytes +
                RbfV040Layout.AlignmentBytes);
        StoreSnapshot beforeStale = CaptureStore(staleSource.Store);

        Assert.Throws<InvalidDataException>(() =>
            CanPrepareAndRotateCertificatePlanner.TryCreate(
                staleSource.Store,
                staleCursor,
                staleInitial.StayB));
        AssertStoreSnapshot(beforeStale, CaptureStore(staleSource.Store));

        CertificateSource identitySource = CreateSmallSource();
        InitialSelection identityInitial = CreateInitialSelection(identitySource);
        FeasibleCandidate<RotateCRevisionPlan> rotate =
            Assert.IsType<FeasibleCandidate<RotateCRevisionPlan>>(
                identityInitial.Pair.RotateCAttempt);
        FeasibleCandidate<StayBRevisionPlan> mismatched = new(
            identityInitial.StayB.Plan,
            rotate.Observation);
        StoreSnapshot beforeIdentity = CaptureStore(identitySource.Store);

        Assert.Throws<ArgumentException>(() =>
            CanPrepareAndRotateCertificatePlanner.TryCreate(
                identitySource.Store,
                identityInitial.Cursor,
                mismatched));
        AssertStoreSnapshot(beforeIdentity, CaptureStore(identitySource.Store));
    }

    private static CertificateSource CreateSmallSource() {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        ObjectVersionDictionaryBuilder aDictionary = new();
        aDictionary.BindSelf(SmallDebtObjectId);
        FrameBuilder aBuilder = new() { ObjectVersionDictionary = aDictionary };
        AddBase(aBuilder, SmallDebtObjectId, payloadBytes: 10);
        AbsoluteFrameAddress a = AppendExact(previous, aBuilder);

        RbfFile current = store.CreateFile();
        FrameBuilder publishedBuilder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder {
                Kind = ObjectVersionDictionaryKind.Delta,
                ParentRevisionFrameTicket = Previous(a.FrameTicket),
            },
        };
        AbsoluteFrameAddress published = AppendExact(current, publishedBuilder);
        return new CertificateSource(
            store,
            previous,
            current,
            [a, published],
            published);
    }

    private static CertificateSource CreateLargeSource() {
        uint[] objectIds = [101, 202, 303];
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        List<AbsoluteFrameAddress> revisions = [];
        AbsoluteFrameAddress? parent = null;
        foreach (uint objectId in objectIds) {
            ObjectVersionDictionaryBuilder dictionary = new();
            if (parent is AbsoluteFrameAddress parentAddress) {
                dictionary.Kind = ObjectVersionDictionaryKind.Delta;
                dictionary.ParentRevisionFrameTicket = Current(
                    parentAddress.FrameTicket);
            }
            dictionary.BindSelf(objectId);
            FrameBuilder builder = new() { ObjectVersionDictionary = dictionary };
            AddBase(builder, objectId, LargePayloadBytes);
            parent = AppendExact(previous, builder);
            revisions.Add(parent.Value);
        }

        RbfFile current = store.CreateFile();
        FrameBuilder publishedBuilder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder {
                Kind = ObjectVersionDictionaryKind.Delta,
                ParentRevisionFrameTicket = Previous(parent!.Value.FrameTicket),
            },
        };
        AbsoluteFrameAddress published = AppendExact(current, publishedBuilder);
        revisions.Add(published);
        return new CertificateSource(
            store,
            previous,
            current,
            revisions,
            published);
    }

    private static InitialSelection CreateInitialSelection(CertificateSource source) {
        NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
            source.Store,
            source.Current.FileNumber,
            source.PublishedRevisionAddress,
            new SaveStep([new CreateObject(InsertObjectId, 1)]));
        StayBSaveDecision stayDecision = new([], []);
        RotateCSaveDecision rotateDecision = new([], []);
        ExplicitCandidatePairEvaluation pair = ExplicitCandidatePairEvaluator.Evaluate(
            source.Store,
            facts,
            stayDecision,
            rotateDecision);
        FeasibleCandidate<StayBRevisionPlan> stay =
            Assert.IsType<FeasibleCandidate<StayBRevisionPlan>>(pair.StayBAttempt);
        ProbeRevisionCursor cursor = new(
            new FileScope(source.Current.FileNumber),
            source.PublishedRevisionAddress,
            source.Current.TailOffsetBytes);
        return new InitialSelection(pair, cursor, stay);
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
                appendBytes = checked(appendBytes - (minimumAppendBytes - (int)after));
            }

            if (appendBytes < minimumAppendBytes ||
                (appendBytes & RbfV040Layout.AlignmentMask) != 0) {
                throw new InvalidOperationException(
                    $"Cannot advance file {file.FileNumber} exactly to {targetTailOffsetBytes}.");
            }

            int payloadBytes = appendBytes - minimumAppendBytes;
            _ = file.Append(new FrameBuilder().Build(), payloadBytes);
        }

        Assert.Equal(targetTailOffsetBytes, file.TailOffsetBytes);
    }

    private static void AddBase(FrameBuilder frame, uint objectId, int payloadBytes) {
        ObjectVersionBuilder version = frame.Add(objectId);
        version.Kind = ObjectVersionKind.Base;
        version.PayloadBytes = payloadBytes;
        version.ReconstructionObjectPayloadBytes = payloadBytes;
        version.ResultBasePayloadBytes = payloadBytes;
        version.LogicalVersionOrdinal = 1;
    }

    private static AbsoluteFrameAddress AppendExact(RbfFile file, FrameBuilder builder) {
        Frame frame = builder.Build();
        ProvisionalRevisionV0Estimate estimate =
            ProvisionalRevisionV0Estimator.Estimate(frame, file.TailOffsetBytes);
        FrameTicket ticket = file.Append(
            frame,
            estimate.RbfLayout.PayloadLengthBytes,
            estimate.RbfLayout.TailMetaLengthBytes);
        return new AbsoluteFrameAddress(file.FileNumber, ticket);
    }

    private static void AssertRuntimeState(
        RbfFileStore store,
        AbsoluteFrameAddress revisionAddress,
        IReadOnlyDictionary<uint, LogicalObjectState> expected) {
        IReadOnlyDictionary<uint, AbsoluteFrameAddress> bindings =
            ObjectVersionDictionaryReader.MaterializeLive(
                store,
                revisionAddress).Bindings;
        AssertExactState(expected, PhysicalStateOracle.Materialize(store, bindings));
    }

    private static void AssertExactState(
        IReadOnlyDictionary<uint, LogicalObjectState> expected,
        IReadOnlyDictionary<uint, LogicalObjectState> actual) {
        Assert.Equal(expected.Count, actual.Count);
        foreach ((uint objectId, LogicalObjectState state) in expected) {
            Assert.True(actual.TryGetValue(objectId, out LogicalObjectState actualState));
            Assert.Equal(state, actualState);
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

    private static void AssertStoreSnapshot(StoreSnapshot expected, StoreSnapshot actual) {
        Assert.Equal(expected.FileCount, actual.FileCount);
        Assert.Equal(expected.Files, actual.Files);
    }

    private sealed record CertificateSource(
        RbfFileStore Store,
        RbfFile Previous,
        RbfFile Current,
        IReadOnlyList<AbsoluteFrameAddress> RevisionAddresses,
        AbsoluteFrameAddress PublishedRevisionAddress);

    private sealed record InitialSelection(
        ExplicitCandidatePairEvaluation Pair,
        ProbeRevisionCursor Cursor,
        FeasibleCandidate<StayBRevisionPlan> StayB);

    private sealed record StoreSnapshot(int FileCount, IReadOnlyList<FileSnapshot> Files);

    private sealed record FileSnapshot(uint FileNumber, int FrameCount, long TailOffsetBytes);
}
