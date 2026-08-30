using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Policies;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Benchmarking;

internal static class BenchmarkV1Runner {
    public static BenchmarkV1BatchRun Run(BenchmarkV1BatchDefinition definition) {
        ArgumentNullException.ThrowIfNull(definition);
        ValidateDefinitionClosure(definition);

        BenchmarkCaseReportV1[] cases = definition.Cases
            .Select(RunCase)
            .ToArray();
        BenchmarkReportV1 report = new(definition.Manifest, cases);
        return new BenchmarkV1BatchRun(definition, report);
    }

    private static BenchmarkCaseReportV1 RunCase(
        BenchmarkV1CaseDefinition definition) {
        BenchmarkV1CaseExecution execution = ExecuteCase(definition);
        BenchmarkCaseManifestV1 manifestCase = definition.ManifestCase;
        return new BenchmarkCaseReportV1(
            manifestCase.CaseId,
            manifestCase.ResolvedTraceSha256,
            BenchmarkOutcomeReportV1.Project(execution.Outcome));
    }

    internal static BenchmarkV1CaseExecution ExecuteCase(
        BenchmarkV1CaseDefinition definition) {
        ArgumentNullException.ThrowIfNull(definition);
        WorkloadTrace trace = definition.Trace;
        BenchmarkCaseManifestV1 manifestCase = definition.ManifestCase;
        BenchmarkV1BootstrappedSource source =
            BenchmarkV1SourceBootstrap.Create(trace);
        EvaluatorV1Session session = new(
            source.Store,
            source.Cursor,
            manifestCase.EvaluatedWorkloadStepCount);

        for (int traceStepIndex = manifestCase.BootstrapStepCount;
            traceStepIndex < trace.Steps.Count;
            traceStepIndex++) {
            NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
                session.Store,
                session.Cursor.FileScope.CurrentFileNumber,
                session.Cursor.PublishedRevisionAddress,
                trace.Steps[traceStepIndex]);
            BenchmarkV1StepSelection selection =
                BenchmarkV1TreatmentSelector.Select(
                    manifestCase.TargetTreatment,
                    manifestCase.DecisionTreatment,
                    facts);
            ExplicitCandidatePairEvaluation pair =
                ExplicitCandidatePairEvaluator.Evaluate(
                    session.Store,
                    facts,
                    selection.StayB,
                    selection.RotateC);
            RotationPolicyStepAttempt attempt =
                session.ApplySelectedWorkloadCommit(
                    pair,
                    selection.Target);

            if (attempt is AppliedStayBPolicyStep or AppliedRotateCPolicyStep) {
                continue;
            }

            if (attempt is SelectedPolicyCandidateCapacityRejected or
                StayBPolicyCompletionRejectedUnproven) {
                break;
            }

            throw new InvalidDataException(
                "The evaluator returned an unsupported workload attempt kind.");
        }

        EvaluatorRunOutcome outcome = session.Complete();
        if (outcome is AdmittedEvaluatorRun admitted) {
            ValidateFinalState(session.Store, admitted, trace);
        }

        return new BenchmarkV1CaseExecution(session.Store, outcome);
    }

    private static void ValidateDefinitionClosure(
        BenchmarkV1BatchDefinition definition) {
        BenchmarkCaseManifestV1[] manifestCases = definition.Manifest.Cases.ToArray();
        BenchmarkCaseManifestV1[] resolvedCases = definition.Cases
            .Select(static benchmarkCase => benchmarkCase.ManifestCase)
            .ToArray();
        if (!manifestCases.SequenceEqual(resolvedCases)) {
            throw new InvalidDataException(
                "The benchmark manifest and resolved case definitions have diverged.");
        }
    }

    private static void ValidateFinalState(
        RbfFileStore store,
        AdmittedEvaluatorRun admitted,
        WorkloadTrace trace) {
        IReadOnlyDictionary<uint, LogicalObjectState> expected =
            WorkloadReplayer.Replay(trace);
        IReadOnlyDictionary<uint, LogicalObjectState> actual =
            PhysicalStateOracle.Materialize(
                store,
                ObjectVersionDictionaryReader.MaterializeLive(
                    store,
                    admitted.FinalCursor.PublishedRevisionAddress).Bindings);
        if (expected.Count != actual.Count || expected.Any(pair =>
            !actual.TryGetValue(pair.Key, out LogicalObjectState state) ||
            state != pair.Value)) {
            throw new InvalidDataException(
                "An admitted benchmark run does not match full-trace logical replay.");
        }
    }
}

internal sealed record BenchmarkV1CaseExecution(
    RbfFileStore Store,
    EvaluatorRunOutcome Outcome);
