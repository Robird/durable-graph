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

    private static readonly CandidateTarget[] TwoEpochTargets = [
        CandidateTarget.StayB,
        CandidateTarget.RotateC,
        CandidateTarget.StayB,
        CandidateTarget.RotateC,
    ];

    private static readonly CandidateTarget[] ChangedDebtTargets = [
        CandidateTarget.StayB,
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
            SelectNoMigrationDecisions,
            SelectFixedTarget);
        PolicyRun paced = Run(
            trace,
            pacedStore,
            CreateCursor(source),
            SelectPacedOneDebtDecisions,
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
        AssertCounterfactualTerminalCandidates(lazy, expectedStayCount: 2);
        AssertCounterfactualTerminalCandidates(paced, expectedStayCount: 2);
        Assert.All(
            lazy.Steps.Take(2).Zip(paced.Steps.Take(2)),
            pair => Assert.True(
                pair.Second.Observation.CounterfactualTerminalC!.AppendBytes <
                pair.First.Observation.CounterfactualTerminalC!.AppendBytes));

        AssertRunObservationConsistency(lazy);
        AssertRunObservationConsistency(paced);
        AssertSingleObservedEpoch(
            lazy,
            expectedSaveCount: 3,
            expectedStayCount: 2,
            expectedClosedByRotation: true);
        AssertSingleObservedEpoch(
            paced,
            expectedSaveCount: 3,
            expectedStayCount: 2,
            expectedClosedByRotation: true);
        Assert.Equal(1, lazy.Reduction.RotationCount);
        Assert.Equal(1, paced.Reduction.RotationCount);
        Assert.Equal(2, CountCounterfactualTerminals(lazy));
        Assert.Equal(2, CountCounterfactualTerminals(paced));
        Assert.True(
            paced.Reduction.PeakRealizedSaveAppendBytes <
            lazy.Reduction.PeakRealizedSaveAppendBytes);
        Assert.True(
            paced.Reduction.PeakRealizedRotationAppendBytes <
            lazy.Reduction.PeakRealizedRotationAppendBytes);

        PolicyRun lazyReplay = Run(
            trace,
            source.Store.ForkForProbe(),
            CreateCursor(source),
            SelectNoMigrationDecisions,
            SelectFixedTarget);
        PolicyRun pacedReplay = Run(
            trace,
            source.Store.ForkForProbe(),
            CreateCursor(source),
            SelectPacedOneDebtDecisions,
            SelectFixedTarget);
        // This compares the explicit observation value projection, not a Store or
        // execution transcript.
        Assert.Equal(
            DescribeReduction(lazy.Reduction),
            DescribeReduction(lazyReplay.Reduction));
        Assert.Equal(
            DescribeReduction(paced.Reduction),
            DescribeReduction(pacedReplay.Reduction));

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
            SelectNoMigrationDecisions,
            SelectDebtZeroThenRotateTarget);
        PolicyRun paced = Run(
            trace,
            pacedStore,
            CreateCursor(source),
            SelectPacedOneDebtDecisions,
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
                Assert.NotNull(step.Observation.CounterfactualTerminalC);
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

        AssertRunObservationConsistency(lazy);
        AssertRunObservationConsistency(paced);
        AssertSingleObservedEpoch(
            lazy,
            expectedSaveCount: 4,
            expectedStayCount: 4,
            expectedClosedByRotation: false);
        AssertSingleObservedEpoch(
            paced,
            expectedSaveCount: 4,
            expectedStayCount: 3,
            expectedClosedByRotation: true);
        Assert.Equal(0, lazy.Reduction.RotationCount);
        Assert.Equal(1, paced.Reduction.RotationCount);
        Assert.Equal(4, CountCounterfactualTerminals(lazy));
        Assert.Equal(3, CountCounterfactualTerminals(paced));

        PolicyStep pacedThird = paced.Steps[2];
        Assert.Empty(
            pacedThird.SelectedObservation.PostLiveReconstruction
                .PreviousFileDependentObjectIds);
        Assert.Equal(1U, pacedThird.ResultCursor.FileScope.PreviousFileNumber);
        Assert.Equal(2U, pacedThird.ResultCursor.FileScope.CurrentFileNumber);
        Assert.Equal(2, pacedThird.FileCountAfterApply);
        Assert.NotNull(pacedThird.CompletionCertificate);
        Assert.NotNull(pacedThird.Observation.CounterfactualTerminalC);
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
        Assert.Null(pacedFourth.Observation.CounterfactualTerminalC);
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
        Assert.Equal(
            new CanonicalObjectIds([10, 20, 30, 1001, 1002, 1003]),
            pacedFourth.Observation.Result.PreviousDebtObjectIds);
        Assert.Equal(
            603L,
            pacedFourth.Observation.Result.PreviousDebtBasePayloadBytes);
        Assert.Equal(3, pacedFourth.Observation.Result.PreviousUniqueFrameCount);

        PolicyRun lazyReplay = Run(
            trace,
            source.Store.ForkForProbe(),
            CreateCursor(source),
            SelectNoMigrationDecisions,
            SelectDebtZeroThenRotateTarget);
        PolicyRun pacedReplay = Run(
            trace,
            source.Store.ForkForProbe(),
            CreateCursor(source),
            SelectPacedOneDebtDecisions,
            SelectDebtZeroThenRotateTarget);
        // This compares the explicit observation value projection, not a Store or
        // execution transcript.
        Assert.Equal(
            DescribeReduction(lazy.Reduction),
            DescribeReduction(lazyReplay.Reduction));
        Assert.Equal(
            DescribeReduction(paced.Reduction),
            DescribeReduction(pacedReplay.Reduction));

        Assert.Equal(2, source.Store.FileCount);
        Assert.Equal(1, source.Store.GetFile(1).FrameCount);
        Assert.Equal(1, source.Store.GetFile(2).FrameCount);
    }

    [Fact]
    public void Reduction_groups_two_realized_rotation_epochs() {
        PolicySource source = CreateSource();
        WorkloadTrace trace = CreateDebtZeroThenRotateTrace();
        PolicyRun run = Run(
            trace,
            source.Store.ForkForProbe(),
            CreateCursor(source),
            SelectNoMigrationDecisions,
            static (index, _) => TwoEpochTargets[index]);

        AssertRunObservationConsistency(run);
        Assert.Equal(2, run.Reduction.Epochs.Count);
        RotationEpochObservation first = run.Reduction.Epochs[0];
        RotationEpochObservation second = run.Reduction.Epochs[1];
        Assert.Equal(new ScopeValue(1, 2), first.SourceScope);
        Assert.Equal(new ScopeValue(2, 3), second.SourceScope);
        Assert.Equal(2, first.ObservedSaveCount);
        Assert.Equal(2, second.ObservedSaveCount);
        Assert.Equal(1, first.StayCount);
        Assert.Equal(1, second.StayCount);
        Assert.True(first.ClosedByRotation);
        Assert.True(second.ClosedByRotation);
        Assert.Equal(
            run.Steps[1].Observation.Result,
            run.Steps[2].Observation.Source);
        Assert.Equal(new ScopeValue(3, 4), run.Steps[^1].Observation.Result.Scope);
        Assert.Equal(2, run.Reduction.RotationCount);

        PolicyRun replay = Run(
            trace,
            source.Store.ForkForProbe(),
            CreateCursor(source),
            SelectNoMigrationDecisions,
            static (index, _) => TwoEpochTargets[index]);
        Assert.Equal(
            DescribeReduction(run.Reduction),
            DescribeReduction(replay.Reduction));
    }

    [Fact]
    public void Changed_a_debt_base_writes_clear_debt_before_the_fixed_rotation() {
        PolicySource source = CreateSource();
        WorkloadTrace trace = CreateChangedDebtTrace();
        PolicyRun deltaControl = Run(
            trace,
            source.Store.ForkForProbe(),
            CreateCursor(source),
            SelectNoMigrationDecisions,
            SelectChangedDebtTarget);
        PolicyRun baseTreatment = Run(
            trace,
            source.Store.ForkForProbe(),
            CreateCursor(source),
            SelectChangedADebtBaseDecisions,
            SelectChangedDebtTarget);

        Assert.Same(trace, deltaControl.SourceTrace);
        Assert.Same(trace, baseTreatment.SourceTrace);
        Assert.NotSame(deltaControl.Store, baseTreatment.Store);
        Assert.Equal(
            ChangedDebtTargets,
            deltaControl.Steps.Select(static step => step.Target));
        Assert.Equal(
            ChangedDebtTargets,
            baseTreatment.Steps.Select(static step => step.Target));
        Assert.Equal(
            [6, 6, 6, 3],
            deltaControl.Steps.Select(static step =>
                step.Observation.ForegroundDomainRecordBytes));
        Assert.Equal(
            [102, 203, 303, 3],
            baseTreatment.Steps.Select(static step =>
                step.Observation.ForegroundDomainRecordBytes));
        Assert.Equal(
            [0, 0, 0, 608],
            deltaControl.Steps.Select(static step =>
                step.Observation.MaintenanceDomainRecordBytes));
        Assert.Equal(
            [0, 0, 0, 0],
            baseTreatment.Steps.Select(static step =>
                step.Observation.MaintenanceDomainRecordBytes));
        Assert.Equal(
            [48, 48, 48, 668],
            deltaControl.Steps.Select(static step => step.Observation.AppendBytes));
        Assert.Equal(
            [144, 244, 344, 60],
            baseTreatment.Steps.Select(static step => step.Observation.AppendBytes));

        uint[] updatedObjectIds = [10, 20, 30];
        for (int index = 0; index < updatedObjectIds.Length; index++) {
            uint objectId = updatedObjectIds[index];
            PolicyStep controlStep = deltaControl.Steps[index];
            PolicyStep treatmentStep = baseTreatment.Steps[index];
            NormalizedUpdateFact controlUpdate = Assert.Single(
                controlStep.SelectedObservation.Facts.Updates);
            NormalizedUpdateFact treatmentUpdate = Assert.Single(
                treatmentStep.SelectedObservation.Facts.Updates);
            Assert.Equal(objectId, controlUpdate.ObjectId);
            Assert.Equal(objectId, treatmentUpdate.ObjectId);
            Assert.Equal(1, controlUpdate.DeltaPayloadBytes);
            Assert.Equal(1, treatmentUpdate.DeltaPayloadBytes);

            ObjectVersion controlVersion = Assert.Single(
                controlStep.SelectedObservation.Candidate.Frame.ObjectVersions).Value;
            ObjectVersion treatmentVersion = Assert.Single(
                treatmentStep.SelectedObservation.Candidate.Frame.ObjectVersions).Value;
            Assert.Equal(ObjectVersionKind.Delta, controlVersion.Kind);
            Assert.Equal(ObjectVersionKind.Base, treatmentVersion.Kind);
            Assert.Equal(1, controlVersion.PayloadBytes);
            Assert.Equal(
                treatmentUpdate.ResultState.BasePayloadBytes,
                treatmentVersion.PayloadBytes);
            Assert.Equal(
                new FileScope(controlStep.SelectedObservation.Facts.CurrentFileNumber)
                    .Relativize(controlUpdate.Source.HeadAddress),
                controlVersion.DeltaParentFrameTicket);
            Assert.Null(treatmentVersion.DeltaParentFrameTicket);
            Assert.Empty(controlStep.MaintenanceObjectIds);
            Assert.Empty(treatmentStep.MaintenanceObjectIds);
            Assert.Equal(0, controlStep.Observation.MaintenanceDomainRecordBytes);
            Assert.Equal(0, treatmentStep.Observation.MaintenanceDomainRecordBytes);
            Assert.True(
                controlStep.Observation.ForegroundDomainRecordBytes <
                treatmentStep.Observation.ForegroundDomainRecordBytes);
            Assert.True(
                controlStep.Observation.AppendBytes <
                treatmentStep.Observation.AppendBytes);
        }

        Assert.Equal(
            ["10,20,30", "10,20,30", "10,20,30"],
            DescribeDebt(deltaControl)[..3]);
        Assert.Equal([600L, 600L, 600L], DescribeDebtBaseBytes(deltaControl)[..3]);
        Assert.Equal(
            ["20,30", "30", ""],
            DescribeDebt(baseTreatment)[..3]);
        Assert.Equal([500L, 300L, 0L], DescribeDebtBaseBytes(baseTreatment)[..3]);
        Assert.Equal(
            [1, 1, 0],
            baseTreatment.Steps.Take(3).Select(static step =>
                step.Observation.Result.PreviousUniqueFrameCount));
        long sharedAFrameBytes = baseTreatment.Steps[0]
            .Observation.Source.PreviousFrameBytes;
        Assert.Equal(
            [sharedAFrameBytes, sharedAFrameBytes, 0L],
            baseTreatment.Steps.Take(3).Select(static step =>
                step.Observation.Result.PreviousFrameBytes));
        Assert.Equal(
            [1, 1, 1],
            deltaControl.Steps.Take(3).Select(static step =>
                step.Observation.Result.PreviousUniqueFrameCount));
        Assert.Equal(
            [sharedAFrameBytes, sharedAFrameBytes, sharedAFrameBytes],
            deltaControl.Steps.Take(3).Select(static step =>
                step.Observation.Result.PreviousFrameBytes));

        PolicyStep controlRotate = deltaControl.Steps[^1];
        PolicyStep treatmentRotate = baseTreatment.Steps[^1];
        Assert.Equal([10U, 20U, 30U], controlRotate.MaintenanceObjectIds);
        Assert.Empty(treatmentRotate.MaintenanceObjectIds);
        Assert.True(
            treatmentRotate.Observation.AppendBytes <
            controlRotate.Observation.AppendBytes);
        Assert.True(
            baseTreatment.Reduction.PeakRealizedSaveAppendBytes <
            deltaControl.Reduction.PeakRealizedSaveAppendBytes);
        Assert.Equal(new ScopeValue(2, 3), controlRotate.Observation.Result.Scope);
        Assert.Equal(new ScopeValue(2, 3), treatmentRotate.Observation.Result.Scope);
        Assert.Equal(new CanonicalObjectIds([]),
            controlRotate.Observation.Result.PreviousDebtObjectIds);
        Assert.Equal(0L,
            controlRotate.Observation.Result.PreviousDebtBasePayloadBytes);
        Assert.Equal(0, controlRotate.Observation.Result.PreviousUniqueFrameCount);
        Assert.Equal(new CanonicalObjectIds([10, 20, 30]),
            treatmentRotate.Observation.Result.PreviousDebtObjectIds);
        Assert.Equal(600L,
            treatmentRotate.Observation.Result.PreviousDebtBasePayloadBytes);
        Assert.Equal(3,
            treatmentRotate.Observation.Result.PreviousUniqueFrameCount);
        Assert.Equal(720L,
            treatmentRotate.Observation.Result.PreviousFrameBytes);
        Assert.Equal(668, deltaControl.Reduction.PeakRealizedSaveAppendBytes);
        Assert.Equal(344, baseTreatment.Reduction.PeakRealizedSaveAppendBytes);

        AssertCounterfactualTerminalCandidates(
            deltaControl,
            expectedStayCount: 3);
        AssertCounterfactualTerminalCandidates(
            baseTreatment,
            expectedStayCount: 3);
        Assert.Equal(
            [660, 660, 660],
            deltaControl.Steps.Take(3).Select(static step =>
                step.Observation.CounterfactualTerminalC!.AppendBytes));
        Assert.Equal(
            [556, 352, 48],
            baseTreatment.Steps.Take(3).Select(static step =>
                step.Observation.CounterfactualTerminalC!.AppendBytes));
        Assert.Equal(
            ["", "", ""],
            deltaControl.Steps.Take(3).Select(static step =>
                step.Observation.CounterfactualTerminalC!
                    .Result.PreviousDebtObjectIds.ToString()));
        Assert.Equal(
            ["10", "10,20", "10,20,30"],
            baseTreatment.Steps.Take(3).Select(static step =>
                step.Observation.CounterfactualTerminalC!
                    .Result.PreviousDebtObjectIds.ToString()));
        Assert.Equal(
            [100L, 300L, 600L],
            baseTreatment.Steps.Take(3).Select(static step =>
                step.Observation.CounterfactualTerminalC!
                    .Result.PreviousDebtBasePayloadBytes));
        Assert.Equal(
            [1, 2, 3],
            baseTreatment.Steps.Take(3).Select(static step =>
                step.Observation.CounterfactualTerminalC!
                    .Result.PreviousUniqueFrameCount));
        Assert.All(
            deltaControl.Steps.Take(3).Zip(baseTreatment.Steps.Take(3)),
            pair => Assert.True(
                pair.Second.Observation.CounterfactualTerminalC!.AppendBytes <
                pair.First.Observation.CounterfactualTerminalC!.AppendBytes));
        AssertRunObservationConsistency(deltaControl);
        AssertRunObservationConsistency(baseTreatment);
        AssertSingleObservedEpoch(
            deltaControl,
            expectedSaveCount: 4,
            expectedStayCount: 3,
            expectedClosedByRotation: true);
        AssertSingleObservedEpoch(
            baseTreatment,
            expectedSaveCount: 4,
            expectedStayCount: 3,
            expectedClosedByRotation: true);
        Assert.Equal(1, deltaControl.Reduction.RotationCount);
        Assert.Equal(1, baseTreatment.Reduction.RotationCount);
        Assert.Equal(3, CountCounterfactualTerminals(deltaControl));
        Assert.Equal(3, CountCounterfactualTerminals(baseTreatment));

        IReadOnlyDictionary<uint, LogicalObjectState> expectedFinalState =
            new Dictionary<uint, LogicalObjectState> {
                [10] = new(100, 2),
                [20] = new(200, 2),
                [30] = new(300, 2),
                [1001] = new(1, 1),
            };
        AssertRuntimeStateAndClosure(
            deltaControl.Store,
            controlRotate.ResultCursor,
            expectedFinalState);
        AssertRuntimeStateAndClosure(
            baseTreatment.Store,
            treatmentRotate.ResultCursor,
            expectedFinalState);

        PolicyRun deltaReplay = Run(
            trace,
            source.Store.ForkForProbe(),
            CreateCursor(source),
            SelectNoMigrationDecisions,
            SelectChangedDebtTarget);
        PolicyRun baseReplay = Run(
            trace,
            source.Store.ForkForProbe(),
            CreateCursor(source),
            SelectChangedADebtBaseDecisions,
            SelectChangedDebtTarget);
        Assert.Equal(
            DescribeReduction(deltaControl.Reduction),
            DescribeReduction(deltaReplay.Reduction));
        Assert.Equal(
            DescribeReduction(baseTreatment.Reduction),
            DescribeReduction(baseReplay.Reduction));

        Assert.Equal(2, source.Store.FileCount);
        Assert.Equal(1, source.Store.GetFile(1).FrameCount);
        Assert.Equal(1, source.Store.GetFile(2).FrameCount);
    }

    private static PolicyRun Run(
        WorkloadTrace trace,
        RbfFileStore store,
        ProbeRevisionCursor initialCursor,
        DecisionSelector selectDecisions,
        Func<int, NormalizedSaveFacts, CandidateTarget> selectTarget) {
        ArgumentNullException.ThrowIfNull(selectDecisions);
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
            ScopedStateObservation sourceObservation = ObserveSource(
                store,
                facts,
                cursor);
            uint[] sourcePreviousDebtObjectIds =
                GetSourcePreviousDebtObjectIds(facts);
            CandidateTarget target = selectTarget(index, facts);
            SaveDecisionPair decisions = selectDecisions(facts);
            ExplicitCandidatePairEvaluation pair =
                ExplicitCandidatePairEvaluator.Evaluate(
                    store,
                    facts,
                    decisions.StayB,
                    decisions.RotateC);

            RotationPolicyStepAttempt attempt =
                ExplicitRotationPolicyStepHarness.TryApplySelected(
                    store,
                    cursor,
                    pair,
                    target);
            CandidateRawObservation selectedObservation;
            CanPrepareAndRotateCertificate? completionCertificate = null;
            switch (attempt) {
                case AppliedStayBPolicyStep appliedStay:
                    Assert.Equal(CandidateTarget.StayB, target);
                    Assert.Same(pair, appliedStay.Evaluation);
                    Assert.Same(appliedStay.Selected, appliedStay.CompletionCertificate.InitialStayB);
                    Assert.Empty(appliedStay.CompletionCertificate.MaintenanceStayBSteps);
                    selectedObservation = appliedStay.Selected.Observation;
                    completionCertificate = appliedStay.CompletionCertificate;
                    cursor = appliedStay.ResultCursor;
                    break;
                case AppliedRotateCPolicyStep appliedRotate:
                    Assert.Equal(CandidateTarget.RotateC, target);
                    Assert.Same(pair, appliedRotate.Evaluation);
                    selectedObservation = appliedRotate.Selected.Observation;
                    cursor = appliedRotate.ResultCursor;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Fixed comparison action was not applied: {attempt.GetType().Name}.");
            }

            ApplyExpectedState(expectedState, saveStep);
            AssertExactState(expectedState, facts.PostLiveStates);
            AssertRuntimeStateAndClosure(store, cursor, expectedState);
            bool actualRotation = attempt is AppliedRotateCPolicyStep;
            RealizedStepObservation observation = new(
                index,
                target,
                sourceObservation,
                ObserveResult(selectedObservation, cursor),
                selectedObservation.ForegroundDomainRecordBytes,
                selectedObservation.MaintenanceDomainRecordBytes,
                GetNonDomainAppendBytes(selectedObservation),
                selectedObservation.Layout.AppendLengthBytes,
                actualRotation,
                completionCertificate is null
                    ? null
                    : ObserveCounterfactualTerminal(completionCertificate));
            steps.Add(new PolicyStep(
                target,
                sourcePreviousDebtObjectIds,
                GetMaintenanceObjectIds(facts, selectedObservation),
                selectedObservation,
                completionCertificate,
                cursor,
                store.FileCount,
                observation));
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

    private static CandidateTarget SelectChangedDebtTarget(
        int index,
        NormalizedSaveFacts _) => ChangedDebtTargets[index];

    private static SaveDecisionPair SelectNoMigrationDecisions(
        NormalizedSaveFacts facts) => SelectDeltaUpdateDecisions(facts, []);

    private static SaveDecisionPair SelectPacedOneDebtDecisions(
        NormalizedSaveFacts facts) => SelectDeltaUpdateDecisions(
        facts,
        SelectSmallestPreviousDebtNoChange(facts));

    private static SaveDecisionPair SelectChangedADebtBaseDecisions(
        NormalizedSaveFacts facts) => CreateDecisions(
        facts,
        facts.Updates.Select(update => new UpdateWriteDecision(
            update.ObjectId,
            IsPreviousDebt(facts, update.Source)
                ? UpdateWriteMode.Base
                : UpdateWriteMode.Delta)),
        []);

    private static SaveDecisionPair SelectDeltaUpdateDecisions(
        NormalizedSaveFacts facts,
        IEnumerable<uint> unchangedMigrationObjectIds) => CreateDecisions(
        facts,
        facts.Updates.Select(static update => new UpdateWriteDecision(
            update.ObjectId,
            UpdateWriteMode.Delta)),
        unchangedMigrationObjectIds);

    private static SaveDecisionPair CreateDecisions(
        NormalizedSaveFacts facts,
        IEnumerable<UpdateWriteDecision> stayBUpdateDecisions,
        IEnumerable<uint> unchangedMigrationObjectIds) => new(
        new StayBSaveDecision(
            stayBUpdateDecisions,
            unchangedMigrationObjectIds),
        new RotateCSaveDecision(
            facts.Updates
                .Where(update => !IsPreviousDebt(facts, update.Source))
                .Select(static update => new UpdateWriteDecision(
                    update.ObjectId,
                    UpdateWriteMode.Delta)),
            []));

    private static bool IsPreviousDebt(
        NormalizedSaveFacts facts,
        SourceObjectFact source) =>
        source.BaseAddress.FileNumber == facts.PreviousFileNumber;

    private static uint[] GetSourcePreviousDebtObjectIds(
        NormalizedSaveFacts facts) => facts.ParentLive.Values
        .Where(source =>
            source.BaseAddress.FileNumber == facts.PreviousFileNumber)
        .Select(static source => source.ObjectId)
        .Order()
        .ToArray();

    private static ScopedStateObservation ObserveSource(
        RbfFileStore store,
        NormalizedSaveFacts facts,
        ProbeRevisionCursor cursor) {
        ScopeValue scope = ScopeValue.From(cursor.FileScope);
        if (scope.PreviousFileNumber != facts.PreviousFileNumber ||
            scope.CurrentFileNumber != facts.CurrentFileNumber ||
            cursor.PublishedRevisionAddress != facts.PublishedRevisionAddress ||
            store.GetFile(scope.CurrentFileNumber).TailOffsetBytes !=
                cursor.CurrentFileTailOffsetBytes) {
            throw new InvalidDataException(
                "Source observation requires facts, cursor, and Store at one exact source.");
        }

        SourceObjectFact[] debt = facts.ParentLive.Values
            .Where(source => source.BaseAddress.FileNumber == scope.PreviousFileNumber)
            .OrderBy(static source => source.ObjectId)
            .ToArray();
        AbsoluteFrameAddress[] previousFrames = facts.ParentLive.Values
            .SelectMany(static source => source.ReconstructionFrameAddresses)
            .Where(address => address.FileNumber == scope.PreviousFileNumber)
            .Distinct()
            .OrderBy(static address => address.FrameTicket.OffsetBytes)
            .ThenBy(static address => address.FrameTicket.LengthBytes)
            .ToArray();
        long previousFrameBytes = previousFrames.Sum(address =>
            (long)store.ReadLayout(address).FrameLengthBytes);
        return new ScopedStateObservation(
            scope,
            new CanonicalObjectIds(debt.Select(static source => source.ObjectId)),
            debt.Sum(static source => (long)source.State.BasePayloadBytes),
            previousFrames.Length,
            previousFrameBytes,
            cursor.CurrentFileTailOffsetBytes,
            GetNextFrameStartSlack(cursor.CurrentFileTailOffsetBytes));
    }

    private static ScopedStateObservation ObserveResult(
        CandidateRawObservation observation,
        ProbeRevisionCursor resultCursor) => ProjectResult(
            observation,
            resultCursor.CurrentFileTailOffsetBytes);

    private static ScopedStateObservation ProjectResult(
        CandidateRawObservation observation,
        long currentTailOffsetBytes) {
        CandidateReconstructionObservation reconstruction =
            observation.PostLiveReconstruction;
        return new ScopedStateObservation(
            ScopeValue.From(reconstruction.ResultScope),
            new CanonicalObjectIds(
                reconstruction.PreviousFileDependentObjectIds),
            reconstruction.PreviousFileDependentBasePayloadBytes,
            reconstruction.PreviousFileUniqueFrameCount,
            reconstruction.PreviousFileFrameBytes,
            currentTailOffsetBytes,
            GetNextFrameStartSlack(currentTailOffsetBytes));
    }

    private static CounterfactualTerminalCObservation
        ObserveCounterfactualTerminal(
            CanPrepareAndRotateCertificate certificate) {
        CandidateRawObservation terminal = certificate.FinalRotateC.Observation;
        return new CounterfactualTerminalCObservation(
            certificate.MaintenanceStayBSteps.Count,
            ScopeValue.FromFacts(terminal.Facts),
            ProjectResult(terminal, terminal.Layout.TailOffsetAfterBytes),
            terminal.ForegroundDomainRecordBytes,
            terminal.MaintenanceDomainRecordBytes,
            GetNonDomainAppendBytes(terminal),
            terminal.Layout.AppendLengthBytes);
    }

    private static int GetNonDomainAppendBytes(
        CandidateRawObservation observation) => checked(
        observation.Layout.AppendLengthBytes -
        observation.ForegroundDomainRecordBytes -
        observation.MaintenanceDomainRecordBytes);

    private static long GetNextFrameStartSlack(long currentTailOffsetBytes) =>
        RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes -
        currentTailOffsetBytes;

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

    private static void AssertCounterfactualTerminalCandidates(
        PolicyRun run,
        int expectedStayCount) {
        Assert.Equal(expectedStayCount + 1, run.Steps.Count);
        foreach (PolicyStep step in run.Steps.Take(expectedStayCount)) {
            CanPrepareAndRotateCertificate certificate = Assert.IsType<
                CanPrepareAndRotateCertificate>(step.CompletionCertificate);
            CandidateRawObservation terminal = certificate.FinalRotateC.Observation;
            CounterfactualTerminalCObservation projected = Assert.IsType<
                CounterfactualTerminalCObservation>(
                    step.Observation.CounterfactualTerminalC);
            Assert.Empty(certificate.MaintenanceStayBSteps);
            Assert.Equal(CandidateTarget.RotateC, terminal.Target);
            Assert.Equal(0, projected.PreparatoryStayCount);
            Assert.Equal(terminal.Layout.AppendLengthBytes, projected.AppendBytes);
            Assert.Equal(3U, terminal.Candidate.FileNumber);
            Assert.Equal(2, step.FileCountAfterApply);
        }

        Assert.All(
            run.Steps.Skip(expectedStayCount),
            step => {
                Assert.Null(step.CompletionCertificate);
                Assert.Null(step.Observation.CounterfactualTerminalC);
            });
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

    private static int CountCounterfactualTerminals(PolicyRun run) => run.Steps
        .Count(static step => step.Observation.CounterfactualTerminalC is not null);

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

    private static RotationRunReduction Reduce(IReadOnlyList<PolicyStep> steps) {
        RealizedStepObservation[] realized = steps
            .Select(static step => step.Observation)
            .ToArray();
        List<RotationEpochObservation> epochs = [];
        int epochStart = 0;
        while (epochStart < realized.Length) {
            ScopeValue scope = realized[epochStart].Source.Scope;
            int epochEnd = epochStart + 1;
            while (epochEnd < realized.Length &&
                realized[epochEnd].Source.Scope == scope) {
                epochEnd++;
            }

            RealizedStepObservation[] epochSteps = realized[epochStart..epochEnd];
            int rotationCount = epochSteps.Count(static step => step.ActualRotation);
            if (rotationCount > 1 ||
                (rotationCount == 1 && !epochSteps[^1].ActualRotation)) {
                throw new InvalidDataException(
                    "A realized Rotate must be the last and only scope-closing Save in its epoch.");
            }

            RealizedByteTotals totals = SumRealizedBytes(epochSteps);
            epochs.Add(new RotationEpochObservation(
                scope,
                epochSteps,
                epochSteps.Length,
                epochSteps.Count(static step =>
                    step.Target == CandidateTarget.StayB),
                rotationCount == 1,
                totals,
                epochSteps.Max(static step => step.AppendBytes),
                rotationCount == 1 ? epochSteps[^1].AppendBytes : null));
            epochStart = epochEnd;
        }

        int[] rotationAppends = realized
            .Where(static step => step.ActualRotation)
            .Select(static step => step.AppendBytes)
            .ToArray();
        return new RotationRunReduction(
            realized,
            epochs.ToArray(),
            SumRealizedBytes(realized),
            realized.Length == 0
                ? 0
                : realized.Max(static step => step.AppendBytes),
            rotationAppends.Length == 0 ? null : rotationAppends.Max(),
            rotationAppends.Length);
    }

    private static RealizedByteTotals SumRealizedBytes(
        IEnumerable<RealizedStepObservation> steps) => new(
        steps.Sum(static step => (long)step.ForegroundDomainRecordBytes),
        steps.Sum(static step => (long)step.MaintenanceDomainRecordBytes),
        steps.Sum(static step => (long)step.NonDomainAppendBytes),
        steps.Sum(static step => (long)step.AppendBytes));

    private static void AssertRunObservationConsistency(PolicyRun run) {
        Assert.Equal(run.Steps.Count, run.Reduction.Steps.Count);
        for (int index = 0; index < run.Steps.Count; index++) {
            PolicyStep step = run.Steps[index];
            RealizedStepObservation observed = step.Observation;
            CandidateRawObservation raw = step.SelectedObservation;
            ScopeValue sourceScope = ScopeValue.FromFacts(raw.Facts);
            ScopeValue resultScope = ScopeValue.From(
                raw.PostLiveReconstruction.ResultScope);

            Assert.Equal(index, observed.StepIndex);
            Assert.Equal(step.Target, observed.Target);
            Assert.Equal(sourceScope, observed.Source.Scope);
            Assert.Equal(resultScope, observed.Result.Scope);
            Assert.Equal(resultScope, ScopeValue.From(step.ResultCursor.FileScope));
            Assert.Equal(raw.Candidate.FileNumber, resultScope.CurrentFileNumber);
            Assert.Equal(
                raw.Layout.TailOffsetAfterBytes,
                observed.Result.CurrentTailOffsetBytes);
            Assert.Equal(
                GetNextFrameStartSlack(observed.Source.CurrentTailOffsetBytes),
                observed.Source.NextFrameStartSlackBytes);
            Assert.Equal(
                GetNextFrameStartSlack(observed.Result.CurrentTailOffsetBytes),
                observed.Result.NextFrameStartSlackBytes);
            Assert.Equal(
                new CanonicalObjectIds(step.SourcePreviousDebtObjectIds),
                observed.Source.PreviousDebtObjectIds);
            Assert.Equal(
                new CanonicalObjectIds(
                    raw.PostLiveReconstruction.PreviousFileDependentObjectIds),
                observed.Result.PreviousDebtObjectIds);
            Assert.Equal(
                raw.PostLiveReconstruction.PreviousFileDependentBasePayloadBytes,
                observed.Result.PreviousDebtBasePayloadBytes);
            Assert.Equal(
                raw.PostLiveReconstruction.PreviousFileUniqueFrameCount,
                observed.Result.PreviousUniqueFrameCount);
            Assert.Equal(
                raw.PostLiveReconstruction.PreviousFileFrameBytes,
                observed.Result.PreviousFrameBytes);
            Assert.Equal(
                raw.Estimate.DomainRecords.Sum(static record =>
                    record.FullRecordBytes),
                observed.ForegroundDomainRecordBytes +
                    observed.MaintenanceDomainRecordBytes);
            Assert.Equal(
                observed.AppendBytes,
                observed.ForegroundDomainRecordBytes +
                    observed.MaintenanceDomainRecordBytes +
                    observed.NonDomainAppendBytes);
            Assert.True(observed.NonDomainAppendBytes >= 0);
            Assert.Equal(step.ActualRotation, observed.ActualRotation);
            Assert.Equal(
                step.Target == CandidateTarget.RotateC,
                observed.ActualRotation);

            if (observed.Target == CandidateTarget.StayB) {
                Assert.Equal(observed.Source.Scope, observed.Result.Scope);
                Assert.Equal(
                    observed.Source.CurrentTailOffsetBytes + observed.AppendBytes,
                    observed.Result.CurrentTailOffsetBytes);
                Assert.True(observed.Result.PreviousDebtObjectIds.IsSubsetOf(
                    observed.Source.PreviousDebtObjectIds));
            } else {
                Assert.Equal(
                    observed.Source.Scope.CurrentFileNumber,
                    observed.Result.Scope.PreviousFileNumber);
                Assert.Equal(
                    checked(observed.Source.Scope.CurrentFileNumber + 1),
                    observed.Result.Scope.CurrentFileNumber);
                Assert.Equal(
                    RbfV040Layout.InitialTailOffsetBytes,
                    raw.Layout.FrameStartOffsetBytes);
            }

            AssertCounterfactualConsistency(step);
            if (index != 0) {
                Assert.Equal(
                    run.Steps[index - 1].Observation.Result,
                    observed.Source);
            }
        }

        AssertReductionConservation(run.Reduction);
    }

    private static void AssertCounterfactualConsistency(PolicyStep step) {
        if (step.CompletionCertificate is null) {
            Assert.Null(step.Observation.CounterfactualTerminalC);
            return;
        }

        CanPrepareAndRotateCertificate certificate = step.CompletionCertificate;
        CandidateRawObservation raw = certificate.FinalRotateC.Observation;
        CounterfactualTerminalCObservation projected = Assert.IsType<
            CounterfactualTerminalCObservation>(
                step.Observation.CounterfactualTerminalC);
        Assert.Equal(CandidateTarget.RotateC, raw.Target);
        Assert.Equal(
            certificate.MaintenanceStayBSteps.Count,
            projected.PreparatoryStayCount);
        AbsoluteFrameAddress expectedTerminalSource =
            certificate.MaintenanceStayBSteps.Count == 0
                ? step.ResultCursor.PublishedRevisionAddress
                : certificate.MaintenanceStayBSteps[^1].Plan.Revision.Address;
        Assert.Equal(
            expectedTerminalSource,
            raw.Facts.PublishedRevisionAddress);
        Assert.Equal(ScopeValue.FromFacts(raw.Facts), projected.SourceScope);
        Assert.Equal(
            ScopeValue.From(raw.PostLiveReconstruction.ResultScope),
            projected.Result.Scope);
        Assert.Equal(
            projected.SourceScope.CurrentFileNumber,
            projected.Result.Scope.PreviousFileNumber);
        Assert.Equal(
            checked(projected.SourceScope.CurrentFileNumber + 1),
            projected.Result.Scope.CurrentFileNumber);
        Assert.Equal(
            RbfV040Layout.InitialTailOffsetBytes,
            raw.Layout.FrameStartOffsetBytes);
        Assert.Equal(
            raw.Layout.TailOffsetAfterBytes,
            projected.Result.CurrentTailOffsetBytes);
        Assert.Equal(
            raw.ForegroundDomainRecordBytes,
            projected.ForegroundDomainRecordBytes);
        Assert.Equal(
            raw.MaintenanceDomainRecordBytes,
            projected.MaintenanceDomainRecordBytes);
        Assert.Equal(GetNonDomainAppendBytes(raw), projected.NonDomainAppendBytes);
        Assert.Equal(raw.Layout.AppendLengthBytes, projected.AppendBytes);
        Assert.Equal(
            projected.AppendBytes,
            projected.ForegroundDomainRecordBytes +
                projected.MaintenanceDomainRecordBytes +
                projected.NonDomainAppendBytes);
    }

    private static void AssertReductionConservation(
        RotationRunReduction reduction) {
        RealizedByteTotals expectedTotals = SumRealizedBytes(reduction.Steps);
        Assert.Equal(expectedTotals, reduction.RealizedTotals);
        Assert.Equal(
            reduction.Steps.Count == 0
                ? 0
                : reduction.Steps.Max(static step => step.AppendBytes),
            reduction.PeakRealizedSaveAppendBytes);
        int[] realizedRotationAppends = reduction.Steps
            .Where(static step => step.ActualRotation)
            .Select(static step => step.AppendBytes)
            .ToArray();
        Assert.Equal(realizedRotationAppends.Length, reduction.RotationCount);
        Assert.Equal(
            realizedRotationAppends.Length == 0
                ? null
                : realizedRotationAppends.Max(),
            reduction.PeakRealizedRotationAppendBytes);
        Assert.Equal(
            reduction.Steps.Count(step => step.Source.Scope != step.Result.Scope),
            reduction.RotationCount);
        Assert.Equal(
            reduction.Epochs.Count(static epoch => epoch.ClosedByRotation),
            reduction.RotationCount);
        Assert.Equal(reduction.Steps.Count, reduction.Epochs.Sum(static epoch =>
            epoch.ObservedSaveCount));
        RealizedStepObservation[] flattenedEpochSteps = reduction.Epochs
            .SelectMany(static epoch => epoch.Steps)
            .ToArray();
        Assert.Equal(reduction.Steps.Count, flattenedEpochSteps.Length);
        for (int index = 0; index < reduction.Steps.Count; index++) {
            Assert.Same(reduction.Steps[index], flattenedEpochSteps[index]);
        }

        Assert.Equal(
            reduction.RealizedTotals,
            new RealizedByteTotals(
                reduction.Epochs.Sum(static epoch =>
                    epoch.RealizedTotals.ForegroundDomainRecordBytes),
                reduction.Epochs.Sum(static epoch =>
                    epoch.RealizedTotals.MaintenanceDomainRecordBytes),
                reduction.Epochs.Sum(static epoch =>
                    epoch.RealizedTotals.NonDomainAppendBytes),
                reduction.Epochs.Sum(static epoch =>
                    epoch.RealizedTotals.AppendBytes)));
        foreach (RotationEpochObservation epoch in reduction.Epochs) {
            Assert.NotEmpty(epoch.Steps);
            Assert.Equal(epoch.ObservedSaveCount, epoch.Steps.Count);
            Assert.All(
                epoch.Steps,
                step => Assert.Equal(epoch.SourceScope, step.Source.Scope));
            Assert.Equal(
                epoch.Steps.Count(static step =>
                    step.Target == CandidateTarget.StayB),
                epoch.StayCount);
            Assert.Equal(SumRealizedBytes(epoch.Steps), epoch.RealizedTotals);
            Assert.Equal(
                epoch.Steps.Max(static step => step.AppendBytes),
                epoch.PeakRealizedSaveAppendBytes);
            Assert.Equal(
                epoch.ClosedByRotation
                    ? epoch.Steps[^1].AppendBytes
                    : null,
                epoch.RotationAppendBytes);
            Assert.Equal(
                epoch.ClosedByRotation,
                epoch.Steps[^1].ActualRotation);
            Assert.DoesNotContain(
                epoch.Steps.Take(epoch.Steps.Count - 1),
                static step => step.ActualRotation);
        }
    }

    private static void AssertSingleObservedEpoch(
        PolicyRun run,
        int expectedSaveCount,
        int expectedStayCount,
        bool expectedClosedByRotation) {
        RotationEpochObservation epoch = Assert.Single(run.Reduction.Epochs);
        Assert.Equal(expectedSaveCount, epoch.ObservedSaveCount);
        Assert.Equal(expectedStayCount, epoch.StayCount);
        Assert.Equal(expectedClosedByRotation, epoch.ClosedByRotation);
        Assert.Equal(
            expectedClosedByRotation ? epoch.Steps[^1].AppendBytes : null,
            epoch.RotationAppendBytes);
    }

    private static string[] DescribeReduction(RotationRunReduction reduction) => [
        .. reduction.Steps.Select(static step =>
            $"step={step.StepIndex}:{step.Target}:" +
            $"source={DescribeScopedState(step.Source)}:" +
            $"result={DescribeScopedState(step.Result)}:" +
            $"bytes={step.ForegroundDomainRecordBytes}/" +
            $"{step.MaintenanceDomainRecordBytes}/" +
            $"{step.NonDomainAppendBytes}/{step.AppendBytes}:" +
            $"rotation={step.ActualRotation}:" +
            $"counterfactual={DescribeCounterfactual(step.CounterfactualTerminalC)}"),
        .. reduction.Epochs.Select(static epoch =>
            $"epoch={epoch.SourceScope}:saves={epoch.ObservedSaveCount}:" +
            $"stays={epoch.StayCount}:closed={epoch.ClosedByRotation}:" +
            $"totals={epoch.RealizedTotals}:" +
            $"peak={epoch.PeakRealizedSaveAppendBytes}:" +
            $"rotation={epoch.RotationAppendBytes}"),
        $"run:totals={reduction.RealizedTotals}:" +
            $"peak={reduction.PeakRealizedSaveAppendBytes}:" +
            $"rotationPeak={reduction.PeakRealizedRotationAppendBytes}:" +
            $"rotations={reduction.RotationCount}",
    ];

    private static string DescribeScopedState(ScopedStateObservation observed) =>
        $"{observed.Scope}:debt={observed.PreviousDebtObjectIds}:" +
        $"base={observed.PreviousDebtBasePayloadBytes}:" +
        $"frames={observed.PreviousUniqueFrameCount}/" +
        $"{observed.PreviousFrameBytes}:tail={observed.CurrentTailOffsetBytes}:" +
        $"slack={observed.NextFrameStartSlackBytes}";

    private static string DescribeCounterfactual(
        CounterfactualTerminalCObservation? observed) => observed is null
        ? "none"
        : $"prep={observed.PreparatoryStayCount}:source={observed.SourceScope}:" +
            $"result={DescribeScopedState(observed.Result)}:" +
            $"bytes={observed.ForegroundDomainRecordBytes}/" +
            $"{observed.MaintenanceDomainRecordBytes}/" +
            $"{observed.NonDomainAppendBytes}/{observed.AppendBytes}";

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

    private static WorkloadTrace CreateChangedDebtTrace() => new(
        scenarioName: "changed-a-debt-base-vs-delta",
        generatorId: "handwritten",
        generatorVersion: 1,
        seed: 0,
        [
            new SaveStep([new UpdateObject(10, 100, 1)]),
            new SaveStep([new UpdateObject(20, 200, 1)]),
            new SaveStep([new UpdateObject(30, 300, 1)]),
            new SaveStep([new CreateObject(1001, 1)]),
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

    private delegate SaveDecisionPair DecisionSelector(
        NormalizedSaveFacts facts);

    private sealed record SaveDecisionPair(
        StayBSaveDecision StayB,
        RotateCSaveDecision RotateC);

    private sealed record PolicyRun(
        WorkloadTrace SourceTrace,
        RbfFileStore Store,
        IReadOnlyList<PolicyStep> Steps) {
        public RotationRunReduction Reduction => Reduce(Steps);
    }

    private enum PolicyRunProgress {
        CompletedTraceWithDeferredPreviousDebt,
        RealizedRotation,
    }

    private sealed record PolicyStep(
        CandidateTarget Target,
        IReadOnlyList<uint> SourcePreviousDebtObjectIds,
        IReadOnlyList<uint> MaintenanceObjectIds,
        CandidateRawObservation SelectedObservation,
        CanPrepareAndRotateCertificate? CompletionCertificate,
        ProbeRevisionCursor ResultCursor,
        int FileCountAfterApply,
        RealizedStepObservation Observation) {
        public bool ActualRotation => Observation.ActualRotation;
    }

    private readonly record struct ScopeValue(
        uint PreviousFileNumber,
        uint CurrentFileNumber) {
        public static ScopeValue From(FileScope scope) => new(
            scope.PreviousFileNumber ?? throw new InvalidDataException(
                "Rotation observations require a two-file scope."),
            scope.CurrentFileNumber);

        public static ScopeValue FromFacts(NormalizedSaveFacts facts) => new(
            facts.PreviousFileNumber,
            facts.CurrentFileNumber);

        public override string ToString() =>
            $"{PreviousFileNumber}/{CurrentFileNumber}";
    }

    private sealed class CanonicalObjectIds : IEquatable<CanonicalObjectIds> {
        private readonly uint[] _values;

        public CanonicalObjectIds(IEnumerable<uint> values) {
            ArgumentNullException.ThrowIfNull(values);
            _values = values.Distinct().Order().ToArray();
        }

        public int Count => _values.Length;

        public bool IsSubsetOf(CanonicalObjectIds other) {
            ArgumentNullException.ThrowIfNull(other);
            return _values.All(other._values.Contains);
        }

        public bool Equals(CanonicalObjectIds? other) => other is not null &&
            _values.SequenceEqual(other._values);

        public override bool Equals(object? obj) =>
            obj is CanonicalObjectIds other && Equals(other);

        public override int GetHashCode() {
            HashCode hash = new();
            foreach (uint value in _values) {
                hash.Add(value);
            }

            return hash.ToHashCode();
        }

        public override string ToString() => string.Join(',', _values);
    }

    private sealed record ScopedStateObservation(
        ScopeValue Scope,
        CanonicalObjectIds PreviousDebtObjectIds,
        long PreviousDebtBasePayloadBytes,
        int PreviousUniqueFrameCount,
        long PreviousFrameBytes,
        long CurrentTailOffsetBytes,
        long NextFrameStartSlackBytes);

    private sealed record CounterfactualTerminalCObservation(
        int PreparatoryStayCount,
        ScopeValue SourceScope,
        ScopedStateObservation Result,
        int ForegroundDomainRecordBytes,
        int MaintenanceDomainRecordBytes,
        int NonDomainAppendBytes,
        int AppendBytes);

    private sealed record RealizedStepObservation(
        int StepIndex,
        CandidateTarget Target,
        ScopedStateObservation Source,
        ScopedStateObservation Result,
        int ForegroundDomainRecordBytes,
        int MaintenanceDomainRecordBytes,
        int NonDomainAppendBytes,
        int AppendBytes,
        bool ActualRotation,
        CounterfactualTerminalCObservation? CounterfactualTerminalC);

    private readonly record struct RealizedByteTotals(
        long ForegroundDomainRecordBytes,
        long MaintenanceDomainRecordBytes,
        long NonDomainAppendBytes,
        long AppendBytes);

    private sealed record RotationEpochObservation(
        ScopeValue SourceScope,
        IReadOnlyList<RealizedStepObservation> Steps,
        int ObservedSaveCount,
        int StayCount,
        bool ClosedByRotation,
        RealizedByteTotals RealizedTotals,
        int PeakRealizedSaveAppendBytes,
        int? RotationAppendBytes);

    private sealed record RotationRunReduction(
        IReadOnlyList<RealizedStepObservation> Steps,
        IReadOnlyList<RotationEpochObservation> Epochs,
        RealizedByteTotals RealizedTotals,
        int PeakRealizedSaveAppendBytes,
        int? PeakRealizedRotationAppendBytes,
        int RotationCount);
}
