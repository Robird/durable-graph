using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Simulation;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class ProvisionalRevisionV0IntegrationTests {
    [Fact]
    public void Default_run_is_exactly_the_explicit_object_payload_only_scope() {
        WorkloadTrace trace = CreateThreeStepTrace();

        SimulationRun defaultRun = WorkloadSimulator.Run(
            trace,
            BaselinePolicy.AlwaysDeltaWhenLegal);
        SimulationRun explicitRun = WorkloadSimulator.Run(
            trace,
            BaselinePolicy.AlwaysDeltaWhenLegal,
            AccountingScope.ObjectPayloadOnly);

        Assert.Equal(AccountingScope.ObjectPayloadOnly, defaultRun.AccountingScope);
        Assert.Equal(defaultRun.RevisionAddresses, explicitRun.RevisionAddresses);
        Assert.Equal(defaultRun.Observations, explicitRun.Observations);
        Assert.Equal(
            [(4L, 136), (144L, 32), (180L, 24)],
            defaultRun.RevisionAddresses
                .Select(static address => (
                    address.FrameTicket.OffsetBytes,
                    address.FrameTicket.LengthBytes)));
    }

    [Fact]
    public void Provisional_run_preserves_logical_state_and_Base_Delta_decisions() {
        WorkloadTrace trace = CreateThreeStepTrace();
        SimulationRun objectPayloadOnly = WorkloadSimulator.Run(
            trace,
            BaselinePolicy.AlwaysDeltaWhenLegal,
            AccountingScope.ObjectPayloadOnly);
        SimulationRun provisional = WorkloadSimulator.Run(
            trace,
            BaselinePolicy.AlwaysDeltaWhenLegal,
            AccountingScope.ProvisionalRevisionV0);

        AssertExactState(
            WorkloadReplayer.Replay(trace),
            PhysicalStateOracle.Materialize(provisional));
        Assert.Equal(
            DescribeVersionKinds(objectPayloadOnly),
            DescribeVersionKinds(provisional));
        Assert.Equal("1:Base;2:Base|1:Delta|", DescribeVersionKinds(provisional));
        Assert.All(
            provisional.Observations,
            static observation => {
                Assert.Equal(
                    AccountingScope.ProvisionalRevisionV0,
                    observation.AccountingScope);
                Assert.True(observation.IncludesObjectVersionHeaders);
                Assert.True(observation.IncludesObjectVersionDict);
                Assert.True(observation.IncludesTailMetaIndex);
                Assert.NotNull(observation.ProvisionalRevisionV0);
            });
    }

    [Fact]
    public void Provisional_run_is_deterministic_and_conserves_every_sized_component() {
        WorkloadTrace trace = CreateThreeStepTrace();

        SimulationRun first = WorkloadSimulator.Run(
            trace,
            BaselinePolicy.AlwaysDeltaWhenLegal,
            AccountingScope.ProvisionalRevisionV0);
        SimulationRun second = WorkloadSimulator.Run(
            trace,
            BaselinePolicy.AlwaysDeltaWhenLegal,
            AccountingScope.ProvisionalRevisionV0);

        Assert.Equal(DescribeAccounting(first), DescribeAccounting(second));
        Assert.Equal(first.RevisionAddresses.Count, first.AccountingEstimates.Count);
        foreach (RevisionObservation observation in first.Observations) {
            ProvisionalRevisionV0Estimate estimate = Assert.IsType<ProvisionalRevisionV0Estimate>(
                observation.ProvisionalRevisionV0);
            FrameAccountingEstimate provenance = first.AccountingEstimates[observation.Address];
            RbfFrameLayoutEstimate storedLayout = first.FileStore.ReadLayout(observation.Address);

            Assert.Same(estimate, provenance.ProvisionalRevisionV0);
            Assert.Equal(AccountingScope.ProvisionalRevisionV0, provenance.Scope);
            Assert.Equal(observation.Address.FrameTicket, estimate.RbfLayout.Ticket);
            Assert.Equal(estimate.RbfLayout, provenance.RbfLayout);
            Assert.Equal(estimate.RbfLayout, storedLayout);
            Assert.Equal(
                estimate.SyntheticObjectPayloadBytes +
                    estimate.DomainRecordHeaderBytes +
                    estimate.ObjectVersionDictionaryRecordBytes,
                estimate.PayloadLengthBytes);
            Assert.Equal(
                estimate.TailMetaDirectoryBytes,
                estimate.RbfLayout.TailMetaLengthBytes);
            Assert.Equal(
                estimate.ObjectVersionDictionaryPayloadOffsetBytes,
                estimate.DomainRecords.Sum(static record => record.FullRecordBytes));
            Assert.Equal(
                estimate.SyntheticObjectPayloadBytes,
                estimate.DomainRecords.Sum(static record => record.SyntheticObjectPayloadBytes));
            Assert.Equal(
                estimate.DomainRecordHeaderBytes,
                estimate.DomainRecords.Sum(static record => record.HeaderBytes));
        }
    }

    [Fact]
    public void Remove_only_revision_has_no_domain_record_but_still_has_Ovd_and_TailMeta() {
        WorkloadTrace trace = new(
            "provisional-remove-only",
            "handwritten",
            1,
            0,
            [
                new SaveStep([new CreateObject(1, 10)]),
                new SaveStep([new RemoveObject(1)]),
            ]);

        SimulationRun run = WorkloadSimulator.Run(
            trace,
            BaselinePolicy.AlwaysBase,
            AccountingScope.ProvisionalRevisionV0);

        RevisionObservation removal = run.Observations[1];
        ProvisionalRevisionV0Estimate estimate = Assert.IsType<ProvisionalRevisionV0Estimate>(
            removal.ProvisionalRevisionV0);
        Assert.Empty(run.FileStore.ReadFrame(removal.Address).ObjectVersions);
        Assert.Equal(0, removal.ObjectVersionCount);
        Assert.Equal(0, removal.ObjectPayloadBytesWritten);
        Assert.Equal(0, estimate.DomainRecordCount);
        Assert.Equal(0, estimate.SyntheticObjectPayloadBytes);
        Assert.Equal(0, estimate.DomainRecordHeaderBytes);
        Assert.True(estimate.ObjectVersionDictionaryRecordBytes > 0);
        Assert.True(estimate.TailMetaDirectoryBytes > 0);
        Assert.True(estimate.PayloadLengthBytes > 0);
        Assert.True(estimate.RbfLayout.FrameLengthBytes > RbfV040Layout.MinFrameLengthBytes);
    }

    [Fact]
    public void Reconstruction_reads_each_unique_frame_once_in_both_scopes() {
        WorkloadTrace trace = new(
            "shared-frame-read",
            "handwritten",
            1,
            0,
            [
                new SaveStep([
                    new CreateObject(1, 10),
                    new CreateObject(2, 20),
                ]),
                new SaveStep([new UpdateObject(1, 8, 2)]),
            ]);
        SimulationRun payloadOnly = WorkloadSimulator.Run(
            trace,
            BaselinePolicy.AlwaysBase,
            AccountingScope.ObjectPayloadOnly);
        SimulationRun provisional = WorkloadSimulator.Run(
            trace,
            BaselinePolicy.AlwaysBase,
            AccountingScope.ProvisionalRevisionV0);

        PostSaveReconstructionMetrics payloadRead = payloadOnly.Observations[^1]
            .PostSaveReconstruction;
        PostSaveReconstructionMetrics provisionalRead = provisional.Observations[^1]
            .PostSaveReconstruction;

        Assert.Equal(2, payloadRead.UniqueFrameCount);
        Assert.Equal(2, payloadRead.RequiredObjectVersionCount);
        Assert.Equal(28, payloadRead.RequiredObjectPayloadBytes);
        Assert.Equal(38, payloadRead.ObjectPayloadBytesInUniqueFrames);
        Assert.Equal(10, payloadRead.CoReadObjectPayloadBytes);
        Assert.Equal(payloadRead.LiveObjectCount, provisionalRead.LiveObjectCount);
        Assert.Equal(
            payloadRead.RequiredObjectVersionCount,
            provisionalRead.RequiredObjectVersionCount);
        Assert.Equal(payloadRead.UniqueFrameCount, provisionalRead.UniqueFrameCount);
        Assert.Equal(
            payloadRead.RequiredObjectPayloadBytes,
            provisionalRead.RequiredObjectPayloadBytes);
        Assert.Equal(
            payloadRead.ObjectPayloadBytesInUniqueFrames,
            provisionalRead.ObjectPayloadBytesInUniqueFrames);
        Assert.Equal(payloadRead.CoReadObjectPayloadBytes, provisionalRead.CoReadObjectPayloadBytes);
        Assert.NotEqual(
            payloadRead.ModeledRbfFrameBytesRead,
            provisionalRead.ModeledRbfFrameBytesRead);
    }

    [Fact]
    public void Estimator_canonicalizes_domain_record_order_by_ObjectId() {
        FrameBuilder descendingBuilder = new();
        ConfigureBase(descendingBuilder.Add(200), payloadBytes: 4);
        ConfigureBase(descendingBuilder.Add(1), payloadBytes: 3);
        ConfigureFullSelfOvd(descendingBuilder);
        FrameBuilder ascendingBuilder = new();
        ConfigureBase(ascendingBuilder.Add(1), payloadBytes: 3);
        ConfigureBase(ascendingBuilder.Add(200), payloadBytes: 4);
        ConfigureFullSelfOvd(ascendingBuilder);

        ProvisionalRevisionV0Estimate descending = ProvisionalRevisionV0Estimator.Estimate(
            descendingBuilder.Build(),
            frameStartOffsetBytes: 4);
        ProvisionalRevisionV0Estimate ascending = ProvisionalRevisionV0Estimator.Estimate(
            ascendingBuilder.Build(),
            frameStartOffsetBytes: 4);

        Assert.Equal([1U, 200U], descending.DomainRecords.Select(static record => record.ObjectId));
        Assert.Equal(descending.DomainRecords, ascending.DomainRecords);
        Assert.Equal(descending.PayloadLengthBytes, ascending.PayloadLengthBytes);
        Assert.Equal(descending.TailMetaDirectoryBytes, ascending.TailMetaDirectoryBytes);
        Assert.Equal(descending.RbfLayout, ascending.RbfLayout);
    }

    [Fact]
    public void Provisional_metadata_can_overflow_a_body_that_object_payload_only_accepts() {
        WorkloadTrace tooLargeForProvisional = new(
            "metadata-overflow",
            "handwritten",
            1,
            0,
            [
                new SaveStep([
                    new CreateObject(1, RbfV040Layout.MaxPayloadAndTailMetaLengthBytes),
                ]),
            ]);

        SimulationRun payloadOnly = WorkloadSimulator.Run(
            tooLargeForProvisional,
            BaselinePolicy.AlwaysBase,
            AccountingScope.ObjectPayloadOnly);

        Assert.Single(payloadOnly.Observations);
        RevisionCandidateCapacityException capacity =
            Assert.Throws<RevisionCandidateCapacityException>(
            () => WorkloadSimulator.Run(
                tooLargeForProvisional,
                BaselinePolicy.AlwaysBase,
                AccountingScope.ProvisionalRevisionV0));
        Assert.Equal(
            RevisionCandidateCapacityLimit.PayloadAndTailMetaLength,
            capacity.Rejection.Limit);
        Assert.True(
            capacity.Rejection.AttemptedValue > capacity.Rejection.MaximumValue);
        Assert.Equal(
            RbfV040Layout.MaxPayloadAndTailMetaLengthBytes,
            capacity.Rejection.MaximumValue);

        SimulationRun retry = WorkloadSimulator.Run(
            CreateThreeStepTrace(),
            BaselinePolicy.AlwaysBase,
            AccountingScope.ProvisionalRevisionV0);
        Assert.Equal(3, retry.Observations.Count);
    }

    [Fact]
    public void TailMeta_directory_accepts_65535_bytes_and_rejects_65536() {
        const int objectCount = 14_782;
        uint[] exactlyAtLimit = Enumerable.Range(1, objectCount)
            .Select(static value => (uint)value)
            .ToArray();
        exactlyAtLimit[0] = 3_000_000;
        uint[] oneByteOverLimit = (uint[])exactlyAtLimit.Clone();
        oneByteOverLimit[0] = 300_000_000;
        Frame acceptedFrame = BuildZeroPayloadFirstRevision(exactlyAtLimit);
        Frame rejectedFrame = BuildZeroPayloadFirstRevision(oneByteOverLimit);

        ProvisionalRevisionV0Estimate accepted = ProvisionalRevisionV0Estimator.Estimate(
            acceptedFrame,
            frameStartOffsetBytes: 4);

        Assert.Equal(RbfV040Layout.MaxTailMetaLengthBytes, accepted.TailMetaDirectoryBytes);
        RevisionCandidateCapacityException exception =
            Assert.Throws<RevisionCandidateCapacityException>(
            () => ProvisionalRevisionV0Estimator.Estimate(
                rejectedFrame,
                frameStartOffsetBytes: 4));
        Assert.Equal(
            new RevisionCandidateCapacityRejection(
                RevisionCandidateCapacityLimit.TailMetaLength,
                AttemptedValue: RbfV040Layout.MaxTailMetaLengthBytes + 1L,
                MaximumValue: RbfV040Layout.MaxTailMetaLengthBytes),
            exception.Rejection);
    }

    [Fact]
    public void Estimator_accepts_last_relative_start_and_rejects_next_aligned_start() {
        FrameBuilder builder = new();
        ConfigureBase(builder.Add(1), payloadBytes: 0);
        ConfigureFullSelfOvd(builder);
        Frame frame = builder.Build();

        ProvisionalRevisionV0Estimate accepted = ProvisionalRevisionV0Estimator.Estimate(
            frame,
            frameStartOffsetBytes: RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes);

        Assert.Equal(
            RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes,
            accepted.RbfLayout.FrameStartOffsetBytes);
        Assert.True(
            accepted.RbfLayout.TailOffsetAfterBytes >
                RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes);
        RevisionCandidateCapacityException relativeException =
            Assert.Throws<RevisionCandidateCapacityException>(
            () => ProvisionalRevisionV0Estimator.Estimate(
                frame,
                frameStartOffsetBytes:
                    RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes +
                    RbfV040Layout.AlignmentBytes));
        Assert.Equal(
            new RevisionCandidateCapacityRejection(
                RevisionCandidateCapacityLimit.TargetFrameStartRelative,
                AttemptedValue:
                    RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes +
                    RbfV040Layout.AlignmentBytes,
                MaximumValue:
                    RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes),
            relativeException.Rejection);

        RevisionCandidateCapacityException nativeException =
            Assert.Throws<RevisionCandidateCapacityException>(
                () => ProvisionalRevisionV0Estimator.Estimate(
                    frame,
                    frameStartOffsetBytes:
                        RbfV040Layout.MaxNativeFrameStartOffsetBytes +
                        RbfV040Layout.AlignmentBytes));
        Assert.Equal(
            new RevisionCandidateCapacityRejection(
                RevisionCandidateCapacityLimit.TargetFrameStartNative,
                AttemptedValue:
                    RbfV040Layout.MaxNativeFrameStartOffsetBytes +
                    RbfV040Layout.AlignmentBytes,
                MaximumValue: RbfV040Layout.MaxNativeFrameStartOffsetBytes),
            nativeException.Rejection);
    }

    [Fact]
    public void Estimator_marks_only_scoped_high_references_as_relative_capacity() {
        RelativeFrameTicket nativeOnlyTicket = new(
            IsPreviousFile: true,
            new FrameTicket(
                RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes +
                    RbfV040Layout.AlignmentBytes,
                RbfV040Layout.MinFrameLengthBytes));
        FrameBuilder builder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder() {
                ParentRevisionFrameTicket = nativeOnlyTicket,
            },
        };

        RevisionCandidateCapacityException exception =
            Assert.Throws<RevisionCandidateCapacityException>(
                () => ProvisionalRevisionV0Estimator.Estimate(
                    builder.Build(),
                    RbfV040Layout.InitialTailOffsetBytes));

        Assert.Equal(
            new RevisionCandidateCapacityRejection(
                RevisionCandidateCapacityLimit.ReferencedFrameTicketRelative,
                nativeOnlyTicket.FrameTicket.OffsetBytes,
                RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes),
            exception.Rejection);
        Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
    }

    [Fact]
    public void Int32_max_component_is_a_frame_capacity_rejection() {
        FrameBuilder builder = new();
        ConfigureBase(builder.Add(1), payloadBytes: int.MaxValue);
        ConfigureFullSelfOvd(builder);

        RevisionCandidateCapacityException exception =
            Assert.Throws<RevisionCandidateCapacityException>(
                () => ProvisionalRevisionV0Estimator.Estimate(
                    builder.Build(),
                    RbfV040Layout.InitialTailOffsetBytes));

        Assert.Equal(
            RevisionCandidateCapacityLimit.PayloadAndTailMetaLength,
            exception.Rejection.Limit);
        Assert.True(
            exception.Rejection.AttemptedValue >
                RbfV040Layout.MaxPayloadAndTailMetaLengthBytes);
        Assert.Equal(
            RbfV040Layout.MaxPayloadAndTailMetaLengthBytes,
            exception.Rejection.MaximumValue);
    }

    private static WorkloadTrace CreateThreeStepTrace() => new(
        "provisional-three-step",
        "handwritten",
        1,
        0,
        [
            new SaveStep([
                new CreateObject(1, 100),
                new CreateObject(2, 10),
            ]),
            new SaveStep([new UpdateObject(1, 60, 7)]),
            new SaveStep([new RemoveObject(2)]),
        ]);

    private static string DescribeVersionKinds(SimulationRun run) => string.Join(
        "|",
        run.RevisionAddresses.Select(address => string.Join(
            ";",
            run.FileStore.ReadFrame(address).ObjectVersions
                .OrderBy(static pair => pair.Key)
                .Select(static pair => $"{pair.Key}:{pair.Value.Kind}"))));

    private static string DescribeAccounting(SimulationRun run) => string.Join(
        "|",
        run.Observations.Select(static observation => {
            ProvisionalRevisionV0Estimate estimate = observation.ProvisionalRevisionV0!;
            return $"{observation.Address.FrameTicket.OffsetBytes}/" +
                $"{observation.Address.FrameTicket.LengthBytes}:" +
                $"{estimate.SyntheticObjectPayloadBytes}+" +
                $"{estimate.DomainRecordHeaderBytes}+" +
                $"{estimate.ObjectVersionDictionaryRecordBytes}+" +
                $"{estimate.TailMetaDirectoryBytes}:" +
                $"{estimate.RbfLayout.FrameLengthBytes}/" +
                $"{estimate.RbfLayout.AppendLengthBytes}:" +
                string.Join(
                    ",",
                    estimate.DomainRecords.Select(static record =>
                        $"{record.ObjectId}@{record.RecordOffsetBytes}/" +
                        $"{record.FullRecordBytes}"));
        }));

    private static void AssertExactState(
        IReadOnlyDictionary<uint, LogicalObjectState> expected,
        IReadOnlyDictionary<uint, LogicalObjectState> actual) {
        Assert.Equal(expected.Count, actual.Count);
        foreach ((uint objectId, LogicalObjectState expectedState) in expected) {
            Assert.True(actual.TryGetValue(objectId, out LogicalObjectState actualState));
            Assert.Equal(expectedState, actualState);
        }
    }

    private static Frame BuildZeroPayloadFirstRevision(
        IEnumerable<uint> objectIds) {
        FrameBuilder builder = new();
        foreach (uint objectId in objectIds) {
            ConfigureBase(builder.Add(objectId), payloadBytes: 0);
        }

        ConfigureFullSelfOvd(builder);
        return builder.Build();
    }

    private static void ConfigureFullSelfOvd(FrameBuilder builder) {
        ObjectVersionDictionaryBuilder dictionary = new();
        foreach (uint objectId in builder.ObjectVersions.Keys) {
            dictionary.BindSelf(objectId);
        }

        builder.ObjectVersionDictionary = dictionary;
    }

    private static void ConfigureBase(
        ObjectVersionBuilder builder,
        int payloadBytes) {
        builder.Kind = ObjectVersionKind.Base;
        builder.PayloadBytes = payloadBytes;
        builder.ReconstructionObjectPayloadBytes = payloadBytes;
        builder.ResultBasePayloadBytes = payloadBytes;
        builder.LogicalVersionOrdinal = 1;
    }
}
