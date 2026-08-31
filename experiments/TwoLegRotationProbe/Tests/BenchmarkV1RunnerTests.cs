using Atelia.TwoLegRotationProbe.Arena;
using Atelia.TwoLegRotationProbe.Baselines;
using Atelia.TwoLegRotationProbe.Benchmarking;
using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Evaluation;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Policies;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class BenchmarkV1RunnerTests {
    [Fact]
    public void Frozen_corpus_defines_fifteen_workloads_with_four_selection_profiles_each() {
        BenchmarkV1BatchDefinition definition = BenchmarkV1Corpus.Create(
            BenchmarkV1Baselines.All);

        Assert.Equal(2, definition.Manifest.Schema.Version);
        Assert.Equal(10, definition.Manifest.ManifestRevision);
        Assert.Equal(BenchmarkV1ProtocolIdentities.Evaluator, definition.Manifest.Evaluator);
        Assert.Equal(
            BenchmarkV1ProtocolIdentities.TerminalSettlement,
            definition.Manifest.TerminalSettlement);
        Assert.Equal(
            BenchmarkV1ProtocolIdentities.ReadSchedule,
            definition.Manifest.ReadSchedule);
        Assert.Equal(BenchmarkV1ProtocolIdentities.Metrics, definition.Manifest.Metrics);
        Assert.Equal(
            BenchmarkV1ProtocolIdentities.FrameLayout,
            definition.Manifest.FrameLayout);
        Assert.Equal(
            BenchmarkV1ProtocolIdentities.RevisionGrammar,
            definition.Manifest.RevisionGrammar);
        Assert.Equal(60, definition.Cases.Count);
        Assert.Equal(
            [
                BenchmarkV1Corpus.DebtZeroBeforeRotateNoMigrationCaseId,
                BenchmarkV1Corpus.DebtZeroBeforeRotatePacedCaseId,
                BenchmarkV1Corpus.DebtZeroBeforeRotateAdaptiveR3B5PercentCaseId,
                BenchmarkV1Corpus.DebtZeroBeforeRotateAdaptiveR4B4PercentCaseId,
                BenchmarkV1Corpus.DebtZeroThenRotateNoMigrationCaseId,
                BenchmarkV1Corpus.DebtZeroThenRotatePacedCaseId,
                BenchmarkV1Corpus.DebtZeroThenRotateAdaptiveR3B5PercentCaseId,
                BenchmarkV1Corpus.DebtZeroThenRotateAdaptiveR4B4PercentCaseId,
                BenchmarkV1Corpus.InsertBurstThreeOneNoMigrationCaseId,
                BenchmarkV1Corpus.InsertBurstThreeOnePacedCaseId,
                BenchmarkV1Corpus.InsertBurstThreeOneAdaptiveR3B5PercentCaseId,
                BenchmarkV1Corpus.InsertBurstThreeOneAdaptiveR4B4PercentCaseId,
                BenchmarkV1Corpus.InsertBurstTwoTwoNoMigrationCaseId,
                BenchmarkV1Corpus.InsertBurstTwoTwoPacedCaseId,
                BenchmarkV1Corpus.InsertBurstTwoTwoAdaptiveR3B5PercentCaseId,
                BenchmarkV1Corpus.InsertBurstTwoTwoAdaptiveR4B4PercentCaseId,
                BenchmarkV1Corpus.LifecycleTransientOverlapNoMigrationCaseId,
                BenchmarkV1Corpus.LifecycleTransientOverlapPacedCaseId,
                BenchmarkV1Corpus.LifecycleTransientOverlapAdaptiveR3B5PercentCaseId,
                BenchmarkV1Corpus.LifecycleTransientOverlapAdaptiveR4B4PercentCaseId,
                BenchmarkV1Corpus.LifecycleTransientSerialNoMigrationCaseId,
                BenchmarkV1Corpus.LifecycleTransientSerialPacedCaseId,
                BenchmarkV1Corpus.LifecycleTransientSerialAdaptiveR3B5PercentCaseId,
                BenchmarkV1Corpus.LifecycleTransientSerialAdaptiveR4B4PercentCaseId,
                BenchmarkV1Corpus.LocalityHighIdNoMigrationCaseId,
                BenchmarkV1Corpus.LocalityHighIdPacedCaseId,
                BenchmarkV1Corpus.LocalityHighIdAdaptiveR3B5PercentCaseId,
                BenchmarkV1Corpus.LocalityHighIdAdaptiveR4B4PercentCaseId,
                BenchmarkV1Corpus.LocalityLowIdNoMigrationCaseId,
                BenchmarkV1Corpus.LocalityLowIdPacedCaseId,
                BenchmarkV1Corpus.LocalityLowIdAdaptiveR3B5PercentCaseId,
                BenchmarkV1Corpus.LocalityLowIdAdaptiveR4B4PercentCaseId,
                BenchmarkV1Corpus.MixedSmallNoMigrationCaseId,
                BenchmarkV1Corpus.MixedSmallPacedCaseId,
                BenchmarkV1Corpus.MixedSmallAdaptiveR3B5PercentCaseId,
                BenchmarkV1Corpus.MixedSmallAdaptiveR4B4PercentCaseId,
                BenchmarkV1Corpus.PreviousDebtGranularitySingleLargeNoMigrationCaseId,
                BenchmarkV1Corpus.PreviousDebtGranularitySingleLargePacedCaseId,
                BenchmarkV1Corpus
                    .PreviousDebtGranularitySingleLargeAdaptiveR3B5PercentCaseId,
                BenchmarkV1Corpus
                    .PreviousDebtGranularitySingleLargeAdaptiveR4B4PercentCaseId,
                BenchmarkV1Corpus.PreviousDebtGranularityThreeSmallNoMigrationCaseId,
                BenchmarkV1Corpus.PreviousDebtGranularityThreeSmallPacedCaseId,
                BenchmarkV1Corpus
                    .PreviousDebtGranularityThreeSmallAdaptiveR3B5PercentCaseId,
                BenchmarkV1Corpus
                    .PreviousDebtGranularityThreeSmallAdaptiveR4B4PercentCaseId,
                BenchmarkV1Corpus.DebtShareDilutionNoMigrationCaseId,
                BenchmarkV1Corpus.DebtShareDilutionPacedCaseId,
                BenchmarkV1Corpus.DebtShareDilutionAdaptiveR3B5PercentCaseId,
                BenchmarkV1Corpus.DebtShareDilutionAdaptiveR4B4PercentCaseId,
                BenchmarkV1Corpus.ThresholdBandNoMigrationCaseId,
                BenchmarkV1Corpus.ThresholdBandPacedCaseId,
                BenchmarkV1Corpus.ThresholdBandAdaptiveR3B5PercentCaseId,
                BenchmarkV1Corpus.ThresholdBandAdaptiveR4B4PercentCaseId,
                BenchmarkV1Corpus.SizeSkewLowIdLargeNoMigrationCaseId,
                BenchmarkV1Corpus.SizeSkewLowIdLargePacedCaseId,
                BenchmarkV1Corpus.SizeSkewLowIdLargeAdaptiveR3B5PercentCaseId,
                BenchmarkV1Corpus.SizeSkewLowIdLargeAdaptiveR4B4PercentCaseId,
                BenchmarkV1Corpus.SizeSkewLowIdSmallNoMigrationCaseId,
                BenchmarkV1Corpus.SizeSkewLowIdSmallPacedCaseId,
                BenchmarkV1Corpus.SizeSkewLowIdSmallAdaptiveR3B5PercentCaseId,
                BenchmarkV1Corpus.SizeSkewLowIdSmallAdaptiveR4B4PercentCaseId,
            ],
            definition.Cases.Select(static benchmarkCase =>
                benchmarkCase.ManifestCase.CaseId));
        BenchmarkV1CaseDefinition debtNoMigration = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.DebtZeroThenRotateNoMigrationCaseId);
        BenchmarkV1CaseDefinition debtPaced = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.DebtZeroThenRotatePacedCaseId);
        BenchmarkV1CaseDefinition debtAdaptive35 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.DebtZeroThenRotateAdaptiveR3B5PercentCaseId);
        BenchmarkV1CaseDefinition debtAdaptive44 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.DebtZeroThenRotateAdaptiveR4B4PercentCaseId);
        BenchmarkV1CaseDefinition debtBeforeNoMigration = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.DebtZeroBeforeRotateNoMigrationCaseId);
        BenchmarkV1CaseDefinition debtBeforePaced = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.DebtZeroBeforeRotatePacedCaseId);
        BenchmarkV1CaseDefinition debtBeforeAdaptive35 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.DebtZeroBeforeRotateAdaptiveR3B5PercentCaseId);
        BenchmarkV1CaseDefinition debtBeforeAdaptive44 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.DebtZeroBeforeRotateAdaptiveR4B4PercentCaseId);
        BenchmarkV1CaseDefinition burstThreeOneNoMigration = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.InsertBurstThreeOneNoMigrationCaseId);
        BenchmarkV1CaseDefinition burstThreeOnePaced = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.InsertBurstThreeOnePacedCaseId);
        BenchmarkV1CaseDefinition burstThreeOneAdaptive35 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.InsertBurstThreeOneAdaptiveR3B5PercentCaseId);
        BenchmarkV1CaseDefinition burstThreeOneAdaptive44 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.InsertBurstThreeOneAdaptiveR4B4PercentCaseId);
        BenchmarkV1CaseDefinition burstTwoTwoNoMigration = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.InsertBurstTwoTwoNoMigrationCaseId);
        BenchmarkV1CaseDefinition burstTwoTwoPaced = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.InsertBurstTwoTwoPacedCaseId);
        BenchmarkV1CaseDefinition burstTwoTwoAdaptive35 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.InsertBurstTwoTwoAdaptiveR3B5PercentCaseId);
        BenchmarkV1CaseDefinition burstTwoTwoAdaptive44 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.InsertBurstTwoTwoAdaptiveR4B4PercentCaseId);
        BenchmarkV1CaseDefinition mixedNoMigration = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.MixedSmallNoMigrationCaseId);
        BenchmarkV1CaseDefinition mixedPaced = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.MixedSmallPacedCaseId);
        BenchmarkV1CaseDefinition mixedAdaptive35 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.MixedSmallAdaptiveR3B5PercentCaseId);
        BenchmarkV1CaseDefinition mixedAdaptive44 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.MixedSmallAdaptiveR4B4PercentCaseId);
        BenchmarkV1CaseDefinition thresholdNoMigration = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.ThresholdBandNoMigrationCaseId);
        BenchmarkV1CaseDefinition thresholdPaced = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.ThresholdBandPacedCaseId);
        BenchmarkV1CaseDefinition thresholdAdaptive35 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.ThresholdBandAdaptiveR3B5PercentCaseId);
        BenchmarkV1CaseDefinition thresholdAdaptive44 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.ThresholdBandAdaptiveR4B4PercentCaseId);
        BenchmarkV1CaseDefinition debtShareNoMigration = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.DebtShareDilutionNoMigrationCaseId);
        BenchmarkV1CaseDefinition debtSharePaced = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.DebtShareDilutionPacedCaseId);
        BenchmarkV1CaseDefinition debtShareAdaptive35 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.DebtShareDilutionAdaptiveR3B5PercentCaseId);
        BenchmarkV1CaseDefinition debtShareAdaptive44 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.DebtShareDilutionAdaptiveR4B4PercentCaseId);
        BenchmarkV1CaseDefinition localityHighNoMigration = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.LocalityHighIdNoMigrationCaseId);
        BenchmarkV1CaseDefinition localityHighPaced = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.LocalityHighIdPacedCaseId);
        BenchmarkV1CaseDefinition localityHighAdaptive35 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.LocalityHighIdAdaptiveR3B5PercentCaseId);
        BenchmarkV1CaseDefinition localityHighAdaptive44 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.LocalityHighIdAdaptiveR4B4PercentCaseId);
        BenchmarkV1CaseDefinition localityLowNoMigration = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.LocalityLowIdNoMigrationCaseId);
        BenchmarkV1CaseDefinition localityLowPaced = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.LocalityLowIdPacedCaseId);
        BenchmarkV1CaseDefinition localityLowAdaptive35 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.LocalityLowIdAdaptiveR3B5PercentCaseId);
        BenchmarkV1CaseDefinition localityLowAdaptive44 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.LocalityLowIdAdaptiveR4B4PercentCaseId);
        BenchmarkV1CaseDefinition sizeSkewLargeNoMigration = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.SizeSkewLowIdLargeNoMigrationCaseId);
        BenchmarkV1CaseDefinition sizeSkewLargePaced = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.SizeSkewLowIdLargePacedCaseId);
        BenchmarkV1CaseDefinition sizeSkewLargeAdaptive35 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.SizeSkewLowIdLargeAdaptiveR3B5PercentCaseId);
        BenchmarkV1CaseDefinition sizeSkewLargeAdaptive44 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.SizeSkewLowIdLargeAdaptiveR4B4PercentCaseId);
        BenchmarkV1CaseDefinition sizeSkewSmallNoMigration = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.SizeSkewLowIdSmallNoMigrationCaseId);
        BenchmarkV1CaseDefinition sizeSkewSmallPaced = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.SizeSkewLowIdSmallPacedCaseId);
        BenchmarkV1CaseDefinition sizeSkewSmallAdaptive35 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.SizeSkewLowIdSmallAdaptiveR3B5PercentCaseId);
        BenchmarkV1CaseDefinition sizeSkewSmallAdaptive44 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.SizeSkewLowIdSmallAdaptiveR4B4PercentCaseId);
        BenchmarkV1CaseDefinition lifecycleOverlapNoMigration = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.LifecycleTransientOverlapNoMigrationCaseId);
        BenchmarkV1CaseDefinition lifecycleOverlapPaced = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.LifecycleTransientOverlapPacedCaseId);
        BenchmarkV1CaseDefinition lifecycleOverlapAdaptive35 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.LifecycleTransientOverlapAdaptiveR3B5PercentCaseId);
        BenchmarkV1CaseDefinition lifecycleOverlapAdaptive44 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.LifecycleTransientOverlapAdaptiveR4B4PercentCaseId);
        BenchmarkV1CaseDefinition lifecycleSerialNoMigration = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.LifecycleTransientSerialNoMigrationCaseId);
        BenchmarkV1CaseDefinition lifecycleSerialPaced = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.LifecycleTransientSerialPacedCaseId);
        BenchmarkV1CaseDefinition lifecycleSerialAdaptive35 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.LifecycleTransientSerialAdaptiveR3B5PercentCaseId);
        BenchmarkV1CaseDefinition lifecycleSerialAdaptive44 = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.LifecycleTransientSerialAdaptiveR4B4PercentCaseId);
        BenchmarkV1CaseDefinition debtGranularitySingleNoMigration =
            FindCaseDefinition(
                definition,
                BenchmarkV1Corpus
                    .PreviousDebtGranularitySingleLargeNoMigrationCaseId);
        BenchmarkV1CaseDefinition debtGranularitySinglePaced = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.PreviousDebtGranularitySingleLargePacedCaseId);
        BenchmarkV1CaseDefinition debtGranularitySingleAdaptive35 =
            FindCaseDefinition(
                definition,
                BenchmarkV1Corpus
                    .PreviousDebtGranularitySingleLargeAdaptiveR3B5PercentCaseId);
        BenchmarkV1CaseDefinition debtGranularitySingleAdaptive44 =
            FindCaseDefinition(
                definition,
                BenchmarkV1Corpus
                    .PreviousDebtGranularitySingleLargeAdaptiveR4B4PercentCaseId);
        BenchmarkV1CaseDefinition debtGranularityThreeNoMigration =
            FindCaseDefinition(
                definition,
                BenchmarkV1Corpus
                    .PreviousDebtGranularityThreeSmallNoMigrationCaseId);
        BenchmarkV1CaseDefinition debtGranularityThreePaced = FindCaseDefinition(
            definition,
            BenchmarkV1Corpus.PreviousDebtGranularityThreeSmallPacedCaseId);
        BenchmarkV1CaseDefinition debtGranularityThreeAdaptive35 =
            FindCaseDefinition(
                definition,
                BenchmarkV1Corpus
                    .PreviousDebtGranularityThreeSmallAdaptiveR3B5PercentCaseId);
        BenchmarkV1CaseDefinition debtGranularityThreeAdaptive44 =
            FindCaseDefinition(
                definition,
                BenchmarkV1Corpus
                    .PreviousDebtGranularityThreeSmallAdaptiveR4B4PercentCaseId);
        AssertMatchedWorkload(
            debtNoMigration,
            debtPaced,
            debtAdaptive35,
            debtAdaptive44);
        AssertMatchedWorkload(
            debtBeforeNoMigration,
            debtBeforePaced,
            debtBeforeAdaptive35,
            debtBeforeAdaptive44);
        AssertMatchedWorkload(
            burstThreeOneNoMigration,
            burstThreeOnePaced,
            burstThreeOneAdaptive35,
            burstThreeOneAdaptive44);
        AssertMatchedWorkload(
            burstTwoTwoNoMigration,
            burstTwoTwoPaced,
            burstTwoTwoAdaptive35,
            burstTwoTwoAdaptive44);
        AssertMatchedWorkload(
            mixedNoMigration,
            mixedPaced,
            mixedAdaptive35,
            mixedAdaptive44);
        AssertMatchedWorkload(
            thresholdNoMigration,
            thresholdPaced,
            thresholdAdaptive35,
            thresholdAdaptive44);
        AssertMatchedWorkload(
            debtShareNoMigration,
            debtSharePaced,
            debtShareAdaptive35,
            debtShareAdaptive44);
        AssertMatchedWorkload(
            localityHighNoMigration,
            localityHighPaced,
            localityHighAdaptive35,
            localityHighAdaptive44);
        AssertMatchedWorkload(
            localityLowNoMigration,
            localityLowPaced,
            localityLowAdaptive35,
            localityLowAdaptive44);
        AssertMatchedWorkload(
            sizeSkewLargeNoMigration,
            sizeSkewLargePaced,
            sizeSkewLargeAdaptive35,
            sizeSkewLargeAdaptive44);
        AssertMatchedWorkload(
            sizeSkewSmallNoMigration,
            sizeSkewSmallPaced,
            sizeSkewSmallAdaptive35,
            sizeSkewSmallAdaptive44);
        AssertMatchedWorkload(
            lifecycleOverlapNoMigration,
            lifecycleOverlapPaced,
            lifecycleOverlapAdaptive35,
            lifecycleOverlapAdaptive44);
        AssertMatchedWorkload(
            lifecycleSerialNoMigration,
            lifecycleSerialPaced,
            lifecycleSerialAdaptive35,
            lifecycleSerialAdaptive44);
        AssertMatchedWorkload(
            debtGranularitySingleNoMigration,
            debtGranularitySinglePaced,
            debtGranularitySingleAdaptive35,
            debtGranularitySingleAdaptive44);
        AssertMatchedWorkload(
            debtGranularityThreeNoMigration,
            debtGranularityThreePaced,
            debtGranularityThreeAdaptive35,
            debtGranularityThreeAdaptive44);
        BenchmarkComponentIdentityV1[] expectedProfiles = [
            BenchmarkV1Baselines.DebtZeroThenRotateDeltaNoMigration.Identity,
            BenchmarkV1Baselines
                .DebtZeroThenRotateDeltaPacedOneDebtByObjectId.Identity,
            BenchmarkV1Baselines.ReadAmplificationBaseBudgetR3B5Percent.Identity,
            BenchmarkV1Baselines.ReadAmplificationBaseBudgetR4B4Percent.Identity,
        ];
        Assert.Equal(
            expectedProfiles.OrderBy(static profile => profile.Id),
            definition.Cases
                .Select(static benchmarkCase =>
                    benchmarkCase.ManifestCase.SelectionProfile)
                .Distinct()
                .OrderBy(static profile => profile.Id));
        Assert.Equal(15, new[] {
            debtBeforeNoMigration.ManifestCase.ResolvedTraceSha256,
            debtNoMigration.ManifestCase.ResolvedTraceSha256,
            burstThreeOneNoMigration.ManifestCase.ResolvedTraceSha256,
            burstTwoTwoNoMigration.ManifestCase.ResolvedTraceSha256,
            debtGranularitySingleNoMigration.ManifestCase.ResolvedTraceSha256,
            debtGranularityThreeNoMigration.ManifestCase.ResolvedTraceSha256,
            lifecycleOverlapNoMigration.ManifestCase.ResolvedTraceSha256,
            lifecycleSerialNoMigration.ManifestCase.ResolvedTraceSha256,
            localityHighNoMigration.ManifestCase.ResolvedTraceSha256,
            localityLowNoMigration.ManifestCase.ResolvedTraceSha256,
            mixedNoMigration.ManifestCase.ResolvedTraceSha256,
            debtShareNoMigration.ManifestCase.ResolvedTraceSha256,
            sizeSkewLargeNoMigration.ManifestCase.ResolvedTraceSha256,
            sizeSkewSmallNoMigration.ManifestCase.ResolvedTraceSha256,
            thresholdNoMigration.ManifestCase.ResolvedTraceSha256,
        }.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Frozen_corpus_runs_all_profiles_to_exact_admitted_raw_outcomes() {
        BenchmarkV1BatchDefinition definition = BenchmarkV1Corpus.Create(
            BenchmarkV1Baselines.All);

        BenchmarkV1BatchRun run = BenchmarkV1Runner.Run(definition);

        Assert.Same(definition, run.Definition);
        AdmittedOutcomeReportV1 debtNoMigration = AssertAdmitted(
            run.Report,
            BenchmarkV1Corpus.DebtZeroThenRotateNoMigrationCaseId,
            realizedCommitCount: 5,
            totalPhysicalWriteBytes: 856,
            peakCommitWriteBytes: 680,
            maxCurrentFileTailBytes: 680,
            finalColdHeadReadBytes: 832,
            previousFileNumber: 2,
            currentFileNumber: 3);
        AdmittedOutcomeReportV1 debtPaced = AssertAdmitted(
            run.Report,
            BenchmarkV1Corpus.DebtZeroThenRotatePacedCaseId,
            realizedCommitCount: 5,
            totalPhysicalWriteBytes: 1536,
            peakCommitWriteBytes: 696,
            maxCurrentFileTailBytes: 804,
            finalColdHeadReadBytes: 756,
            previousFileNumber: 3,
            currentFileNumber: 4);
        AdmittedOutcomeReportV1 debtAdaptive35 = AssertAdmitted(
            run.Report,
            BenchmarkV1Corpus.DebtZeroThenRotateAdaptiveR3B5PercentCaseId,
            realizedCommitCount: 5,
            totalPhysicalWriteBytes: 1536,
            peakCommitWriteBytes: 696,
            maxCurrentFileTailBytes: 804,
            finalColdHeadReadBytes: 756,
            previousFileNumber: 3,
            currentFileNumber: 4);
        AdmittedOutcomeReportV1 debtAdaptive44 = AssertAdmitted(
            run.Report,
            BenchmarkV1Corpus.DebtZeroThenRotateAdaptiveR4B4PercentCaseId,
            realizedCommitCount: 5,
            totalPhysicalWriteBytes: 1536,
            peakCommitWriteBytes: 696,
            maxCurrentFileTailBytes: 804,
            finalColdHeadReadBytes: 756,
            previousFileNumber: 3,
            currentFileNumber: 4);
        AdmittedOutcomeReportV1 mixedNoMigration = AssertAdmitted(
            run.Report,
            BenchmarkV1Corpus.MixedSmallNoMigrationCaseId,
            realizedCommitCount: 3,
            totalPhysicalWriteBytes: 368,
            peakCommitWriteBytes: 164,
            maxCurrentFileTailBytes: 344,
            finalColdHeadReadBytes: 352,
            previousFileNumber: 2,
            currentFileNumber: 3);
        AdmittedOutcomeReportV1 mixedPaced = AssertAdmitted(
            run.Report,
            BenchmarkV1Corpus.MixedSmallPacedCaseId,
            realizedCommitCount: 3,
            totalPhysicalWriteBytes: 368,
            peakCommitWriteBytes: 164,
            maxCurrentFileTailBytes: 344,
            finalColdHeadReadBytes: 352,
            previousFileNumber: 2,
            currentFileNumber: 3);
        AdmittedOutcomeReportV1 mixedAdaptive35 = AssertAdmitted(
            run.Report,
            BenchmarkV1Corpus.MixedSmallAdaptiveR3B5PercentCaseId,
            realizedCommitCount: 3,
            totalPhysicalWriteBytes: 440,
            peakCommitWriteBytes: 164,
            maxCurrentFileTailBytes: 184,
            finalColdHeadReadBytes: 280,
            previousFileNumber: 3,
            currentFileNumber: 4);
        AdmittedOutcomeReportV1 mixedAdaptive44 = AssertAdmitted(
            run.Report,
            BenchmarkV1Corpus.MixedSmallAdaptiveR4B4PercentCaseId,
            realizedCommitCount: 3,
            totalPhysicalWriteBytes: 440,
            peakCommitWriteBytes: 164,
            maxCurrentFileTailBytes: 184,
            finalColdHeadReadBytes: 280,
            previousFileNumber: 3,
            currentFileNumber: 4);
        Assert.NotEqual(debtNoMigration.Metrics, debtPaced.Metrics);
        Assert.Equal(debtPaced.Metrics, debtAdaptive35.Metrics);
        Assert.Equal(debtAdaptive35.Metrics, debtAdaptive44.Metrics);
        Assert.Equal(mixedNoMigration.Metrics, mixedPaced.Metrics);
        Assert.Equal(mixedNoMigration.FinalCursor, mixedPaced.FinalCursor);
        Assert.Equal(
            mixedNoMigration.Settlement.MigratedObjectIds,
            mixedPaced.Settlement.MigratedObjectIds);
        Assert.Equal(mixedAdaptive35.Metrics, mixedAdaptive44.Metrics);
        Assert.Equal(mixedAdaptive35.FinalCursor, mixedAdaptive44.FinalCursor);
    }

    [Fact]
    public void Rebuilt_corpus_manifest_and_report_are_byte_identical() {
        BenchmarkV1BatchRun first = BenchmarkV1Runner.Run(
            BenchmarkV1Corpus.Create(BenchmarkV1Baselines.All));
        BenchmarkV1BatchRun second = BenchmarkV1Runner.Run(
            BenchmarkV1Corpus.Create(BenchmarkV1Baselines.All.Reverse()));

        Assert.Equal(
            BenchmarkV1Json.WriteManifest(first.Definition.Manifest),
            BenchmarkV1Json.WriteManifest(second.Definition.Manifest));
        Assert.Equal(
            BenchmarkV1Json.WriteReport(first.Report),
            BenchmarkV1Json.WriteReport(second.Report));
        Assert.Equal(
            "4d780b510f8b0c33523499705e0237508f066acadaec2369a7487109c4d75e95",
            first.Report.ManifestSha256);
        Assert.Equal(
            "9f9541fc8c57eaa6d2c3089e48013ead29240556a7e6cc020899ffa7bbc9f766",
            BenchmarkV1Json.ComputeSha256(
                BenchmarkV1Json.WriteReport(first.Report)));
        Assert.Equal(
            "cd064c19d4cd94f0a25536481fa0c901ca77e7187b0ff9350b95a2b804d286e2",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.DebtZeroThenRotateNoMigrationCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "cd064c19d4cd94f0a25536481fa0c901ca77e7187b0ff9350b95a2b804d286e2",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.DebtZeroThenRotatePacedCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "cd064c19d4cd94f0a25536481fa0c901ca77e7187b0ff9350b95a2b804d286e2",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.DebtZeroThenRotateAdaptiveR3B5PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "cd064c19d4cd94f0a25536481fa0c901ca77e7187b0ff9350b95a2b804d286e2",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.DebtZeroThenRotateAdaptiveR4B4PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "5bd76d8da2021ed92de43e3d34d08e47cfd6b8b2a79831a2b81cfc41e4d219fc",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.MixedSmallNoMigrationCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "5bd76d8da2021ed92de43e3d34d08e47cfd6b8b2a79831a2b81cfc41e4d219fc",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.MixedSmallPacedCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "5bd76d8da2021ed92de43e3d34d08e47cfd6b8b2a79831a2b81cfc41e4d219fc",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.MixedSmallAdaptiveR3B5PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "5bd76d8da2021ed92de43e3d34d08e47cfd6b8b2a79831a2b81cfc41e4d219fc",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.MixedSmallAdaptiveR4B4PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "0d94e562a41af07ab793fd59f5d56d6730933d49e5f1f1ac3309cdeb28b2591d",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.ThresholdBandNoMigrationCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "0d94e562a41af07ab793fd59f5d56d6730933d49e5f1f1ac3309cdeb28b2591d",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.ThresholdBandPacedCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "0d94e562a41af07ab793fd59f5d56d6730933d49e5f1f1ac3309cdeb28b2591d",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.ThresholdBandAdaptiveR3B5PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "0d94e562a41af07ab793fd59f5d56d6730933d49e5f1f1ac3309cdeb28b2591d",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.ThresholdBandAdaptiveR4B4PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "b18b43f55f9233b14ef0d3c1787643e565350c98b7f33b9c9b0463e901c5b1e0",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.DebtShareDilutionNoMigrationCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "b18b43f55f9233b14ef0d3c1787643e565350c98b7f33b9c9b0463e901c5b1e0",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.DebtShareDilutionPacedCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "b18b43f55f9233b14ef0d3c1787643e565350c98b7f33b9c9b0463e901c5b1e0",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.DebtShareDilutionAdaptiveR3B5PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "b18b43f55f9233b14ef0d3c1787643e565350c98b7f33b9c9b0463e901c5b1e0",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.DebtShareDilutionAdaptiveR4B4PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "fdc0630433f49dbf445e2805d4f567c1b175c67d7dfde0dd44cb8e2de0b738fd",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.LocalityHighIdNoMigrationCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "fdc0630433f49dbf445e2805d4f567c1b175c67d7dfde0dd44cb8e2de0b738fd",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.LocalityHighIdPacedCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "fdc0630433f49dbf445e2805d4f567c1b175c67d7dfde0dd44cb8e2de0b738fd",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.LocalityHighIdAdaptiveR3B5PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "fdc0630433f49dbf445e2805d4f567c1b175c67d7dfde0dd44cb8e2de0b738fd",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.LocalityHighIdAdaptiveR4B4PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "4ab42dae6600bc32058838bd4e738fecdd41428f4d049e59135160a5445e4c42",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.LocalityLowIdNoMigrationCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "4ab42dae6600bc32058838bd4e738fecdd41428f4d049e59135160a5445e4c42",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.LocalityLowIdPacedCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "4ab42dae6600bc32058838bd4e738fecdd41428f4d049e59135160a5445e4c42",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.LocalityLowIdAdaptiveR3B5PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "4ab42dae6600bc32058838bd4e738fecdd41428f4d049e59135160a5445e4c42",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.LocalityLowIdAdaptiveR4B4PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "ed38845b162bb0583dce831dcdb50c628665fa7397b6b20ac15c2a014a62adc7",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.SizeSkewLowIdLargeNoMigrationCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "ed38845b162bb0583dce831dcdb50c628665fa7397b6b20ac15c2a014a62adc7",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.SizeSkewLowIdLargePacedCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "ed38845b162bb0583dce831dcdb50c628665fa7397b6b20ac15c2a014a62adc7",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.SizeSkewLowIdLargeAdaptiveR3B5PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "ed38845b162bb0583dce831dcdb50c628665fa7397b6b20ac15c2a014a62adc7",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.SizeSkewLowIdLargeAdaptiveR4B4PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "ffe2a889d56706cc1d8b058e8c35ab76b08c670bd99c869f06e9af5947c97979",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.SizeSkewLowIdSmallNoMigrationCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "ffe2a889d56706cc1d8b058e8c35ab76b08c670bd99c869f06e9af5947c97979",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.SizeSkewLowIdSmallPacedCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "ffe2a889d56706cc1d8b058e8c35ab76b08c670bd99c869f06e9af5947c97979",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.SizeSkewLowIdSmallAdaptiveR3B5PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "ffe2a889d56706cc1d8b058e8c35ab76b08c670bd99c869f06e9af5947c97979",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.SizeSkewLowIdSmallAdaptiveR4B4PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "eea0f08a47c5fcb466bbbad0a66d3fbe6411ef5fecb49e4e12777d7e51c5ee43",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.LifecycleTransientOverlapNoMigrationCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "eea0f08a47c5fcb466bbbad0a66d3fbe6411ef5fecb49e4e12777d7e51c5ee43",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.LifecycleTransientOverlapPacedCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "eea0f08a47c5fcb466bbbad0a66d3fbe6411ef5fecb49e4e12777d7e51c5ee43",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.LifecycleTransientOverlapAdaptiveR3B5PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "eea0f08a47c5fcb466bbbad0a66d3fbe6411ef5fecb49e4e12777d7e51c5ee43",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.LifecycleTransientOverlapAdaptiveR4B4PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "637764ae11d12e1f03ebb51418498d4a58f4240dd06ee6d7d1c85e4a118e462c",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.LifecycleTransientSerialNoMigrationCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "637764ae11d12e1f03ebb51418498d4a58f4240dd06ee6d7d1c85e4a118e462c",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.LifecycleTransientSerialPacedCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "637764ae11d12e1f03ebb51418498d4a58f4240dd06ee6d7d1c85e4a118e462c",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.LifecycleTransientSerialAdaptiveR3B5PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "637764ae11d12e1f03ebb51418498d4a58f4240dd06ee6d7d1c85e4a118e462c",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.LifecycleTransientSerialAdaptiveR4B4PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "dbe729ca29f27f601f4390e0b63ebfdfaa25b3f070a7de70fd5314dea0655ab8",
            FindCase(
                first.Report,
                BenchmarkV1Corpus
                    .PreviousDebtGranularitySingleLargeNoMigrationCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "dbe729ca29f27f601f4390e0b63ebfdfaa25b3f070a7de70fd5314dea0655ab8",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.PreviousDebtGranularitySingleLargePacedCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "dbe729ca29f27f601f4390e0b63ebfdfaa25b3f070a7de70fd5314dea0655ab8",
            FindCase(
                first.Report,
                BenchmarkV1Corpus
                    .PreviousDebtGranularitySingleLargeAdaptiveR3B5PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "dbe729ca29f27f601f4390e0b63ebfdfaa25b3f070a7de70fd5314dea0655ab8",
            FindCase(
                first.Report,
                BenchmarkV1Corpus
                    .PreviousDebtGranularitySingleLargeAdaptiveR4B4PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "9f2cb14e217f8d2cd09307e0df0a5bbbe03c864e79db49c89c64d967c784f67a",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.PreviousDebtGranularityThreeSmallNoMigrationCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "9f2cb14e217f8d2cd09307e0df0a5bbbe03c864e79db49c89c64d967c784f67a",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.PreviousDebtGranularityThreeSmallPacedCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "9f2cb14e217f8d2cd09307e0df0a5bbbe03c864e79db49c89c64d967c784f67a",
            FindCase(
                first.Report,
                BenchmarkV1Corpus
                    .PreviousDebtGranularityThreeSmallAdaptiveR3B5PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "9f2cb14e217f8d2cd09307e0df0a5bbbe03c864e79db49c89c64d967c784f67a",
            FindCase(
                first.Report,
                BenchmarkV1Corpus
                    .PreviousDebtGranularityThreeSmallAdaptiveR4B4PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "fcc62106479d509ea7716e20b4030e07d60436c8804a0c97c42562ee08d0f7ef",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.DebtZeroBeforeRotateNoMigrationCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "fcc62106479d509ea7716e20b4030e07d60436c8804a0c97c42562ee08d0f7ef",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.DebtZeroBeforeRotatePacedCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "fcc62106479d509ea7716e20b4030e07d60436c8804a0c97c42562ee08d0f7ef",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.DebtZeroBeforeRotateAdaptiveR3B5PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "fcc62106479d509ea7716e20b4030e07d60436c8804a0c97c42562ee08d0f7ef",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.DebtZeroBeforeRotateAdaptiveR4B4PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "c1dd88f6b7a863e01e53eb61eeb7c8e318496c63527594c856fdaa1cd645ec7e",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.InsertBurstThreeOneNoMigrationCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "c1dd88f6b7a863e01e53eb61eeb7c8e318496c63527594c856fdaa1cd645ec7e",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.InsertBurstThreeOnePacedCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "c1dd88f6b7a863e01e53eb61eeb7c8e318496c63527594c856fdaa1cd645ec7e",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.InsertBurstThreeOneAdaptiveR3B5PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "c1dd88f6b7a863e01e53eb61eeb7c8e318496c63527594c856fdaa1cd645ec7e",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.InsertBurstThreeOneAdaptiveR4B4PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "67ffc1d30452ac97ac39bb6a498208492987abb6fcb17d97f982887aea041571",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.InsertBurstTwoTwoNoMigrationCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "67ffc1d30452ac97ac39bb6a498208492987abb6fcb17d97f982887aea041571",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.InsertBurstTwoTwoPacedCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "67ffc1d30452ac97ac39bb6a498208492987abb6fcb17d97f982887aea041571",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.InsertBurstTwoTwoAdaptiveR3B5PercentCaseId)
                .ResolvedTraceSha256);
        Assert.Equal(
            "67ffc1d30452ac97ac39bb6a498208492987abb6fcb17d97f982887aea041571",
            FindCase(
                first.Report,
                BenchmarkV1Corpus.InsertBurstTwoTwoAdaptiveR4B4PercentCaseId)
                .ResolvedTraceSha256);
    }

    [Fact]
    public void Bootstrap_places_step_zero_Bases_in_A_and_excludes_them_from_W_and_P() {
        BenchmarkV1CaseDefinition definition = BenchmarkV1Corpus.Create(
            BenchmarkV1Baselines.All).Cases
            .Single(benchmarkCase => benchmarkCase.ManifestCase.CaseId ==
                BenchmarkV1Corpus.DebtZeroThenRotatePacedCaseId);
        BenchmarkV1BootstrappedSource source = BenchmarkV1SourceBootstrap.Create(
            definition.Trace);

        ObjectVersionDictionaryMaterializationInspection materialized =
            ObjectVersionDictionaryReader.MaterializeLive(
                source.Store,
                source.Cursor.PublishedRevisionAddress);
        Assert.Equal([10U, 20U, 30U], materialized.Bindings.Keys.Order());
        Assert.All(
            materialized.Bindings.Values,
            address => Assert.Equal(1U, address.FileNumber));
        Assert.All(
            materialized.Bindings,
            pair => Assert.Equal(
                1U,
                PhysicalStateOracle.InspectObjectReconstruction(
                    source.Store,
                    pair.Key,
                    pair.Value).BaseAddress.FileNumber));

        long bootstrapBytes = TotalTailBytes(source.Store);
        EvaluatorV1Session session = new(
            source.Store,
            source.Cursor,
            totalWorkloadStepCount: 0);
        AdmittedEvaluatorRun admitted = Assert.IsType<AdmittedEvaluatorRun>(
            session.Complete());
        long horizonGrowth = TotalTailBytes(session.Store) - bootstrapBytes;

        Assert.True(bootstrapBytes > 0);
        Assert.Equal(bootstrapBytes, TotalTailBytes(source.Store));
        Assert.Equal(horizonGrowth, admitted.Metrics.TotalPhysicalWriteBytes);
        Assert.Equal(horizonGrowth, admitted.Metrics.PeakCommitWriteBytes);
        Assert.NotEqual(
            TotalTailBytes(session.Store),
            admitted.Metrics.TotalPhysicalWriteBytes);
    }

    [Fact]
    public void Pacing_and_control_read_the_same_facts_and_differ_only_by_smallest_debt_migration() {
        BenchmarkV1CaseDefinition definition = BenchmarkV1Corpus.Create(
            BenchmarkV1Baselines.All).Cases
            .Single(benchmarkCase => benchmarkCase.ManifestCase.CaseId ==
                BenchmarkV1Corpus.DebtZeroThenRotatePacedCaseId);
        BenchmarkV1BootstrappedSource source = BenchmarkV1SourceBootstrap.Create(
            definition.Trace);
        NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
            source.Store,
            source.Cursor.FileScope.CurrentFileNumber,
            source.Cursor.PublishedRevisionAddress,
            definition.Trace.Steps[1]);

        StrategyStepViewV1 view = StrategyStepViewV1.Create(facts);
        StrategySelectionV1 control = BenchmarkV1Baselines.Select(
            BenchmarkV1Baselines.DebtZeroThenRotateDeltaNoMigration.Identity,
            view);
        StrategySelectionV1 paced = BenchmarkV1Baselines.Select(
            BenchmarkV1Baselines
                .DebtZeroThenRotateDeltaPacedOneDebtByObjectId.Identity,
            view);

        Assert.Equal(StrategyTargetV1.StayB, control.Target);
        Assert.Equal(control.Target, paced.Target);
        Assert.Equal(
            control.Stay.UpdateDecisions,
            paced.Stay.UpdateDecisions);
        Assert.Equal(
            control.Rotate.BContainedUpdateDecisions,
            paced.Rotate.BContainedUpdateDecisions);
        Assert.Empty(control.Stay.UnchangedMigrationObjectIds);
        Assert.Equal([10U], paced.Stay.UnchangedMigrationObjectIds);
        Assert.Empty(control.Rotate.BContainedNoChangeBaseObjectIds);
        Assert.Empty(paced.Rotate.BContainedNoChangeBaseObjectIds);
    }

    [Fact]
    public void Adaptive_registry_profiles_equal_their_direct_pure_policy_selections() {
        BenchmarkV1CaseDefinition definition = BenchmarkV1Corpus.Create(
            BenchmarkV1Baselines.All).Cases
            .Single(benchmarkCase => benchmarkCase.ManifestCase.CaseId ==
                BenchmarkV1Corpus.DebtZeroThenRotateAdaptiveR3B5PercentCaseId);
        BenchmarkV1BootstrappedSource source = BenchmarkV1SourceBootstrap.Create(
            definition.Trace);
        NormalizedSaveFacts facts = SaveStepNormalizer.Normalize(
            source.Store,
            source.Cursor.FileScope.CurrentFileNumber,
            source.Cursor.PublishedRevisionAddress,
            definition.Trace.Steps[1]);

        AssertAdaptiveProfileEqualsDirectPolicy(
            BenchmarkV1Baselines.ReadAmplificationBaseBudgetR3B5Percent.Identity,
            new ReadAmplificationBaseBudgetPolicyParameters(3m, 0.05m),
            facts);
        AssertAdaptiveProfileEqualsDirectPolicy(
            BenchmarkV1Baselines.ReadAmplificationBaseBudgetR4B4Percent.Identity,
            new ReadAmplificationBaseBudgetPolicyParameters(4m, 0.04m),
            facts);
    }

    [Fact]
    public void Identity_only_case_is_manifest_only_and_runner_fails_closed() {
        WorkloadTrace trace = BenchmarkV1Corpus.Create(
            BenchmarkV1Baselines.All).Cases[0].Trace;
        BenchmarkComponentIdentityV1 traceDefinition = new(
            trace.ScenarioName,
            1);

        BenchmarkV1CaseDefinition unknown = new(
            "unknown-selection-profile",
            traceDefinition,
            trace,
            new BenchmarkComponentIdentityV1("unknown-selection-profile", 1));
        BenchmarkV1CaseDefinition wrongVersion = new(
            "wrong-version-selection-profile",
            traceDefinition,
            trace,
            new BenchmarkComponentIdentityV1(
                BenchmarkV1Baselines
                    .ReadAmplificationBaseBudgetR3B5Percent.Identity.Id,
                2));

        Assert.Null(unknown.Strategy);
        Assert.Null(wrongVersion.Strategy);
        Assert.Throws<InvalidDataException>(() =>
            BenchmarkV1Runner.ExecuteCase(unknown));
        Assert.Throws<InvalidDataException>(() =>
            BenchmarkV1Runner.ExecuteCase(wrongVersion));
    }

    [Fact]
    public void Non_Create_bootstrap_fails_closed_before_an_evaluator_outcome_exists() {
        WorkloadTrace invalid = new(
            "invalid-bootstrap",
            "handwritten",
            generatorVersion: 1,
            seed: 0,
            [new SaveStep([new UpdateObject(1, 10, 1)])]);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => BenchmarkV1SourceBootstrap.Create(invalid));

        Assert.Contains("step 0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Selected_capacity_rejection_stops_the_case_without_fallback_or_partial_metrics() {
        WorkloadTrace trace = new(
            "selected-capacity",
            "handwritten",
            generatorVersion: 1,
            seed: 0,
            [
                new SaveStep([new CreateObject(1, 10)]),
                new SaveStep([new CreateObject(
                    2,
                    RbfV040Layout.MaxPayloadAndTailMetaLengthBytes)]),
                new SaveStep([new CreateObject(3, 1)]),
            ]);
        BenchmarkV1CaseDefinition benchmarkCase = new(
            "selected-capacity-case",
            new BenchmarkComponentIdentityV1("selected-capacity", 1),
            trace,
            BenchmarkV1Baselines.DebtZeroThenRotateDeltaNoMigration);

        BenchmarkV1BatchRun run = BenchmarkV1Runner.Run(Batch(benchmarkCase));

        CapacityRejectedOutcomeReportV1 rejected =
            Assert.IsType<CapacityRejectedOutcomeReportV1>(
                Assert.Single(run.Report.Cases).Outcome);
        Assert.Equal(CandidateTarget.StayB, rejected.SelectedTarget);
        Assert.Equal(EvaluatorRunPhase.Workload, rejected.Position.Phase);
        Assert.Equal(0, rejected.Position.CompletedWorkloadStepCount);
        Assert.Equal(2, rejected.Position.TotalWorkloadStepCount);
        Assert.Equal(
            RevisionCandidateCapacityLimit.PayloadAndTailMetaLength,
            rejected.Rejection.Limit);
    }

    [Fact]
    public void One_step_trace_has_zero_evaluated_workload_and_still_settles() {
        WorkloadTrace trace = new(
            "bootstrap-only",
            "handwritten",
            generatorVersion: 1,
            seed: 0,
            [new SaveStep([new CreateObject(1, 10)])]);
        BenchmarkV1CaseDefinition benchmarkCase = new(
            "bootstrap-only-case",
            new BenchmarkComponentIdentityV1("bootstrap-only", 1),
            trace,
            BenchmarkV1Baselines.DebtZeroThenRotateDeltaNoMigration);
        BenchmarkV1BatchRun run = BenchmarkV1Runner.Run(Batch(benchmarkCase));

        Assert.Equal(0, benchmarkCase.ManifestCase.EvaluatedWorkloadStepCount);
        AdmittedOutcomeReportV1 admitted = Assert.IsType<AdmittedOutcomeReportV1>(
            Assert.Single(run.Report.Cases).Outcome);
        Assert.Equal(1, admitted.Metrics.RealizedCommitCount);
        Assert.Equal(2U, admitted.FinalCursor.PreviousFileNumber);
        Assert.Equal(3U, admitted.FinalCursor.CurrentFileNumber);
    }

    [Fact]
    public void Batch_rejects_protocol_identity_that_does_not_match_the_runner() {
        BenchmarkV1CaseDefinition benchmarkCase = BenchmarkV1Corpus.Create(
            BenchmarkV1Baselines.All).Cases[0];

        Assert.Throws<ArgumentException>(() => new BenchmarkV1BatchDefinition(
            BenchmarkV1Corpus.ManifestId,
            BenchmarkV1Corpus.ManifestRevision,
            new BenchmarkComponentIdentityV1("not-the-evaluator", 1),
            BenchmarkV1ProtocolIdentities.TerminalSettlement,
            BenchmarkV1ProtocolIdentities.ReadSchedule,
            BenchmarkV1ProtocolIdentities.Metrics,
            BenchmarkV1ProtocolIdentities.FrameLayout,
            BenchmarkV1ProtocolIdentities.RevisionGrammar,
            [benchmarkCase]));
    }

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

    private static BenchmarkCaseReportV1 FindCase(
        BenchmarkReportV1 report,
        string caseId) => report.Cases.Single(benchmarkCase =>
            benchmarkCase.CaseId == caseId);

    private static BenchmarkV1CaseDefinition FindCaseDefinition(
        BenchmarkV1BatchDefinition definition,
        string caseId) => definition.Cases.Single(benchmarkCase =>
            benchmarkCase.ManifestCase.CaseId == caseId);

    private static void AssertMatchedWorkload(
        params BenchmarkV1CaseDefinition[] cases) {
        Assert.Equal(4, cases.Length);
        BenchmarkV1CaseDefinition first = cases[0];
        BenchmarkCaseManifestV1 firstManifest = first.ManifestCase;
        Assert.Equal(4, cases
            .Select(static benchmarkCase =>
                benchmarkCase.ManifestCase.SelectionProfile)
            .Distinct()
            .Count());

        foreach (BenchmarkV1CaseDefinition benchmarkCase in cases.Skip(1)) {
            BenchmarkCaseManifestV1 manifest = benchmarkCase.ManifestCase;
            Assert.NotEqual(firstManifest.CaseId, manifest.CaseId);
            Assert.Same(first.Trace, benchmarkCase.Trace);
            Assert.Equal(firstManifest.SourceFixture, manifest.SourceFixture);
            Assert.Equal(firstManifest.TraceDefinition, manifest.TraceDefinition);
            Assert.Equal(firstManifest.Generator, manifest.Generator);
            Assert.Equal(firstManifest.Seed, manifest.Seed);
            Assert.Equal(
                firstManifest.ResolvedTraceSha256,
                manifest.ResolvedTraceSha256);
            Assert.Equal(
                firstManifest.BootstrapStepCount,
                manifest.BootstrapStepCount);
            Assert.Equal(firstManifest.TraceStepCount, manifest.TraceStepCount);
            Assert.Equal(
                firstManifest.EvaluatedWorkloadStepCount,
                manifest.EvaluatedWorkloadStepCount);
        }
    }

    private static void AssertAdaptiveProfileEqualsDirectPolicy(
        BenchmarkComponentIdentityV1 profile,
        ReadAmplificationBaseBudgetPolicyParameters parameters,
        NormalizedSaveFacts facts) {
        StrategyStepViewV1 view = StrategyStepViewV1.Create(facts);
        StrategySelectionV1 registry = BenchmarkV1Baselines.Select(profile, view);
        ReadAmplificationBaseBudgetPolicySelection direct =
            ReadAmplificationBaseBudgetPolicy.Select(
                ReadAmplificationBaseBudgetPolicyProjection.Create(view),
                parameters);

        Assert.Equal(direct.Target, registry.Target);
        Assert.Equal(direct.StayB.UpdateDecisions, registry.Stay.UpdateDecisions);
        Assert.Equal(
            direct.StayB.UnchangedMigrationObjectIds,
            registry.Stay.UnchangedMigrationObjectIds);
        Assert.Equal(
            direct.RotateC.BContainedUpdateDecisions,
            registry.Rotate.BContainedUpdateDecisions);
        Assert.Equal(
            direct.RotateC.BContainedNoChangeBaseObjectIds,
            registry.Rotate.BContainedNoChangeBaseObjectIds);
    }

    private static AdmittedOutcomeReportV1 AssertAdmitted(
        BenchmarkReportV1 report,
        string caseId,
        int realizedCommitCount,
        long totalPhysicalWriteBytes,
        long peakCommitWriteBytes,
        long maxCurrentFileTailBytes,
        long finalColdHeadReadBytes,
        uint previousFileNumber,
        uint currentFileNumber) {
        BenchmarkCaseReportV1 benchmarkCase = FindCase(report, caseId);
        AdmittedOutcomeReportV1 admitted = Assert.IsType<AdmittedOutcomeReportV1>(
            benchmarkCase.Outcome);
        Assert.Equal(realizedCommitCount, admitted.Metrics.RealizedCommitCount);
        Assert.Equal(
            totalPhysicalWriteBytes,
            admitted.Metrics.TotalPhysicalWriteBytes);
        Assert.Equal(peakCommitWriteBytes, admitted.Metrics.PeakCommitWriteBytes);
        Assert.Equal(
            maxCurrentFileTailBytes,
            admitted.Metrics.MaxCurrentFileTailBytes);
        Assert.Equal(
            finalColdHeadReadBytes,
            admitted.Metrics.FinalColdHeadReadBytes);
        Assert.Equal(previousFileNumber, admitted.FinalCursor.PreviousFileNumber);
        Assert.Equal(currentFileNumber, admitted.FinalCursor.CurrentFileNumber);
        Assert.Empty(admitted.Settlement.MigratedObjectIds);
        return admitted;
    }

    private static long TotalTailBytes(
        Atelia.TwoLegRotationProbe.Model.RbfFileStore store) => Enumerable
        .Range(1, store.FileCount)
        .Sum(index => store.GetFile((uint)index).TailOffsetBytes);
}
