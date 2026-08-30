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

            ApplyExpectedState(expectedState, step);
            AssertExactState(expectedState, facts.PostLiveStates);
            AssertRuntimeStateAndClosure(session.Store, session.Cursor, expectedState);
        }

        AdmittedEvaluatorRun admitted = Assert.IsType<AdmittedEvaluatorRun>(
            session.Complete());
        AssertFixedHorizonScope(
            admitted.FinalCursor,
            expectedPreviousFileNumber,
            expectedCurrentFileNumber);
        AssertDirectFixedHorizonSettlement(admitted.Settlement);
        AssertRuntimeStateAndClosure(
            session.Store,
            admitted.FinalCursor,
            expectedState);

        return new TerminalLiabilityEpoch(
            session.Store,
            admitted.FinalCursor,
            admitted,
            ProjectFixedHorizonRaw(admitted),
            sourceDebtByStep,
            migrationObjectIdsByStep,
            GetFixedHorizonPreviousDebtObjectIds(
                session.Store,
                admitted.FinalCursor));
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
        uint[] DebtAfterSettlement);

}
