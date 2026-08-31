using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atelia.TwoLegRotationProbe.Baselines;
using Atelia.TwoLegRotationProbe.Benchmarking;
using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class BenchmarkV1JsonTests {
    [Fact]
    public void Manifest_json_is_deterministic_canonical_and_sorts_cases() {
        BenchmarkManifestV1 manifest = Manifest([
            Case("z-last", ulong.MaxValue),
            Case("a-first", 1),
        ]);

        byte[] first = BenchmarkV1Json.WriteManifest(manifest);
        byte[] second = BenchmarkV1Json.WriteManifest(manifest);
        string json = System.Text.Encoding.UTF8.GetString(first);

        Assert.Equal(first, second);
        Assert.EndsWith("\n", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", json, StringComparison.Ordinal);
        Assert.False(json.EndsWith("\n\n", StringComparison.Ordinal));
        Assert.Contains(
            "\"seed\":\"ffffffffffffffff\"",
            json,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"schema\":{\"id\":\"two-leg-benchmark-manifest\",\"version\":2}",
            json,
            StringComparison.Ordinal);
        Assert.Contains("\"selectionProfile\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"targetTreatment\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"decisionTreatment\":", json, StringComparison.Ordinal);
        Assert.True(
            json.IndexOf("\"caseId\":\"a-first\"", StringComparison.Ordinal) <
            json.IndexOf("\"caseId\":\"z-last\"", StringComparison.Ordinal));
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(first)).ToLowerInvariant(),
            BenchmarkV1Json.ComputeManifestSha256(manifest));
    }

    [Fact]
    public void Case_step_counts_allow_zero_evaluated_workload_but_reject_mismatch() {
        BenchmarkCaseManifestV1 zeroWorkload = Case(
            "zero-workload",
            seed: 0,
            bootstrapStepCount: 1,
            traceStepCount: 1,
            evaluatedWorkloadStepCount: 0);

        Assert.Equal(0, zeroWorkload.EvaluatedWorkloadStepCount);
        Assert.Throws<ArgumentException>(() => Case(
            "mismatch",
            seed: 0,
            bootstrapStepCount: 1,
            traceStepCount: 2,
            evaluatedWorkloadStepCount: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Case(
            "missing-bootstrap",
            seed: 0,
            bootstrapStepCount: 0,
            traceStepCount: 1,
            evaluatedWorkloadStepCount: 1));
        Assert.Throws<ArgumentException>(() => new BenchmarkCaseManifestV1(
            "wrong-source",
            Component("some-other-source"),
            Component("trace"),
            Component("generator"),
            seed: 0,
            resolvedTraceSha256:
                "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            bootstrapStepCount: 1,
            traceStepCount: 1,
            evaluatedWorkloadStepCount: 0,
            Component("selection-profile")));
    }

    [Fact]
    public void Expanded_trace_hash_is_stable_and_covers_seed_and_expanded_changes() {
        WorkloadTrace first = Trace(seed: 42, updatedBaseBytes: 25);
        WorkloadTrace equal = Trace(seed: 42, updatedBaseBytes: 25);
        WorkloadTrace changedSeed = Trace(seed: 43, updatedBaseBytes: 25);
        WorkloadTrace changedPayload = Trace(seed: 42, updatedBaseBytes: 26);

        Assert.Equal(
            WorkloadTraceSha256.Compute(first),
            WorkloadTraceSha256.Compute(equal));
        Assert.NotEqual(
            WorkloadTraceSha256.Compute(first),
            WorkloadTraceSha256.Compute(changedSeed));
        Assert.NotEqual(
            WorkloadTraceSha256.Compute(first),
            WorkloadTraceSha256.Compute(changedPayload));

        string expanded = System.Text.Encoding.UTF8.GetString(
            BenchmarkV1Json.WriteExpandedTrace(first));
        Assert.Contains(
            "\"seed\":\"000000000000002a\"",
            expanded,
            StringComparison.Ordinal);
        Assert.Contains(
            "{\"kind\":\"create\",\"objectId\":1,\"basePayloadBytes\":10}",
            expanded,
            StringComparison.Ordinal);
        Assert.Contains(
            "{\"kind\":\"update\",\"objectId\":1," +
            "\"resultBasePayloadBytes\":25,\"deltaPayloadBytes\":7}",
            expanded,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Manifest_hash_changes_when_the_resolved_trace_changes() {
        BenchmarkV1CaseDefinition firstCase = new(
            "same-case",
            Component("hash-test"),
            Trace(seed: 42, updatedBaseBytes: 25),
            BenchmarkV1Baselines.ReadAmplificationBaseBudgetR3B5Percent.Identity);
        BenchmarkV1CaseDefinition secondCase = new(
            "same-case",
            Component("hash-test"),
            Trace(seed: 42, updatedBaseBytes: 26),
            BenchmarkV1Baselines.ReadAmplificationBaseBudgetR3B5Percent.Identity);
        BenchmarkManifestV1 firstManifest = Batch(firstCase).Manifest;
        BenchmarkManifestV1 secondManifest = Batch(secondCase).Manifest;

        Assert.NotEqual(
            firstCase.ManifestCase.ResolvedTraceSha256,
            secondCase.ManifestCase.ResolvedTraceSha256);
        Assert.NotEqual(
            BenchmarkV1Json.ComputeManifestSha256(firstManifest),
            BenchmarkV1Json.ComputeManifestSha256(secondManifest));
    }

    [Fact]
    public void Four_outcome_leaves_have_disjoint_machine_readable_shapes() {
        BenchmarkManifestV1 manifest = Manifest([
            Case("admitted"),
            Case("capacity"),
            Case("incomplete"),
            Case(
                "unproven",
                traceStepCount: 1,
                evaluatedWorkloadStepCount: 0),
        ]);
        const string traceHash =
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        EvaluatorPositionReportV1 terminal = new(
            EvaluatorRunPhase.TerminalSettlement,
            completedWorkloadStepCount: 0,
            totalWorkloadStepCount: 0);
        EvaluatorPositionReportV1 admittedTerminal = new(
            EvaluatorRunPhase.TerminalSettlement,
            completedWorkloadStepCount: 1,
            totalWorkloadStepCount: 1);
        EvaluatorPositionReportV1 workload = new(
            EvaluatorRunPhase.Workload,
            completedWorkloadStepCount: 0,
            totalWorkloadStepCount: 1);
        CapacityRejectionReportV1 capacity = new(
            RevisionCandidateCapacityLimit.PayloadAndTailMetaLength,
            attemptedValue: 101,
            maximumValue: 100);
        BenchmarkReportV1 report = new(manifest, [
            new BenchmarkCaseReportV1(
                "unproven",
                traceHash,
                new RejectedUnprovenOutcomeReportV1(
                    terminal,
                    new CompletionRejectionReportV1(
                        CanPrepareAndRotateRejectionStage.FinalRotateC,
                        completedMigrationCount: 2,
                        blockingObjectId: null,
                        capacity))),
            new BenchmarkCaseReportV1(
                "incomplete",
                traceHash,
                new IncompleteOutcomeReportV1(workload)),
            new BenchmarkCaseReportV1(
                "capacity",
                traceHash,
                new CapacityRejectedOutcomeReportV1(
                    workload,
                    CandidateTarget.RotateC,
                    capacity)),
            new BenchmarkCaseReportV1(
                "admitted",
                traceHash,
                new AdmittedOutcomeReportV1(
                    admittedTerminal,
                    new EvaluatorMetricsReportV1(
                        totalPhysicalWriteBytes: 100,
                        workloadPhysicalWriteBytes: 60,
                        terminalSettlementPhysicalWriteBytes: 40,
                        totalWorkloadDeltaReferencePayloadBytes: 7,
                        totalWorkloadBaseReferencePayloadBytes: 10,
                        peakWorkloadCommitWriteBytes: 60,
                        maxCurrentFileTailBytes: 200,
                        totalWorkloadColdReadBytes: 48,
                        totalWorkloadLogicalBasePayloadBytes: 10))),
        ]);

        byte[] bytes = BenchmarkV1Json.WriteReport(report);
        string json = System.Text.Encoding.UTF8.GetString(bytes);
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement cases = document.RootElement.GetProperty("cases");

        Assert.Equal(
            4,
            document.RootElement.GetProperty("schema").GetProperty("version").GetInt32());
        Assert.Equal(BenchmarkV1Json.ComputeManifestSha256(manifest),
            document.RootElement.GetProperty("manifestSha256").GetString());
        Assert.Equal(
            ["admitted", "capacity", "incomplete", "unproven"],
            cases.EnumerateArray()
                .Select(static item => item.GetProperty("caseId").GetString()!)
                .ToArray());

        JsonElement admitted = GetOutcome(cases, "admitted");
        Assert.Equal("admitted", admitted.GetProperty("kind").GetString());
        JsonElement metrics = admitted.GetProperty("metrics");
        Assert.Equal(
            [
                "totalPhysicalWriteBytes",
                "workloadPhysicalWriteBytes",
                "terminalSettlementPhysicalWriteBytes",
                "totalWorkloadDeltaReferencePayloadBytes",
                "totalWorkloadBaseReferencePayloadBytes",
                "peakWorkloadCommitWriteBytes",
                "maxCurrentFileTailBytes",
                "totalWorkloadColdReadBytes",
                "totalWorkloadLogicalBasePayloadBytes",
            ],
            metrics.EnumerateObject().Select(static property => property.Name));
        Assert.Equal(60, metrics.GetProperty("workloadPhysicalWriteBytes").GetInt64());
        Assert.Equal(
            40,
            metrics.GetProperty("terminalSettlementPhysicalWriteBytes").GetInt64());
        Assert.Equal(
            7,
            metrics.GetProperty("totalWorkloadDeltaReferencePayloadBytes").GetInt64());
        Assert.Equal(
            10,
            metrics.GetProperty("totalWorkloadBaseReferencePayloadBytes").GetInt64());
        Assert.Equal(48, metrics.GetProperty("totalWorkloadColdReadBytes").GetInt64());
        Assert.Equal(
            10,
            metrics.GetProperty("totalWorkloadLogicalBasePayloadBytes").GetInt64());
        Assert.Equal(60, metrics.GetProperty("peakWorkloadCommitWriteBytes").GetInt64());
        Assert.False(admitted.TryGetProperty("finalCursor", out _));
        Assert.False(admitted.TryGetProperty("settlement", out _));
        Assert.DoesNotContain("\"realizedCommitCount\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\"workloadColdReadSampleCount\"",
            json,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\"terminalColdHeadReadBytes\"",
            json,
            StringComparison.Ordinal);

        JsonElement capacityOutcome = GetOutcome(cases, "capacity");
        Assert.Equal("capacity-rejected", capacityOutcome.GetProperty("kind").GetString());
        Assert.Equal("rotate-c", capacityOutcome.GetProperty("selectedTarget").GetString());
        AssertNoAdmittedFields(capacityOutcome);

        JsonElement incomplete = GetOutcome(cases, "incomplete");
        Assert.Equal("incomplete", incomplete.GetProperty("kind").GetString());
        AssertNoAdmittedFields(incomplete);

        JsonElement unproven = GetOutcome(cases, "unproven");
        Assert.Equal("rejected-unproven", unproven.GetProperty("kind").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            unproven.GetProperty("rejection")
                .GetProperty("blockingObjectId")
                .ValueKind);
        AssertNoAdmittedFields(unproven);
        Assert.DoesNotContain("\"metrics\":null", json, StringComparison.Ordinal);
        Assert.EndsWith("\n", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Metrics_report_requires_exact_write_phase_conservation() {
        Assert.Throws<ArgumentException>(() => new EvaluatorMetricsReportV1(
            totalPhysicalWriteBytes: 100,
            workloadPhysicalWriteBytes: 60,
            terminalSettlementPhysicalWriteBytes: 39,
            totalWorkloadDeltaReferencePayloadBytes: 7,
            totalWorkloadBaseReferencePayloadBytes: 10,
            peakWorkloadCommitWriteBytes: 60,
            maxCurrentFileTailBytes: 200,
            totalWorkloadColdReadBytes: 48,
            totalWorkloadLogicalBasePayloadBytes: 10));

        Assert.Throws<ArgumentException>(() => new EvaluatorMetricsReportV1(
            totalPhysicalWriteBytes: 100,
            workloadPhysicalWriteBytes: 60,
            terminalSettlementPhysicalWriteBytes: 40,
            totalWorkloadDeltaReferencePayloadBytes: 7,
            totalWorkloadBaseReferencePayloadBytes: 10,
            peakWorkloadCommitWriteBytes: 0,
            maxCurrentFileTailBytes: 200,
            totalWorkloadColdReadBytes: 48,
            totalWorkloadLogicalBasePayloadBytes: 10));

        Assert.Throws<ArgumentException>(() => new EvaluatorMetricsReportV1(
            totalPhysicalWriteBytes: 100,
            workloadPhysicalWriteBytes: 0,
            terminalSettlementPhysicalWriteBytes: 100,
            totalWorkloadDeltaReferencePayloadBytes: 0,
            totalWorkloadBaseReferencePayloadBytes: 0,
            peakWorkloadCommitWriteBytes: 0,
            maxCurrentFileTailBytes: 200,
            totalWorkloadColdReadBytes: 48,
            totalWorkloadLogicalBasePayloadBytes: 0));

        Assert.Throws<OverflowException>(() => new EvaluatorMetricsReportV1(
            totalPhysicalWriteBytes: long.MaxValue,
            workloadPhysicalWriteBytes: long.MaxValue,
            terminalSettlementPhysicalWriteBytes: 1,
            totalWorkloadDeltaReferencePayloadBytes: 0,
            totalWorkloadBaseReferencePayloadBytes: 0,
            peakWorkloadCommitWriteBytes: 1,
            maxCurrentFileTailBytes: 200,
            totalWorkloadColdReadBytes: 0,
            totalWorkloadLogicalBasePayloadBytes: 0));

        Assert.Throws<ArgumentException>(() => new EvaluatorMetricsReportV1(
            totalPhysicalWriteBytes: 100,
            workloadPhysicalWriteBytes: 60,
            terminalSettlementPhysicalWriteBytes: 40,
            totalWorkloadDeltaReferencePayloadBytes: 7,
            totalWorkloadBaseReferencePayloadBytes: 10,
            peakWorkloadCommitWriteBytes: 61,
            maxCurrentFileTailBytes: 200,
            totalWorkloadColdReadBytes: 48,
            totalWorkloadLogicalBasePayloadBytes: 10));
    }

    [Fact]
    public void Identifier_contract_rejects_noncanonical_text() {
        Assert.Throws<ArgumentException>(() => Component("Uppercase"));
        Assert.Throws<ArgumentException>(() => Component("has space"));
        Assert.Throws<ArgumentException>(() => Component("-leading-dash"));
        _ = Component("valid.id_v1/path-part");
    }

    [Fact]
    public void Report_rejects_a_trace_hash_that_differs_from_its_manifest() {
        BenchmarkManifestV1 manifest = Manifest([Case("one")]);
        EvaluatorPositionReportV1 position = new(
            EvaluatorRunPhase.Workload,
            completedWorkloadStepCount: 0,
            totalWorkloadStepCount: 1);

        Assert.Throws<ArgumentException>(() => new BenchmarkReportV1(
            manifest,
            [new BenchmarkCaseReportV1(
                "one",
                "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff",
                new IncompleteOutcomeReportV1(position))]));
    }

    [Fact]
    public void Report_rejects_an_outcome_horizon_that_differs_from_its_manifest() {
        BenchmarkManifestV1 manifest = Manifest([Case("one")]);
        EvaluatorPositionReportV1 wrongHorizon = new(
            EvaluatorRunPhase.Workload,
            completedWorkloadStepCount: 0,
            totalWorkloadStepCount: 2);

        Assert.Throws<ArgumentException>(() => new BenchmarkReportV1(
            manifest,
            [new BenchmarkCaseReportV1(
                "one",
                manifest.Cases[0].ResolvedTraceSha256,
                new IncompleteOutcomeReportV1(wrongHorizon))]));
    }

    [Fact]
    public void Report_rejects_outcome_phases_the_evaluator_cannot_emit() {
        BenchmarkManifestV1 admittedManifest = Manifest([Case("admitted")]);
        BenchmarkManifestV1 capacityManifest = Manifest([
            Case(
                "capacity",
                traceStepCount: 1,
                evaluatedWorkloadStepCount: 0),
        ]);
        EvaluatorPositionReportV1 workload = new(
            EvaluatorRunPhase.Workload,
            completedWorkloadStepCount: 0,
            totalWorkloadStepCount: 1);
        EvaluatorPositionReportV1 terminal = new(
            EvaluatorRunPhase.TerminalSettlement,
            completedWorkloadStepCount: 0,
            totalWorkloadStepCount: 0);
        CapacityRejectionReportV1 rejection = new(
            RevisionCandidateCapacityLimit.PayloadAndTailMetaLength,
            attemptedValue: 2,
            maximumValue: 1);

        Assert.Throws<ArgumentException>(() => new BenchmarkReportV1(
            admittedManifest,
            [new BenchmarkCaseReportV1(
                "admitted",
                admittedManifest.Cases[0].ResolvedTraceSha256,
                Admitted(workload))]));
        Assert.Throws<ArgumentException>(() => new BenchmarkReportV1(
            capacityManifest,
            [new BenchmarkCaseReportV1(
                "capacity",
                capacityManifest.Cases[0].ResolvedTraceSha256,
                new CapacityRejectedOutcomeReportV1(
                    terminal,
                    CandidateTarget.RotateC,
                    rejection))]));
    }

    [Fact]
    public void Report_accepts_both_phases_emitted_by_unproven_and_incomplete_outcomes() {
        BenchmarkManifestV1 workloadManifest = Manifest([Case("workload")]);
        BenchmarkManifestV1 terminalManifest = Manifest([
            Case(
                "terminal",
                traceStepCount: 1,
                evaluatedWorkloadStepCount: 0),
        ]);
        EvaluatorPositionReportV1 workload = new(
            EvaluatorRunPhase.Workload,
            completedWorkloadStepCount: 0,
            totalWorkloadStepCount: 1);
        EvaluatorPositionReportV1 terminal = new(
            EvaluatorRunPhase.TerminalSettlement,
            completedWorkloadStepCount: 0,
            totalWorkloadStepCount: 0);
        CompletionRejectionReportV1 rejection = new(
            CanPrepareAndRotateRejectionStage.FinalRotateC,
            completedMigrationCount: 0,
            blockingObjectId: null,
            new CapacityRejectionReportV1(
                RevisionCandidateCapacityLimit.PayloadAndTailMetaLength,
                attemptedValue: 2,
                maximumValue: 1));

        _ = new BenchmarkReportV1(
            workloadManifest,
            [new BenchmarkCaseReportV1(
                "workload",
                workloadManifest.Cases[0].ResolvedTraceSha256,
                new RejectedUnprovenOutcomeReportV1(workload, rejection))]);
        _ = new BenchmarkReportV1(
            terminalManifest,
            [new BenchmarkCaseReportV1(
                "terminal",
                terminalManifest.Cases[0].ResolvedTraceSha256,
                new IncompleteOutcomeReportV1(terminal))]);
    }

    private static JsonElement GetOutcome(JsonElement cases, string caseId) => cases
        .EnumerateArray()
        .Single(item => item.GetProperty("caseId").GetString() == caseId)
        .GetProperty("outcome");

    private static void AssertNoAdmittedFields(JsonElement outcome) {
        Assert.False(outcome.TryGetProperty("metrics", out _));
        Assert.False(outcome.TryGetProperty("finalCursor", out _));
        Assert.False(outcome.TryGetProperty("settlement", out _));
    }

    private static AdmittedOutcomeReportV1 Admitted(
        EvaluatorPositionReportV1 position) => new(
        position,
        new EvaluatorMetricsReportV1(
            totalPhysicalWriteBytes: 100,
            workloadPhysicalWriteBytes: 0,
            terminalSettlementPhysicalWriteBytes: 100,
            totalWorkloadDeltaReferencePayloadBytes: 0,
            totalWorkloadBaseReferencePayloadBytes: 0,
            peakWorkloadCommitWriteBytes: 0,
            maxCurrentFileTailBytes: 200,
            totalWorkloadColdReadBytes: 0,
            totalWorkloadLogicalBasePayloadBytes: 0));

    private static BenchmarkManifestV1 Manifest(
        IEnumerable<BenchmarkCaseManifestV1> cases) => new(
        "benchmark-v1",
        manifestRevision: 1,
        Component("two-leg-evaluator"),
        Component("direct-rotate-else-ascending-single-debt"),
        Component("after-every-workload-save-cold-load"),
        new BenchmarkComponentIdentityV1("raw-wpfr", 4),
        Component("rbf-v0.40-envelope"),
        Component("provisional-revision-v0"),
        cases);

    private static BenchmarkCaseManifestV1 Case(
        string caseId,
        ulong seed = 0,
        int bootstrapStepCount = 1,
        int traceStepCount = 2,
        int evaluatedWorkloadStepCount = 1) => new(
        caseId,
        BenchmarkV1Identities.SourceFixture,
        Component("trace-definition"),
        Component("scenario-generator"),
        seed,
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
        bootstrapStepCount,
        traceStepCount,
        evaluatedWorkloadStepCount,
        Component("selection-profile"));

    private static BenchmarkComponentIdentityV1 Component(string id) => new(id, 1);

    private static BenchmarkV1BatchDefinition Batch(
        params BenchmarkV1CaseDefinition[] cases) => new(
        BenchmarkV1Corpus.ManifestId,
        BenchmarkV1Corpus.ManifestRevision,
        BenchmarkV1ProtocolIdentities.Evaluator,
        BenchmarkV1ProtocolIdentities.TerminalSettlement,
        BenchmarkV1ProtocolIdentities.ReadSchedule,
        BenchmarkV1ProtocolIdentities.Metrics,
        BenchmarkV1ProtocolIdentities.FrameLayout,
        BenchmarkV1ProtocolIdentities.RevisionGrammar,
        cases);

    private static WorkloadTrace Trace(ulong seed, int updatedBaseBytes) => new(
        "hash-test",
        "two-leg-rotation-probe/scenario",
        generatorVersion: 1,
        seed,
        [
            new SaveStep([
                new CreateObject(1, 10),
                new CreateObject(2, 10),
            ]),
            new SaveStep([
                new RemoveObject(2),
                new UpdateObject(1, updatedBaseBytes, 7),
            ]),
        ]);
}
