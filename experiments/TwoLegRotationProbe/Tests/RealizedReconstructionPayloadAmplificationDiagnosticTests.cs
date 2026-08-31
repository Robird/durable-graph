using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Planning;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class RealizedReconstructionPayloadAmplificationDiagnosticTests {
    [Fact]
    public void Sample_classifies_finite_zero_over_zero_and_infinity_without_faking_a_decimal() {
        RealizedReconstructionPayloadAmplificationSample finite = new(
            objectId: 1,
            headReconstructionObjectPayloadBytes: 9,
            basePayloadBytes: 4);
        RealizedReconstructionPayloadAmplificationSample zeroOverZero = new(
            objectId: 2,
            headReconstructionObjectPayloadBytes: 0,
            basePayloadBytes: 0);
        RealizedReconstructionPayloadAmplificationSample infinity = new(
            objectId: 3,
            headReconstructionObjectPayloadBytes: 1,
            basePayloadBytes: 0);

        Assert.Equal(
            RealizedReconstructionPayloadAmplificationKind.Finite,
            finite.Kind);
        Assert.Equal(2.25m, finite.EffectiveHeadToBasePayloadAmplification);
        Assert.Equal(
            RealizedReconstructionPayloadAmplificationKind.ZeroOverZero,
            zeroOverZero.Kind);
        Assert.Equal(1m, zeroOverZero.EffectiveHeadToBasePayloadAmplification);
        Assert.Equal(
            RealizedReconstructionPayloadAmplificationKind
                .PositiveOverZeroInfinity,
            infinity.Kind);
        Assert.Null(infinity.EffectiveHeadToBasePayloadAmplification);
    }

    [Fact]
    public void Sample_rejects_negative_raw_payload_values() {
        Assert.Equal(
            "headReconstructionObjectPayloadBytes",
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new RealizedReconstructionPayloadAmplificationSample(
                    objectId: 1,
                    headReconstructionObjectPayloadBytes: -1,
                    basePayloadBytes: 0)).ParamName);
        Assert.Equal(
            "basePayloadBytes",
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new RealizedReconstructionPayloadAmplificationSample(
                    objectId: 1,
                    headReconstructionObjectPayloadBytes: 0,
                    basePayloadBytes: -1)).ParamName);
    }

    [Fact]
    public void Capture_freezes_all_parent_live_samples_in_ObjectId_order_without_mutating_store() {
        DiagnosticFixture fixture = CreateFixture();
        NormalizedSaveFacts facts = SaveStepNormalizer.NormalizeMaintenanceOnly(
            fixture.Store,
            fixture.Current.FileNumber,
            fixture.PublishedRevision);
        StoreSnapshot before = CaptureStore(fixture.Store);

        RealizedReconstructionPayloadAmplificationDiagnostic first =
            RealizedReconstructionPayloadAmplificationDiagnostic.Capture(facts);
        RealizedReconstructionPayloadAmplificationDiagnostic second =
            RealizedReconstructionPayloadAmplificationDiagnostic.Capture(facts);

        AssertStoreSnapshot(before, CaptureStore(fixture.Store));
        Assert.Equal(first.Samples, second.Samples);
        Assert.Equal([10U, 20U, 30U],
            first.Samples.Select(static sample => sample.ObjectId));
        Assert.Equal(
            new RealizedReconstructionPayloadAmplificationSample(10, 6, 4),
            first.Samples[0]);
        Assert.Equal(
            new RealizedReconstructionPayloadAmplificationSample(20, 0, 0),
            first.Samples[1]);
        Assert.Equal(
            new RealizedReconstructionPayloadAmplificationSample(30, 5, 0),
            first.Samples[2]);

        IList<RealizedReconstructionPayloadAmplificationSample> readOnly =
            Assert.IsAssignableFrom<
                IList<RealizedReconstructionPayloadAmplificationSample>>(
                first.Samples);
        Assert.True(readOnly.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => readOnly.Add(first.Samples[0]));
    }

    [Fact]
    public void Empty_parent_live_produces_an_empty_read_only_checkpoint() {
        DiagnosticFixture fixture = CreateEmptyFixture();
        NormalizedSaveFacts facts = SaveStepNormalizer.NormalizeMaintenanceOnly(
            fixture.Store,
            fixture.Current.FileNumber,
            fixture.PublishedRevision);

        RealizedReconstructionPayloadAmplificationDiagnostic diagnostic =
            RealizedReconstructionPayloadAmplificationDiagnostic.Capture(facts);

        Assert.Empty(diagnostic.Samples);
        IList<RealizedReconstructionPayloadAmplificationSample> readOnly =
            Assert.IsAssignableFrom<
                IList<RealizedReconstructionPayloadAmplificationSample>>(
                diagnostic.Samples);
        Assert.True(readOnly.IsReadOnly);
    }

    private static DiagnosticFixture CreateFixture() {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        ObjectVersionDictionaryBuilder previousDictionary = new();
        FrameBuilder previousBuilder = new() {
            ObjectVersionDictionary = previousDictionary,
        };
        AddBase(previousBuilder, objectId: 30, payloadBytes: 0);
        AddBase(previousBuilder, objectId: 10, payloadBytes: 4);
        AddBase(previousBuilder, objectId: 20, payloadBytes: 0);
        previousDictionary.BindSelf(30);
        previousDictionary.BindSelf(10);
        previousDictionary.BindSelf(20);
        AbsoluteFrameAddress previousRevision = AppendExact(
            previous,
            previousBuilder);

        RbfFile current = store.CreateFile();
        ObjectVersionDictionaryBuilder currentDictionary = new() {
            Kind = ObjectVersionDictionaryKind.Delta,
            ParentRevisionFrameTicket = Previous(previousRevision.FrameTicket),
        };
        currentDictionary.BindSelf(10);
        currentDictionary.BindSelf(30);
        FrameBuilder currentBuilder = new() {
            ObjectVersionDictionary = currentDictionary,
        };
        AddDelta(
            currentBuilder,
            objectId: 10,
            Previous(previousRevision.FrameTicket),
            parentBasePayloadBytes: 4,
            resultBasePayloadBytes: 4,
            deltaPayloadBytes: 2,
            reconstructionPayloadBytes: 6);
        AddDelta(
            currentBuilder,
            objectId: 30,
            Previous(previousRevision.FrameTicket),
            parentBasePayloadBytes: 0,
            resultBasePayloadBytes: 0,
            deltaPayloadBytes: 5,
            reconstructionPayloadBytes: 5);
        AbsoluteFrameAddress publishedRevision = AppendExact(
            current,
            currentBuilder);
        return new DiagnosticFixture(store, current, publishedRevision);
    }

    private static DiagnosticFixture CreateEmptyFixture() {
        RbfFileStore store = new();
        RbfFile previous = store.CreateFile();
        FrameBuilder previousBuilder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder(),
        };
        AbsoluteFrameAddress previousRevision = AppendExact(
            previous,
            previousBuilder);

        RbfFile current = store.CreateFile();
        FrameBuilder currentBuilder = new() {
            ObjectVersionDictionary = new ObjectVersionDictionaryBuilder {
                Kind = ObjectVersionDictionaryKind.Delta,
                ParentRevisionFrameTicket = Previous(previousRevision.FrameTicket),
            },
        };
        AbsoluteFrameAddress publishedRevision = AppendExact(
            current,
            currentBuilder);
        return new DiagnosticFixture(store, current, publishedRevision);
    }

    private static void AddBase(
        FrameBuilder frame,
        uint objectId,
        int payloadBytes) => RevisionCandidateRecordBuilder.AddBase(
            frame,
            objectId,
            new LogicalObjectState(payloadBytes, LogicalVersionOrdinal: 1));

    private static void AddDelta(
        FrameBuilder frame,
        uint objectId,
        RelativeFrameTicket parent,
        int parentBasePayloadBytes,
        int resultBasePayloadBytes,
        int deltaPayloadBytes,
        long reconstructionPayloadBytes) {
        ObjectVersionBuilder version = frame.Add(objectId);
        version.Kind = ObjectVersionKind.Delta;
        version.PayloadBytes = deltaPayloadBytes;
        version.ReconstructionObjectPayloadBytes = reconstructionPayloadBytes;
        version.ResultBasePayloadBytes = resultBasePayloadBytes;
        version.ExpectedParentBasePayloadBytes = parentBasePayloadBytes;
        version.LogicalVersionOrdinal = 2;
        version.DeltaParentFrameTicket = parent;
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
        Assert.Equal(estimate.RbfLayout.Ticket, ticket);
        return new AbsoluteFrameAddress(file.FileNumber, ticket);
    }

    private static StoreSnapshot CaptureStore(RbfFileStore store) => new(
        store.FileCount,
        Enumerable.Range(1, store.FileCount)
            .Select(index => store.GetFile((uint)index))
            .Select(static file => new FileSnapshot(
                file.FileNumber,
                file.FrameCount,
                file.TailOffsetBytes))
            .ToArray());

    private static void AssertStoreSnapshot(
        StoreSnapshot expected,
        StoreSnapshot actual) {
        Assert.Equal(expected.FileCount, actual.FileCount);
        Assert.Equal(expected.Files, actual.Files);
    }

    private static RelativeFrameTicket Previous(FrameTicket ticket) =>
        new(IsPreviousFile: true, ticket);

    private sealed record DiagnosticFixture(
        RbfFileStore Store,
        RbfFile Current,
        AbsoluteFrameAddress PublishedRevision);

    private sealed record StoreSnapshot(
        int FileCount,
        IReadOnlyList<FileSnapshot> Files);

    private sealed record FileSnapshot(
        uint FileNumber,
        int FrameCount,
        long TailOffsetBytes);
}
