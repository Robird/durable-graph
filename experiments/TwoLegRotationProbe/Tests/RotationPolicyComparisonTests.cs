using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Policies;
using Atelia.TwoLegRotationProbe.Rotation;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class RotationPolicyComparisonTests {
    private static readonly CandidateTarget[] FixedTargets = [
        CandidateTarget.StayB,
        CandidateTarget.StayB,
        CandidateTarget.RotateC,
    ];

    [Fact]
    public void Paced_one_debt_comparison_is_causal_over_one_shared_trace() {
        PolicySource source = CreateSource();
        WorkloadTrace trace = CreateTrace();
        RbfFileStore lazyStore = source.Store.ForkForProbe();
        RbfFileStore pacedStore = source.Store.ForkForProbe();

        PolicyRun lazy = Run(
            trace,
            lazyStore,
            CreateCursor(source),
            paceOneDebtObject: false,
            SelectFixedTarget);
        PolicyRun paced = Run(
            trace,
            pacedStore,
            CreateCursor(source),
            paceOneDebtObject: true,
            SelectFixedTarget);

        Assert.Same(trace, lazy.SourceTrace);
        Assert.Same(trace, paced.SourceTrace);
        Assert.NotSame(lazy.Store, paced.Store);
        Assert.NotSame(lazy.Store.GetFile(1), paced.Store.GetFile(1));
        Assert.Equal(FixedTargets, lazy.Steps.Select(static step => step.Target));
        Assert.Equal(FixedTargets, paced.Steps.Select(static step => step.Target));

        Assert.Equal(
            ["", "", "10,20,30"],
            DescribeMigrations(lazy));
        Assert.Equal(
            ["10", "20", "30"],
            DescribeMigrations(paced));
        Assert.Equal(
            ["10,20,30", "10,20,30", "1001,1002"],
            DescribeDebt(lazy));
        Assert.Equal(
            ["20,30", "30", "10,20,1001,1002"],
            DescribeDebt(paced));
        Assert.Equal([600L, 600L, 2L], DescribeDebtBaseBytes(lazy));
        Assert.Equal([500L, 300L, 302L], DescribeDebtBaseBytes(paced));
        Assert.Equal(
            [1, 1, 2],
            lazy.Steps.Select(static step => step.SelectedObservation
                .PostLiveReconstruction.PreviousFileUniqueFrameCount));
        Assert.Equal(
            [1, 1, 2],
            paced.Steps.Select(static step => step.SelectedObservation
                .PostLiveReconstruction.PreviousFileUniqueFrameCount));
        Assert.Equal(
            [2, 3, 3],
            lazy.Steps.Select(static step => step.SelectedObservation
                .PostLiveReconstruction.Metrics.UniqueFrameCount));
        Assert.Equal(
            [2, 3, 3],
            paced.Steps.Select(static step => step.SelectedObservation
                .PostLiveReconstruction.Metrics.UniqueFrameCount));
        Assert.Equal(
            lazy.Steps.Take(2).Select(static step => step.SelectedObservation
                .PostLiveReconstruction.PreviousFileFrameBytes),
            paced.Steps.Take(2).Select(static step => step.SelectedObservation
                .PostLiveReconstruction.PreviousFileFrameBytes));

        Assert.Equal(
            lazy.Steps.Select(static step =>
                step.SelectedObservation.ForegroundDomainRecordBytes),
            paced.Steps.Select(static step =>
                step.SelectedObservation.ForegroundDomainRecordBytes));
        Assert.Equal(
            lazy.Steps.Sum(static step =>
                step.SelectedObservation.MaintenanceDomainRecordBytes),
            paced.Steps.Sum(static step =>
                step.SelectedObservation.MaintenanceDomainRecordBytes));

        Assert.True(MaxRealizedAppendBytes(paced) < MaxRealizedAppendBytes(lazy));
        Assert.True(
            paced.Steps[^1].SelectedObservation.Layout.AppendLengthBytes <
            lazy.Steps[^1].SelectedObservation.Layout.AppendLengthBytes);
        Assert.True(
            paced.Steps[^1].SelectedObservation.PostLiveReconstruction
                .PreviousFileDependentBasePayloadBytes >
            lazy.Steps[^1].SelectedObservation.PostLiveReconstruction
                .PreviousFileDependentBasePayloadBytes);
        Assert.True(
            paced.Steps[^1].SelectedObservation.PostLiveReconstruction
                .PreviousFileFrameBytes >
            lazy.Steps[^1].SelectedObservation.PostLiveReconstruction
                .PreviousFileFrameBytes);

        Assert.Equal([2, 2, 3], lazy.Steps.Select(static step => step.FileCountAfterApply));
        Assert.Equal([2, 2, 3], paced.Steps.Select(static step => step.FileCountAfterApply));
        AssertImmediatePostStayTerminalCandidates(lazy);
        AssertImmediatePostStayTerminalCandidates(paced);
        Assert.All(
            lazy.Steps.Take(2).Zip(paced.Steps.Take(2)),
            pair => Assert.True(
                pair.Second.ImmediatePostStayTerminal!.Layout.AppendLengthBytes <
                pair.First.ImmediatePostStayTerminal!.Layout.AppendLengthBytes));

        PolicyRun lazyReplay = Run(
            trace,
            source.Store.ForkForProbe(),
            CreateCursor(source),
            paceOneDebtObject: false,
            SelectFixedTarget);
        PolicyRun pacedReplay = Run(
            trace,
            source.Store.ForkForProbe(),
            CreateCursor(source),
            paceOneDebtObject: true,
            SelectFixedTarget);
        Assert.Equal(DescribeRun(lazy), DescribeRun(lazyReplay));
        Assert.Equal(DescribeRun(paced), DescribeRun(pacedReplay));

        Assert.Equal(2, source.Store.FileCount);
        Assert.Equal(1, source.Store.GetFile(2).FrameCount);
    }

    [Fact]
    public void Debt_zero_then_rotate_exposes_lazy_stall_and_paced_progress() {
        PolicySource source = CreateSource();
        WorkloadTrace trace = CreateDebtZeroThenRotateTrace();
        RbfFileStore lazyStore = source.Store.ForkForProbe();
        RbfFileStore pacedStore = source.Store.ForkForProbe();

        PolicyRun lazy = Run(
            trace,
            lazyStore,
            CreateCursor(source),
            paceOneDebtObject: false,
            SelectDebtZeroThenRotateTarget);
        PolicyRun paced = Run(
            trace,
            pacedStore,
            CreateCursor(source),
            paceOneDebtObject: true,
            SelectDebtZeroThenRotateTarget);

        Assert.Same(trace, lazy.SourceTrace);
        Assert.Same(trace, paced.SourceTrace);
        Assert.NotSame(lazy.Store, paced.Store);
        Assert.NotSame(lazy.Store.GetFile(1), paced.Store.GetFile(1));
        Assert.Equal(trace.Steps.Count, lazy.Steps.Count);
        Assert.Equal(trace.Steps.Count, paced.Steps.Count);

        Assert.Equal(
            ["10,20,30", "10,20,30", "10,20,30", "10,20,30"],
            DescribeSourceDebt(lazy));
        Assert.Equal(
            [3, 3, 3, 3],
            lazy.Steps.Select(static step => step.SourcePreviousDebtObjectIds.Count));
        Assert.All(
            lazy.Steps,
            step => Assert.Equal(CandidateTarget.StayB, step.Target));
        Assert.Equal(["", "", "", ""], DescribeMigrations(lazy));
        Assert.All(lazy.Steps, static step => Assert.False(step.ActualRotation));
        Assert.All(
            lazy.Steps,
            step => {
                Assert.NotNull(step.CompletionCertificate);
                CandidateRawObservation terminal = Assert.IsType<
                    CandidateRawObservation>(step.ImmediatePostStayTerminal);
                Assert.Equal(CandidateTarget.RotateC, terminal.Target);
                Assert.Equal(2, step.FileCountAfterApply);
            });
        Assert.Equal(
            ["10,20,30", "10,20,30", "10,20,30", "10,20,30"],
            DescribeDebt(lazy));
        Assert.Equal(0, CountActualRotations(lazy));
        Assert.Equal(
            PolicyRunProgress.CompletedTraceWithDeferredPreviousDebt,
            ClassifyProgress(lazy));

        Assert.Equal(
            ["10,20,30", "20,30", "30", ""],
            DescribeSourceDebt(paced));
        Assert.Equal(
            [3, 2, 1, 0],
            paced.Steps.Select(static step => step.SourcePreviousDebtObjectIds.Count));
        Assert.Equal(
            [
                CandidateTarget.StayB,
                CandidateTarget.StayB,
                CandidateTarget.StayB,
                CandidateTarget.RotateC,
            ],
            paced.Steps.Select(static step => step.Target));
        Assert.Equal(["10", "20", "30", ""], DescribeMigrations(paced));
        Assert.Equal(
            [false, false, false, true],
            paced.Steps.Select(static step => step.ActualRotation));
        Assert.Equal(
            ["20,30", "30", "", "10,20,30,1001,1002,1003"],
            DescribeDebt(paced));
        Assert.Equal([500L, 300L, 0L, 603L], DescribeDebtBaseBytes(paced));
        Assert.Equal(1, CountActualRotations(paced));
        Assert.Equal(PolicyRunProgress.RealizedRotation, ClassifyProgress(paced));

        PolicyStep pacedThird = paced.Steps[2];
        Assert.Empty(
            pacedThird.SelectedObservation.PostLiveReconstruction
                .PreviousFileDependentObjectIds);
        Assert.Equal(1U, pacedThird.ResultCursor.FileScope.PreviousFileNumber);
        Assert.Equal(2U, pacedThird.ResultCursor.FileScope.CurrentFileNumber);
        Assert.Equal(2, pacedThird.FileCountAfterApply);
        Assert.NotNull(pacedThird.CompletionCertificate);
        Assert.Equal(
            CandidateTarget.RotateC,
            Assert.IsType<CandidateRawObservation>(
                pacedThird.ImmediatePostStayTerminal).Target);
        Assert.False(pacedThird.ActualRotation);
        WorkloadTrace pacedPrefixTrace = new(
            scenarioName: "debt-zero-awaiting-next-save",
            generatorId: "handwritten",
            generatorVersion: 1,
            seed: 0,
            trace.Steps.Take(3));
        PolicyRun debtFreeAwaitingNextSave = paced with {
            SourceTrace = pacedPrefixTrace,
            Steps = paced.Steps.Take(3).ToArray(),
        };
        Assert.Throws<InvalidOperationException>(
            () => ClassifyProgress(debtFreeAwaitingNextSave));

        PolicyStep pacedFourth = paced.Steps[3];
        Assert.Equal(
            1U,
            pacedFourth.SelectedObservation.Facts.PreviousFileNumber);
        Assert.Equal(
            2U,
            pacedFourth.SelectedObservation.Facts.CurrentFileNumber);
        Assert.Equal(2U, pacedFourth.ResultCursor.FileScope.PreviousFileNumber);
        Assert.Equal(3U, pacedFourth.ResultCursor.FileScope.CurrentFileNumber);
        Assert.Equal(3, pacedFourth.FileCountAfterApply);
        Assert.True(pacedFourth.ActualRotation);
        Assert.Null(pacedFourth.CompletionCertificate);
        Assert.Null(pacedFourth.ImmediatePostStayTerminal);
        Assert.Equal(
            [10U, 20U, 30U, 1001U, 1002U, 1003U],
            pacedFourth.SelectedObservation.PostLiveReconstruction
                .PreviousFileDependentObjectIds);
        Assert.Equal(
            603L,
            pacedFourth.SelectedObservation.PostLiveReconstruction
                .PreviousFileDependentBasePayloadBytes);
        Assert.Equal(
            3,
            pacedFourth.SelectedObservation.PostLiveReconstruction
                .PreviousFileUniqueFrameCount);

        PolicyRun lazyReplay = Run(
            trace,
            source.Store.ForkForProbe(),
            CreateCursor(source),
            paceOneDebtObject: false,
            SelectDebtZeroThenRotateTarget);
        PolicyRun pacedReplay = Run(
            trace,
            source.Store.ForkForProbe(),
            CreateCursor(source),
            paceOneDebtObject: true,
            SelectDebtZeroThenRotateTarget);
        Assert.Equal(DescribeRun(lazy), DescribeRun(lazyReplay));
        Assert.Equal(DescribeRun(paced), DescribeRun(pacedReplay));

        Assert.Equal(2, source.Store.FileCount);
        Assert.Equal(1, source.Store.GetFile(1).FrameCount);
        Assert.Equal(1, source.Store.GetFile(2).FrameCount);
    }

    private static PolicyRun Run(
        WorkloadTrace trace,
        RbfFileStore store,
        ProbeRevisionCursor initialCursor,
        bool paceOneDebtObject,
        Func<int, NormalizedSaveFacts, CandidateTarget> selectTarget) {
        ArgumentNullException.ThrowIfNull(selectTarget);

        ProbeRevisionCursor cursor = initialCursor;
        Dictionary<uint, LogicalObjectState> expectedState = new() {
            [10] = new(100, 1),
            [20] = new(200, 1),
            [30] = new(300, 1),
        };
        List<PolicyStep> steps = [];

        for (int index = 0; index < trace.Steps.Count; index++) {
            SaveStep saveStep = trace.Steps[index];
            NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
                store,
                cursor.FileScope.CurrentFileNumber,
                cursor.PublishedRevisionAddress,
                saveStep);
            uint[] sourcePreviousDebtObjectIds =
                GetSourcePreviousDebtObjectIds(facts);
            CandidateTarget target = selectTarget(index, facts);
            uint[] requestedMigrations = target == CandidateTarget.StayB &&
                paceOneDebtObject
                ? SelectSmallestPreviousDebtNoChange(facts)
                : [];
            StayBSaveDecision stayDecision = new([], requestedMigrations);
            RotateCSaveDecision rotateDecision = new([], []);
            ExplicitCandidatePairEvaluation pair =
                ExplicitCandidatePairEvaluator.Evaluate(
                    store,
                    facts,
                    stayDecision,
                    rotateDecision);

            RotationPolicyStepAttempt attempt =
                ExplicitRotationPolicyStepHarness.TryApplySelected(
                    store,
                    cursor,
                    pair,
                    target);
            CandidateRawObservation selectedObservation;
            CandidateRawObservation? immediatePostStayTerminal = null;
            CanPrepareAndRotateCertificate? completionCertificate = null;
            bool actualRotation = false;
            switch (attempt) {
                case AppliedStayBPolicyStep appliedStay:
                    Assert.Equal(CandidateTarget.StayB, target);
                    Assert.Same(pair, appliedStay.Evaluation);
                    Assert.Same(appliedStay.Selected, appliedStay.CompletionCertificate.InitialStayB);
                    Assert.Empty(appliedStay.CompletionCertificate.MaintenanceStayBSteps);
                    selectedObservation = appliedStay.Selected.Observation;
                    completionCertificate = appliedStay.CompletionCertificate;
                    immediatePostStayTerminal =
                        appliedStay.CompletionCertificate.FinalRotateC.Observation;
                    cursor = appliedStay.ResultCursor;
                    break;
                case AppliedRotateCPolicyStep appliedRotate:
                    Assert.Equal(CandidateTarget.RotateC, target);
                    Assert.Same(pair, appliedRotate.Evaluation);
                    selectedObservation = appliedRotate.Selected.Observation;
                    cursor = appliedRotate.ResultCursor;
                    actualRotation = true;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Fixed comparison action was not applied: {attempt.GetType().Name}.");
            }

            ApplyExpectedState(expectedState, saveStep);
            AssertExactState(expectedState, facts.PostLiveStates);
            AssertRuntimeStateAndClosure(store, cursor, expectedState);
            steps.Add(new PolicyStep(
                target,
                sourcePreviousDebtObjectIds,
                GetMaintenanceObjectIds(facts, selectedObservation),
                selectedObservation,
                immediatePostStayTerminal,
                completionCertificate,
                cursor,
                store.FileCount,
                actualRotation));
        }

        return new PolicyRun(trace, store, steps);
    }

    private static CandidateTarget SelectFixedTarget(
        int index,
        NormalizedSaveFacts _) => FixedTargets[index];

    private static CandidateTarget SelectDebtZeroThenRotateTarget(
        int _,
        NormalizedSaveFacts facts) =>
        GetSourcePreviousDebtObjectIds(facts).Length == 0
            ? CandidateTarget.RotateC
            : CandidateTarget.StayB;

    private static uint[] GetSourcePreviousDebtObjectIds(
        NormalizedSaveFacts facts) => facts.ParentLive.Values
        .Where(source =>
            source.BaseAddress.FileNumber == facts.PreviousFileNumber)
        .Select(static source => source.ObjectId)
        .Order()
        .ToArray();

    private static uint[] SelectSmallestPreviousDebtNoChange(
        NormalizedSaveFacts facts) => facts.NoChanges
        .Where(fact => fact.Source.BaseAddress.FileNumber == facts.PreviousFileNumber)
        .Select(static fact => fact.ObjectId)
        .Order()
        .Take(1)
        .ToArray();

    private static uint[] GetMaintenanceObjectIds(
        NormalizedSaveFacts facts,
        CandidateRawObservation observation) {
        HashSet<uint> noChangeIds = facts.NoChanges
            .Select(static fact => fact.ObjectId)
            .ToHashSet();
        return observation.Candidate.Frame.ObjectVersions.Keys
            .Where(noChangeIds.Contains)
            .Order()
            .ToArray();
    }

    private static void AssertImmediatePostStayTerminalCandidates(PolicyRun run) {
        foreach (PolicyStep step in run.Steps.Take(2)) {
            CanPrepareAndRotateCertificate certificate = Assert.IsType<
                CanPrepareAndRotateCertificate>(step.CompletionCertificate);
            CandidateRawObservation terminal = Assert.IsType<
                CandidateRawObservation>(step.ImmediatePostStayTerminal);
            Assert.Empty(certificate.MaintenanceStayBSteps);
            Assert.Same(certificate.FinalRotateC.Observation, terminal);
            Assert.Equal(CandidateTarget.RotateC, terminal.Target);
            Assert.Equal(3U, terminal.Candidate.FileNumber);
            Assert.Equal(2, step.FileCountAfterApply);
        }

        Assert.Null(run.Steps[^1].CompletionCertificate);
        Assert.Null(run.Steps[^1].ImmediatePostStayTerminal);
    }

    private static int MaxRealizedAppendBytes(PolicyRun run) => run.Steps
        .Max(static step => step.SelectedObservation.Layout.AppendLengthBytes);

    private static string[] DescribeMigrations(PolicyRun run) => run.Steps
        .Select(static step => string.Join(',', step.MaintenanceObjectIds))
        .ToArray();

    private static string[] DescribeSourceDebt(PolicyRun run) => run.Steps
        .Select(static step => string.Join(',', step.SourcePreviousDebtObjectIds))
        .ToArray();

    private static string[] DescribeDebt(PolicyRun run) => run.Steps
        .Select(static step => string.Join(
            ',',
            step.SelectedObservation.PostLiveReconstruction
                .PreviousFileDependentObjectIds))
        .ToArray();

    private static long[] DescribeDebtBaseBytes(PolicyRun run) => run.Steps
        .Select(static step => step.SelectedObservation.PostLiveReconstruction
            .PreviousFileDependentBasePayloadBytes)
        .ToArray();

    private static int CountActualRotations(PolicyRun run) => run.Steps
        .Count(static step => step.ActualRotation);

    private static PolicyRunProgress ClassifyProgress(PolicyRun run) {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Steps.Count == 0) {
            throw new InvalidOperationException(
                "An empty trace has no applied policy outcome to classify.");
        }

        if (run.Steps.Count != run.SourceTrace.Steps.Count) {
            throw new InvalidOperationException(
                "Policy progress is classified only after the complete frozen trace is consumed.");
        }

        if (CountActualRotations(run) != 0) {
            return PolicyRunProgress.RealizedRotation;
        }

        if (run.Steps[^1].SelectedObservation.PostLiveReconstruction
            .PreviousFileDependentObjectIds.Count != 0) {
            return PolicyRunProgress.CompletedTraceWithDeferredPreviousDebt;
        }

        throw new InvalidOperationException(
            "A debt-free run without an applied Rotate awaits another Save and is not stalled.");
    }

    private static string[] DescribeRun(PolicyRun run) => run.Steps
        .Select(static step =>
            $"{step.Target}:{string.Join(',', step.MaintenanceObjectIds)}:" +
            $"source={string.Join(',', step.SourcePreviousDebtObjectIds)}:" +
            $"rotated={step.ActualRotation}:" +
            $"scope={step.SelectedObservation.Facts.PreviousFileNumber}/" +
            $"{step.SelectedObservation.Facts.CurrentFileNumber}->" +
            $"{step.ResultCursor.FileScope.PreviousFileNumber}/" +
            $"{step.ResultCursor.FileScope.CurrentFileNumber}:" +
            $"{step.SelectedObservation.ForegroundDomainRecordBytes}:" +
            $"{step.SelectedObservation.MaintenanceDomainRecordBytes}:" +
            $"{step.SelectedObservation.Layout.AppendLengthBytes}:" +
            $"{string.Join(',', step.SelectedObservation.PostLiveReconstruction.PreviousFileDependentObjectIds)}:" +
            $"{step.SelectedObservation.PostLiveReconstruction.PreviousFileDependentBasePayloadBytes}:" +
            $"{step.SelectedObservation.PostLiveReconstruction.PreviousFileFrameBytes}:" +
            $"terminal={step.ImmediatePostStayTerminal?.Layout.AppendLengthBytes}")
        .ToArray();

    private static void ApplyExpectedState(
        IDictionary<uint, LogicalObjectState> expected,
        SaveStep step) {
        foreach (WorkloadChange change in step.Changes) {
            switch (change) {
                case CreateObject create:
                    expected.Add(create.ObjectId, new LogicalObjectState(
                        create.BasePayloadBytes,
                        LogicalVersionOrdinal: 1));
                    break;
                case UpdateObject update:
                    LogicalObjectState previous = expected[update.ObjectId];
                    expected[update.ObjectId] = new LogicalObjectState(
                        update.ResultBasePayloadBytes,
                        checked(previous.LogicalVersionOrdinal + 1));
                    break;
                case RemoveObject remove:
                    _ = expected.Remove(remove.ObjectId);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported workload change {change.GetType().Name}.");
            }
        }
    }

    private static void AssertRuntimeStateAndClosure(
        RbfFileStore store,
        ProbeRevisionCursor cursor,
        IReadOnlyDictionary<uint, LogicalObjectState> expectedState) {
        ObjectVersionDictionaryMaterializationInspection materialization =
            ObjectVersionDictionaryReader.MaterializeLive(
                store,
                cursor.PublishedRevisionAddress);
        AssertExactState(
            expectedState,
            PhysicalStateOracle.Materialize(store, materialization.Bindings));

        uint[] allowedFileNumbers = [
            cursor.FileScope.PreviousFileNumber!.Value,
            cursor.FileScope.CurrentFileNumber,
        ];
        Assert.All(
            materialization.DictionaryRevisionAddresses,
            address => Assert.Contains(address.FileNumber, allowedFileNumbers));
        foreach ((uint objectId, AbsoluteFrameAddress headAddress) in
            materialization.Bindings) {
            ObjectReconstructionInspection reconstruction =
                PhysicalStateOracle.InspectObjectReconstruction(
                    store,
                    objectId,
                    headAddress);
            Assert.All(
                reconstruction.ReconstructionFrameAddresses,
                address => Assert.Contains(address.FileNumber, allowedFileNumbers));
        }
    }

    private static void AssertExactState(
        IReadOnlyDictionary<uint, LogicalObjectState> expected,
        IReadOnlyDictionary<uint, LogicalObjectState> actual) {
        Assert.Equal(expected.Count, actual.Count);
        foreach ((uint objectId, LogicalObjectState expectedState) in expected) {
            Assert.True(actual.TryGetValue(objectId, out LogicalObjectState actualState));
            Assert.Equal(expectedState, actualState);
        }
    }

    private static PolicySource CreateSource() {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        ObjectVersionDictionaryBuilder previousDictionary = new();
        previousDictionary.BindSelf(10);
        previousDictionary.BindSelf(20);
        previousDictionary.BindSelf(30);
        FrameBuilder previousBuilder = new() {
            ObjectVersionDictionary = previousDictionary,
        };
        AddBase(previousBuilder, objectId: 10, payloadBytes: 100);
        AddBase(previousBuilder, objectId: 20, payloadBytes: 200);
        AddBase(previousBuilder, objectId: 30, payloadBytes: 300);
        AbsoluteFrameAddress previousRevision = AppendExact(
            previous,
            previousBuilder);

        RbfFile current = store.CreateFile();
        FrameBuilder publishedBuilder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder {
                Kind = ObjectVersionDictionaryKind.Delta,
                ParentRevisionFrameTicket = new RelativeFrameTicket(
                    IsPreviousFile: true,
                    previousRevision.FrameTicket),
            },
        };
        AbsoluteFrameAddress publishedRevision = AppendExact(
            current,
            publishedBuilder);
        return new PolicySource(store, current, publishedRevision);
    }

    private static WorkloadTrace CreateTrace() => new(
        scenarioName: "fixed-target-causal-comparison",
        generatorId: "handwritten",
        generatorVersion: 1,
        seed: 0,
        [
            new SaveStep([new CreateObject(1001, 1)]),
            new SaveStep([new CreateObject(1002, 1)]),
            new SaveStep([new CreateObject(1003, 1)]),
        ]);

    private static WorkloadTrace CreateDebtZeroThenRotateTrace() => new(
        scenarioName: "debt-zero-then-rotate",
        generatorId: "handwritten",
        generatorVersion: 1,
        seed: 0,
        [
            new SaveStep([new CreateObject(1001, 1)]),
            new SaveStep([new CreateObject(1002, 1)]),
            new SaveStep([new CreateObject(1003, 1)]),
            new SaveStep([new CreateObject(1004, 1)]),
        ]);

    private static ProbeRevisionCursor CreateCursor(PolicySource source) => new(
        new FileScope(source.Current.FileNumber),
        source.PublishedRevisionAddress,
        source.Current.TailOffsetBytes);

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
            ProvisionalRevisionV0Estimator.Estimate(frame, file.TailOffsetBytes);
        FrameTicket ticket = file.Append(
            frame,
            estimate.RbfLayout.PayloadLengthBytes,
            estimate.RbfLayout.TailMetaLengthBytes);
        return new AbsoluteFrameAddress(file.FileNumber, ticket);
    }

    private sealed record PolicySource(
        RbfFileStore Store,
        RbfFile Current,
        AbsoluteFrameAddress PublishedRevisionAddress);

    private sealed record PolicyRun(
        WorkloadTrace SourceTrace,
        RbfFileStore Store,
        IReadOnlyList<PolicyStep> Steps);

    private enum PolicyRunProgress {
        CompletedTraceWithDeferredPreviousDebt,
        RealizedRotation,
    }

    private sealed record PolicyStep(
        CandidateTarget Target,
        IReadOnlyList<uint> SourcePreviousDebtObjectIds,
        IReadOnlyList<uint> MaintenanceObjectIds,
        CandidateRawObservation SelectedObservation,
        CandidateRawObservation? ImmediatePostStayTerminal,
        CanPrepareAndRotateCertificate? CompletionCertificate,
        ProbeRevisionCursor ResultCursor,
        int FileCountAfterApply,
        bool ActualRotation);
}
