using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Policies;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed partial class RotationPolicyComparisonTests {
    [Fact]
    public void Fixed_cadence_two_epoch_witness_carries_terminal_liability_into_next_epoch() {
        PolicySource source = CreateSource();
        SaveStep[] firstEpoch = [
            new SaveStep([new CreateObject(1001, 1)]),
            new SaveStep([
                new RemoveObject(1001),
                new CreateObject(1002, 1),
            ]),
            new SaveStep([new RemoveObject(1002)]),
        ];
        SaveStep[] secondEpoch = [
            new SaveStep([new CreateObject(1101, 1)]),
            new SaveStep([
                new RemoveObject(1101),
                new CreateObject(1102, 1),
            ]),
            new SaveStep([new RemoveObject(1102)]),
        ];

        TerminalLiabilityRun control = RunFixedCadenceTreatment(
            source,
            firstEpoch,
            secondEpoch,
            SelectDeltaNoMigrationDecisions);
        TerminalLiabilityRun paced = RunFixedCadenceTreatment(
            source,
            firstEpoch,
            secondEpoch,
            SelectDeltaPacedOneDebtDecisions);

        Assert.Equal(
            [
                new uint[] { 10, 20, 30 },
                new uint[] { 10, 20, 30 },
                new uint[] { 10, 20, 30 },
            ],
            control.FirstEpoch.SourceDebtByStep);
        Assert.All(
            control.FirstEpoch.MigrationObjectIdsByStep,
            static objectIds => Assert.Empty(objectIds));
        Assert.Empty(control.FirstEpoch.DebtAfterSettlement);
        Assert.Equal(
            [Array.Empty<uint>(), Array.Empty<uint>(), Array.Empty<uint>()],
            control.SecondEpoch.SourceDebtByStep);
        Assert.All(
            control.SecondEpoch.MigrationObjectIdsByStep,
            static objectIds => Assert.Empty(objectIds));

        Assert.Equal(
            [
                new uint[] { 10, 20, 30 },
                new uint[] { 20, 30 },
                new uint[] { 30 },
            ],
            paced.FirstEpoch.SourceDebtByStep);
        Assert.Equal(
            [new uint[] { 10 }, new uint[] { 20 }, new uint[] { 30 }],
            paced.FirstEpoch.MigrationObjectIdsByStep);
        Assert.Equal([10U, 20U, 30U], paced.FirstEpoch.DebtAfterSettlement);
        Assert.Equal(
            paced.FirstEpoch.SourceDebtByStep,
            paced.SecondEpoch.SourceDebtByStep);
        Assert.Equal(
            paced.FirstEpoch.MigrationObjectIdsByStep,
            paced.SecondEpoch.MigrationObjectIdsByStep);

        Assert.Equal([10U, 20U, 30U], control.FinalDebtObjectIds);
        Assert.Equal(control.FinalDebtObjectIds, paced.FinalDebtObjectIds);
        AssertExactState(source.InitialExpectedState, control.FinalState);
        AssertExactState(control.FinalState, paced.FinalState);

        Assert.Equal(
            new FixedHorizonRawVector(4, 796, 664, 664, 656),
            control.FirstEpoch.Raw);
        Assert.Equal(
            new FixedHorizonRawVector(4, 188, 52, 800, 700),
            control.SecondEpoch.Raw);
        Assert.Equal(
            new FixedHorizonRawVector(8, 984, 664, 800, 700),
            control.CombinedRaw);
        Assert.Equal(
            new FixedHorizonRawVector(4, 812, 348, 800, 792),
            paced.FirstEpoch.Raw);
        Assert.Equal(
            new FixedHorizonRawVector(4, 812, 348, 812, 792),
            paced.SecondEpoch.Raw);
        Assert.Equal(
            new FixedHorizonRawVector(8, 1624, 348, 812, 792),
            paced.CombinedRaw);
    }

    [Fact]
    public void Fixed_cadence_common_third_epoch_exposes_retained_frame_layout() {
        PolicySource source = CreateSource();
        SaveStep[] firstEpoch = CreateFixedCadenceSteps(1001, 1002);
        SaveStep[] secondEpoch = CreateFixedCadenceSteps(1101, 1102);
        SaveStep[] commonThirdEpoch = CreateFixedCadenceSteps(1201, 1202);

        TerminalLiabilityRun control = RunFixedCadenceTreatment(
            source,
            firstEpoch,
            secondEpoch,
            SelectDeltaNoMigrationDecisions);
        TerminalLiabilityRun paced = RunFixedCadenceTreatment(
            source,
            firstEpoch,
            secondEpoch,
            SelectDeltaPacedOneDebtDecisions);

        AssertExactState(control.FinalState, paced.FinalState);
        AssertFixedHorizonScope(control.SecondEpoch.FinalCursor, 3, 4);
        AssertFixedHorizonScope(paced.SecondEpoch.FinalCursor, 3, 4);
        Assert.Equal([10U, 20U, 30U], control.FinalDebtObjectIds);
        Assert.Equal(control.FinalDebtObjectIds, paced.FinalDebtObjectIds);
        AssertAlignedFixedCadenceContinuationInputs(control.SecondEpoch);
        AssertAlignedFixedCadenceContinuationInputs(paced.SecondEpoch);

        FixedCadenceEndpointLayout controlLayout = ObserveFixedCadenceEndpointLayout(
            control.SecondEpoch);
        FixedCadenceEndpointLayout pacedLayout = ObserveFixedCadenceEndpointLayout(
            paced.SecondEpoch);
        Assert.Equal(
            control.SecondEpoch.FinalCursor.CurrentFileTailOffsetBytes,
            paced.SecondEpoch.FinalCursor.CurrentFileTailOffsetBytes);
        Assert.Equal(
            control.SecondEpoch.FinalCursor.PublishedRevisionAddress,
            paced.SecondEpoch.FinalCursor.PublishedRevisionAddress);
        Assert.Equal(52, control.SecondEpoch.FinalCursor.CurrentFileTailOffsetBytes);
        Assert.Equal([10U, 20U, 30U], Assert.Single(
            controlLayout.ObjectFrameGroups));
        Assert.Equal(3, pacedLayout.ObjectFrameGroups.Count);
        Assert.All(
            pacedLayout.ObjectFrameGroups,
            static objectIds => Assert.Single(objectIds));
        Assert.NotEqual(
            controlLayout.ColdHeadRead.ObjectReconstructionFrameBytes,
            pacedLayout.ColdHeadRead.ObjectReconstructionFrameBytes);
        Assert.Equal(700, controlLayout.ColdHeadRead.UniqueFrameBytes);
        Assert.Equal(792, pacedLayout.ColdHeadRead.UniqueFrameBytes);
        Assert.Equal(44, controlLayout.ColdHeadRead.DictionaryFrameBytes);
        Assert.Equal(
            controlLayout.ColdHeadRead.DictionaryFrameBytes,
            pacedLayout.ColdHeadRead.DictionaryFrameBytes);
        Assert.Equal(656, controlLayout.ColdHeadRead.ObjectReconstructionFrameBytes);
        Assert.Equal(748, pacedLayout.ColdHeadRead.ObjectReconstructionFrameBytes);

        TerminalLiabilityEpoch controlThird = RunFixedCadenceEpoch(
            control.SecondEpoch.Store,
            control.SecondEpoch.FinalCursor,
            commonThirdEpoch,
            SelectDeltaPacedOneDebtDecisions,
            new Dictionary<uint, LogicalObjectState>(control.FinalState),
            expectedPreviousFileNumber: 4,
            expectedCurrentFileNumber: 5);
        TerminalLiabilityEpoch pacedThird = RunFixedCadenceEpoch(
            paced.SecondEpoch.Store,
            paced.SecondEpoch.FinalCursor,
            commonThirdEpoch,
            SelectDeltaPacedOneDebtDecisions,
            new Dictionary<uint, LogicalObjectState>(paced.FinalState),
            expectedPreviousFileNumber: 4,
            expectedCurrentFileNumber: 5);

        uint[][] expectedSourceDebt = [
            [10U, 20U, 30U],
            [20U, 30U],
            [30U],
        ];
        uint[][] expectedMigrations = [[10U], [20U], [30U]];
        Assert.Equal(expectedSourceDebt, controlThird.SourceDebtByStep);
        Assert.Equal(expectedSourceDebt, pacedThird.SourceDebtByStep);
        Assert.Equal(expectedMigrations, controlThird.MigrationObjectIdsByStep);
        Assert.Equal(expectedMigrations, pacedThird.MigrationObjectIdsByStep);
        Assert.Equal([10U, 20U, 30U], controlThird.DebtAfterSettlement);
        Assert.Equal(
            controlThird.DebtAfterSettlement,
            pacedThird.DebtAfterSettlement);

        Assert.Equal(
            controlThird.WorkloadCommitWriteBytes,
            pacedThird.WorkloadCommitWriteBytes);
        Assert.Equal(
            controlThird.SettlementCommitWriteBytes,
            pacedThird.SettlementCommitWriteBytes);
        Assert.Equal(controlThird.Raw, pacedThird.Raw);
        Assert.Equal(
            new FixedHorizonRawVector(4, 812, 348, 812, 792),
            controlThird.Raw);
        Assert.Equal(
            [152L, 260L, 348L],
            controlThird.WorkloadCommitWriteBytes);
        Assert.Equal(52, controlThird.SettlementCommitWriteBytes);
        Assert.Equal(
            [1, 1, 0],
            controlThird.WorkloadCandidateObservations.Select(static observation =>
                observation.PostLiveReconstruction.PreviousFileUniqueFrameCount));
        Assert.Equal(
            [2, 1, 0],
            pacedThird.WorkloadCandidateObservations.Select(static observation =>
                observation.PostLiveReconstruction.PreviousFileUniqueFrameCount));
        Assert.Equal(
            [848L, 1104L, 792L],
            controlThird.ColdHeadReadsAfterWorkloadSteps.Select(static read =>
                read.UniqueFrameBytes));
        Assert.Equal(
            [792L, 792L, 792L],
            pacedThird.ColdHeadReadsAfterWorkloadSteps.Select(static read =>
                read.UniqueFrameBytes));
        Assert.Equal(
            [804L, 1060L, 748L],
            controlThird.ColdHeadReadsAfterWorkloadSteps.Select(static read =>
                read.ObjectReconstructionFrameBytes));
        Assert.Equal(
            [748L, 748L, 748L],
            pacedThird.ColdHeadReadsAfterWorkloadSteps.Select(static read =>
                read.ObjectReconstructionFrameBytes));
        Assert.Equal(
            controlThird.ColdHeadReadsAfterWorkloadSteps[^1].UniqueFrameBytes,
            pacedThird.ColdHeadReadsAfterWorkloadSteps[^1].UniqueFrameBytes);
        Assert.Equal(
            controlThird.Raw.FinalColdHeadReadBytes,
            pacedThird.Raw.FinalColdHeadReadBytes);

        FixedHorizonRawVector controlCombined = ConcatenateFixedHorizonSegments(
            control.FirstEpoch.Admitted,
            control.SecondEpoch.Admitted,
            controlThird.Admitted);
        FixedHorizonRawVector pacedCombined = ConcatenateFixedHorizonSegments(
            paced.FirstEpoch.Admitted,
            paced.SecondEpoch.Admitted,
            pacedThird.Admitted);
        Assert.Equal(
            new FixedHorizonRawVector(12, 1796, 664, 812, 792),
            controlCombined);
        Assert.Equal(
            new FixedHorizonRawVector(12, 2436, 348, 812, 792),
            pacedCombined);
        long sourceTailBytes = FixedHorizonTotalTailBytes(source.Store);
        Assert.Equal(
            controlCombined.TotalPhysicalWriteBytes,
            checked(FixedHorizonTotalTailBytes(controlThird.Store) -
                sourceTailBytes));
        Assert.Equal(
            pacedCombined.TotalPhysicalWriteBytes,
            checked(FixedHorizonTotalTailBytes(pacedThird.Store) -
                sourceTailBytes));
    }

    private static SaveStep[] CreateFixedCadenceSteps(
        uint firstObjectId,
        uint secondObjectId) => [
        new SaveStep([new CreateObject(firstObjectId, 1)]),
        new SaveStep([
            new RemoveObject(firstObjectId),
            new CreateObject(secondObjectId, 1),
        ]),
        new SaveStep([new RemoveObject(secondObjectId)]),
    ];

    private static TerminalLiabilityRun RunFixedCadenceTreatment(
        PolicySource source,
        IReadOnlyList<SaveStep> firstEpochSteps,
        IReadOnlyList<SaveStep> secondEpochSteps,
        DecisionSelector selectDecisions) {
        long sourceTailBytes = FixedHorizonTotalTailBytes(source.Store);
        Dictionary<uint, LogicalObjectState> expectedState = new(
            source.InitialExpectedState);

        TerminalLiabilityEpoch firstEpoch = RunFixedCadenceEpoch(
            source.Store,
            CreateCursor(source),
            firstEpochSteps,
            selectDecisions,
            expectedState,
            expectedPreviousFileNumber: 2,
            expectedCurrentFileNumber: 3);
        TerminalLiabilityEpoch secondEpoch = RunFixedCadenceEpoch(
            firstEpoch.Store,
            firstEpoch.FinalCursor,
            secondEpochSteps,
            selectDecisions,
            expectedState,
            expectedPreviousFileNumber: 3,
            expectedCurrentFileNumber: 4);

        FixedHorizonRawVector combined = ConcatenateFixedHorizonSegments(
            firstEpoch.Admitted,
            secondEpoch.Admitted);
        Assert.Equal(8, combined.RealizedCommitCount);
        Assert.Equal(
            combined.TotalPhysicalWriteBytes,
            checked(FixedHorizonTotalTailBytes(secondEpoch.Store) - sourceTailBytes));
        AssertRuntimeStateAndClosure(
            secondEpoch.Store,
            secondEpoch.FinalCursor,
            source.InitialExpectedState);

        IReadOnlyDictionary<uint, LogicalObjectState> finalState =
            PhysicalStateOracle.Materialize(
                secondEpoch.Store,
                ObjectVersionDictionaryReader.MaterializeLive(
                    secondEpoch.Store,
                    secondEpoch.FinalCursor.PublishedRevisionAddress).Bindings);
        return new TerminalLiabilityRun(
            firstEpoch,
            secondEpoch,
            combined,
            GetFixedHorizonPreviousDebtObjectIds(
                secondEpoch.Store,
                secondEpoch.FinalCursor),
            finalState);
    }

    private static TerminalLiabilityEpoch RunFixedCadenceEpoch(
        RbfFileStore sourceStore,
        ProbeRevisionCursor initialCursor,
        IReadOnlyList<SaveStep> steps,
        DecisionSelector selectDecisions,
        Dictionary<uint, LogicalObjectState> expectedState,
        uint expectedPreviousFileNumber,
        uint expectedCurrentFileNumber) {
        EvaluatorV1Session session = new(
            sourceStore,
            initialCursor,
            totalWorkloadStepCount: steps.Count);
        List<uint[]> sourceDebtByStep = [];
        List<uint[]> migrationObjectIdsByStep = [];
        List<long> workloadCommitWriteBytes = [];
        List<FinalColdHeadReadObservation> coldHeadReadsAfterWorkloadSteps = [];
        List<CandidateRawObservation> workloadCandidateObservations = [];
        long segmentSourceTailBytes = FixedHorizonTotalTailBytes(sourceStore);

        foreach (SaveStep step in steps) {
            NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
                session.Store,
                session.Cursor.FileScope.CurrentFileNumber,
                session.Cursor.PublishedRevisionAddress,
                step);
            sourceDebtByStep.Add(GetSourcePreviousDebtObjectIds(facts));
            SaveDecisionPair decisions = selectDecisions(facts);
            ExplicitCandidatePairEvaluation pair =
                ExplicitCandidatePairEvaluator.Evaluate(
                    session.Store,
                    facts,
                    decisions.StayB,
                    decisions.RotateC);

            long commitSourceTailBytes = FixedHorizonTotalTailBytes(session.Store);
            RotationPolicyStepAttempt attempt = session.ApplySelectedWorkloadCommit(
                pair,
                CandidateTarget.StayB);
            AppliedStayBPolicyStep applied = Assert.IsType<AppliedStayBPolicyStep>(
                attempt);
            Assert.Same(pair, applied.Evaluation);
            Assert.Same(
                applied.Selected,
                applied.CompletionCertificate.InitialStayB);
            migrationObjectIdsByStep.Add([
                .. applied.Selected.Plan.Decision.UnchangedMigrationObjectIds,
            ]);
            workloadCommitWriteBytes.Add(checked(
                FixedHorizonTotalTailBytes(session.Store) - commitSourceTailBytes));
            coldHeadReadsAfterWorkloadSteps.Add(FinalColdHeadReadMeasurer.Measure(
                session.Store,
                session.Cursor.PublishedRevisionAddress));
            workloadCandidateObservations.Add(applied.Selected.Observation);

            ApplyExpectedState(expectedState, step);
            AssertExactState(expectedState, facts.PostLiveStates);
            AssertRuntimeStateAndClosure(session.Store, session.Cursor, expectedState);
        }

        long settlementSourceTailBytes = FixedHorizonTotalTailBytes(session.Store);
        AdmittedEvaluatorRun admitted = Assert.IsType<AdmittedEvaluatorRun>(
            session.Complete());
        long settlementCommitWriteBytes = checked(
            FixedHorizonTotalTailBytes(session.Store) - settlementSourceTailBytes);
        AssertFixedHorizonScope(
            admitted.FinalCursor,
            expectedPreviousFileNumber,
            expectedCurrentFileNumber);
        AssertDirectFixedHorizonSettlement(admitted.Settlement);
        AssertRuntimeStateAndClosure(
            session.Store,
            admitted.FinalCursor,
            expectedState);
        Assert.Equal(
            steps.Count + 1,
            admitted.Metrics.RealizedCommitCount);
        Assert.Equal(
            admitted.Metrics.TotalPhysicalWriteBytes,
            checked(FixedHorizonTotalTailBytes(session.Store) -
                segmentSourceTailBytes));

        return new TerminalLiabilityEpoch(
            session.Store,
            admitted.FinalCursor,
            admitted,
            ProjectFixedHorizonRaw(admitted),
            sourceDebtByStep,
            migrationObjectIdsByStep,
            GetFixedHorizonPreviousDebtObjectIds(
                session.Store,
                admitted.FinalCursor),
            workloadCommitWriteBytes.ToArray(),
            settlementCommitWriteBytes,
            coldHeadReadsAfterWorkloadSteps.ToArray(),
            workloadCandidateObservations.ToArray());
    }

    private static void AssertAlignedFixedCadenceContinuationInputs(
        TerminalLiabilityEpoch endpoint) {
        NormalizedSaveFacts facts = SaveStepNormalizer.NormalizeMaintenanceOnly(
            endpoint.Store,
            endpoint.FinalCursor.FileScope.CurrentFileNumber,
            endpoint.FinalCursor.PublishedRevisionAddress);
        Assert.Equal([10U, 20U, 30U], facts.NoChanges.Select(static fact =>
            fact.ObjectId).ToArray());
        Assert.All(facts.NoChanges, fact => {
            Assert.Equal(fact.Source.HeadAddress, fact.Source.BaseAddress);
            Assert.Equal(
                fact.Source.State.BasePayloadBytes,
                fact.Source.HeadReconstructionObjectPayloadBytes);
            Assert.Equal(
                endpoint.FinalCursor.FileScope.PreviousFileNumber,
                fact.Source.BaseAddress.FileNumber);
        });
    }

    private static FixedCadenceEndpointLayout ObserveFixedCadenceEndpointLayout(
        TerminalLiabilityEpoch endpoint) {
        NormalizedSaveFacts facts = SaveStepNormalizer.NormalizeMaintenanceOnly(
            endpoint.Store,
            endpoint.FinalCursor.FileScope.CurrentFileNumber,
            endpoint.FinalCursor.PublishedRevisionAddress);
        uint[][] groups = facts.NoChanges
            .GroupBy(static fact => fact.Source.BaseAddress)
            .Select(static group => group
                .Select(static fact => fact.ObjectId)
                .Order()
                .ToArray())
            .OrderBy(static group => group[0])
            .ToArray();
        return new FixedCadenceEndpointLayout(
            groups,
            FinalColdHeadReadMeasurer.Measure(
                endpoint.Store,
                endpoint.FinalCursor.PublishedRevisionAddress));
    }

    private sealed record TerminalLiabilityRun(
        TerminalLiabilityEpoch FirstEpoch,
        TerminalLiabilityEpoch SecondEpoch,
        FixedHorizonRawVector CombinedRaw,
        uint[] FinalDebtObjectIds,
        IReadOnlyDictionary<uint, LogicalObjectState> FinalState);

    private sealed record TerminalLiabilityEpoch(
        RbfFileStore Store,
        ProbeRevisionCursor FinalCursor,
        AdmittedEvaluatorRun Admitted,
        FixedHorizonRawVector Raw,
        IReadOnlyList<uint[]> SourceDebtByStep,
        IReadOnlyList<uint[]> MigrationObjectIdsByStep,
        uint[] DebtAfterSettlement,
        IReadOnlyList<long> WorkloadCommitWriteBytes,
        long SettlementCommitWriteBytes,
        IReadOnlyList<FinalColdHeadReadObservation>
            ColdHeadReadsAfterWorkloadSteps,
        IReadOnlyList<CandidateRawObservation> WorkloadCandidateObservations);

    private sealed record FixedCadenceEndpointLayout(
        IReadOnlyList<uint[]> ObjectFrameGroups,
        FinalColdHeadReadObservation ColdHeadRead);

}
