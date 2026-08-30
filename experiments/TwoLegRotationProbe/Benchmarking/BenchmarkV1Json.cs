using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Benchmarking;

internal static class BenchmarkV1Json {
    private static readonly JsonWriterOptions WriterOptions = new() {
        Indented = false,
        SkipValidation = false,
    };

    public static byte[] WriteManifest(BenchmarkManifestV1 manifest) {
        ArgumentNullException.ThrowIfNull(manifest);
        return WriteCanonical(writer => WriteManifestCore(writer, manifest));
    }

    public static string ComputeManifestSha256(BenchmarkManifestV1 manifest) =>
        ComputeSha256(WriteManifest(manifest));

    public static byte[] WriteReport(BenchmarkReportV1 report) {
        ArgumentNullException.ThrowIfNull(report);
        return WriteCanonical(writer => WriteReportCore(writer, report));
    }

    internal static byte[] WriteExpandedTrace(WorkloadTrace trace) {
        ArgumentNullException.ThrowIfNull(trace);
        return WriteCanonical(writer => WriteExpandedTraceCore(writer, trace));
    }

    internal static string ComputeSha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] WriteCanonical(Action<Utf8JsonWriter> write) {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer, WriterOptions)) {
            write(writer);
            writer.Flush();
        }

        byte[] result = new byte[checked(buffer.WrittenCount + 1)];
        buffer.WrittenSpan.CopyTo(result);
        result[^1] = (byte)'\n';
        return result;
    }

    private static void WriteManifestCore(
        Utf8JsonWriter writer,
        BenchmarkManifestV1 manifest) {
        writer.WriteStartObject();
        WriteComponent(writer, "schema", manifest.Schema);
        writer.WriteString("manifestId", manifest.ManifestId);
        writer.WriteNumber("manifestRevision", manifest.ManifestRevision);
        WriteComponent(writer, "evaluator", manifest.Evaluator);
        WriteComponent(writer, "terminalSettlement", manifest.TerminalSettlement);
        WriteComponent(writer, "readSchedule", manifest.ReadSchedule);
        WriteComponent(writer, "metrics", manifest.Metrics);
        WriteComponent(writer, "frameLayout", manifest.FrameLayout);
        WriteComponent(writer, "revisionGrammar", manifest.RevisionGrammar);
        writer.WriteStartArray("cases");
        foreach (BenchmarkCaseManifestV1 benchmarkCase in manifest.Cases
            .OrderBy(static candidate => candidate.CaseId, StringComparer.Ordinal)) {
            WriteManifestCase(writer, benchmarkCase);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteManifestCase(
        Utf8JsonWriter writer,
        BenchmarkCaseManifestV1 benchmarkCase) {
        writer.WriteStartObject();
        writer.WriteString("caseId", benchmarkCase.CaseId);
        WriteComponent(writer, "sourceFixture", benchmarkCase.SourceFixture);
        WriteComponent(writer, "traceDefinition", benchmarkCase.TraceDefinition);
        WriteComponent(writer, "generator", benchmarkCase.Generator);
        writer.WriteString(
            "seed",
            benchmarkCase.Seed.ToString("x16", CultureInfo.InvariantCulture));
        writer.WriteString(
            "resolvedTraceSha256",
            benchmarkCase.ResolvedTraceSha256);
        writer.WriteNumber("bootstrapStepCount", benchmarkCase.BootstrapStepCount);
        writer.WriteNumber("traceStepCount", benchmarkCase.TraceStepCount);
        writer.WriteNumber(
            "evaluatedWorkloadStepCount",
            benchmarkCase.EvaluatedWorkloadStepCount);
        WriteComponent(writer, "targetTreatment", benchmarkCase.TargetTreatment);
        WriteComponent(writer, "decisionTreatment", benchmarkCase.DecisionTreatment);
        writer.WriteEndObject();
    }

    private static void WriteReportCore(
        Utf8JsonWriter writer,
        BenchmarkReportV1 report) {
        writer.WriteStartObject();
        WriteComponent(writer, "schema", report.Schema);
        writer.WriteString("manifestId", report.ManifestId);
        writer.WriteNumber("manifestRevision", report.ManifestRevision);
        writer.WriteString("manifestSha256", report.ManifestSha256);
        WriteComponent(writer, "evaluator", report.Evaluator);
        WriteComponent(writer, "terminalSettlement", report.TerminalSettlement);
        WriteComponent(writer, "readSchedule", report.ReadSchedule);
        WriteComponent(writer, "metrics", report.Metrics);
        WriteComponent(writer, "frameLayout", report.FrameLayout);
        WriteComponent(writer, "revisionGrammar", report.RevisionGrammar);
        writer.WriteStartArray("cases");
        foreach (BenchmarkCaseReportV1 benchmarkCase in report.Cases
            .OrderBy(static candidate => candidate.CaseId, StringComparer.Ordinal)) {
            writer.WriteStartObject();
            writer.WriteString("caseId", benchmarkCase.CaseId);
            writer.WriteString("resolvedTraceSha256", benchmarkCase.ResolvedTraceSha256);
            writer.WritePropertyName("outcome");
            WriteOutcome(writer, benchmarkCase.Outcome);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteOutcome(
        Utf8JsonWriter writer,
        BenchmarkOutcomeReportV1 outcome) {
        writer.WriteStartObject();
        switch (outcome) {
            case AdmittedOutcomeReportV1 admitted:
                writer.WriteString("kind", "admitted");
                WritePosition(writer, admitted.Position);
                WriteMetrics(writer, admitted.Metrics);
                WriteFinalCursor(writer, admitted.FinalCursor);
                WriteSettlement(writer, admitted.Settlement);
                break;
            case CapacityRejectedOutcomeReportV1 capacity:
                writer.WriteString("kind", "capacity-rejected");
                WritePosition(writer, capacity.Position);
                writer.WriteString(
                    "selectedTarget",
                    GetCandidateTargetToken(capacity.SelectedTarget));
                WriteCapacityRejection(writer, "rejection", capacity.Rejection);
                break;
            case RejectedUnprovenOutcomeReportV1 unproven:
                writer.WriteString("kind", "rejected-unproven");
                WritePosition(writer, unproven.Position);
                writer.WriteStartObject("rejection");
                writer.WriteString("stage", GetRejectionStageToken(unproven.Rejection.Stage));
                writer.WriteNumber(
                    "completedMigrationCount",
                    unproven.Rejection.CompletedMigrationCount);
                if (unproven.Rejection.BlockingObjectId is uint blockingObjectId) {
                    writer.WriteNumber("blockingObjectId", blockingObjectId);
                } else {
                    writer.WriteNull("blockingObjectId");
                }

                WriteCapacityRejection(
                    writer,
                    "capacity",
                    unproven.Rejection.Capacity);
                writer.WriteEndObject();
                break;
            case IncompleteOutcomeReportV1 incomplete:
                writer.WriteString("kind", "incomplete");
                WritePosition(writer, incomplete.Position);
                break;
            default:
                throw new InvalidDataException(
                    "The report contains an unsupported evaluator outcome kind.");
        }

        writer.WriteEndObject();
    }

    private static void WritePosition(
        Utf8JsonWriter writer,
        EvaluatorPositionReportV1 position) {
        writer.WriteStartObject("position");
        writer.WriteString("phase", position.Phase switch {
            EvaluatorRunPhase.Workload => "workload",
            EvaluatorRunPhase.TerminalSettlement => "terminal-settlement",
            _ => throw new InvalidDataException(
                "The report contains an unsupported evaluator phase."),
        });
        writer.WriteNumber(
            "completedWorkloadStepCount",
            position.CompletedWorkloadStepCount);
        writer.WriteNumber("totalWorkloadStepCount", position.TotalWorkloadStepCount);
        writer.WriteEndObject();
    }

    private static void WriteMetrics(
        Utf8JsonWriter writer,
        EvaluatorMetricsReportV1 metrics) {
        writer.WriteStartObject("metrics");
        writer.WriteNumber("realizedCommitCount", metrics.RealizedCommitCount);
        writer.WriteNumber(
            "totalPhysicalWriteBytes",
            metrics.TotalPhysicalWriteBytes);
        writer.WriteNumber("peakCommitWriteBytes", metrics.PeakCommitWriteBytes);
        writer.WriteNumber(
            "maxCurrentFileTailBytes",
            metrics.MaxCurrentFileTailBytes);
        writer.WriteNumber(
            "finalColdHeadReadBytes",
            metrics.FinalColdHeadReadBytes);
        writer.WriteEndObject();
    }

    private static void WriteFinalCursor(
        Utf8JsonWriter writer,
        FinalCursorReportV1 cursor) {
        writer.WriteStartObject("finalCursor");
        writer.WriteNumber("previousFileNumber", cursor.PreviousFileNumber);
        writer.WriteNumber("currentFileNumber", cursor.CurrentFileNumber);
        writer.WriteStartObject("publishedRevision");
        writer.WriteNumber("fileNumber", cursor.PublishedRevision.FileNumber);
        writer.WriteNumber("offsetBytes", cursor.PublishedRevision.OffsetBytes);
        writer.WriteNumber("lengthBytes", cursor.PublishedRevision.LengthBytes);
        writer.WriteEndObject();
        writer.WriteNumber("currentFileTailBytes", cursor.CurrentFileTailBytes);
        writer.WriteEndObject();
    }

    private static void WriteSettlement(
        Utf8JsonWriter writer,
        TerminalSettlementReportV1 settlement) {
        writer.WriteStartObject("settlement");
        writer.WriteNumber(
            "maintenanceRevisionCount",
            settlement.MaintenanceRevisionCount);
        writer.WriteNumber("realizedRevisionCount", settlement.RealizedRevisionCount);
        writer.WriteStartArray("migratedObjectIds");
        foreach (uint objectId in settlement.MigratedObjectIds) {
            writer.WriteNumberValue(objectId);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteCapacityRejection(
        Utf8JsonWriter writer,
        string propertyName,
        CapacityRejectionReportV1 rejection) {
        writer.WriteStartObject(propertyName);
        writer.WriteString("limit", GetCapacityLimitToken(rejection.Limit));
        writer.WriteNumber("attemptedValue", rejection.AttemptedValue);
        writer.WriteNumber("maximumValue", rejection.MaximumValue);
        writer.WriteEndObject();
    }

    private static void WriteComponent(
        Utf8JsonWriter writer,
        string propertyName,
        BenchmarkComponentIdentityV1 component) {
        writer.WriteStartObject(propertyName);
        writer.WriteString("id", component.Id);
        writer.WriteNumber("version", component.Version);
        writer.WriteEndObject();
    }

    private static void WriteExpandedTraceCore(
        Utf8JsonWriter writer,
        WorkloadTrace trace) {
        writer.WriteStartObject();
        writer.WriteStartObject("schema");
        writer.WriteString("id", "two-leg-expanded-workload-trace");
        writer.WriteNumber("version", 1);
        writer.WriteEndObject();
        writer.WriteString("scenarioName", trace.ScenarioName);
        writer.WriteStartObject("generator");
        writer.WriteString("id", trace.GeneratorId);
        writer.WriteNumber("version", trace.GeneratorVersion);
        writer.WriteEndObject();
        writer.WriteString("seed", trace.Seed.ToString("x16", CultureInfo.InvariantCulture));
        writer.WriteStartArray("steps");
        foreach (SaveStep step in trace.Steps) {
            writer.WriteStartObject();
            writer.WriteStartArray("changes");
            foreach (WorkloadChange change in step.Changes) {
                writer.WriteStartObject();
                switch (change) {
                    case CreateObject create:
                        writer.WriteString("kind", "create");
                        writer.WriteNumber("objectId", create.ObjectId);
                        writer.WriteNumber("basePayloadBytes", create.BasePayloadBytes);
                        break;
                    case UpdateObject update:
                        writer.WriteString("kind", "update");
                        writer.WriteNumber("objectId", update.ObjectId);
                        writer.WriteNumber(
                            "resultBasePayloadBytes",
                            update.ResultBasePayloadBytes);
                        writer.WriteNumber("deltaPayloadBytes", update.DeltaPayloadBytes);
                        break;
                    case RemoveObject remove:
                        writer.WriteString("kind", "remove");
                        writer.WriteNumber("objectId", remove.ObjectId);
                        break;
                    default:
                        throw new InvalidDataException(
                            "The trace contains an unsupported workload change kind.");
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static string GetCandidateTargetToken(CandidateTarget target) => target switch {
        CandidateTarget.StayB => "stay-b",
        CandidateTarget.RotateC => "rotate-c",
        _ => throw new InvalidDataException(
            "The report contains an unsupported candidate target."),
    };

    private static string GetRejectionStageToken(
        CanPrepareAndRotateRejectionStage stage) => stage switch {
            CanPrepareAndRotateRejectionStage.PreparatoryMigration =>
                "preparatory-migration",
            CanPrepareAndRotateRejectionStage.FinalRotateC => "final-rotate-c",
            _ => throw new InvalidDataException(
                "The report contains an unsupported completion-rejection stage."),
        };

    private static string GetCapacityLimitToken(
        RevisionCandidateCapacityLimit limit) => limit switch {
            RevisionCandidateCapacityLimit.TargetFrameStartNative =>
                "target-frame-start-native",
            RevisionCandidateCapacityLimit.TargetFrameStartRelative =>
                "target-frame-start-relative",
            RevisionCandidateCapacityLimit.ReferencedFrameTicketRelative =>
                "referenced-frame-ticket-relative",
            RevisionCandidateCapacityLimit.TailMetaLength => "tail-meta-length",
            RevisionCandidateCapacityLimit.PayloadAndTailMetaLength =>
                "payload-and-tail-meta-length",
            _ => throw new InvalidDataException(
                "The report contains an unsupported capacity limit."),
        };
}

internal static class WorkloadTraceSha256 {
    public static string Compute(WorkloadTrace trace) =>
        BenchmarkV1Json.ComputeSha256(BenchmarkV1Json.WriteExpandedTrace(trace));
}
